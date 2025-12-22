using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Event publisher interface.
/// Agents publish events through this interface; the Actor layer implements the routing logic.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publish event (broadcast mode).
    /// Propagates through hierarchical streams.
    /// </summary>
    /// <param name="evt">Event message</param>
    /// <param name="direction">Propagation direction (default: Down)</param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="isInternalCall">
    /// If true (Agent internal call), keeps PublisherId for self-handling check.
    /// If false (external call), clears PublisherId so Agent can handle the event.
    /// Default is false for external API compatibility.
    /// </param>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <returns>Event ID</returns>
    Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage;

    /// <summary>
    /// Point-to-point send (direct delivery mode).
    /// Sends directly to the specified agent, bypassing hierarchical broadcast.
    /// </summary>
    /// <param name="targetAgentId">Target agent ID (format varies by runtime)</param>
    /// <param name="evt">Event message</param>
    /// <param name="onArrivalDirection">
    /// Propagation direction after arrival:
    /// - Unspecified: Pure P2P, only target processes, no propagation
    /// - Down: Target processes then broadcasts to all its children
    /// - Up: Target processes then propagates up to its parent
    /// - Both: Target processes then propagates in both directions
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <param name="isInternalCall">
    /// If true (Agent internal call), keeps PublisherId for self-handling check.
    /// If false (external call), clears PublisherId so Agent can handle the event.
    /// </param>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <returns>Event ID</returns>
    Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage;
}

/// <summary>
/// Null implementation for scenarios where EventPublisher is not configured.
/// </summary>
public sealed class NullEventPublisher : IEventPublisher
{
    public static NullEventPublisher Instance { get; } = new();

    public Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false) where TEvent : IMessage
    {
        return Task.FromResult(string.Empty);
    }

    public Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false) where TEvent : IMessage
    {
        return Task.FromResult(string.Empty);
    }
}