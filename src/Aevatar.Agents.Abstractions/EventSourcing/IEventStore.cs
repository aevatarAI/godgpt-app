namespace Aevatar.Agents.Abstractions.EventSourcing;

/// <summary>
/// EventSourcing storage interface (unified across all runtimes)
/// Uses Protobuf AgentStateEvent for serialization consistency
/// 
/// Design reference: Aevatar.EventSourcing.Core.ILogConsistentStorage
/// Enhanced features: Snapshot, range query, optimistic concurrency, per-type collection
/// </summary>
public interface IEventStore
{
    // ========== Event Operations ==========

    /// <summary>
    /// Append events with optimistic concurrency control
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="events">Events to append</param>
    /// <param name="expectedVersion">Expected current version (optimistic concurrency)</param>
    /// <param name="agentTypeName">Agent type name for per-type collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>New version number</returns>
    Task<long> AppendEventsAsync(
        string agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        string? agentTypeName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get events with range query and pagination support
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="fromVersion">Start version (inclusive)</param>
    /// <param name="toVersion">End version (inclusive)</param>
    /// <param name="maxCount">Maximum count (pagination)</param>
    /// <param name="agentTypeName">Agent type name for per-type collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Event list</returns>
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        string agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get latest version number
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="agentTypeName">Agent type name for per-type collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    Task<long> GetLatestVersionAsync(
        string agentId, 
        string? agentTypeName = null,
        CancellationToken ct = default);

    // Note: Snapshots are handled by IStateStore<TState> in GAgentBase (per-type collections)
}
