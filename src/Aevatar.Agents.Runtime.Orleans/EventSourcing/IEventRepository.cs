namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

/// <summary>
/// Repository interface for event storage
/// Abstracts the underlying storage implementation (MongoDB, SQL, etc.)
/// Each agent type should have its own collection for better isolation and performance
/// </summary>
public interface IEventRepository
{
    /// <summary>
    /// Append events for an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="events">Events to append</param>
    /// <param name="agentTypeName">Agent type name for collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    Task<long> AppendEventsAsync(
        Guid agentId, 
        IEnumerable<AgentStateEvent> events,
        string? agentTypeName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get events for an agent with optional version range
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="fromVersion">Start version (inclusive)</param>
    /// <param name="toVersion">End version (inclusive)</param>
    /// <param name="maxCount">Maximum events to return</param>
    /// <param name="agentTypeName">Agent type name for collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Get the latest version for an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="agentTypeName">Agent type name for collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    Task<long> GetLatestVersionAsync(
        Guid agentId,
        string? agentTypeName = null,
        CancellationToken ct = default);

    /// <summary>
    /// Delete events older than a specific version (for cleanup after snapshot)
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="version">Version threshold</param>
    /// <param name="agentTypeName">Agent type name for collection routing (optional)</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteEventsBeforeVersionAsync(
        Guid agentId, 
        long version,
        string? agentTypeName = null,
        CancellationToken ct = default);
}

