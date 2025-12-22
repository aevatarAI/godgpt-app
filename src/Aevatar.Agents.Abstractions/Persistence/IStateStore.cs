namespace Aevatar.Agents.Abstractions.Persistence;

/// <summary>
/// State store interface
/// Manages agent state persistence
/// </summary>
/// <typeparam name="TState">State type</typeparam>
public interface IStateStore<TState>
{
    /// <summary>
    /// Load agent state
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>State object, or null if not exists</returns>
    Task<TState?> LoadAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Save agent state
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="state">State object</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveAsync(string agentId, TState state, CancellationToken ct = default);

    /// <summary>
    /// Delete agent state
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(string agentId, CancellationToken ct = default);

    /// <summary>
    /// Check if state exists
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<bool> ExistsAsync(string agentId, CancellationToken ct = default);
}

/// <summary>
/// Versioned state store interface with optimistic concurrency control
/// </summary>
/// <typeparam name="TState">State type</typeparam>
public interface IVersionedStateStore<TState> : IStateStore<TState>
    where TState : class
{
    /// <summary>
    /// Save with version control (for optimistic concurrency)
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="state">State object</param>
    /// <param name="expectedVersion">Expected version number</param>
    /// <param name="ct">Cancellation token</param>
    /// <exception cref="StateVersionConflictException">Thrown when version conflict</exception>
    Task SaveAsync(string agentId, TState state, long expectedVersion, CancellationToken ct = default);

    /// <summary>
    /// Get current version number
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<long> GetCurrentVersionAsync(string agentId, CancellationToken ct = default);
}

/// <summary>
/// State version conflict exception
/// </summary>
public class StateVersionConflictException : Exception
{
    public string AgentId { get; }
    public long ExpectedVersion { get; }
    public long ActualVersion { get; }

    public StateVersionConflictException(string agentId, long expectedVersion, long actualVersion)
        : base($"State version conflict for agent {agentId}: expected {expectedVersion}, actual {actualVersion}")
    {
        AgentId = agentId;
        ExpectedVersion = expectedVersion;
        ActualVersion = actualVersion;
    }
}
