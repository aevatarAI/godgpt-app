namespace Aevatar.Agents.Abstractions.Persistence;

/// <summary>
/// Configuration store interface for agent-specific configurations
/// Allows each agent instance to have its own configuration
/// Configurations are isolated by both agent type and agent ID
/// </summary>
/// <typeparam name="TConfig">Configuration type</typeparam>
public interface IConfigStore<TConfig>
    where TConfig : class, new()
{
    /// <summary>
    /// Load agent configuration
    /// </summary>
    /// <param name="agentType">Type of the agent</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Configuration object, or null if not exists</returns>
    Task<TConfig?> LoadAsync(Type agentType, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Save agent configuration
    /// </summary>
    /// <param name="agentType">Type of the agent</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="config">Configuration object</param>
    /// <param name="ct">Cancellation token</param>
    Task SaveAsync(Type agentType, string agentId, TConfig config, CancellationToken ct = default);

    /// <summary>
    /// Delete agent configuration
    /// </summary>
    /// <param name="agentType">Type of the agent</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task DeleteAsync(Type agentType, string agentId, CancellationToken ct = default);

    /// <summary>
    /// Check if configuration exists
    /// </summary>
    /// <param name="agentType">Type of the agent</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="ct">Cancellation token</param>
    Task<bool> ExistsAsync(Type agentType, string agentId, CancellationToken ct = default);
}
