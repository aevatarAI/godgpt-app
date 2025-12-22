namespace Aevatar.Agents.Abstractions.EventRouting;

/// <summary>
/// EventRouter hierarchy persistence interface
/// Manages agent parent-child relationship persistence
/// </summary>
public interface IEventRouterStore
{
    /// <summary>
    /// Load hierarchy for an agent
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Hierarchy object, or null if not exists</returns>
    Task<EventRouterHierarchy?> LoadAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Save agent hierarchy
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="hierarchy">Hierarchy object</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveAsync(string agentId, EventRouterHierarchy hierarchy, CancellationToken ct = default);

    /// <summary>
    /// Delete agent hierarchy
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Check if hierarchy exists
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<bool> ExistsAsync(string agentId, CancellationToken ct = default);
}
