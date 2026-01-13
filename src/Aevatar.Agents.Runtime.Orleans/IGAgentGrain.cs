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
    /// Publish event by envelope bytes (non-blocking, via Stream).
    /// Used by Silo-internal actor to keep a single IGAgentActor API surface.
    /// </summary>
    /// <param name="envelopeBytes">EventEnvelope serialized bytes</param>
    /// <param name="direction">Propagation direction</param>
    /// <param name="isInternalCall">
    /// If true, keeps PublisherId for self-handling check; if false, clears PublisherId so Agent can handle the event.
    /// </param>
    /// <returns>Event ID</returns>
    Task<string> PublishEventAsync(byte[] envelopeBytes, EventDirection direction = EventDirection.Down, bool isInternalCall = false);

    /// <summary>
    /// Point-to-point send by envelope bytes (non-blocking, via Stream).
    /// Used by Silo-internal actor to keep a single IGAgentActor API surface.
    /// </summary>
    /// <param name="targetAgentId">Target agent id (full ActorId)</param>
    /// <param name="envelopeBytes">EventEnvelope serialized bytes</param>
    /// <param name="onArrivalDirection">Propagation direction after arrival</param>
    /// <param name="isInternalCall">Same semantics as PublishEventAsync</param>
    /// <returns>Event ID</returns>
    Task<string> SendToAsync(string targetAgentId, byte[] envelopeBytes, EventDirection onArrivalDirection = EventDirection.Unspecified, bool isInternalCall = false);

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
    /// [AlwaysInterleave] allows RPC calls to execute concurrently, preventing blocking when Grain is busy.
    /// This is critical for read operations (like GetChatMessageAsync) to not block on long-running AI operations.
    /// Note: Business logic should handle its own thread-safety if needed.
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    [AlwaysInterleave]
    Task<byte[]> InvokeRpcAsync(byte[] requestBytes);
}