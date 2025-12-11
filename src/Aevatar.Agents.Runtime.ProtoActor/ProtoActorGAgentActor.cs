using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Observability;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 实现
/// 使用 ProtoActorMessageStream 作为消息传输机制
/// </summary>
public class ProtoActorGAgentActor : GAgentActorBase
{
    private static int _activeActorCount = 0;
    private readonly IRootContext _rootContext;
    private readonly PID _actorPid;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly ProtoActorMessageStream _myStream; // 这个 Actor 的 Stream
    private IMessageStreamSubscription? _parentStreamSubscription; // 父节点stream订阅句柄

    public ProtoActorGAgentActor(
        IGAgent agent,
        IRootContext rootContext,
        PID actorPid,
        ProtoActorMessageStreamRegistry streamRegistry)
        : base(agent)
    {
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _actorPid = actorPid ?? throw new ArgumentNullException(nameof(actorPid));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));

        // 注册 PID 并获取 Stream
        _streamRegistry.RegisterPid(agent.Id, actorPid);
        _myStream = _streamRegistry.GetStream(agent.Id)!;
    }

    /// <summary>
    /// 获取 Proto.Actor PID
    /// </summary>
    public PID GetPid() => _actorPid;

    // ============ 层级关系管理（重写基类方法） ============

    protected override async Task SetParentAsync(Guid parentId, CancellationToken ct = default)
    {
        // 如果已有父节点，先清除
        if (EventRouter.GetParent() != null)
        {
            await ClearParentAsync(ct);
        }

        // 调用基类方法设置父节点
        await base.SetParentAsync(parentId, ct);

        // 订阅父节点的stream
        var parentStream = _streamRegistry.GetStream(parentId);
        if (parentStream != null)
        {
            // 注意：事件类型过滤功能已废弃
            // 因为Protobuf不支持继承，无法在类型层面进行有效过滤
            // 所有事件过滤应在Agent的事件处理器内部基于事件内容进行

            // 创建过滤器：过滤掉自己发布的事件
            Func<EventEnvelope, bool>? combinedFilter = envelope =>
            {
                // 过滤掉自己发布的事件，避免循环
                if (envelope.PublisherId == Id.ToString())
                {
                    return false;
                }

                return true;
            };

            // Agent订阅父节点的stream，接收组内广播的事件
            _parentStreamSubscription = await parentStream.SubscribeAsync(
                async envelope =>
                {
                    // 从父stream接收到的事件，只需要处理，不需要继续传播
                    // 因为这个事件已经在父stream中广播了
                    // Direct call - no reflection needed since IGAgent defines HandleEventAsync
                    await Agent.HandleEventAsync(envelope, ct);
                },
                combinedFilter,
                ct);

            Logger.LogDebug("Agent {AgentId} subscribed to parent {ParentId} stream", Id, parentId);
        }
    }

    protected override async Task ClearParentAsync(CancellationToken ct = default)
    {
        // 调用基类方法清除父节点
        await base.ClearParentAsync(ct);

        // 取消订阅父节点的stream
        if (_parentStreamSubscription != null)
        {
            await _parentStreamSubscription.UnsubscribeAsync();
            _parentStreamSubscription = null;
            Logger.LogDebug("Agent {AgentId} unsubscribed from parent stream", Id);
        }
    }

    // ============ 抽象方法实现 ============

    /// <summary>
    /// 发送事件给自己（通过自己的 Stream）
    /// </summary>
    protected override async Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        Console.WriteLine($"ProtoActor {Id} SendToSelfAsync called with event {envelope.Id}");
        Logger.LogDebug("ProtoActor {AgentId} sending event to self via stream", Id);
        await _myStream.ProduceAsync(envelope, ct);
        Logger.LogDebug("ProtoActor {AgentId} sent event to self via stream", Id);
    }

    /// <summary>
    /// 发送事件到指定的 Actor（通过目标 Actor 的 Stream）
    /// </summary>
    protected override async Task SendEventToActorAsync(Guid actorId, EventEnvelope envelope, CancellationToken ct)
    {
        var targetStream = _streamRegistry.GetStream(actorId);
        if (targetStream != null)
        {
            await targetStream.ProduceAsync(envelope, ct);
        }
        else
        {
            Logger.LogWarning("Stream for actor {ActorId} not found", actorId);
        }
    }

    // ============ 生命周期 ============

    protected override async Task OnActivateAsync(CancellationToken ct)
    {
        Console.WriteLine($"ProtoActorGAgentActor.ActivateAsync called for agent {Id}");
        Logger.LogInformation("Activating agent {AgentId}", Id);

        // 订阅自己的 Stream
        await _myStream.SubscribeAsync<EventEnvelope>(
            async envelope =>
            {
                try
                {
                    Console.WriteLine($"ProtoActor {Id} received event {envelope.Id} from stream");
                    Logger.LogDebug("ProtoActor {AgentId} received event {EventId} from stream", Id, envelope.Id);
                    // Directly handle the event instead of routing it again to avoid infinite loop
                    // RouteEventAsync would call SendToSelfAsync which produces to stream again
                    await Agent.HandleEventAsync(envelope, ct);
                    Logger.LogDebug("ProtoActor {AgentId} handled event {EventId}", Id, envelope.Id);
                    Console.WriteLine($"ProtoActor {Id} handled event {envelope.Id}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ProtoActor {Id} ERROR handling event: {ex.Message}");
                    Logger.LogError(ex, "Error handling event {EventId}", envelope.Id);
                    throw;
                }
            },
            null, // No filter needed for self stream
            ct);

        Logger.LogInformation("ProtoActorGAgentActor {Id} activated and subscribed to stream", Id);

        // 更新活跃 Actor 计数
        var count = Interlocked.Increment(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct)
    {
        Logger.LogInformation("Deactivating agent {AgentId}", Id);

        // 停止 Proto.Actor
        _rootContext.Send(_actorPid, new Stop());

        // 从 Registry 中移除 Agent，以允许后续重新创建相同 ID 的 Actor
        _streamRegistry.Remove(Id);

        Logger.LogDebug("Agent {AgentId} removed from registry", Id);

        // 更新活跃 Actor 计数
        var count = Interlocked.Decrement(ref _activeActorCount);
        AgentMetrics.UpdateActiveActorCount(count);
        Logger.LogDebug("Active actor count: {Count}", count);
    }

    // RPC: Inherited from GAgentActorBase (uses shared RpcInvoker)
}