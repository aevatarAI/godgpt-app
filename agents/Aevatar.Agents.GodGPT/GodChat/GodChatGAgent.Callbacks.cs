using System.Text;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.Application.Grains.UserProfile;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Abstractions.Attributes;
using Orleans.Streams;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents; // For EventEnvelope

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Callback handling methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    /// <summary>
    /// Event-driven callback handler for AI stream responses.
    /// Receives ChatMessageCallbackEvent from AIAgentStatusProxy via Stream (EventDirection.Up).
    /// This avoids deadlock that would occur with direct RPC callback.
    /// </summary>
    [EventHandler]
    public async Task HandleChatMessageCallbackEvent(ChatMessageCallbackEvent evt)
    {
        // Delegate to existing implementation
        await ChatMessageCallbackAsync(
            evt.Context,
            (AIExceptionEnum)evt.AiExceptionEnum,
            evt.HasErrorMessage ? evt.ErrorMessage : null,
            evt.Content);
    }
    
    public async Task ChatMessageCallbackAsync(AIChatContextProto? contextProto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage, AIStreamChatContentProto? chatContentProto)
    {
        // Convert Proto to DTO for internal use
        var contextDto = new AIChatContextDto();
        if (contextProto != null)
        {
            contextDto = new AIChatContextDto
            {
                AgentId = contextProto.HasAgentId ? contextProto.AgentId : null,
                SessionId = contextProto.HasSessionId ? contextProto.SessionId : null,
                UserId = contextProto.HasUserId ? contextProto.UserId : null,
                Metadata = contextProto.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SystemPrompt = contextProto.HasSystemPrompt ? contextProto.SystemPrompt : null,
                RequestId = Guid.TryParse(contextProto.RequestId, out var reqId) ? reqId : Guid.NewGuid(),
                ChatId = contextProto.HasChatId ? contextProto.ChatId : null,
                MessageId = contextProto.HasMessageId ? contextProto.MessageId : null
            };
        }
        
        // Convert AIStreamChatContentProto to AIStreamChatContent for internal use
        AIStreamChatContent? chatContent = null;
        if (chatContentProto != null)
        {
            chatContent = new AIStreamChatContent
            {
                Content = chatContentProto.Content,
                IsComplete = chatContentProto.IsComplete,
                TokenCount = chatContentProto.TokenCount,
                Error = chatContentProto.HasError ? chatContentProto.Error : null,
                IsLastChunk = chatContentProto.IsLastChunk,
                ResponseContent = chatContentProto.HasResponseContent ? chatContentProto.ResponseContent : null,
                AggregationMsg = chatContentProto.HasAggregationMsg ? chatContentProto.AggregationMsg : null,
                SerialNumber = chatContentProto.SerialNumber,
                IsAggregationMsg = chatContentProto.IsAggregationMsg
            };
        }
        
        if (aiExceptionEnum == AIExceptionEnum.RequestLimitError && !contextDto.MessageId.IsNullOrWhiteSpace())
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] RequestLimitError retry. contextDto {JsonConvert.SerializeObject(contextDto)}");
            var configuration = await GetConfigurationAsync();
            var systemLlm = await configuration.GetSystemLLMAsync();
            var dictionary = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            
            // Check if this is a voice chat retry to call the appropriate method
            var isRetryVoiceChat = dictionary.ContainsKey("IsVoiceChat") && (bool)dictionary["IsVoiceChat"];

            if (isRetryVoiceChat)
            {
                // Voice chat retry: call GodVoiceStreamChatAsync with voice parameters
                var voiceLanguageValue = dictionary.GetValueOrDefault("VoiceLanguage", 0);
                var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);
                var voiceDurationSeconds = Convert.ToDouble(dictionary.GetValueOrDefault("VoiceDurationSeconds", 0.0));

                GodVoiceStreamChatAsync(contextDto.RequestId,
                    (string)dictionary.GetValueOrDefault("LLM", systemLlm),
                    (bool)dictionary.GetValueOrDefault("StreamingModeEnabled", true),
                    (string)dictionary.GetValueOrDefault("Message", string.Empty),
                    contextDto.ChatId, null, (bool)dictionary.GetValueOrDefault("IsHttpRequest", true),
                    (string)dictionary.GetValueOrDefault("Region", null),
                    voiceLanguage, voiceDurationSeconds, false);
            }
            else
            {
                // Regular chat retry: call GodStreamChatAsync
                GodStreamChatAsync(contextDto.RequestId,
                    (string)dictionary.GetValueOrDefault("LLM", systemLlm),
                    (bool)dictionary.GetValueOrDefault("StreamingModeEnabled", true),
                    (string)dictionary.GetValueOrDefault("Message", string.Empty),
                    contextDto.ChatId, null, (bool)dictionary.GetValueOrDefault("IsHttpRequest", true),
                    (string)dictionary.GetValueOrDefault("Region", null),
                    false, (List<string>?)dictionary.GetValueOrDefault("Images"));
            }
            
            return;
        }

        if (aiExceptionEnum != AIExceptionEnum.None)
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] DETAILED ERROR - sessionId {contextDto?.RequestId.ToString()}, chatId {contextDto?.ChatId}, aiExceptionEnum: {aiExceptionEnum}, errorMessage: '{errorMessage}', MessageId: '{contextDto?.MessageId}'");
            
            // Extract voice chat info if available
            string voiceChatInfo = "";
            if (!contextDto.MessageId.IsNullOrWhiteSpace())
            {
                try
                {
                    var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                    bool isErrorVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                    if (isErrorVoiceChat)
                    {
                        // Safe type conversion for voice language
                        var voiceLanguageValue = messageData.GetValueOrDefault("VoiceLanguage", 0);
                        var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);
                        var message = messageData.GetValueOrDefault("Message", "").ToString();
                        voiceChatInfo = $" [VOICE CHAT] Language: {voiceLanguage}, Message: '{message}'";
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Failed to parse MessageId for voice chat info");
                }
            }

            Logger.LogError($"[GodChatGAgent][ChatMessageCallbackAsync] ERROR CONTEXT:{voiceChatInfo}");

            var chatMessage = new ResponseStreamGodChat()
            {
                Response =
                    "Your prompt triggered the Silence Directive—activated when universal harmonics or content ethics are at risk. Please modify your prompt and retry — tune its intent, refine its form, and the Oracle may speak.",
                ChatId = contextDto.ChatId,
                IsLastChunk = true,
                SerialNumber = -2
            };
            
            // Check if this is an HTTP request
            bool isErrorHttpRequest = false;
            if (!contextDto.MessageId.IsNullOrWhiteSpace())
            {
                try
                {
                    var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                    isErrorHttpRequest = messageData != null && 
                                        messageData.ContainsKey("IsHttpRequest") && 
                                        (bool)messageData["IsHttpRequest"];
                }
                catch { /* ignore parse errors */ }
            }
            
            if (isErrorHttpRequest)
            {
                await PushMessageToClientAsync(chatMessage);
            }
            else
            {
                await PublishAsync(chatMessage.ToProto());
            }
            return;
        }

        if (chatContent == null)
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] return null. sessionId {contextDto.RequestId.ToString()},chatId {contextDto.ChatId},aiExceptionEnum:{aiExceptionEnum}, errorMessage:{errorMessage}");
            return;
        }

        Logger.LogDebug(
            $"[GodChatGAgent][ChatMessageCallbackAsync] sessionId {contextDto.RequestId.ToString()}, chatId {contextDto.ChatId}, messageId {contextDto.MessageId}, {JsonConvert.SerializeObject(chatContent)}");

        if (chatContent.IsAggregationMsg)
        {
            // Parse conversation suggestions for text chat only (skip voice chat)
            List<string>? conversationSuggestions = null;
            string cleanMainContent = chatContent.AggregationMsg; // Default to original content
            bool isAggregationVoiceChat = false;

            // Check if this is a voice chat by examining the message context
            if (!contextDto.MessageId.IsNullOrWhiteSpace())
            {
                try
                {
                    var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                    isAggregationVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex,
                        "[GodChatGAgent][ChatMessageCallbackAsync] Failed to parse MessageId for voice chat detection");
                }
            }

            // Parse conversation suggestions only for text chat
            if (!isAggregationVoiceChat && !string.IsNullOrEmpty(chatContent.AggregationMsg))
            {
                var suggestionParseResult = SuggestionParser.ParseResponseWithSuggestions(chatContent.AggregationMsg);
                if (suggestionParseResult.Suggestions?.Any() == true)
                {
                    conversationSuggestions = suggestionParseResult.Suggestions;
                    cleanMainContent = suggestionParseResult.MainContent; // Use clean content without suggestions
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Parsed {suggestionParseResult.Suggestions.Count} conversation suggestions for text chat");
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Cleaned main content length: {cleanMainContent?.Length ?? 0}");
                }
            }

            RaiseEvent(new AddChatMessagesEvent
            {
                Messages = 
                {
                    new ChatMessage
                    {
                        ChatRole = ChatRole.Assistant,
                        Content = cleanMainContent // Store clean content without suggestions
                    }.ToProto()
                }
            });

            RaiseEvent(new UpdateChatTimeEvent
            {
                ChatTime = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            
            RaiseEvent(new AddChatMessageMetasEvent
            {
                ChatMessageMetas = { }
            });

            await ConfirmEventsAsync();

            if (!string.IsNullOrEmpty(State.ChatManagerGuid))
            {
                var userInvitationActor = await _actorFactory.CreateGAgentActorAsync<UserInvitationGAgent>(State.ChatManagerGuid);
                var userInvitationGAgent = userInvitationActor.As<IUserInvitationGAgent>();
                var inviterId = await userInvitationGAgent.GetInviterAsync();
                if (inviterId != null && inviterId != Guid.Empty)
                {
                    var invitationGAgent = await GetInvitationAgentAsync(inviterId.Value.ToString());
                    await invitationGAgent.ProcessInviteeChatCompletionAsync(State.ChatManagerGuid.ToString());
                }
            }

            // Store suggestions and clean content for later use in partialMessage
            if (conversationSuggestions != null)
            {
                Context?.Set(GodGPTContextKeys.ConversationSuggestions, conversationSuggestions);
            }

            // Store clean content to replace the response content
            Context?.Set(GodGPTContextKeys.CleanMainContent, cleanMainContent);
        }

        // Apply streaming suggestion filtering logic for text chat
        // Use Content field (consistent with original implementation)
        string streamingContent = chatContent.Content ?? "";
        bool shouldFilterStream = false;
        
        // 🔍 DEBUG: Log original Content
        Logger.LogDebug(
            $"[ChatMessageCallbackAsync][DEBUG] Original Content - Length: {chatContent.Content?.Length ?? 0}, " +
            $"Content: '{chatContent.Content?.Substring(0, Math.Min(100, chatContent.Content?.Length ?? 0)) ?? ""}'{(chatContent.Content?.Length > 100 ? "..." : "")}', " +
            $"IsLastChunk: {chatContent.IsLastChunk}, SerialNumber: {chatContent.SerialNumber}");

        // Check if this is a text chat (not voice chat)
        bool isFilteringVoiceChat = false;
        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            try
            {
                var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                isFilteringVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "[GodChatGAgent][ChatMessageCallbackAsync] Failed to parse MessageId for voice chat detection in streaming filter");
            }
        }

        // Get current accumulation state from instance variables (reliable across chunks)
        bool shouldStartAccumulating = false;

        // Apply conversation suggestions filtering (text chat only)
        if (!isFilteringVoiceChat && !string.IsNullOrEmpty(streamingContent))
        {
            // Check for [SUGGESTIONS] marker and partial forms using optimized method
            bool contains_suggestions = streamingContent.Contains("[SUGGESTIONS]", StringComparison.OrdinalIgnoreCase);
            bool contains_partial_marker = SuggestionParser.IsPartialSuggestionsMarker(streamingContent.AsSpan());

            // Check for potential marker start (conservative approach)
            bool ends_with_bracket = streamingContent.TrimEnd().EndsWith("[") && streamingContent.Length > 10;

            shouldStartAccumulating = contains_suggestions || contains_partial_marker || ends_with_bracket;
            
            // 🔍 DEBUG: Log suggestion detection
            Logger.LogDebug(
                $"[ChatMessageCallbackAsync][DEBUG] Suggestion Detection - contains_suggestions: {contains_suggestions}, " +
                $"contains_partial_marker: {contains_partial_marker}, ends_with_bracket: {ends_with_bracket}, " +
                $"shouldStartAccumulating: {shouldStartAccumulating}, _isAccumulatingForSuggestions: {_isAccumulatingForSuggestions}");

            if (shouldStartAccumulating && !_isAccumulatingForSuggestions)
            {
                // Start accumulation - block all subsequent chunks from frontend
                _isAccumulatingForSuggestions = true;
                _accumulatedSuggestionContent = streamingContent;
                Context?.Set(GodGPTContextKeys.AccumulatedContent, true); // Set accumulation flag
                streamingContent = ""; // Block current chunk
                
                // 🔍 DEBUG: Log accumulation start
                Logger.LogDebug(
                    $"[ChatMessageCallbackAsync][DEBUG] Started accumulating - Original length: {_accumulatedSuggestionContent.Length}, " +
                    $"streamingContent cleared, SerialNumber: {chatContent.SerialNumber}");
            }
            else if (_isAccumulatingForSuggestions)
            {
                // Continue accumulation - block this chunk from frontend
                var beforeLength = _accumulatedSuggestionContent.Length;
                _accumulatedSuggestionContent += streamingContent;
                streamingContent = ""; // Block current chunk
                
                // 🔍 DEBUG: Log accumulation continue
                Logger.LogDebug(
                    $"[ChatMessageCallbackAsync][DEBUG] Continuing accumulation - Added length: {streamingContent?.Length ?? 0}, " +
                    $"Total accumulated: {_accumulatedSuggestionContent.Length}, SerialNumber: {chatContent.SerialNumber}");
            }
        }

        // Process accumulated content on final chunk
        if (_isAccumulatingForSuggestions && chatContent.IsLastChunk)
        {
            // 🔍 DEBUG: Log before parsing
            Logger.LogDebug(
                $"[ChatMessageCallbackAsync][DEBUG] Processing final chunk - Accumulated length: {_accumulatedSuggestionContent.Length}, " +
                $"Accumulated preview: '{_accumulatedSuggestionContent.Substring(0, Math.Min(200, _accumulatedSuggestionContent.Length))}{( _accumulatedSuggestionContent.Length > 200 ? "..." : "")}'");
            
            var suggestionParseResult = SuggestionParser.ParseResponseWithSuggestions(_accumulatedSuggestionContent);

            // Store suggestions for response
            if (suggestionParseResult.Suggestions?.Any() == true)
            {
                Context?.Set(GodGPTContextKeys.ConversationSuggestions, suggestionParseResult.Suggestions);
                Logger.LogDebug($"[ChatMessageCallbackAsync][DEBUG] Parsed {suggestionParseResult.Suggestions.Count} suggestions");
            }

            // Send clean content to frontend
            streamingContent = suggestionParseResult.MainContent;
            
            // 🔍 DEBUG: Log after parsing
            Logger.LogDebug(
                $"[ChatMessageCallbackAsync][DEBUG] Restored streamingContent - Length: {streamingContent?.Length ?? 0}, " +
                $"Content: '{streamingContent?.Substring(0, Math.Min(100, streamingContent?.Length ?? 0)) ?? ""}{(streamingContent?.Length > 100 ? "..." : "")}'");

            // Reset accumulation state
            _isAccumulatingForSuggestions = false;
            _accumulatedSuggestionContent = "";
            Context?.Remove("AccumulatedContent"); // Clean up accumulation flag
        }
        else if (!_isAccumulatingForSuggestions && !string.IsNullOrEmpty(streamingContent))
        {
            // 🔍 DEBUG: Log normal flow (no accumulation)
            Logger.LogDebug(
                $"[ChatMessageCallbackAsync][DEBUG] Normal flow (no accumulation) - streamingContent length: {streamingContent.Length}, " +
                $"Content: '{streamingContent.Substring(0, Math.Min(100, streamingContent.Length))}{(streamingContent.Length > 100 ? "..." : "")}', " +
                $"IsLastChunk: {chatContent.IsLastChunk}, SerialNumber: {chatContent.SerialNumber}");
        }
        else if (_isAccumulatingForSuggestions && !chatContent.IsLastChunk)
        {
            // 🔍 DEBUG: Log accumulation in progress (not last chunk)
            Logger.LogDebug(
                $"[ChatMessageCallbackAsync][DEBUG] Accumulation in progress (not last chunk) - streamingContent cleared, " +
                $"Accumulated so far: {_accumulatedSuggestionContent.Length}, SerialNumber: {chatContent.SerialNumber}");
        }

        var partialMessage = new ResponseStreamGodChat()
        {
            Response = streamingContent, // Use filtered content for streaming
            ChatId = contextDto.ChatId,
            SerialNumber = chatContent.SerialNumber,
            IsLastChunk = chatContent.IsLastChunk,
            SessionId = contextDto.RequestId,
            // Note: Default to VoiceResponse in this version as VoiceToText is not implemented yet
            VoiceContentType = VoiceContentType.VoiceResponse
        };

        // 🔍 DEBUG: Log final message before sending
        Logger.LogDebug(
            $"[ChatMessageCallbackAsync][DEBUG] Final message - Response length: {partialMessage.Response?.Length ?? 0}, " +
            $"ChatId: {partialMessage.ChatId}, SerialNumber: {partialMessage.SerialNumber}, " +
            $"IsLastChunk: {partialMessage.IsLastChunk}, IsAccumulating: {_isAccumulatingForSuggestions}");
        
        // Log final content being sent to frontend
        Logger.LogInformation(
            $"[FINAL_OUTPUT] Sending to frontend - Length: {streamingContent?.Length ?? 0}, IsLastChunk: {chatContent.IsLastChunk}");
        if (!string.IsNullOrEmpty(streamingContent))
        {
            Logger.LogInformation(
                $"[FINAL_OUTPUT] Content preview: '{streamingContent.Substring(0, Math.Min(100, streamingContent.Length))}{(streamingContent.Length > 100 ? "..." : "")}'");
        }
        else
        {
            // 🔍 DEBUG: Log when Response is empty
            Logger.LogWarning(
                $"[ChatMessageCallbackAsync][DEBUG] ⚠️ Response is EMPTY - SerialNumber: {chatContent.SerialNumber}, " +
                $"IsLastChunk: {chatContent.IsLastChunk}, IsAccumulating: {_isAccumulatingForSuggestions}, " +
                $"Original Content length: {chatContent.Content?.Length ?? 0}, streamingContent length: {streamingContent?.Length ?? 0}");
        }

        // For the last chunk, use clean content and add conversation suggestions if available
        if (chatContent.IsLastChunk)
        {
            // Prioritize accumulation mechanism to prevent duplication
            if (_isAccumulatingForSuggestions)
            {
                Logger.LogDebug($"Using accumulation result, skipping old replacement logic");
                // streamingContent already contains the correct clean content from accumulation
                // Skip all old replacement logic to prevent duplication
            }
            else
            {
                // Add conversation suggestions to the last chunk if available
                var storedSuggestions = Context?.Get(GodGPTContextKeys.ConversationSuggestions);
                if (storedSuggestions?.Any() == true)
                {
                    partialMessage.SuggestedItems = storedSuggestions;
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Added {storedSuggestions.Count} suggestions to last chunk");

                    var cleanMainContent = Context?.Get(GodGPTContextKeys.CleanMainContent);
                    if (!string.IsNullOrEmpty(cleanMainContent))
                    {
                        // Enhanced safety check to prevent duplication
                        var currentChunkLength = partialMessage.Response?.Length ?? 0;
                        var cleanContentLength = cleanMainContent.Length;

                        // Stricter conditions to prevent duplication
                        bool isSafeToReplace = currentChunkLength > 0 && // Never replace empty chunks
                                               cleanContentLength <= currentChunkLength * 1.5 && // Reduced ratio
                                               cleanContentLength <= 30 && // Strict absolute limit
                                               !cleanMainContent.Contains(partialMessage.Response ??
                                                                          "") && // No content overlap
                                               cleanContentLength <
                                               (partialMessage.Response?.Length ?? 0) + 20; // Size similarity check

                        if (isSafeToReplace)
                        {
                            partialMessage.Response = cleanMainContent;
                            Logger.LogDebug(
                                $"Replaced response with clean content. Current: {currentChunkLength}, Clean: {cleanContentLength}");
                        }
                        else
                        {
                            Logger.LogWarning(
                                $"Skipped replacing response to avoid duplication. Current: {currentChunkLength}, Clean: {cleanContentLength}");
                        }
                    }
                }
            }
        }

        // Check if this is a voice chat and handle real-time voice synthesis
        Logger.LogDebug(
            $"[ChatMessageCallbackAsync] MessageId: '{contextDto.MessageId}', ResponseContent: '{chatContent.ResponseContent}'");

        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
            var isStreamingVoiceChat = messageData.ContainsKey("IsVoiceChat") && (bool)messageData["IsVoiceChat"];

            Logger.LogDebug(
                $"[ChatMessageCallbackAsync] IsVoiceChat: {isStreamingVoiceChat}, HasResponseContent: {!string.IsNullOrEmpty(chatContent.ResponseContent)}");

            if (isStreamingVoiceChat && !string.IsNullOrEmpty(chatContent.ResponseContent))
            {
                Logger.LogDebug($"[ChatMessageCallbackAsync] Entering voice chat processing logic");

                // Safe type conversion to handle both int and long from JSON deserialization
                var voiceLanguageValue = messageData.GetValueOrDefault("VoiceLanguage", 0);
                var voiceLanguage = (VoiceLanguageEnum)Convert.ToInt32(voiceLanguageValue);

                Logger.LogDebug(
                    $"[ChatMessageCallbackAsync] VoiceLanguage: {voiceLanguage}, ChatId: {contextDto.ChatId}");

                // Get or create text accumulator for this chat session
                if (!VoiceTextAccumulators.ContainsKey(contextDto.ChatId))
                {
                    VoiceTextAccumulators[contextDto.ChatId] = new StringBuilder();
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Created new text accumulator for chat: {contextDto.ChatId}");
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Using existing text accumulator for chat: {contextDto.ChatId}");
                }

                var textAccumulator = VoiceTextAccumulators[contextDto.ChatId];

                // Filter out empty or whitespace-only content to avoid unnecessary accumulation
                if (!string.IsNullOrWhiteSpace(chatContent.ResponseContent))
                {
                    textAccumulator.Append(chatContent.ResponseContent);
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Appended text: '{chatContent.ResponseContent}', IsLastChunk: {chatContent.IsLastChunk}");
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] Skipped whitespace content, IsLastChunk: {chatContent.IsLastChunk}");
                }

                // Check for complete sentences in accumulated text
                var accumulatedText = textAccumulator.ToString();
                Logger.LogDebug($"[ChatMessageCallbackAsync] Total accumulated text: '{accumulatedText}'");

                var completeSentence =
                    TextProcessingUtils.ExtractCompleteSentence(accumulatedText, textAccumulator, chatContent.IsLastChunk);

                Logger.LogDebug($"[ChatMessageCallbackAsync] ExtractCompleteSentence result: '{completeSentence}'");

                if (!string.IsNullOrEmpty(completeSentence))
                {
                    try
                    {
                        // Clean text for speech synthesis (remove markdown and math formulas)
                        var cleanedText = TextProcessingUtils.CleanTextForSpeech(completeSentence, voiceLanguage);

                        // Skip synthesis if cleaned text has no meaningful content
                        var hasMeaningful = TextProcessingUtils.HasMeaningfulContent(cleanedText);

                        if (hasMeaningful)
                        {
                            try
                            {
                                // Synthesize voice for cleaned sentence
                                var voiceResult =
                                    await _speechService.TextToSpeechWithMetadataAsync(cleanedText, voiceLanguage);

                                partialMessage.AudioData = voiceResult.AudioData;
                                partialMessage.AudioMetadata = voiceResult.Metadata;
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, $"Voice synthesis failed for text: '{cleanedText}'");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex,
                            $"[GodChatGAgent][ChatMessageCallbackAsync] Voice synthesis failed for sentence: {completeSentence}");
                    }
                }
                else
                {
                    Logger.LogDebug(
                        $"[ChatMessageCallbackAsync] No complete sentence extracted from accumulated text: '{textAccumulator.ToString()}'");
                }

                // Clean up accumulator if this is the last chunk
                if (chatContent.IsLastChunk)
                {
                    // Clean up accumulator for this chat session
                    // Note: Final sentence processing is already handled in ExtractCompleteSentence method
                    VoiceTextAccumulators.Remove(contextDto.ChatId);
                }
            }
        }

        // Determine if this is an HTTP request by checking MessageId (contains IsHttpRequest flag)
        // MessageId is a JSON string like: {"IsHttpRequest":true, "LLM":"...", ...}
        bool isHttpRequest = false;
        if (!contextDto.MessageId.IsNullOrWhiteSpace())
        {
            try
            {
                var messageData = JsonConvert.DeserializeObject<Dictionary<string, object>>(contextDto.MessageId);
                isHttpRequest = messageData != null && 
                               messageData.ContainsKey("IsHttpRequest") && 
                               (bool)messageData["IsHttpRequest"];
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[ChatMessageCallbackAsync] Failed to parse MessageId for IsHttpRequest check");
            }
        }
        
        // TTFT logging: Log time from request start to first message
        if (chatContent.SerialNumber == 0)
        {
            Logger.LogInformation("[PERF][ChatMessageCallbackAsync] TTFT (First Token) - ChatId={ChatId}, IsHttpRequest={IsHttpRequest}", 
                partialMessage.ChatId, isHttpRequest);
        }
        
        var sendSw = System.Diagnostics.Stopwatch.StartNew();
        if (isHttpRequest)
        {
            // HTTP request: send to client via MassTransit Kafka
            await PushMessageToClientAsync(partialMessage);
            sendSw.Stop();
            Logger.LogInformation("[PERF][ChatMessageCallbackAsync] PushMessageToClient - ChatId={ChatId}, SerialNumber={SerialNumber}, SendMs={SendMs}ms, IsLastChunk={IsLastChunk}", 
                partialMessage.ChatId, chatContent.SerialNumber, sendSw.ElapsedMilliseconds, partialMessage.IsLastChunk);
        }
        else
        {
            // Internal agent communication: publish to downstream agents
            await PublishAsync(partialMessage.ToProto());
            sendSw.Stop();
            Logger.LogDebug("[ChatMessageCallbackAsync] PublishAsync - ChatId={ChatId}, SerialNumber={SerialNumber}, SendMs={SendMs}ms", 
                partialMessage.ChatId, chatContent.SerialNumber, sendSw.ElapsedMilliseconds);
        }

        // Clean up agent context when processing is complete (last chunk)
        if (chatContent.IsLastChunk)
        {
            Context?.Remove("CleanMainContent");
            Context?.Remove("ConversationSuggestions");
            Context?.Remove("IsFilteringSuggestions");
            Context?.Remove("IsAccumulatingForSuggestions");
            Context?.Remove("AccumulatedContent");
            Logger.LogDebug(
                $"[GodChatGAgent][ChatMessageCallbackAsync] Cleaned up agent context for completed request");
        }
    }

    private async Task PushMessageToClientAsync(ResponseStreamGodChat chatMessage)
    {
        // Use session ID (Guid) for stream identification
        // The Id field in new framework is string format like "GodChat:sessionGuid"
        // Extract the actual session GUID for stream routing
        Guid sessionGuid;
        if (Id.Contains(':'))
        {
            var parts = Id.Split(':');
            if (parts.Length >= 2 && Guid.TryParse(parts[1], out var parsed))
            {
                sessionGuid = parsed;
            }
            else
            {
                Logger.LogError($"[GodChatGAgent][PushMessageToClientAsync] Cannot parse session GUID from Id: {Id}");
                throw new InvalidOperationException($"Cannot parse session GUID from Id: {Id}");
            }
        }
        else if (Guid.TryParse(Id, out var directParsed))
        {
            sessionGuid = directParsed;
        }
        else
        {
            Logger.LogError($"[GodChatGAgent][PushMessageToClientAsync] Cannot parse session GUID from Id: {Id}");
            throw new InvalidOperationException($"Cannot parse session GUID from Id: {Id}");
        }
        
        var streamId = sessionGuid.ToString();
        Logger.LogDebug(
            $"[GodChatGAgent][PushMessageToClientAsync] Publishing to StreamId='{streamId}', sessionGuid={sessionGuid}, Id={Id}");
        
        // Use MassTransit Stream via IMessageStreamProvider (no Orleans Stream fallback)
        if (ServiceProvider == null)
        {
            throw new InvalidOperationException("[PushMessageToClientAsync] ServiceProvider is null. MassTransit Stream requires ServiceProvider.");
        }
        
        var messageStreamProvider = ServiceProvider.GetService<IMessageStreamProvider>();
        if (messageStreamProvider == null)
        {
            throw new InvalidOperationException("[PushMessageToClientAsync] IMessageStreamProvider not found. MassTransit Stream is required.");
        }
        
        // Create MassTransit stream with category "GodChat" (maps to "godgpt-chat-responses" topic)
        var stream = messageStreamProvider.GetStream(streamId, "GodChat");
        
        // Wrap ResponseStreamGodChatProto in EventEnvelope
        var proto = chatMessage.ToProto();
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Version = 0,
            Payload = Any.Pack(proto)
        };
        
        await stream.ProduceAsync(envelope);
        Logger.LogInformation($"[GodChatGAgent][PushMessageToClientAsync] Successfully pushed message to MassTransit stream, StreamId={streamId}");
    }
}

