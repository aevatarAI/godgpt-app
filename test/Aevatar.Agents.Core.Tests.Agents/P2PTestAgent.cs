using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;

namespace Aevatar.Agents.Core.Tests.Agents;

/// <summary>
/// Point-to-point communication test agent.
/// Used to test SendToAsync functionality.
/// </summary>
public class P2PTestAgent : GAgentBase<P2PAgentState>
{
    // ============ State Tracking ============
    
    public int ReceivedDirectMessageCount => State.DirectMessageCount;
    public int ReceivedBroadcastCount => State.BroadcastCount;
    public string? LastReceivedMessageId { get; private set; }
    public string? LastReceivedContent { get; private set; }
    public string? LastSenderId { get; private set; }

    // ============ Point-to-Point Send Methods ============

    /// <summary>
    /// Pure P2P send: only target agent processes
    /// </summary>
    public async Task<string> SendDirectMessageAsync(Guid targetId, string content)
    {
        var message = new DirectMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = content,
            SenderId = Id.ToString()
        };
        
        State.SentDirectMessages.Add(message.MessageId);
        
        // Pure P2P: onArrivalDirection = Unspecified
        return await SendToAsync(targetId, message, EventDirection.Unspecified);
    }

    /// <summary>
    /// P2P + group broadcast: target receives then broadcasts to its children
    /// </summary>
    public async Task<string> SendToGroupAsync(Guid coordinatorId, string content)
    {
        var message = new DirectMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = content,
            SenderId = Id.ToString()
        };
        
        State.SentDirectMessages.Add(message.MessageId);
        
        // P2P + broadcast: onArrivalDirection = Down
        return await SendToAsync(coordinatorId, message, EventDirection.Down);
    }

    /// <summary>
    /// P2P + upward propagation: target receives then continues reporting up
    /// </summary>
    public async Task<string> SendWithUpPropagationAsync(Guid targetId, string content)
    {
        var message = new DirectMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = content,
            SenderId = Id.ToString()
        };
        
        State.SentDirectMessages.Add(message.MessageId);
        
        // P2P + up: onArrivalDirection = Up
        return await SendToAsync(targetId, message, EventDirection.Up);
    }

    // ============ Broadcast Send Methods (for comparison) ============

    /// <summary>
    /// Traditional broadcast send
    /// </summary>
    public async Task<string> BroadcastMessageAsync(string content)
    {
        var message = new DirectMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Content = content,
            SenderId = Id.ToString()
        };
        
        return await PublishAsync(message, EventDirection.Down);
    }

    // ============ Event Handlers ============

    [EventHandler]
    public Task HandleDirectMessage(DirectMessage message)
    {
        State.DirectMessageCount++;
        State.ReceivedDirectMessages.Add(message.MessageId);
        
        LastReceivedMessageId = message.MessageId;
        LastReceivedContent = message.Content;
        LastSenderId = message.SenderId;
        
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleDirectRequest(DirectRequest request)
    {
        State.DirectMessageCount++;
        LastReceivedMessageId = request.RequestId;
        LastReceivedContent = request.Query;
        
        return Task.CompletedTask;
    }

    // ============ Broadcast Message Handling (for comparison) ============

    [EventHandler]
    public Task HandleTestEvent(TestEvent evt)
    {
        State.BroadcastCount++;
        return Task.CompletedTask;
    }

    public override string GetDescription()
    {
        return $"P2PTestAgent: {State.AgentName}, DirectMsgs={State.DirectMessageCount}, Broadcasts={State.BroadcastCount}";
    }
}
