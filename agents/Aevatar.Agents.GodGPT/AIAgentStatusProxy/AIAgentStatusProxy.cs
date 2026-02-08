using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Agents.GodGPT.Protos.GodChatVoice;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using GodGPT.GAgents.SpeechChat;
// GodChatStream: Contains GodChatStreamEnvelopeProto, ControlProto, TextChunkProto, etc.
using Aevatar.Agents.GodGPT.Protos.GodChatStream;
// GodChat namespace: Contains StreamingSuggestionsFilter, SuggestionParser
using Aevatar.Application.Grains.GodChat;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;
using Orleans;
using Aevatar.Agents.Runtime.Orleans;
using System.Diagnostics;
using Volo.Abp.BlobStoring;
using ChatMessage = Aevatar.GAgents.AI.Abstractions.ChatMessage;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;

/// <summary>
/// AI Agent Status Proxy - Migrated to new Aevatar.Agents.AI framework.
/// Manages AI agent availability status and proxies chat requests.
/// </summary>
[Reentrant]
public class AIAgentStatusProxy : 
    AIGAgentBase<AIAgentStatusProxyStateProto, AIAgentStatusProxyConfigProto>,
    IAIAgentStatusProxy
{
    // Injected by OrleansGAgentGrain via reflection
    public IGAgentActorFactory? ActorFactory { get; set; }
    
    // Injected by OrleansGAgentGrain via InjectServiceProviderProperty for direct Kafka push
    // NOTE: Must be non-nullable IServiceProvider to match InjectServiceProviderProperty type check
    public IServiceProvider ServiceProvider { get; set; } = null!;
    
    // Message aggregation to reduce Kafka message count
    // Aggregates multiple LLM tokens into fewer SendToAsync calls
    private const int AggregationIntervalMs = 150; // Send every 150ms
    private const int MaxAggregatedTokens = 15;    // Or every 15 tokens
    
    // IMPORTANT: Do NOT store per-request data in fields.
    // This grain is [Reentrant]; concurrent requests could corrupt shared fields and break stream semantics.
    private bool _voiceSynthesisAgentInitialized;

    private const string VoiceSynthesisAgentId = "VoiceSynthesis";

    #region Activation

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // Initialize default state values
        if (CustomState.RecoveryDelay == null)
        {
            CustomState.RecoveryDelay = Duration.FromTimeSpan(TimeSpan.FromSeconds(60));
        }

        // AI initialization is deferred to ConfigAsync to allow specifying provider
        // ConfigAsync will use specified provider or fallback to default
        Logger.LogInformation("[AIAgentStatusProxyNew] Activated with Id={AgentId}", Id);
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"AI Agent Status Proxy (Available: {CustomState.IsAvailable}, Exceptions: {CustomState.ExceptionCount})");
    }

    #endregion

    #region Configuration

    /// <summary>
    /// Configure the proxy agent
    /// </summary>
    public async Task ConfigAsync(AIAgentStatusProxyConfigProto config)
    {
        Logger.LogDebug("[AIAgentStatusProxyNew][ConfigAsync] Configuring proxy with ParentId={ParentId}, ProviderName={ProviderName}", 
            config.ParentId, config.ProviderName);
        
        // Initialize AI with specified provider or fallback to default
        // InitializeAsync has internal guard: if (_isInitialized) return;
        var factory = RequireLLMProviderFactory();
        if (!string.IsNullOrWhiteSpace(config.ProviderName))
        {
            if (factory.HasProvider(config.ProviderName))
            {
                await InitializeAsync(config.ProviderName, cancellationToken: default);
            }
            else
            {
                Logger.LogWarning("[AIAgentStatusProxyNew][ConfigAsync] Provider '{ProviderName}' not found, using default", config.ProviderName);
                await InitializeAsync(factory.GetDefaultProviderConfig(), cancellationToken: default);
            }
        }
        else
        {
            // Fallback: if no provider specified, ensure AI is initialized with default
            // This handles the case where proxy is restored from persistence
            await InitializeAsync(factory.GetDefaultProviderConfig(), cancellationToken: default);
        }
        
        RaiseEvent(new SetStatusProxyConfigEvent
        {
            RecoveryDelay = config.RequestRecoveryDelay,
            ParentId = config.ParentId
        });

        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Set the prompt template
    /// </summary>
    public async Task SetPromptTemplateAsync(string promptTemplate)
    {
        RaiseEvent(new SetPromptTemplateEvent
        {
            PromptTemplate = promptTemplate
        });

        await ConfirmEventsAsync();
    }

    #endregion

    #region Availability

    public async Task<bool> IsAvailableAsync()
    {
        if (CustomState.IsAvailable)
        {
            return true;
        }

        if (CustomState.UnavailableSince == null)
        {
            Logger.LogDebug("[AIAgentStatusProxyNew][IsAvailableAsync] UnavailableSince is null");
            return true;
        }

        var now = DateTime.UtcNow;
        var unavailableSince = CustomState.UnavailableSince.ToDateTime();
        var recoveryDelay = CustomState.RecoveryDelay?.ToTimeSpan() ?? TimeSpan.FromSeconds(60);
        var timeElapsed = now - unavailableSince;
        
        if (timeElapsed > recoveryDelay)
        {
            RaiseEvent(new SetAvailableEvent { IsAvailable = true });
            await ConfirmEventsAsync();
            return true;
        }

        return false;
    }

    private async Task MarkUnavailableAsync(int exceptionCount = 1)
    {
        RaiseEvent(new SetAvailableEvent
        {
            IsAvailable = false,
            ExceptionCount = exceptionCount
        });
        await ConfirmEventsAsync();
    }

    #endregion

    #region Chat Methods

    /// <summary>
    /// Chat with history (Proto version for RPC)
    /// </summary>
    public async Task<ChatWithHistoryResultProto> ChatWithHistoryProtoAsync(ChatWithHistoryInputProto input)
    {
        // Convert proto to internal types
        var prompt = input.Prompt;
        var history = input.History?.Select(h => new ChatMessage
        {
            Role = h.Role,
            Content = h.Content,
            Timestamp = new DateTime(h.TimestampTicks, DateTimeKind.Utc),
            ChatRole = (Aevatar.GAgents.ChatAgent.Dtos.ChatRole)h.ChatRole,
            ImageKeys = h.ImageKeys?.ToList()
        }).ToList();
        
        ExecutionPromptSettings? promptSettings = null;
        if (input.PromptSettings != null)
        {
            promptSettings = new ExecutionPromptSettings
            {
                Temperature = input.PromptSettings.HasTemperature ? input.PromptSettings.Temperature : null,
                MaxTokens = input.PromptSettings.HasMaxTokens ? input.PromptSettings.MaxTokens : null,
                TopP = input.PromptSettings.HasTopP ? input.PromptSettings.TopP : null,
                FrequencyPenalty = input.PromptSettings.HasFrequencyPenalty ? input.PromptSettings.FrequencyPenalty : null,
                PresencePenalty = input.PromptSettings.HasPresencePenalty ? input.PromptSettings.PresencePenalty : null,
                StopSequences = input.PromptSettings.StopSequences?.ToList(),
                Model = input.PromptSettings.HasModel ? input.PromptSettings.Model : null
            };
        }
        
        AIChatContextDto? context = null;
        if (input.Context != null)
        {
            context = new AIChatContextDto
            {
                AgentId = input.Context.HasAgentId ? input.Context.AgentId : null,
                SessionId = input.Context.HasSessionId ? input.Context.SessionId : null,
                UserId = input.Context.HasUserId ? input.Context.UserId : null,
                Metadata = input.Context.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SystemPrompt = input.Context.HasSystemPrompt ? input.Context.SystemPrompt : null,
                RequestId = Guid.TryParse(input.Context.RequestId, out var reqId) ? reqId : Guid.NewGuid(),
                ChatId = input.Context.HasChatId ? input.Context.ChatId : null,
                MessageId = input.Context.HasMessageId ? input.Context.MessageId : null
            };
        }
        
        return await ChatWithHistoryInternalAsync(prompt, history, promptSettings, context);
    }

    /// <summary>
    /// Internal chat with history implementation
    /// </summary>
    private async Task<ChatWithHistoryResultProto> ChatWithHistoryInternalAsync(
        string prompt, 
        List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, 
        AIChatContextDto? context = null)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var requestId = context?.ChatId ?? Guid.NewGuid().ToString();
        
        var systemPrompt = CustomState.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        
        Logger.LogInformation("[PERF][AIAgentStatusProxy] ChatWithHistory_START - RequestId={RequestId}, PromptLen={PromptLen}, HistoryCount={HistoryCount}, SelectedHistory={SelectedHistory}", 
            requestId, prompt?.Length ?? 0, history?.Count ?? 0, selectedHistory.Count);

        try
        {
            // Build chat request
            var request = new ChatRequest
            {
                Message = prompt,
                RequestId = requestId
            };

            // Inject selected history into ChatRequest for multi-turn conversation context
            if (selectedHistory.Count > 0)
            {
                InjectHistoryIntoRequest(request, selectedHistory);
            }

            if (promptSettings?.Temperature != null && double.TryParse(promptSettings.Temperature, out var temp))
            {
                request.Temperature = (float)temp;
            }

            // Use the AI framework's ChatAsync
            var chatStartMs = sw.ElapsedMilliseconds;
            var response = await ChatAsync(request);
            var chatEndMs = sw.ElapsedMilliseconds;

            Logger.LogInformation("[PERF][AIAgentStatusProxy] ChatWithHistory_END - RequestId={RequestId}, ChatAsync_Duration={Duration}ms, ResponseLen={ResponseLen}", 
                requestId, chatEndMs - chatStartMs, response.Content?.Length ?? 0);

            // Convert to proto format
            var result = new ChatWithHistoryResultProto();
            result.Messages.Add(new ChatMessageProto
            {
                Role = "assistant",
                Content = response.Content,
                TimestampTicks = DateTime.UtcNow.Ticks,
                ChatRole = (int)Aevatar.GAgents.ChatAgent.Dtos.ChatRole.Assistant
            });

            sw.Stop();
            Logger.LogInformation("[PERF][AIAgentStatusProxy] ChatWithHistory_COMPLETE - RequestId={RequestId}, Total_Duration={Duration}ms", 
                requestId, sw.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.LogError(ex, "[PERF][AIAgentStatusProxy] ChatWithHistory_ERROR - RequestId={RequestId}, Duration={Duration}ms, Error={Error}", 
                requestId, sw.ElapsedMilliseconds, ex.Message);
            await HandleChatErrorAsync(context, AIExceptionEnum.Unknown, ex.Message, null);
            return new ChatWithHistoryResultProto(); // Return empty result on error
        }
    }

    /// <summary>
    /// Prompt with streaming response (Proto version for RPC)
    /// </summary>
    public async Task<bool> PromptWithStreamProtoAsync(PromptWithStreamInputProto input)
    {
        // Convert proto to internal types
        var prompt = input.Prompt;
        var history = input.History?.Select(h => new ChatMessage
        {
            Role = h.Role,
            Content = h.Content,
            Timestamp = new DateTime(h.TimestampTicks, DateTimeKind.Utc),
            ChatRole = (Aevatar.GAgents.ChatAgent.Dtos.ChatRole)h.ChatRole,
            ImageKeys = h.ImageKeys?.ToList()
        }).ToList();
        
        ExecutionPromptSettings? promptSettings = null;
        if (input.PromptSettings != null)
        {
            promptSettings = new ExecutionPromptSettings
            {
                Temperature = input.PromptSettings.HasTemperature ? input.PromptSettings.Temperature : null,
                MaxTokens = input.PromptSettings.HasMaxTokens ? input.PromptSettings.MaxTokens : null,
                TopP = input.PromptSettings.HasTopP ? input.PromptSettings.TopP : null,
                FrequencyPenalty = input.PromptSettings.HasFrequencyPenalty ? input.PromptSettings.FrequencyPenalty : null,
                PresencePenalty = input.PromptSettings.HasPresencePenalty ? input.PromptSettings.PresencePenalty : null,
                StopSequences = input.PromptSettings.StopSequences?.ToList(),
                Model = input.PromptSettings.HasModel ? input.PromptSettings.Model : null
            };
        }
        
        AIChatContextDto? context = null;
        if (input.Context != null)
        {
            context = new AIChatContextDto
            {
                AgentId = input.Context.HasAgentId ? input.Context.AgentId : null,
                SessionId = input.Context.HasSessionId ? input.Context.SessionId : null,
                UserId = input.Context.HasUserId ? input.Context.UserId : null,
                Metadata = input.Context.Metadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                SystemPrompt = input.Context.HasSystemPrompt ? input.Context.SystemPrompt : null,
                RequestId = Guid.TryParse(input.Context.RequestId, out var reqId) ? reqId : Guid.NewGuid(),
                ChatId = input.Context.HasChatId ? input.Context.ChatId : null,
                MessageId = input.Context.HasMessageId ? input.Context.MessageId : null
            };
        }
        
        var imageKeys = input.ImageKeys?.ToList();
        
        // Extract new fields for direct Kafka push
        var streamId = input.HasStreamId ? input.StreamId : null;
        var isHttpRequest = input.IsHttpRequest;
        
        return await PromptWithStreamInternalAsync(prompt, history, promptSettings, context, imageKeys, streamId, isHttpRequest);
    }

    /// <summary>
    /// Internal streaming implementation with token aggregation
    /// Aggregates multiple LLM tokens to reduce Kafka message count
    /// When isHttpRequest=true, pushes directly to Kafka (bypassing parent callback queue)
    /// </summary>
    private async Task<bool> PromptWithStreamInternalAsync(
        string prompt, 
        List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, 
        AIChatContextDto? context = null, 
        List<string>? imageKeys = null,
        string? streamId = null,
        bool isHttpRequest = false)
    {
        var (isVoiceChat, voiceLanguage) = ParseVoiceChatFlags(context?.MessageId);
        
        Logger.LogInformation("[AIAgentStatusProxy] PromptWithStream started - IsHttpRequest={IsHttpRequest}, StreamId={StreamId}, ChatId={ChatId}",
            isHttpRequest, streamId ?? "null", context?.ChatId ?? "null");
        
        var systemPrompt = CustomState.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);

        try
        {
            var request = new ChatRequest
            {
                Message = prompt,
                RequestId = context?.ChatId ?? Guid.NewGuid().ToString()
            };

            // Inject selected history into ChatRequest for multi-turn conversation context
            if (selectedHistory.Count > 0)
            {
                InjectHistoryIntoRequest(request, selectedHistory);
                Logger.LogInformation("[AIAgentStatusProxy] Injected {Count} history messages into ChatRequest - ChatId={ChatId}",
                    selectedHistory.Count, context?.ChatId ?? "null");
            }

            if (promptSettings?.Temperature != null && double.TryParse(promptSettings.Temperature, out var temp))
            {
                request.Temperature = (float)temp;
            }
            
            // Add image keys for multimodal requests
            Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] imageKeys parameter: {ImageKeys}", 
                imageKeys != null ? string.Join(",", imageKeys) : "NULL");
            if (imageKeys != null && imageKeys.Count > 0)
            {
                request.ImageKeys.AddRange(imageKeys);
                Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] Added {Count} image keys to ChatRequest: {Keys}", 
                    imageKeys.Count, string.Join(",", imageKeys));
            }
            else
            {
                Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] No image keys to add");
            }

            var fullResponse = new System.Text.StringBuilder();
            var serialNumber = 0;
            var messagesSent = 0;

            // Aggregation buffer
            var aggregationBuffer = new System.Text.StringBuilder();
            var aggregatedTokenCount = 0;
            var lastSendTime = DateTime.UtcNow;

            // Use the AI framework's streaming capability
            var firstTokenReceived = false;
            var streamStartMs = System.Diagnostics.Stopwatch.StartNew();
            
            // SUGGESTIONS filtering for HTTP text chat (filters [SUGGESTIONS] block from client stream)
            // Create per-request filter instance (thread-safe for this single request)
            var suggestionsFilter = (isHttpRequest && !isVoiceChat) 
                ? new StreamingSuggestionsFilter() 
                : null;
            
            await foreach (var token in ChatStreamAsync(request))
            {
                serialNumber++;
                fullResponse.Append(token);  // 原始响应（用于最终存储）
                
                // ========================================
                // CRITICAL FIX: Filter 只处理新 token，不是累积内容！
                // StreamingSuggestionsFilter 是状态机，每个字符只能处理一次
                // ========================================
                string filteredToken = token;
                bool tokenBlocked = false;
                
                if (suggestionsFilter != null)
                {
                    var filterResult = suggestionsFilter.ProcessChunk(token);  // ✓ 只传新 token
                    if (filterResult.WasBlocked && string.IsNullOrEmpty(filterResult.FilteredContent))
                    {
                        tokenBlocked = true;
                        Logger.LogDebug("[AIAgentStatusProxy] Token blocked by SUGGESTIONS filter - ChatId={ChatId}", context?.ChatId ?? "null");
                    }
                    else
                    {
                        filteredToken = filterResult.FilteredContent ?? "";
                    }
                }
                
                // 把 filtered 内容（而非原始 token）添加到聚合缓冲
                if (!tokenBlocked && !string.IsNullOrEmpty(filteredToken))
                {
                    aggregationBuffer.Append(filteredToken);
                    aggregatedTokenCount++;
                }
                else if (tokenBlocked)
                {
                    // Token 被阻止，跳过本次循环
                    continue;
                }
                
                // CRITICAL: Send first token immediately for best TTFT (Time To First Token)
                if (!firstTokenReceived && aggregationBuffer.Length > 0)
                {
                    firstTokenReceived = true;
                    Logger.LogInformation("[PERF][AIAgentStatusProxy] TTFT - First token received after {ElapsedMs}ms, ChatId={ChatId}",
                        streamStartMs.ElapsedMilliseconds, context?.ChatId ?? "null");
                    
                    var firstTokenContent = aggregationBuffer.ToString();
                    
                    // Send first token immediately
                    var firstContent = new AIStreamChatContent
                    {
                        Content = firstTokenContent,
                        IsComplete = false,
                        SerialNumber = serialNumber,
                        IsLastChunk = false
                    };
                    await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, firstContent, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
                    messagesSent++;
                    
                    Logger.LogInformation("[PERF][AIAgentStatusProxy] First token sent immediately - SerialNumber={SerialNumber}, ChatId={ChatId}",
                        serialNumber, context?.ChatId ?? "null");
                    
                    // Reset aggregation for subsequent tokens
                    aggregationBuffer.Clear();
                    aggregatedTokenCount = 0;
                    lastSendTime = DateTime.UtcNow;
                    continue;
                }
                
                // Check if we should send aggregated message
                var timeSinceLastSend = (DateTime.UtcNow - lastSendTime).TotalMilliseconds;
                var shouldSend = aggregatedTokenCount >= MaxAggregatedTokens || 
                                 timeSinceLastSend >= AggregationIntervalMs;
                
                if (shouldSend && aggregationBuffer.Length > 0)
                {
                    var contentToSend = aggregationBuffer.ToString();
                    var streamContent = new AIStreamChatContent
                    {
                        Content = contentToSend,
                        IsComplete = false,
                        SerialNumber = serialNumber,
                        IsLastChunk = false
                    };

                    await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, streamContent, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
                    messagesSent++;
                    
                    if (messagesSent <= 5) // Log first 5 callbacks for debugging
                    {
                        Logger.LogInformation("[PERF][AIAgentStatusProxy] Aggregated callback sent - SerialNumber={SerialNumber}, Tokens={Tokens}, ElapsedMs={ElapsedMs}, ChatId={ChatId}",
                            serialNumber, aggregatedTokenCount, timeSinceLastSend, context?.ChatId ?? "null");
                    }
                    
                    // Reset aggregation
                    aggregationBuffer.Clear();
                    aggregatedTokenCount = 0;
                    lastSendTime = DateTime.UtcNow;
                }
            }
            
            // Send any remaining buffered content
            if (aggregationBuffer.Length > 0)
            {
                var remainingContentStr = aggregationBuffer.ToString();
                var remainingContent = new AIStreamChatContent
                {
                    Content = remainingContentStr,
                    IsComplete = false,
                    SerialNumber = serialNumber,
                    IsLastChunk = false
                };
                await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, remainingContent, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
                messagesSent++;
            }
            
            // Extract final filtered content if we were accumulating SUGGESTIONS
            List<string>? extractedSuggestions = null;
            string? cleanedAggregationResponse = null;
            
            if (suggestionsFilter != null && suggestionsFilter.IsAccumulating)
            {
                var finalFilterResult = suggestionsFilter.ExtractFinalContent();
                extractedSuggestions = finalFilterResult.ExtractedSuggestions;
                
                // Send the clean main content if any
                if (!string.IsNullOrEmpty(finalFilterResult.FilteredContent))
                {
                    var cleanContent = new AIStreamChatContent
                    {
                        Content = finalFilterResult.FilteredContent,
                        IsComplete = false,
                        SerialNumber = serialNumber,
                        IsLastChunk = false
                    };
                    await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, cleanContent, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
                    messagesSent++;
                    
                    Logger.LogDebug("[AIAgentStatusProxy] Sent clean content after SUGGESTIONS extraction - Length={Length}, Suggestions={Count}, ChatId={ChatId}",
                        finalFilterResult.FilteredContent.Length, extractedSuggestions?.Count ?? 0, context?.ChatId ?? "null");
                }
                
                // CRITICAL: Clean aggregation message to remove SUGGESTIONS block
                // This ensures GodChatGAgent receives clean content and doesn't need to clean again
                var fullResponseStr = fullResponse.ToString();
                var parseResult = SuggestionParser.ParseResponseWithSuggestions(fullResponseStr);
                cleanedAggregationResponse = parseResult.MainContent;
                
                Logger.LogDebug("[AIAgentStatusProxy] Cleaned aggregation message - Original={OrigLen}, Cleaned={CleanLen}, Suggestions={Count}, ChatId={ChatId}",
                    fullResponseStr.Length, cleanedAggregationResponse?.Length ?? 0, extractedSuggestions?.Count ?? 0, context?.ChatId ?? "null");
            }

            // Send final chunk with aggregation message for state persistence
            // Use cleaned content if suggestions were filtered, otherwise use full response
            var aggregatedResponse = cleanedAggregationResponse ?? fullResponse.ToString();
            var finalContent = new AIStreamChatContent
            {
                Content = aggregatedResponse,
                IsComplete = true,
                IsLastChunk = true,
                SerialNumber = serialNumber + 1,
                // CRITICAL: Set these fields to trigger AI message persistence in GodChatGAgent.Callbacks
                IsAggregationMsg = true,
                AggregationMsg = aggregatedResponse, // Already cleaned if suggestions were filtered
                // Pass extracted suggestions for client
                ExtractedSuggestions = extractedSuggestions
            };

            await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, finalContent, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
            messagesSent++;
            
            Logger.LogInformation("[PERF][AIAgentStatusProxy] Stream completed - TotalTokens={TotalTokens}, MessagesSent={MessagesSent}, ChatId={ChatId}",
                serialNumber, messagesSent, context?.ChatId ?? "null");
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AIAgentStatusProxyNew][PromptWithStreamAsync] Error");
            
            if (ex.Message.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                await MarkUnavailableAsync();
                await HandleChatErrorAsync(context, AIExceptionEnum.RequestLimitError, ex.Message, null, streamId, isHttpRequest);
            }
            else
            {
                await HandleChatErrorAsync(context, AIExceptionEnum.Unknown, ex.Message, null, streamId, isHttpRequest);
            }
            
            return false;
        }
    }

    private async Task HandleChatErrorAsync(
        AIChatContextDto? context, 
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content,
        string? streamId = null,
        bool isHttpRequest = false)
    {
        Logger.LogDebug("[AIAgentStatusProxyNew][HandleChatErrorAsync] Error: {Error}, Message: {Message}", 
            errorEnum, errorMessage);

        if (errorEnum == AIExceptionEnum.RequestLimitError)
        {
            await MarkUnavailableAsync();
        }

        var (isVoiceChat, voiceLanguage) = ParseVoiceChatFlags(context?.MessageId);
        await SendStreamCallbackAsync(context, errorEnum, errorMessage, content, streamId, isHttpRequest, isVoiceChat, voiceLanguage);
    }

    private async Task SendStreamCallbackAsync(
        AIChatContextDto? context,
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content,
        string? streamId,
        bool isHttpRequest,
        bool isVoiceChat,
        int voiceLanguage)
    {
        // CRITICAL: If this is an HTTP request AND we have a StreamId, push directly to Kafka
        // This bypasses the parent (GodChatGAgent) callback queue, avoiding the Orleans Grain blocking issue
        Logger.LogInformation("[AIAgentStatusProxy] SendStreamCallback - IsHttpRequest={IsHttpRequest}, StreamId={StreamId}, SerialNumber={SerialNumber}, IsVoiceChat={IsVoiceChat}, VoiceLanguage={VoiceLanguage}",
            isHttpRequest, streamId ?? "null", content?.SerialNumber ?? 0, isVoiceChat, voiceLanguage);
            
        if (isHttpRequest && !string.IsNullOrEmpty(streamId))
        {
            // For voice chat, additionally dispatch internal TTS jobs (fire-and-forget).
            // IMPORTANT: do NOT feed aggregated persistence message to TTS (it would duplicate audio).
            if (isVoiceChat)
            {
                Logger.LogInformation("[AIAgentStatusProxy] Dispatching VoiceSynthesis job - StreamId={StreamId}, ContentLen={ContentLen}, IsLastChunk={IsLastChunk}, IsAggregation={IsAggregation}",
                    streamId, content?.Content?.Length ?? 0, content?.IsLastChunk ?? false, content?.IsAggregationMsg ?? false);
                _ = DispatchVoiceSynthesisJobAsync(context, errorEnum, content, streamId, voiceLanguage);
            }

            await PushToClientDirectlyAsync(context, errorEnum, errorMessage, content, streamId, isVoiceChat);

            // Persist assistant message/history via GodChatGAgent callback (ONLY once, at end).
            // We keep streaming direct-to-client, but still need the final aggregation message to update session state.
            if (content?.IsAggregationMsg == true)
            {
                _ = SendAggregationPersistenceCallbackAsync(context, errorEnum, errorMessage, content);
            }
            return;
        }
        
        // Fallback: Use event-driven callback via SendToAsync to parent
        // Parent (GodChatGAgent) receives this event through [EventHandler]
            
        // Convert AIChatContextDto to AIChatContextProto
        AIChatContextProto? contextProto = null;
        if (context != null)
        {
            contextProto = new AIChatContextProto
            {
                AgentId = context.AgentId ?? "",
                SessionId = context.SessionId ?? "",
                UserId = context.UserId ?? "",
                SystemPrompt = context.SystemPrompt ?? "",
                RequestId = context.RequestId.ToString(),
                ChatId = context.ChatId ?? "",
                MessageId = context.MessageId ?? ""
            };
            if (context.Metadata != null)
            {
                foreach (var kvp in context.Metadata)
                {
                    contextProto.Metadata[kvp.Key] = kvp.Value;
                }
            }
        }
            
        // Convert AIStreamChatContent to AIStreamChatContentProto
        AIStreamChatContentProto? contentProto = null;
        if (content != null)
        {
            contentProto = new AIStreamChatContentProto
            {
                Content = content.Content ?? "",
                IsComplete = content.IsComplete,
                TokenCount = content.TokenCount,
                Error = content.Error ?? "",
                IsLastChunk = content.IsLastChunk,
                ResponseContent = content.ResponseContent ?? "",
                AggregationMsg = content.AggregationMsg ?? "",
                SerialNumber = content.SerialNumber,
                IsAggregationMsg = content.IsAggregationMsg
            };
        }
            
        // Send callback event via MassTransit Stream (event-driven, non-blocking)
        // This uses the agent framework's stream mechanism
        var parentId = CustomState.ParentId;
        if (string.IsNullOrEmpty(parentId))
        {
            Logger.LogWarning("[AIAgentStatusProxyNew] ParentId not configured, cannot send callback event");
            return;
        }
        
        var callbackEvent = new ChatMessageCallbackEvent
        {
            Context = contextProto,
            AiExceptionEnum = (int)errorEnum,
            ErrorMessage = errorMessage ?? "",
            Content = contentProto
        };
        
        Logger.LogInformation("[AIAgentStatusProxyNew] Sending ChatMessageCallbackEvent to parent {ParentId} via SendToAsync", parentId);
        await SendToAsync(parentId, callbackEvent);
    }
    
    /// <summary>
    /// Push streaming content directly to Kafka, bypassing GodChatGAgent callback queue.
    /// This solves the Orleans Grain blocking issue where GodChatGAgent awaits PromptWithStreamProtoAsync
    /// and cannot process callback events until the stream completes.
    /// 
    /// NOTE: Produces EventEnvelope(Payload=Any.Pack(GodChatStreamEnvelopeProto)) to MassTransit stream category "GodChat".
    /// ChatMiddleware MUST only unpack GodChatStreamEnvelopeProto for client-facing streaming.
    /// </summary>
    private async Task PushToClientDirectlyAsync(
        AIChatContextDto? context,
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content,
        string streamId,
        bool isVoiceChat)
    {
        Logger.LogInformation("[AIAgentStatusProxy] PushToClientDirectly - START, SerialNumber={SerialNumber}, StreamId={StreamId}",
            content?.SerialNumber ?? 0, streamId);
            
        if (ServiceProvider == null)
        {
            Logger.LogWarning("[AIAgentStatusProxy] ServiceProvider is NULL, cannot push directly to Kafka");
            return;
        }
        
        Logger.LogInformation("[AIAgentStatusProxy] PushToClientDirectly - ServiceProvider OK, getting IMessageStreamProvider");
        
        var messageStreamProvider = ServiceProvider.GetService<IMessageStreamProvider>();
        if (messageStreamProvider == null)
        {
            Logger.LogWarning("[AIAgentStatusProxy] IMessageStreamProvider not available in ServiceProvider");
            return;
        }
        
        Logger.LogInformation("[AIAgentStatusProxy] PushToClientDirectly - IMessageStreamProvider OK, ChatId={ChatId}",
            context?.ChatId ?? "null");
        
        try
        {
            // Get stream with GodChat category for correct topic routing
            var stream = messageStreamProvider.GetStream(streamId, "GodChat");
            
            var seq = (long)(content?.SerialNumber ?? 0);

            // For HTTP streaming:
            // - Text chunks are emitted as payload=text with is_last=false
            // - Completion is emitted as payload=control(type=ALL_COMPLETED)
            // - Errors are emitted as payload=control(type=ERROR) and MUST be terminal
            // This avoids closing SSE on a text chunk and keeps semantics explicit.
            var streamEnvelope = new GodChatStreamEnvelopeProto
            {
                StreamId = streamId,
                ChatId = context?.ChatId ?? "",
                RequestId = context?.RequestId.ToString() ?? "",
                Seq = seq,
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };

            if (errorEnum != AIExceptionEnum.None)
            {
                streamEnvelope.Control = new ControlProto
                {
                    Type = ControlProto.Types.ControlType.Error,
                    Scope = "all",
                    Message = errorMessage ?? "",
                    ErrorCode = MapLegacyChatErrorCode(errorEnum, errorMessage)
                };
            }
            else if (content?.IsLastChunk == true)
            {
                // CRITICAL FIX: If we have extracted suggestions, send TextChunk with suggestedItems first,
                // then send ControlProto(AllCompleted)
                if (content.ExtractedSuggestions?.Any() == true)
                {
                    // Send final text chunk with suggestedItems
                    var textEnvelope = new GodChatStreamEnvelopeProto
                    {
                        StreamId = streamId,
                        ChatId = context?.ChatId ?? "",
                        RequestId = context?.RequestId.ToString() ?? "",
                        Seq = seq,
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                        Text = new TextChunkProto
                        {
                            Content = "",
                            IsLast = true,
                            SentenceIndex = 0,
                            // AI response: always VoiceResponse (1)
                            VoiceContentType = (int)VoiceContentType.VoiceResponse
                        }
                    };
                    // Ensure fixed "I don't understand" suggestion is present (always 4 items)
                    var ensuredSuggestions = FixedSuggestions.EnsureFixedSuggestion(content.ExtractedSuggestions);
                    textEnvelope.Text.SuggestedItems.AddRange(ensuredSuggestions);
                    
                    var textEventEnvelope = new EventEnvelope
                    {
                        Id = Guid.NewGuid().ToString(),
                        Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                        Version = 0,
                        Payload = Any.Pack(textEnvelope)
                    };
                    await stream.ProduceAsync(textEventEnvelope, CancellationToken.None);
                    Logger.LogInformation("[AIAgentStatusProxy] Sent suggestedItems - Count={Count}, StreamId={StreamId}",
                        ensuredSuggestions.Count, streamId);
                    
                    // Update seq for control message
                    seq++;
                    streamEnvelope.Seq = seq;
                }
                
                // For voice chat: send TextCompleted instead of AllCompleted
                // VoiceSynthesisGAgent will send AllCompleted after the last audio chunk
                // This prevents SSE from closing before all audio chunks are delivered
                if (isVoiceChat)
                {
                    streamEnvelope.Control = new ControlProto
                    {
                        Type = ControlProto.Types.ControlType.TextCompleted,
                        Scope = "text",
                        Message = "",
                        ErrorCode = 0
                    };
                    Logger.LogInformation("[AIAgentStatusProxy] Voice chat: sent TextCompleted (not AllCompleted) - StreamId={StreamId}, ChatId={ChatId}",
                        streamId, context?.ChatId ?? "null");
                }
                else
                {
                    // Text chat: send AllCompleted to close SSE immediately
                    streamEnvelope.Control = new ControlProto
                    {
                        Type = ControlProto.Types.ControlType.AllCompleted,
                        Scope = "all",
                        Message = "",
                        ErrorCode = 0
                    };
                }
            }
            else
            {
                streamEnvelope.Text = new TextChunkProto
                {
                    Content = content?.Content ?? "",
                    IsLast = false,
                    SentenceIndex = 0,
                    // AI response: always VoiceResponse (1)
                    VoiceContentType = (int)VoiceContentType.VoiceResponse
                };
            }

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
                Version = 0,
                Payload = Any.Pack(streamEnvelope)
            };
            
            Logger.LogInformation("[AIAgentStatusProxy] ProduceAsync START - TypeUrl={TypeUrl}, StreamId={StreamId}, SerialNumber={SerialNumber}",
                envelope.Payload.TypeUrl, streamId, content?.SerialNumber ?? 0);
            
            await stream.ProduceAsync(envelope, CancellationToken.None);
            
            Logger.LogInformation("[AIAgentStatusProxy] ProduceAsync DONE - StreamId={StreamId}, SerialNumber={SerialNumber}, IsLastChunk={IsLastChunk}",
                streamId, content?.SerialNumber ?? 0, content?.IsLastChunk ?? false);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AIAgentStatusProxy] Failed to push message directly to Kafka - StreamId={StreamId}", streamId);
        }
    }

    private static int MapLegacyChatErrorCode(AIExceptionEnum errorEnum, string? errorMessage)
    {
        // Keep aligned with legacy ChatErrorCode values for client compatibility:
        // 20001 ParamInvalid, 20003 InsufficientCredits, 20004 RateLimitExceeded
        if (errorEnum == AIExceptionEnum.RequestLimitError || errorEnum == AIExceptionEnum.RateLimitExceeded)
        {
            return (int)ChatErrorCode.RateLimitExceeded;
        }

        if (!string.IsNullOrEmpty(errorMessage) &&
            errorMessage.Contains("credit", StringComparison.OrdinalIgnoreCase))
        {
            return (int)ChatErrorCode.InsufficientCredits;
        }

        return (int)ChatErrorCode.ParamInvalid;
    }

    private static (bool IsVoiceChat, int VoiceLanguage) ParseVoiceChatFlags(string? messageId)
    {
        if (string.IsNullOrWhiteSpace(messageId))
        {
            return (false, (int)VoiceLanguageEnum.English);
        }

        try
        {
            var dict = JsonConvert.DeserializeObject<Dictionary<string, object>>(messageId);
            if (dict == null)
            {
                return (false, (int)VoiceLanguageEnum.English);
            }

            var isVoiceChat = false;
            if (dict.TryGetValue("IsVoiceChat", out var isVoiceObj) && isVoiceObj != null)
            {
                _ = bool.TryParse(isVoiceObj.ToString(), out isVoiceChat);
            }

            var voiceLanguage = (int)VoiceLanguageEnum.English;
            if (dict.TryGetValue("VoiceLanguage", out var langObj) && langObj != null &&
                int.TryParse(langObj.ToString(), out var parsed))
            {
                voiceLanguage = parsed;
            }

            return (isVoiceChat, voiceLanguage);
        }
        catch
        {
            return (false, (int)VoiceLanguageEnum.English);
        }
    }

    private async Task DispatchVoiceSynthesisJobAsync(
        AIChatContextDto? context,
        AIExceptionEnum errorEnum,
        AIStreamChatContent? content,
        string streamId,
        int voiceLanguage)
    {
        // Do not synthesize audio if the stream itself errored.
        if (errorEnum != AIExceptionEnum.None)
        {
            return;
        }

        if (content == null)
        {
            return;
        }

        await EnsureVoiceSynthesisAgentInitializedAsync();

        // Skip aggregation/persistence message (full response) to avoid duplicate audio.
        if (content.IsAggregationMsg)
        {
            // But we still want to flush when the text stream completes.
            if (content.IsLastChunk)
            {
                await SendToAsync(VoiceSynthesisAgentId, new VoiceSynthesisJobProto
                {
                    StreamId = streamId,
                    ChatId = context?.ChatId ?? "",
                    RequestId = context?.RequestId.ToString() ?? "",
                    TextDelta = "",
                    TextSeq = content.SerialNumber,
                    TextIsLast = true,
                    VoiceLanguage = voiceLanguage,
                    Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
                });
            }
            return;
        }

        await SendToAsync(VoiceSynthesisAgentId, new VoiceSynthesisJobProto
        {
            StreamId = streamId,
            ChatId = context?.ChatId ?? "",
            RequestId = context?.RequestId.ToString() ?? "",
            TextDelta = content.Content ?? "",
            TextSeq = content.SerialNumber,
            TextIsLast = content.IsLastChunk,
            VoiceLanguage = voiceLanguage,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        });
    }

    private async Task EnsureVoiceSynthesisAgentInitializedAsync()
    {
        if (_voiceSynthesisAgentInitialized)
        {
            return;
        }

        try
        {
            var grainFactory = ServiceProvider.GetService<IGrainFactory>();
            if (grainFactory == null)
            {
                Logger.LogWarning("[AIAgentStatusProxy] IGrainFactory not available, cannot init VoiceSynthesisGAgent");
                return;
            }

            var grain = grainFactory.GetGrain<IGAgentGrain>(VoiceSynthesisAgentId);
            if (await grain.IsInitializedAsync())
            {
                _voiceSynthesisAgentInitialized = true;
                return;
            }

            var ok = await grain.InitializeAgentAsync(typeof(Aevatar.Application.Grains.Agents.ChatManager.VoiceSynthesis.VoiceSynthesisGAgent).AssemblyQualifiedName!);
            Logger.LogInformation("[AIAgentStatusProxy] VoiceSynthesis agent initialized: {Ok}", ok);
            _voiceSynthesisAgentInitialized = ok;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AIAgentStatusProxy] Failed to initialize VoiceSynthesis agent");
        }
    }

    private async Task SendAggregationPersistenceCallbackAsync(
        AIChatContextDto? context,
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent content)
    {
        try
        {
            var parentId = CustomState.ParentId;
            if (string.IsNullOrWhiteSpace(parentId))
            {
                Logger.LogWarning("[AIAgentStatusProxy] ParentId not configured, cannot persist aggregation callback");
                return;
            }

            // Convert AIChatContextDto to AIChatContextProto
            AIChatContextProto? contextProto = null;
            if (context != null)
            {
                contextProto = new AIChatContextProto
                {
                    AgentId = context.AgentId ?? "",
                    SessionId = context.SessionId ?? "",
                    UserId = context.UserId ?? "",
                    SystemPrompt = context.SystemPrompt ?? "",
                    RequestId = context.RequestId.ToString(),
                    ChatId = context.ChatId ?? "",
                    MessageId = context.MessageId ?? ""
                };
                if (context.Metadata != null)
                {
                    foreach (var kvp in context.Metadata)
                    {
                        contextProto.Metadata[kvp.Key] = kvp.Value;
                    }
                }
            }

            // Convert AIStreamChatContent to AIStreamChatContentProto
            var contentProto = new AIStreamChatContentProto
            {
                Content = content.Content ?? "",
                IsComplete = content.IsComplete,
                TokenCount = content.TokenCount,
                Error = content.Error ?? "",
                IsLastChunk = content.IsLastChunk,
                ResponseContent = content.ResponseContent ?? "",
                AggregationMsg = content.AggregationMsg ?? "",
                SerialNumber = content.SerialNumber,
                IsAggregationMsg = content.IsAggregationMsg
            };

            var callbackEvent = new ChatMessageCallbackEvent
            {
                Context = contextProto,
                AiExceptionEnum = (int)errorEnum,
                ErrorMessage = errorMessage ?? "",
                Content = contentProto
            };

            Logger.LogInformation("[AIAgentStatusProxy] Sending aggregation persistence callback to parent {ParentId}", parentId);
            await SendToAsync(parentId, callbackEvent);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AIAgentStatusProxy] Failed to send aggregation persistence callback");
        }
    }

    #endregion

    #region History Conversion

    /// <summary>
    /// Convert legacy ChatMessage list to AevatarChatMessage list for ChatRequest.History.
    /// Maps GodGPT ChatRole → AevatarChatRole (different enum values).
    /// </summary>
    private static void InjectHistoryIntoRequest(ChatRequest request, List<ChatMessage> selectedHistory)
    {
        foreach (var msg in selectedHistory)
        {
            var role = msg.ChatRole switch
            {
                Aevatar.GAgents.ChatAgent.Dtos.ChatRole.User => AevatarChatRole.User,
                Aevatar.GAgents.ChatAgent.Dtos.ChatRole.Assistant => AevatarChatRole.Assistant,
                Aevatar.GAgents.ChatAgent.Dtos.ChatRole.System => AevatarChatRole.System,
                Aevatar.GAgents.ChatAgent.Dtos.ChatRole.Tool => AevatarChatRole.Tool,
                _ => AevatarChatRole.User
            };

            request.History.Add(new AevatarChatMessage
            {
                Role = role,
                Content = msg.Content ?? ""
            });
        }
    }

    #endregion

    #region State Transition

    protected override void TransitionState(AIAgentStatusProxyStateProto state, IMessage evt)
    {
        switch (evt)
        {
            case SetStatusProxyConfigEvent configEvt:
                if (configEvt.RecoveryDelay != null)
                {
                    state.RecoveryDelay = configEvt.RecoveryDelay;
                }
                state.ParentId = configEvt.ParentId;
                break;

            case SetAvailableEvent availableEvt:
                state.IsAvailable = availableEvt.IsAvailable;
                if (state.IsAvailable)
                {
                    state.UnavailableSince = null;
                }
                else
                {
                    state.UnavailableSince = Timestamp.FromDateTime(DateTime.UtcNow);
                    state.UnavailableCount++;
                    state.ExceptionCount += availableEvt.ExceptionCount;
                }
                break;

            case SetPromptTemplateEvent promptEvt:
                state.PromptTemplate = promptEvt.PromptTemplate;
                break;
        }
    }

    #endregion
    
    #region Multimodal Image Support

    /// <summary>
    /// Resolve image keys to actual image data from blob storage
    /// Overrides base implementation to provide actual blob storage integration
    /// </summary>
    protected override async Task<IList<AevatarImageData>?> ResolveImageKeysAsync(
        IEnumerable<string> imageKeys,
        CancellationToken cancellationToken = default)
    {
        Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] ResolveImageKeysAsync CALLED with keys: {Keys}",
            string.Join(",", imageKeys));
        
        if (ServiceProvider == null)
        {
            Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] ServiceProvider is NULL!");
            return null;
        }
        Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] ServiceProvider OK, getting IBlobContainer...");

        var blobContainer = ServiceProvider.GetService<IBlobContainer>();
        if (blobContainer == null)
        {
            Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] IBlobContainer is NULL in ServiceProvider!");
            return null;
        }
        Logger.LogWarning("[AIAgentStatusProxy][IMAGE_DEBUG] IBlobContainer OK, starting download...");

        var imageDataList = new List<AevatarImageData>();
        var keyList = imageKeys.ToList();
        
        Logger.LogInformation("[AIAgentStatusProxy] Resolving {Count} image keys from blob storage", keyList.Count);
        
        // Download all images concurrently
        var downloadTasks = keyList.Select(async key =>
        {
            try
            {
                var bytes = await blobContainer.GetAllBytesAsync(key, cancellationToken);
                var mediaType = GetMediaTypeFromKey(key);
                
                Logger.LogDebug("[AIAgentStatusProxy] Downloaded image: Key={Key}, Size={Size} bytes, MediaType={MediaType}",
                    key, bytes.Length, mediaType);
                
                return new AevatarImageData
                {
                    Key = key,
                    Data = new ReadOnlyMemory<byte>(bytes),
                    MediaType = mediaType
                };
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[AIAgentStatusProxy] Failed to download image: Key={Key}", key);
                return null;
            }
        });

        var results = await Task.WhenAll(downloadTasks);
        
        foreach (var result in results)
        {
            if (result != null)
            {
                imageDataList.Add(result);
            }
        }

        Logger.LogInformation("[AIAgentStatusProxy] Successfully resolved {Count}/{Total} images",
            imageDataList.Count, keyList.Count);
        
        return imageDataList.Count > 0 ? imageDataList : null;
    }

    /// <summary>
    /// Get MIME type from file key/extension
    /// </summary>
    private static string GetMediaTypeFromKey(string key)
    {
        var extension = Path.GetExtension(key)?.ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".svg" => "image/svg+xml",
            _ => "image/jpeg" // Default to JPEG
        };
    }

    #endregion
}

/// <summary>
/// Interface for the AI Agent Status Proxy
/// </summary>
public interface IAIAgentStatusProxy
{
    Task<bool> IsAvailableAsync();
    Task ConfigAsync(AIAgentStatusProxyConfigProto config);
    Task SetPromptTemplateAsync(string promptTemplate);
    
    /// <summary>
    /// Prompt with streaming response (Proto version for RPC)
    /// </summary>
    Task<bool> PromptWithStreamProtoAsync(PromptWithStreamInputProto input);
    
    /// <summary>
    /// Chat with history (Proto version for RPC)
    /// </summary>
    Task<ChatWithHistoryResultProto> ChatWithHistoryProtoAsync(ChatWithHistoryInputProto input);
}

