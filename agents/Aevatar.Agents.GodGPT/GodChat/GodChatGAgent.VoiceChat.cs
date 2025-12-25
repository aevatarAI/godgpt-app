using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.AI.Exceptions;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Voice chat related methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    public async Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData,
        string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null,
        VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English, double voiceDurationSeconds = 0.0)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} START - file: {fileName}, size: {voiceData?.Length ?? 0} chars, language: {voiceLanguage}, duration: {voiceDurationSeconds}s");
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);

        // Validate voiceData
        if (string.IsNullOrEmpty(voiceData) || voiceLanguage == VoiceLanguageEnum.Unset)
        {
            Logger.LogError($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Invalid voice data");
            var errMsg = _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidVoiceMessage,language);
            if (voiceLanguage == VoiceLanguageEnum.Unset)
            {
                errMsg = _localizationService.GetLocalizedException(ExceptionMessageKeys.UnSetVoiceLanguage,language);
            }

            var errorMessage = new ResponseStreamGodChat()
            {
                Response = errMsg,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.ParamInvalid,
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(errorMessage);
            }
            else
            {
                await PublishAsync(errorMessage.ToProto());
            }

            return;
        }

        // Convert MP3 data to byte array - track processing time
        var conversionStopwatch = Stopwatch.StartNew();
        var voiceDataBytes = Convert.FromBase64String(voiceData);
        conversionStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} Base64_Conversion: {conversionStopwatch.ElapsedMilliseconds}ms, bytes: {voiceDataBytes.Length}");

        string voiceContent;
        var voiceParseSuccess = true;
        string? voiceParseErrorMessage = null;

        // STT Processing - track time and performance
        var sttStopwatch = Stopwatch.StartNew();
        try
        {
            voiceContent = await _speechService.SpeechToTextAsync(voiceDataBytes, voiceLanguage);
            sttStopwatch.Stop();
            
            if (string.IsNullOrWhiteSpace(voiceContent))
            {
                voiceParseSuccess = false;
                voiceParseErrorMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechTimeout,language);
                voiceContent = _localizationService.GetLocalizedException(ExceptionMessageKeys.TranscriptUnavailable,language);
                Logger.LogWarning($"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - FAILED (empty result)");
            }
            else
            {
                Logger.LogInformation($"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - SUCCESS, length: {voiceContent.Length} chars, content: '{voiceContent}'");
            }
        }
        catch (Exception ex)
        {
            sttStopwatch.Stop();
            Logger.LogError(ex, $"[PERF][VoiceChat] {sessionId} STT_Processing: {sttStopwatch.ElapsedMilliseconds}ms - FAILED with exception");
            voiceParseSuccess = false;
            voiceParseErrorMessage = ex.Message.Contains("timeout") ? _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechTimeout,language) :
                ex.Message.Contains("format") ? _localizationService.GetLocalizedException(ExceptionMessageKeys.AudioFormatUnsupported,language) :
                _localizationService.GetLocalizedException(ExceptionMessageKeys.SpeechServiceUnavailable,language);
            voiceContent = _localizationService.GetLocalizedException(ExceptionMessageKeys.TranscriptUnavailable,language);
        }

        // If voice parsing failed, don't call LLM, just save the failed message
        if (!voiceParseSuccess)
        {
            Logger.LogWarning(
                $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsing failed: {voiceParseErrorMessage}");

            // Save conversation data with voice metadata
            await SetSessionTitleAsync(sessionId, voiceContent);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = voiceContent
            });

            // Save voice message with failure status
            var chatMessageMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = false,
                VoiceParseErrorMessage = voiceParseErrorMessage,
                VoiceDurationSeconds = voiceDurationSeconds
            };

            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = { chatMessages.ToProtoList() }
            });
            
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { chatMessageMeta.ToProto() }
            });
            
            await ConfirmEventsAsync();

            // Send error response
            var errorResponse = new ResponseStreamGodChat()
            {
                Response = _localizationService.GetLocalizedException(ExceptionMessageKeys.LanguageNotRecognised,language),
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = ChatErrorCode.VoiceParsingFailed,
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(errorResponse);
            }
            else
            {
                await PublishAsync(errorResponse.ToProto());
            }

            totalStopwatch.Stop();
            Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms - FAILED (parse error)");
            return;
        }

        Logger.LogDebug(
            $"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Voice parsed successfully: {voiceContent}");

        // Send STT result immediately to frontend via VoiceToText message
        var sttResultMessage = new ResponseStreamGodChat()
        {
            Response = voiceContent, // STT converted text
            ChatId = chatId,
            IsLastChunk = false,
            SerialNumber = 0, // Mark as first message (STT result)
            SessionId = sessionId,
            VoiceContentType = VoiceContentType.VoiceToText, // Critical: indicate this is STT result
            ErrorCode = ChatErrorCode.Success,
            NewTitle = string.Empty,
            AudioData = null, // No audio data for STT result
            AudioMetadata = null
        };

        // Send STT result using the same streaming mechanism
        if (isHttpRequest)
        {
            await PushMessageToClientAsync(sttResultMessage);
        }
        else
        {
            await PublishAsync(sttResultMessage.ToProto());
        }

        Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} STT result sent to frontend: '{voiceContent}'");

        var quotaStopwatch = Stopwatch.StartNew();
        var userQuotaGAgent = await GetUserQuotaAgentAsync(State.ChatManagerGuid);
        var actionResultDto = await userQuotaGAgent.ExecuteVoiceActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString());
        
        quotaStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} Quota_Check: {quotaStopwatch.ElapsedMilliseconds}ms - success: {actionResultDto.Success}");
        if (!actionResultDto.Success)
        {
            Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} Access restricted");

            //save conversation data with voice metadata
            await SetSessionTitleAsync(sessionId, voiceContent);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = voiceContent
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });

            var userVoiceMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            };
            var assistantResponseMeta = new ChatMessageMeta
            {
                IsVoiceMessage = false,
                VoiceLanguage = VoiceLanguageEnum.English,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = 0.0
            };

            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = { chatMessages.ToProtoList() }
            });
            
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { userVoiceMeta.ToProto(), assistantResponseMeta.ToProto() }
            });
            
            await ConfirmEventsAsync();

            //2. Directly respond with error information.
            var errorCode = actionResultDto.Code switch
            {
                ExecuteActionStatus.InsufficientCredits => ChatErrorCode.InsufficientCredits,
                ExecuteActionStatus.RateLimitExceeded => ChatErrorCode.RateLimitExceeded,
                _ => ChatErrorCode.RateLimitExceeded
            };

            var chatMessage = new ResponseStreamGodChat()
            {
                Response = actionResultDto.Message,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
                ErrorCode = errorCode,
                VoiceContentType = VoiceContentType.VoiceResponse
            };

            if (isHttpRequest)
            {
                await PushMessageToClientAsync(chatMessage);
            }
            else
            {
                await PublishAsync(chatMessage.ToProto());
            }

            totalStopwatch.Stop();
            Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms - FAILED (quota denied)");
            return;
        }

        Logger.LogDebug($"[GodChatGAgent][StreamVoiceChatWithSession] {sessionId.ToString()} - Validation passed");
        
        await SetSessionTitleAsync(sessionId, voiceContent);

        var llmStopwatch = Stopwatch.StartNew();
        var configuration = await GetConfigurationAsync();
        await GodVoiceStreamChatAsync(sessionId, await configuration.GetSystemLLMAsync(),
            await configuration.GetStreamingModeEnabledAsync(),
            voiceContent, chatId, promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);
        llmStopwatch.Stop();
        
        totalStopwatch.Stop();
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} LLM_Processing: {llmStopwatch.ElapsedMilliseconds}ms");
        Logger.LogInformation($"[PERF][VoiceChat] {sessionId} TOTAL_Time: {totalStopwatch.ElapsedMilliseconds}ms");
    }

    public async Task<string> GodVoiceStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled,
        string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English,
        double voiceDurationSeconds = 0.0, bool addToHistory = true)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GodVoiceStreamChatAsync] {sessionId.ToString()} start with message: {message}, language: {voiceLanguage}");

        // Step 1: Get configuration and system message (same as GodStreamChatAsync)
        var configuration = await GetConfigurationAsync();
        var sysMessage = await configuration.GetPromptAsync();

        // Step 2: Initialize LLM if needed (same as GodStreamChatAsync)

        // Step 3: Create voice chat context with voice-specific metadata
        var aiChatContextDto = CreateVoiceChatContext(sessionId, llm, streamingModeEnabled, message, chatId, 
            promptSettings, isHttpRequest, region, voiceLanguage, voiceDurationSeconds);

        // Step 4: Get AI proxy and start streaming chat (same as GodStreamChatAsync)
        var (aiAgentStatusProxy, proxyId) = await GetProxyByRegionAsync(region);
        
        if (aiAgentStatusProxy != null && proxyId != null)
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] agent {proxyId}, session {sessionId.ToString()}, chat {chatId}");
            
            // Set default temperature for voice chat
            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "1.0";
            
            // Start streaming with voice context (timestamp now in system prompt)
            var promptMsg = message;
            switch (voiceLanguage)
            {
                case  VoiceLanguageEnum.English:
                    promptMsg += ".Requirement: Please reply in English.";
                    break;
                case VoiceLanguageEnum.Chinese:
                    promptMsg += ".Requirement: Please reply in Chinese.";
                    break;
                case VoiceLanguageEnum.Spanish:
                    promptMsg += ".Requirement: Please reply in Spanish.";
                    break;
                case VoiceLanguageEnum.Unset:
                    break;
                default:
                    break;
            }
            Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] promptMsg: {promptMsg}");

            // Build PromptWithStreamInputProto for RPC call
            var protoInput = BuildPromptWithStreamInputProto(promptMsg, State.ChatHistory.FromProtoList(), settings, aiChatContextDto, null);
            var result = await aiAgentStatusProxy.PromptWithStreamProtoAsync(protoInput);
            if (!result)
            {
                Logger.LogError(
                    $"[GodChatGAgent][GodVoiceStreamChatAsync] Failed to initiate voice streaming response. {Id.ToString()}");
            }

            if (!addToHistory)
            {
                totalStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] TOTAL_Time (no history) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
                return string.Empty;
            }

            var historyStopwatch = Stopwatch.StartNew();
            var userVoiceMeta = new ChatMessageMeta
            {
                IsVoiceMessage = true,
                VoiceLanguage = voiceLanguage,
                VoiceParseSuccess = true,
                VoiceParseErrorMessage = null,
                VoiceDurationSeconds = voiceDurationSeconds
            };

            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = 
                {
                    new ChatMessage
                    {
                        ChatRole = ChatRole.User,
                        Content = message
                    }.ToProto()
                }
            });
                
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { userVoiceMeta.ToProto() }
            });

            historyStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] AddToHistory - Duration: {historyStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        }
        else
        {
            Logger.LogDebug(
                $"[GodChatGAgent][GodVoiceStreamChatAsync] fallback to history agent, session {sessionId.ToString()}, chat {chatId}");
            // Fallback to non-streaming chat if no proxy available
        }

        // Voice synthesis and streaming handled in ChatMessageCallbackAsync
        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GodVoiceStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        return string.Empty;
    }

    private AIChatContextDto CreateVoiceChatContext(Guid sessionId, string llm, bool streamingModeEnabled,
        string message, string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English,
        double voiceDurationSeconds = 0.0)
    {
        var aiChatContextDto = new AIChatContextDto()
        {
            ChatId = chatId,
            RequestId = sessionId
        };
        if (isHttpRequest)
        {
            aiChatContextDto.MessageId = JsonConvert.SerializeObject(new Dictionary<string, object>()
            {
                { "IsHttpRequest", true },
                { "IsVoiceChat", true },
                { "LLM", llm },
                { "StreamingModeEnabled", streamingModeEnabled },
                { "Message", message },
                { "Region", region },
                { "VoiceLanguage", (int)voiceLanguage },
                { "VoiceDurationSeconds", voiceDurationSeconds }
            });
        }

        return aiChatContextDto;
    }
}

