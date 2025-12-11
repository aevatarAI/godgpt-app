using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain 接口
/// Agent 业务逻辑在 Grain (Silo) 内执行
/// </summary>
public interface IGAgentGrain : IGrainWithStringKey
{
    /// <summary>
    /// 获取关联的 Agent ID
    /// </summary>
    Task<Guid> GetIdAsync();

    /// <summary>
    /// 初始化 Agent 实例（在 Silo 内创建）
    /// </summary>
    /// <param name="agentTypeName">Agent 类型的程序集限定名</param>
    /// <returns>是否成功初始化</returns>
    Task<bool> InitializeAgentAsync(string agentTypeName);

    /// <summary>
    /// 检查 Agent 是否已初始化
    /// </summary>
    Task<bool> IsInitializedAsync();

    /// <summary>
    /// 获取 Agent 描述
    /// </summary>
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// 处理事件（在 Silo 内执行业务逻辑）
    /// </summary>
    Task HandleEventAsync(byte[] envelopeBytes);

    /// <summary>
    /// 添加子 Agent
    /// </summary>
    Task AddChildAsync(Guid childId);

    /// <summary>
    /// 移除子 Agent
    /// </summary>
    Task RemoveChildAsync(Guid childId);

    /// <summary>
    /// 设置父 Agent
    /// </summary>
    Task SetParentAsync(Guid parentId);

    /// <summary>
    /// 清除父 Agent
    /// </summary>
    Task ClearParentAsync();

    /// <summary>
    /// 获取所有子 Agent ID
    /// </summary>
    Task<IReadOnlyList<Guid>> GetChildrenAsync();

    /// <summary>
    /// 获取父 Agent ID
    /// </summary>
    Task<Guid?> GetParentAsync();

    /// <summary>
    /// 激活并设置Agent类型（已废弃，请使用 InitializeAgentAsync）
    /// </summary>
    [Obsolete("Use InitializeAgentAsync instead")]
    Task ActivateAsync(string? agentTypeName = null, string? stateTypeName = null);

    /// <summary>
    /// 停用
    /// </summary>
    Task DeactivateAsync();

    /// <summary>
    /// Protobuf RPC method invocation
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    Task<byte[]> InvokeRpcAsync(byte[] requestBytes);
}