using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Grain 接口（基础接口）
/// Agent 业务逻辑在 Grain (Silo) 内执行
/// </summary>
public interface IGAgentGrain : IGrainWithStringKey
{
    /// <summary>
    /// 获取关联的 Agent ID
    /// [AlwaysInterleave] allows this to execute even when Grain is processing other requests
    /// </summary>
    [AlwaysInterleave]
    Task<string> GetIdAsync();

    /// <summary>
    /// 初始化 Agent 实例（在 Silo 内创建）
    /// Agent ID 从 Grain 的 PrimaryKey 获取（Grain ID = Agent ID）
    /// </summary>
    /// <param name="agentTypeName">Agent 类型的程序集限定名</param>
    /// <returns>是否成功初始化</returns>
    Task<bool> InitializeAgentAsync(string agentTypeName);

    /// <summary>
    /// 检查 Agent 是否已初始化
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<bool> IsInitializedAsync();

    /// <summary>
    /// 获取 Agent 描述
    /// [AlwaysInterleave] allows this read-only operation to execute without waiting for other calls
    /// </summary>
    [AlwaysInterleave]
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// 处理事件（在 Silo 内执行业务逻辑）
    /// </summary>
    Task HandleEventAsync(byte[] envelopeBytes);

    /// <summary>
    /// 添加子 Agent
    /// </summary>
    Task AddChildAsync(string childId);

    /// <summary>
    /// 移除子 Agent
    /// </summary>
    Task RemoveChildAsync(string childId);

    /// <summary>
    /// 设置父 Agent
    /// </summary>
    Task SetParentAsync(string parentId);

    /// <summary>
    /// 清除父 Agent
    /// </summary>
    Task ClearParentAsync();

    /// <summary>
    /// 获取所有子 Agent ID
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<IReadOnlyList<string>> GetChildrenAsync();

    /// <summary>
    /// 获取父 Agent ID
    /// [AlwaysInterleave] allows concurrent read access
    /// </summary>
    [AlwaysInterleave]
    Task<string?> GetParentAsync();

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