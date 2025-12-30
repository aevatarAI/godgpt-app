using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Aevatar.Agents.Abstractions.Extensions;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Text chat related methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    public async Task StartStreamChatAsync(StartStreamChatInputProto input)
    {
        // Convert Protobuf input to internal types
        Guid sessionId = Guid.Parse(input.SessionId);
        string sysmLLM = input.SysmLlm; 
        string content = input.Content;
        string chatId = input.ChatId;
        ExecutionPromptSettings? promptSettings = input.PromptSettings != null 
            ? new ExecutionPromptSettings
            {
                Temperature = input.PromptSettings.HasTemperature ? input.PromptSettings.Temperature : null,
                MaxTokens = input.PromptSettings.HasMaxTokens ? input.PromptSettings.MaxTokens : null,
                TopP = input.PromptSettings.HasTopP ? input.PromptSettings.TopP : null,
                FrequencyPenalty = input.PromptSettings.HasFrequencyPenalty ? input.PromptSettings.FrequencyPenalty : null,
                PresencePenalty = input.PromptSettings.HasPresencePenalty ? input.PromptSettings.PresencePenalty : null,
                StopSequences = input.PromptSettings.StopSequences?.ToList(),
                Model = input.PromptSettings.HasModel ? input.PromptSettings.Model : null
            } 
            : null;
        bool isHttpRequest = input.IsHttpRequest;
        string? region = input.HasRegion ? input.Region : null;
        List<string>? images = input.Images?.Count > 0 ? input.Images.ToList() : null;
        // For Timestamp message types, use null check instead of Has property
        DateTime? userLocalTime = input.UserLocalTime != null ? input.UserLocalTime.ToDateTime() : null;
        string? userTimeZoneId = input.HasUserTimeZoneId ? input.UserTimeZoneId : null;
        
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId.ToString()} start. region:{region}, ChatManagerGuid:{State.ChatManagerGuid}");

        // Get language from RequestContext with error handling
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] Language from context: {language}");

        var actionType = images == null || images.IsNullOrEmpty()
            ? ActionType.Conversation
            : ActionType.ImageConversation;
        
        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} - Calling ExecuteActionAsync");
        var userQuotaGAgent = await GetUserQuotaAgentAsync(State.ChatManagerGuid);
        var actionResultDto =
            await userQuotaGAgent.ExecuteActionAsync(sessionId.ToString(), State.ChatManagerGuid.ToString(), actionType);
        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} - ExecuteActionAsync result: Success={actionResultDto.Success}, Message={actionResultDto.Message}");
        if (!actionResultDto.Success)
        {
            Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} Access restricted - pushing error response");

            //save conversation data
            await SetSessionTitleAsync(sessionId, content);
            var chatMessages = new List<ChatMessage>();
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.User,
                Content = content,
                ImageKeys = images
            });
            chatMessages.Add(new ChatMessage
            {
                ChatRole = ChatRole.Assistant,
                Content = actionResultDto.Message
            });
            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = { chatMessages.ToProtoList() }
            });
            
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { }
            });
            
            await ConfirmEventsAsync();

            //2. Directly respond with error information.
            var chatMessage = new ResponseStreamGodChat()
            {
                Response = actionResultDto.Message,
                ChatId = chatId,
                IsLastChunk = true,
                SerialNumber = -99,
                SessionId = sessionId,
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
            return;
        }

        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} - Validation passed, calling GodStreamChatAsync");
        
        await SetSessionTitleAsync(sessionId, content);
        var configuration = await GetConfigurationAsync();
        var systemLLM = await configuration.GetSystemLLMAsync();
        var streamingEnabled = await configuration.GetStreamingModeEnabledAsync();
        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} - Calling GodStreamChatAsync: systemLLM={systemLLM}, streamingEnabled={streamingEnabled}, isHttpRequest={isHttpRequest}");
        
        await GodStreamChatAsync(sessionId, systemLLM, streamingEnabled,
            content, chatId, promptSettings, isHttpRequest, region, images: images, 
            userLocalTime: userLocalTime, userTimeZoneId: userTimeZoneId);
        
        Logger.LogInformation($"[GodChatGAgent][StartStreamChatAsync] {sessionId} - GodStreamChatAsync completed");
        
        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][StartStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
    }

    [Obsolete("Use StartStreamChatAsync(StartStreamChatInputProto) for RPC calls")]
    public async Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null, 
        List<string>? images = null)
    {
        Logger.LogDebug($"[GodChatGAgent][StreamChatWithSessionAsync] start: {sessionId.ToString()}");
        await StartStreamChatAsync(new StartStreamChatInputProto
        {
            SessionId = sessionId.ToString(),
            SysmLlm = sysmLLM,
            Content = content,
            ChatId = chatId,
            // Note: promptSettings conversion would need helper method
            IsHttpRequest = isHttpRequest,
            Region = region ?? "",
        });
    }

    public async Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, bool addToHistory = true, List<string>? images = null, DateTime? userLocalTime = null,
        string? userTimeZoneId = null)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogDebug(
            $"[GodChatGAgent][GodStreamChatAsync] agent start  session {sessionId.ToString()}, chat {chatId}, region {region}");
        
        var aiChatContextDto =
            CreateAIChatContext(sessionId, llm, streamingModeEnabled, message, chatId, promptSettings, isHttpRequest,
                region, images);

        Logger.LogInformation($"[GodChatGAgent][GodStreamChatAsync] {sessionId} - Calling GetProxyByRegionAsync with region={region}");
        var (aiAgentStatusProxy, proxyId) = await GetProxyByRegionAsync(region);
        Logger.LogInformation($"[GodChatGAgent][GodStreamChatAsync] {sessionId} - GetProxyByRegionAsync result: proxyId={proxyId}, hasProxy={(aiAgentStatusProxy != null)}");

        if (aiAgentStatusProxy != null && proxyId != null)
        {
            Logger.LogInformation(
                $"[GodChatGAgent][GodStreamChatAsync] agent {proxyId}, session {sessionId.ToString()}, chat {chatId} - Will call PromptWithStreamAsync");

            // Check if this is a voice chat from context
            bool isPromptVoiceChat = false;
            if (aiChatContextDto.MessageId != null)
            {
                try
                {
                    var messageData =
                        JsonConvert.DeserializeObject<Dictionary<string, object>>(aiChatContextDto.MessageId);
                    isPromptVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "[GodChatGAgent][GodStreamChatAsync] Failed to parse MessageId for voice chat detection");
                }
            }

            // User message without additional prefixes (timestamp now in system prompt)
            string enhancedMessage = message;
            if (!isPromptVoiceChat)
            {
                var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} Language from context: {language}");
                var homeDosAndDontPromptMessage = _localizationService.GetLocalizedMessage(ExceptionMessageKeys.HomeDosAndDontPrompt,language);
                var chatPageMessageAfterSync = _localizationService.GetLocalizedMessage(ExceptionMessageKeys.ChatPageMessageAfterSync,language);
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} homeDosAndDontPromptMessage: {homeDosAndDontPromptMessage}");
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} message: {message}, {message == homeDosAndDontPromptMessage}");
                var isDailyGuide = State.PromptTemplate == DailyGuide;
                if (isDailyGuide && (message.StartsWith(homeDosAndDontPromptMessage) || message.StartsWith(chatPageMessageAfterSync)))
                {
                    enhancedMessage = await GenerateDailyRecommendationsAsync(language, userLocalTime, userTimeZoneId);
                    Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] {sessionId} enhancedMessage: {enhancedMessage}");
                }
                
                enhancedMessage = enhancedMessage + ChatPrompts.ConversationSuggestionsPrompt;
                Logger.LogDebug(
                    $"[GodChatGAgent][GodStreamChatAsync] {sessionId} Added conversation suggestions prompt for text chat");
            }

            var settings = promptSettings ?? new ExecutionPromptSettings();
            settings.Temperature = "1.0";
            
            // Build PromptWithStreamInputProto for RPC call
            var protoInput = BuildPromptWithStreamInputProto(enhancedMessage, State.ChatHistory.FromProtoList(), settings, aiChatContextDto, images);
            
            var llmStartMs = totalStopwatch.ElapsedMilliseconds;
            Logger.LogInformation("[PERF][GodStreamChatAsync] LLM_Call_Start - SessionId={SessionId}, ChatId={ChatId}, ElapsedMs={ElapsedMs}ms", 
                sessionId, chatId, llmStartMs);
            
            var result = await aiAgentStatusProxy.PromptWithStreamProtoAsync(protoInput);
            
            var llmEndMs = totalStopwatch.ElapsedMilliseconds;
            Logger.LogInformation("[PERF][GodStreamChatAsync] LLM_Call_End - SessionId={SessionId}, ChatId={ChatId}, LLMMs={LLMMs}ms, TotalElapsedMs={TotalElapsedMs}ms", 
                sessionId, chatId, llmEndMs - llmStartMs, llmEndMs);
            
            if (!result)
            {
                Logger.LogError($"Failed to initiate streaming response. {Id.ToString()}");
            }

            if (addToHistory)
            {
                var historyStopwatch = Stopwatch.StartNew();
                // Optimize: Use combined event to reduce RaiseEvent calls from 3 to 1
                RaiseEvent(new StreamChatCombinedEvent
                {
                    ChatList = 
                    {
                        new ChatMessage
                        {
                            ChatRole = ChatRole.User,
                            Content = message,
                            ImageKeys = images
                        }.ToProto()
                    },
                    ChatTime = Timestamp.FromDateTime(DateTime.UtcNow),
                    ChatMessageMetas = { }
                });

                historyStopwatch.Stop();
                Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] Combined RaiseEvent - Duration: {historyStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
            }
        }
        else
        {
            Logger.LogWarning(
                $"[GodChatGAgent][GodStreamChatAsync] AI proxy not available - session {sessionId}, chat {chatId}, region {region}");
        }

        totalStopwatch.Stop();
        Logger.LogDebug($"[GodChatGAgent][GodStreamChatAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        return string.Empty;
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        var title = "";
        if (State.Title.IsNullOrEmpty())
        {
            // Take first 4 words and limit total length to 100 characters
            title = string.Join(" ", content.Split(" ").Take(4));
            if (title.Length > 100)
            {
                title = title.Substring(0, 100);
            }

            RaiseEvent(new Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent()
            {
                Title = title
            });

            await ConfirmEventsAsync();

            var chatManagerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(State.ChatManagerGuid);
            var chatManagerGAgent = chatManagerActor.As<IChatManagerGAgent>();
            await chatManagerGAgent.RenameChatTitleAsync(new Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent()
            {
                SessionId = sessionId.ToString(),
                Title = title
            });
        }

        var configuration = await GetConfigurationAsync();
        var response = await GodChatAsync(await configuration.GetSystemLLMAsync(), content, promptSettings);
        return new Tuple<string, string>(response, title);
    }

    public async Task<ChatMessageListProto> ChatWithHistory(Guid sessionId, string systemLLM, string content,
        string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();
        var history = State.ChatHistory;
        if (history.IsNullOrEmpty())
        {
            return new ChatMessageListProto();
        }

        var configuration = await GetConfigurationAsync();
        var llm = await configuration.GetSystemLLMAsync();
        var streamingModeEnabled = await configuration.GetStreamingModeEnabledAsync();

        var (aiAgentStatusProxy, _) = await GetInitializedProxyAsync(region, sessionId);
        if (aiAgentStatusProxy == null)
        {
            Logger.LogError($"[GodChatGAgent][ChatWithHistory] No AIGAgent available. {sessionId.ToString()}");
            return new ChatMessageListProto();
        }

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "1.0";

        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId,
            promptSettings, isHttpRequest, region);
        var protoInput = BuildChatWithHistoryInputProto(content, State.ChatHistory.FromProtoList(), settings, aiChatContextDto);
        var response = await aiAgentStatusProxy.ChatWithHistoryProtoAsync(protoInput);
        sw.Stop();
        Logger.LogDebug(
            $"[GodChatGAgent][ChatWithHistory] {sessionId.ToString()}, response messages count:{response?.Messages?.Count ?? 0} - step4,time use:{sw.ElapsedMilliseconds}");
        return ConvertToChatMessageListProto(response);
    }
    
    public async Task<ChatMessageListProto> ChatWithoutHistoryAsync(Guid sessionId, string systemLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null)
    {
        Logger.LogDebug($"[GodChatGAgent][ChatWithUserId] {sessionId.ToString()} content:{content} start.");
        var sw = new Stopwatch();
        sw.Start();

        var configuration = await GetConfigurationAsync();
        var llm = await configuration.GetSystemLLMAsync();
        var streamingModeEnabled = await configuration.GetStreamingModeEnabledAsync();
        
        var (aiAgentStatusProxy2, _) = await GetInitializedProxyAsync(region, sessionId);
        if (aiAgentStatusProxy2 == null)
        {
            Logger.LogError($"[GodChatGAgent][ChatWithoutHistory] No AIGAgent available. {sessionId.ToString()}");
            return new ChatMessageListProto();
        }

        var settings = promptSettings ?? new ExecutionPromptSettings();
        settings.Temperature = "1.0";
        
        var aiChatContextDto = CreateAIChatContext(sessionId, llm, streamingModeEnabled, content, chatId, promptSettings, isHttpRequest, region);
        var protoInput = BuildChatWithHistoryInputProto(content, State.ChatHistory.FromProtoList(), settings, aiChatContextDto);
        var response = await aiAgentStatusProxy2.ChatWithHistoryProtoAsync(protoInput);
        sw.Stop();
        Logger.LogDebug($"[GodChatGAgent][ChatWithoutHistory] {sessionId.ToString()}, response messages count:{response?.Messages?.Count ?? 0} - step4,time use:{sw.ElapsedMilliseconds}");
        return ConvertToChatMessageListProto(response);
    }

    public async Task<string> GodChatAsync(string llm, string message,
        ExecutionPromptSettings? promptSettings = null)
    {
        throw new Exception("The method has expired");
    }
}

