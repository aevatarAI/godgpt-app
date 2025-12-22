using Aevatar.AI.Exceptions;
using Aevatar.AI.Feature.StreamSyncWoker;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.ProxySEvents;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Agent;
using Aevatar.GAgents.AIGAgent.Dtos;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans.Concurrency;
using System.Diagnostics;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.GEvents;
using Aevatar.Agents.Abstractions;

namespace Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;

[GAgent]
[Reentrant]
public class AIAgentStatusProxy :
    AIGAgentBase<AIAgentStatusProxyState, AIAgentStatusProxyLogEvent, EventBase, AIAgentStatusProxyConfig>,
    IAIAgentStatusProxy
{
    // Injected by OrleansGAgentGrain via reflection (see InjectActorFactory)
    public IGAgentActorFactory? ActorFactory { get; set; }
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("AIGAgent supporting state management");
    }
    
    /// <summary>
    /// Public interface method for configuration - delegates to PerformConfigAsync
    /// </summary>
    public new async Task ConfigAsync(AIAgentStatusProxyConfig config)
    {
        await PerformConfigAsync(config);
    }

    protected sealed override async Task PerformConfigAsync(AIAgentStatusProxyConfig configuration)
    {
        RaiseEvent(new SetStatusProxyConfigLogEvent
        {
            RecoveryDelay = configuration.RequestRecoveryDelay,
            ParentId = configuration.ParentId
        });
    }
    [EventHandler]
    private async Task HandlerEventAsync(AIAgentStatusProxyInitializeGEvent @event)
    {
        var stopwatch = Stopwatch.StartNew();
        Logger.LogDebug($"[HandlerEventAsync][AIAgentStatusProxyInitializeGEvent] Start- SessionId:{Id}, event:{JsonConvert.SerializeObject(@event)}");
        //await SendProxyInitStatusUpdateAsync(ProxyInitStatus.Initializing);

        await InitializeAsync(@event.InitializeDto);
        // Send status update to GodChatGAgent - Initialized
        await SendProxyInitStatusUpdateAsync(ProxyInitStatus.Initialized);
        stopwatch.Stop();
        Logger.LogDebug($"[HandlerEventAsync][AIAgentStatusProxyInitializeGEvent] End - SessionId: {Id} ,Duration: {stopwatch.ElapsedMilliseconds}ms");
    }
    
    private async Task SendProxyInitStatusUpdateAsync(ProxyInitStatus status)
    {
        try
        {
            if (State.ParentId != Guid.Empty)
            {
                // Use new framework IGAgentActorFactory to get GodChatGAgent
                if (ActorFactory == null)
                {
                    Logger.LogError("[AIAgentStatusProxy][SendProxyInitStatusUpdateAsync] ActorFactory is not injected");
                    return;
                }
                
                var godChatActor = await ActorFactory.CreateGAgentActorAsync<GodChatGAgent>(State.ParentId);
                var godChat = (IGodChat)godChatActor.GetAgent();
                await godChat.UpdateProxyInitStatusAsync(Id, status);

                Logger.LogDebug($"[AIAgentStatusProxy][SendProxyInitStatusUpdateAsync] Sent status update: {status} for proxy: {Id} to GodChatGAgent: {State.ParentId}");
            }
            else
            {
                Logger.LogWarning($"[AIAgentStatusProxy][SendProxyInitStatusUpdateAsync] ParentId is empty, cannot send status update");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"[AIAgentStatusProxy][SendProxyInitStatusUpdateAsync] Failed to send status update: {status} for proxy: {Id}");
        }
    }
    public new async Task<List<ChatMessage>?> ChatWithHistory(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null)
    {
        var systemPrompt = State.PromptTemplate;
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        Logger.LogDebug($"[AIAgentStatusProxy][ChatWithHistory] Original history count: {history?.Count ?? 0}, Selected history count: {selectedHistory.Count}");
        
        return await base.ChatWithHistory(prompt, selectedHistory, promptSettings, context: context);
    }

    public new async Task<bool> PromptWithStreamAsync(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null, List<string>? imageKeys = null)
    {
        // Get system prompt
        var systemPrompt = State.PromptTemplate;
        
        // Intelligently select historical messages
        var selectedHistory = TokenHelper.SelectHistoryMessages(history, prompt, systemPrompt);
        
        Logger.LogDebug($"[AIAgentStatusProxy][PromptWithStreamAsync] Original history count: {history?.Count ?? 0}, Selected history count: {selectedHistory.Count}");
        
        // Call base method with filtered history messages
        return await base.PromptWithStreamAsync(prompt, selectedHistory, promptSettings, context, imageKeys: imageKeys);
    }

    protected override async Task AIChatHandleStreamAsync(AIChatContextDto context, AIExceptionEnum errorEnum,
        string? errorMessage,
        AIStreamChatContent? content)
    {
        Logger.LogDebug(
            $"[AIAgentStatusProxy][AIChatHandleStreamAsync] sessionId {context?.RequestId.ToString()}, chatId {context?.ChatId}, errorEnum {errorEnum}, errorMessage {errorMessage}: {JsonConvert.SerializeObject(content)}");
        if (errorEnum == AIExceptionEnum.RequestLimitError)
        {
            RaiseEvent(new SetAvailableLogEvent
            {
                IsAvailable = false,
                ExceptionCount = 1
            });
            await ConfirmEventsAsync();
        }
        
        if (ActorFactory == null)
        {
            Logger.LogError("[AIAgentStatusProxy][AIChatHandleStreamAsync] ActorFactory is not injected");
            return;
        }
        
        var godChatActor = await ActorFactory.CreateGAgentActorAsync<GodChatGAgent>(State.ParentId);
        var godChat = (IGodChat)godChatActor.GetAgent();
        await godChat.ChatMessageCallbackAsync(context, errorEnum, errorMessage, content);
    }

    public async Task<bool> IsAvailableAsync()
    {
        if (State.IsAvailable)
        {
            return true;
        }

        if (State.UnavailableSince == null)
        {
            Logger.LogDebug($"[AIAgentStatusProxy][IsAvailableAsync] State.UnavailableSince is null");
            return true;
        }

        var now = DateTime.UtcNow;
        var unavailableSince = State.UnavailableSince;
        var timeElapsed = now - unavailableSince;
        if (timeElapsed > State.RecoveryDelay)
        {
            RaiseEvent(new SetAvailableLogEvent
            {
                IsAvailable = true
            });
            await ConfirmEventsAsync();
            return true;
        }

        return false;
    }

    protected override void AIGAgentTransitionState(AIAgentStatusProxyState state,
        StateLogEventBase<AIAgentStatusProxyLogEvent> @event)
    {
        switch (@event)
        {
            case SetStatusProxyConfigLogEvent setStatusProxyConfigLogEvent:
                if (setStatusProxyConfigLogEvent.RecoveryDelay != null)
                {
                    state.RecoveryDelay = (TimeSpan)setStatusProxyConfigLogEvent.RecoveryDelay;
                }

                state.ParentId = setStatusProxyConfigLogEvent.ParentId;
                break;
            case SetAvailableLogEvent setAvailableLogEvent:
                state.IsAvailable = setAvailableLogEvent.IsAvailable;
                if (state.IsAvailable)
                {
                    state.UnavailableSince = null;
                }
                else
                {
                    state.UnavailableSince = DateTime.UtcNow;
                    state.UnavailableCount += 1;
                    state.ExceptionCount += setAvailableLogEvent.ExceptionCount;
                }
                break;
        }
    }
}

public interface IAIAgentStatusProxy : IGAgent, IAIGAgent
{
    Task<bool> IsAvailableAsync();

    Task<List<ChatMessage>?> ChatWithHistory(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null);

    Task<bool> PromptWithStreamAsync(string prompt, List<ChatMessage>? history = null,
        ExecutionPromptSettings? promptSettings = null, AIChatContextDto? context = null, List<string>? imageKeys = null);
    
    /// <summary>
    /// Configure the proxy agent
    /// </summary>
    Task ConfigAsync(AIAgentStatusProxyConfig config);
}