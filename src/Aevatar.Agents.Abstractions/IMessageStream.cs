using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream interface for event streaming
/// Supports subscription management and type filtering
/// </summary>
public interface IMessageStream
{
    /// <summary>
    /// Stream unique identifier
    ///
    /// Unified format: <c>"AgentTypeShortName:RawId"</c>
    /// Example: <c>"ChatAgent:12345678-..."</c>
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// Publish message to stream
    /// </summary>
    Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage;

    /// <summary>
    /// Subscribe to stream messages
    /// </summary>
    /// <returns>Subscription handle for managing subscription lifecycle</returns>
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        CancellationToken ct = default) where T : IMessage;

    /// <summary>
    /// Subscribe to stream messages (with type filter)
    /// </summary>
    /// <returns>Subscription handle for managing subscription lifecycle</returns>
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage;
}