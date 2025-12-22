namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 管理器接口
/// 负责全局 Actor 的注册、查找、生命周期管理和类型发现
/// </summary>
public interface IGAgentActorManager
{
    #region 生命周期管理
    
    /// <summary>
    /// 创建并注册 Agent Actor
    /// </summary>
    /// <param name="id">Agent ID (format varies by runtime: Orleans uses "AgentType:Guid", Local uses "Guid")</param>
    Task<IGAgentActor> CreateAndRegisterAsync<TAgent>(
        string id,
        CancellationToken ct = default)
        where TAgent : IGAgent;
    
    /// <summary>
    /// 批量创建并注册 Agent Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> CreateBatchAsync<TAgent>(
        IEnumerable<string> ids,
        CancellationToken ct = default)
        where TAgent : IGAgent;
    
    /// <summary>
    /// 停用并注销 Actor
    /// </summary>
    Task DeactivateAndUnregisterAsync(string id, CancellationToken ct = default);
    
    /// <summary>
    /// 批量停用指定的 Actor
    /// </summary>
    Task DeactivateBatchAsync(IEnumerable<string> ids, CancellationToken ct = default);
    
    /// <summary>
    /// 批量停用所有 Actor
    /// </summary>
    Task DeactivateAllAsync(CancellationToken ct = default);
    
    #endregion
    
    #region 查询和获取
    
    /// <summary>
    /// 获取已注册的 Actor
    /// </summary>
    Task<IGAgentActor?> GetActorAsync(string id);
    
    /// <summary>
    /// 批量获取 Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsAsync(IEnumerable<string> ids);
    
    /// <summary>
    /// 获取所有已注册的 Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetAllActorsAsync();
    
    /// <summary>
    /// 按类型获取 Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeAsync<TAgent>()
        where TAgent : IGAgent;
    
    /// <summary>
    /// 按类型名称获取 Actor
    /// </summary>
    Task<IReadOnlyList<IGAgentActor>> GetActorsByTypeNameAsync(string typeName);
    
    /// <summary>
    /// 检查 Actor 是否存在
    /// </summary>
    Task<bool> ExistsAsync(string id);
    
    /// <summary>
    /// 获取 Actor 数量
    /// </summary>
    Task<int> GetCountAsync();
    
    /// <summary>
    /// 按类型获取 Actor 数量
    /// </summary>
    Task<int> GetCountByTypeAsync<TAgent>()
        where TAgent : IGAgent;
    
    #endregion

    #region 层级关系协调

    /// <summary>
    /// 将指定的子 Actor 加入到父 Actor 名下，内部会同时更新双方的 EventRouter。
    /// </summary>
    Task LinkParentChildAsync(string parentId, string childId, CancellationToken ct = default);

    /// <summary>
    /// 解除父子关系。如果没有提供 parentId，将自动查询子节点当前父节点后再解除。
    /// </summary>
    Task UnlinkParentChildAsync(string childId, string? parentId = null, CancellationToken ct = default);

    #endregion
    
    #region 监控和诊断
    
    /// <summary>
    /// 获取 Actor 健康状态
    /// </summary>
    Task<ActorHealthStatus> GetHealthStatusAsync(string id);
    
    /// <summary>
    /// 获取所有 Actor 的统计信息
    /// </summary>
    Task<ActorManagerStatistics> GetStatisticsAsync();
    
    #endregion
}

/// <summary>
/// Actor 健康状态
/// </summary>
public record ActorHealthStatus
{
    /// <summary>
    /// Actor ID
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// 是否健康
    /// </summary>
    public bool IsHealthy { get; init; }
    
    /// <summary>
    /// 上次活动时间
    /// </summary>
    public DateTimeOffset? LastActivityTime { get; init; }
    
    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Actor 管理器统计信息
/// </summary>
public record ActorManagerStatistics
{
    /// <summary>
    /// 总 Actor 数量
    /// </summary>
    public int TotalActors { get; init; }
    
    /// <summary>
    /// 活跃 Actor 数量
    /// </summary>
    public int ActiveActors { get; init; }
    
    /// <summary>
    /// 按类型分组的 Actor 数量
    /// </summary>
    public Dictionary<string, int> ActorsByType { get; init; } = new();
    
    /// <summary>
    /// 统计时间
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

