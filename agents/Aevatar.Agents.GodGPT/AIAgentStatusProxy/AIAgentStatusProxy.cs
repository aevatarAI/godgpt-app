using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;
using System.Diagnostics;
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
    
    // Message aggregation to reduce Kafka message count
    // Aggregates multiple LLM tokens into fewer SendToAsync calls
    private const int AggregationIntervalMs = 150; // Send every 150ms
    private const int MaxAggregatedTokens = 15;    // Or every 15 tokens

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
        var systemPrompt = CustomState.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        Logger.LogDebug("[AIAgentStatusProxyNew][ChatWithHistoryAsync] Original: {Original}, Selected: {Selected}", 
            history?.Count ?? 0, selectedHistory.Count);

        try
        {
            // Build chat request
            var request = new ChatRequest
            {
                Message = prompt,
                RequestId = context?.ChatId ?? Guid.NewGuid().ToString()
            };

            if (promptSettings?.Temperature != null && double.TryParse(promptSettings.Temperature, out var temp))
            {
                request.Temperature = (float)temp;
            }

            // Use the AI framework's ChatAsync
            var response = await ChatAsync(request);

            // Convert to proto format
            var result = new ChatWithHistoryResultProto();
            result.Messages.Add(new ChatMessageProto
            {
                Role = "assistant",
                Content = response.Content,
                TimestampTicks = DateTime.UtcNow.Ticks,
                ChatRole = (int)Aevatar.GAgents.ChatAgent.Dtos.ChatRole.Assistant
            });

            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[AIAgentStatusProxyNew][ChatWithHistoryAsync] Error");
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
        
        return await PromptWithStreamInternalAsync(prompt, history, promptSettings, context, imageKeys);
    }

    /// <summary>
    /// Internal streaming implementation with token aggregation
    /// Aggregates multiple LLM tokens to reduce Kafka message count
    /// </summary>
    private async Task<bool> PromptWithStreamInternalAsync(
        string prompt, 
        List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, 
        AIChatContextDto? context = null, 
        List<string>? imageKeys = null)
    {
        var systemPrompt = CustomState.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        
        Logger.LogDebug("[AIAgentStatusProxyNew][PromptWithStreamAsync] Original: {Original}, Selected: {Selected}", 
            history?.Count ?? 0, selectedHistory.Count);

        try
        {
            var request = new ChatRequest
            {
                Message = prompt,
                RequestId = context?.ChatId ?? Guid.NewGuid().ToString()
            };

            if (promptSettings?.Temperature != null && double.TryParse(promptSettings.Temperature, out var temp))
            {
                request.Temperature = (float)temp;
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
            
            await foreach (var token in ChatStreamAsync(request))
            {
                serialNumber++;
                fullResponse.Append(token);
                aggregationBuffer.Append(token);
                aggregatedTokenCount++;
                
                // CRITICAL: Send first token immediately for best TTFT (Time To First Token)
                // Users should see response start immediately, subsequent tokens can be aggregated
                if (!firstTokenReceived)
                {
                    firstTokenReceived = true;
                    Logger.LogInformation("[PERF][AIAgentStatusProxy] TTFT - First token received after {ElapsedMs}ms, ChatId={ChatId}",
                        streamStartMs.ElapsedMilliseconds, context?.ChatId ?? "null");
                    
                    // Send first token immediately
                    var firstContent = new AIStreamChatContent
                    {
                        Content = aggregationBuffer.ToString(),
                        IsComplete = false,
                        SerialNumber = serialNumber,
                        IsLastChunk = false
                    };
                    await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, firstContent);
                    messagesSent++;
                    
                    Logger.LogInformation("[PERF][AIAgentStatusProxy] First token sent immediately - SerialNumber={SerialNumber}, ChatId={ChatId}",
                        serialNumber, context?.ChatId ?? "null");
                    
                    // Reset aggregation for subsequent tokens
                    aggregationBuffer.Clear();
                    aggregatedTokenCount = 0;
                    lastSendTime = DateTime.UtcNow;
                    continue; // Skip the aggregation check for first token
                }
                
                // Check if we should send aggregated message
                var timeSinceLastSend = (DateTime.UtcNow - lastSendTime).TotalMilliseconds;
                var shouldSend = aggregatedTokenCount >= MaxAggregatedTokens || 
                                 timeSinceLastSend >= AggregationIntervalMs;
                
                if (shouldSend && aggregationBuffer.Length > 0)
                {
                    var streamContent = new AIStreamChatContent
                    {
                        Content = aggregationBuffer.ToString(),
                        IsComplete = false,
                        SerialNumber = serialNumber,
                        IsLastChunk = false
                    };

                    await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, streamContent);
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
                var remainingContent = new AIStreamChatContent
                {
                    Content = aggregationBuffer.ToString(),
                    IsComplete = false,
                    SerialNumber = serialNumber,
                    IsLastChunk = false
                };
                await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, remainingContent);
                messagesSent++;
            }

            // Send final chunk with aggregation message for state persistence
            var aggregatedResponse = fullResponse.ToString();
            var finalContent = new AIStreamChatContent
            {
                Content = aggregatedResponse,
                IsComplete = true,
                IsLastChunk = true,
                SerialNumber = serialNumber + 1,
                // CRITICAL: Set these fields to trigger AI message persistence in GodChatGAgent.Callbacks
                IsAggregationMsg = true,
                AggregationMsg = aggregatedResponse
            };

            await SendStreamCallbackAsync(context, AIExceptionEnum.None, null, finalContent);
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
                await HandleChatErrorAsync(context, AIExceptionEnum.RequestLimitError, ex.Message, null);
            }
            else
            {
                await HandleChatErrorAsync(context, AIExceptionEnum.Unknown, ex.Message, null);
            }
            
            return false;
        }
    }

    private async Task HandleChatErrorAsync(
        AIChatContextDto? context, 
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content)
    {
        Logger.LogDebug("[AIAgentStatusProxyNew][HandleChatErrorAsync] Error: {Error}, Message: {Message}", 
            errorEnum, errorMessage);

        if (errorEnum == AIExceptionEnum.RequestLimitError)
        {
            await MarkUnavailableAsync();
        }

        await SendStreamCallbackAsync(context, errorEnum, errorMessage, content);
    }

    private async Task SendStreamCallbackAsync(
        AIChatContextDto? context,
        AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content)
    {
        // Use event-driven callback via PublishAsync to avoid deadlock
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

