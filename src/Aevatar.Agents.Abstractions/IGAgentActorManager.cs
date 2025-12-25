namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor manager interface
/// Responsible for global Actor registration, lookup, lifecycle management and type discovery
/// </summary>
public interface IGAgentActorManager
{
    #region Lifecycle Management
    
    /// <summary>
    /// Create and register Agent Actor
    /// </summary>
    /// <param name="id">
    /// Id input during creation (recommended to pass RawId, e.g., Guid string).
    /// System will automatically normalize to full ActorId based on <typeparamref name="TAgent"/>: <c>"AgentTypeShortName:RawId"</c>.
    /// </param>
    Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(
        string id,
        CancellationToken ct = default)
        where TAgent : IGAgent;
    
    /// <summary>
    /// Batch create and register Agent Actors
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(
        IEnumerable<string> ids,
        CancellationToken ct = default)
        where TAgent : IGAgent;
    
    /// <summary>
    /// Deactivate and unregister Actor
    /// </summary>
    Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default);
    
    /// <summary>
    /// Batch deactivate specified Actors
    /// </summary>
    Task DeactivateBatchAsync(IEnumerable<string> ids, CancellationToken ct = default);
    
    /// <summary>
    /// Batch deactivate all Actors
    /// </summary>
    Task DeactivateAllAsync(CancellationToken ct = default);
    
    #endregion
    
    #region Query and Retrieval
    
    /// <summary>
    /// Get registered Actor
    /// </summary>
    Task<IGAgentActor?> GetActorAsync(string id);
    
    /// <summary>
    /// Batch get Actors
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<string> ids);
    
    /// <summary>
    /// Get all registered Actors
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync();
    
    /// <summary>
    /// Get Actors by type
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>()
        where TAgent : IGAgent;
    
    /// <summary>
    /// Get Actors by type name
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName);
    
    /// <summary>
    /// Check if Actor exists
    /// </summary>
    Task<bool> ExistsAsync(string id);
    
    /// <summary>
    /// Get Actor count
    /// </summary>
    Task<int> GetCountAsync();
    
    /// <summary>
    /// Get Actor count by type
    /// </summary>
    Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent;
    
    #endregion

    #region Hierarchy Coordination

    /// <summary>
    /// Add specified child Actor under parent Actor, internally updates both parties' EventRouter simultaneously.
    /// </summary>
    Task LinkParentChildAsync(string parentId, string childId, CancellationToken ct = default);

    /// <summary>
    /// Unlink parent-child relationship. If parentId is not provided, will automatically query child's current parent before unlinking.
    /// </summary>
    Task UnlinkParentChildAsync(string childId, string? parentId = null, CancellationToken ct = default);

    #endregion
    
    #region Monitoring and Diagnostics
    
    /// <summary>
    /// Get Actor health status
    /// </summary>
    Task<ActorHealthStatus> GetHealthStatusAsync(string id);
    
    /// <summary>
    /// Get statistics for all Actors
    /// </summary>
    Task<ActorManagerStatistics> GetStatisticsAsync();
    
    #endregion
}

/// <summary>
/// Actor health status
/// </summary>
public record ActorHealthStatus
{
    /// <summary>
    /// Actor ID
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// Whether healthy
    /// </summary>
    public bool IsHealthy { get; init; }
    
    /// <summary>
    /// Last activity time
    /// </summary>
    public DateTimeOffset? LastActivityTime { get; init; }
    
    /// <summary>
    /// Error message
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Actor manager statistics
/// </summary>
public record ActorManagerStatistics
{
    /// <summary>
    /// Total Actor count
    /// </summary>
    public int TotalActors { get; init; }
    
    /// <summary>
    /// Active Actor count
    /// </summary>
    public int ActiveActors { get; init; }
    
    /// <summary>
    /// Actor count grouped by type
    /// </summary>
    public Dictionary<string, int> ActorsByType { get; init; } = new();
    
    /// <summary>
    /// Statistics timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

