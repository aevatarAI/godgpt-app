using System.Text;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.Application.Grains.UserProfile;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Callback handling methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    public async Task ChatMessageCallbackAsync(AIChatContextDto contextDto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage, AIStreamChatContent? chatContent)
    {
        if (aiExceptionEnum == AIExceptionEnum.RequestLimitError && !contextDto.MessageId.IsNullOrWhiteSpace())
        {
            Logger.LogError(
                $"[GodChatGAgent][ChatMessageCallbackAsync] RequestLimitError retry. contextDto {JsonConvert.SerializeObject(contextDto)}");
            var configuration = await GetConfigurationAsync();
            var systemLlm = configuration.GetSystemLLM();
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
            if (contextDto.MessageId.IsNullOrWhiteSpace())
            {
                await PublishAsync(chatMessage.ToProto());
                return;
            }

            await PushMessageToClientAsync(chatMessage);
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
                var userInvitationActor = await _actorFactory.CreateGAgentActorAsync<UserInvitationGAgent>(Guid.Parse(State.ChatManagerGuid));
                var userInvitationGAgent = (IUserInvitationGAgent)userInvitationActor.GetAgent();
                var inviterId = await userInvitationGAgent.GetInviterAsync();
                if (inviterId != null && inviterId != Guid.Empty)
                {
                    var invitationGAgent = await GetInvitationAgentAsync((Guid)inviterId);
                    await invitationGAgent.ProcessInviteeChatCompletionAsync(State.ChatManagerGuid.ToString());
                }
            }

            // Store suggestions and clean content for later use in partialMessage
            if (conversationSuggestions != null)
            {
                RequestContext.Set("ConversationSuggestions", conversationSuggestions);
            }

            // Store clean content to replace the response content
            RequestContext.Set("CleanMainContent", cleanMainContent);
        }

        // Apply streaming suggestion filtering logic for text chat
        string streamingContent = chatContent.ResponseContent;
        bool shouldFilterStream = false;

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

            if (shouldStartAccumulating && !_isAccumulatingForSuggestions)
            {
                // Start accumulation - block all subsequent chunks from frontend
                _isAccumulatingForSuggestions = true;
                _accumulatedSuggestionContent = streamingContent;
                RequestContext.Set("AccumulatedContent", true); // Set accumulation flag
                streamingContent = ""; // Block current chunk
            }
            else if (_isAccumulatingForSuggestions)
            {
                // Continue accumulation - block this chunk from frontend
                _accumulatedSuggestionContent += streamingContent;
                streamingContent = ""; // Block current chunk
            }
        }

        // Process accumulated content on final chunk
        if (_isAccumulatingForSuggestions && chatContent.IsLastChunk)
        {
            var suggestionParseResult = SuggestionParser.ParseResponseWithSuggestions(_accumulatedSuggestionContent);

            // Store suggestions for response
            if (suggestionParseResult.Suggestions?.Any() == true)
            {
                RequestContext.Set("ConversationSuggestions", suggestionParseResult.Suggestions);
            }

            // Send clean content to frontend
            streamingContent = suggestionParseResult.MainContent;

            // Reset accumulation state
            _isAccumulatingForSuggestions = false;
            _accumulatedSuggestionContent = "";
            RequestContext.Remove("AccumulatedContent"); // Clean up accumulation flag
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

        // Log final content being sent to frontend
        Logger.LogInformation(
            $"[FINAL_OUTPUT] Sending to frontend - Length: {streamingContent?.Length ?? 0}, IsLastChunk: {chatContent.IsLastChunk}");
        if (!string.IsNullOrEmpty(streamingContent))
        {
            Logger.LogInformation(
                $"[FINAL_OUTPUT] Content preview: '{streamingContent.Substring(0, Math.Min(100, streamingContent.Length))}{(streamingContent.Length > 100 ? "..." : "")}'");
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
                var storedSuggestions = RequestContext.Get("ConversationSuggestions") as List<string>;
                if (storedSuggestions?.Any() == true)
                {
                    partialMessage.SuggestedItems = storedSuggestions;
                    Logger.LogDebug(
                        $"[GodChatGAgent][ChatMessageCallbackAsync] Added {storedSuggestions.Count} suggestions to last chunk");

                    var cleanMainContent = RequestContext.Get("CleanMainContent") as string;
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

        if (contextDto.MessageId.IsNullOrWhiteSpace())
        {
            await PublishAsync(partialMessage.ToProto());
        }
        else
        {
            await PushMessageToClientAsync(partialMessage);
        }

        // Clean up RequestContext when processing is complete (last chunk)
        if (chatContent.IsLastChunk)
        {
            RequestContext.Remove("CleanMainContent");
            RequestContext.Remove("ConversationSuggestions");
            RequestContext.Remove("IsFilteringSuggestions");
            RequestContext.Remove("IsAccumulatingForSuggestions");
            RequestContext.Remove("AccumulatedContent");
            Logger.LogDebug(
                $"[GodChatGAgent][ChatMessageCallbackAsync] Cleaned up RequestContext for completed request");
        }
    }

    private async Task PushMessageToClientAsync(ResponseStreamGodChat chatMessage)
    {
        var streamId = StreamId.Create(StreamNamespace, Id);
        Logger.LogDebug(
            $"[GodChatGAgent][PushMessageToClientAsync] sessionId {Id.ToString()}, namespace {StreamNamespace}, streamId {streamId.ToString()}");
        // TODO: [CLIENT_STREAM] New framework doesn't inherit Grain, need alternative for client push
        // Original: var streamProvider = this.GetStreamProvider(StreamProviderName);
        // var stream = streamProvider.GetStream<ResponseStreamGodChat>(streamId);
        // await stream.OnNextAsync(chatMessage);
        // For now, use PublishAsync which broadcasts to child agents
        await PublishAsync(chatMessage.ToProto());
    }
}

