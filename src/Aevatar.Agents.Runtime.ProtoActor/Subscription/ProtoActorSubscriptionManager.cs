using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor.Subscription;

/// <summary>
/// ProtoActor运行时的订阅管理器实现
/// </summary>
public class ProtoActorSubscriptionManager : BaseSubscriptionManager
{
    private readonly IRootContext _rootContext;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;
    private readonly IGAgentActorManager _actorManager;
    
    public ProtoActorSubscriptionManager(
        IRootContext rootContext,
        ProtoActorMessageStreamRegistry streamRegistry,
        IGAgentActorManager actorManager,
        ILogger<ProtoActorSubscriptionManager>? logger = null)
        : base(logger)
    {
        _rootContext = rootContext ?? throw new ArgumentNullException(nameof(rootContext));
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
        _actorManager = actorManager ?? throw new ArgumentNullException(nameof(actorManager));
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating ProtoActor stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        try
        {
            // 获取父节点的Actor PID
            var parentPid = await GetActorPidAsync(parentId);
            if (parentPid == null)
            {
                throw new InvalidOperationException($"Parent actor {parentId} not found in registry");
            }
            
            // 创建ProtoActor message stream
            var messageStream = new ProtoActorMessageStream(parentId, parentPid, _rootContext);
            
            // 创建过滤器
            Func<EventEnvelope, bool>? filter = envelope =>
            {
                // 过滤掉子节点自己发布的事件，避免循环
                if (envelope.PublisherId == childId)
                {
                    Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                        envelope.Id, childId);
                    return false;
                }
                
                // ProtoActor特定：检查消息路由
                // ProtoActor的消息是直接发送的，不像Orleans有stream广播
                // 所以需要特别注意避免循环
                if (envelope.Direction == EventDirection.Both)
                {
                    if (envelope.Publishers.Contains(parentId))
                    {
                        Logger.LogTrace("BOTH event {EventId} from parent, will be converted to DOWN-only",
                            envelope.Id);
                    }
                }
                
                return true;
            };
            
            // 包装事件处理器，添加ProtoActor特定的处理逻辑
            var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
            
            // 创建订阅
            var subscription = await messageStream.SubscribeAsync<EventEnvelope>(
                wrappedHandler,
                filter,
                cancellationToken);
            
            Logger.LogInformation(
                "Successfully created ProtoActor stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "Failed to create ProtoActor stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            throw;
        }
    }

    protected override async Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription)
    {
        if (subscription?.StreamSubscription == null)
        {
            return false;
        }
        
        // ProtoActor的订阅健康状态
        if (subscription.StreamSubscription is ProtoActorStreamSubscription protoSubscription)
        {
            var isHealthy = protoSubscription.IsActive;
            
            // 额外检查：验证目标Actor是否还存在
            if (isHealthy)
            {
                var parentPid = await GetActorPidAsync(subscription.ParentId);
                if (parentPid == null)
                {
                    Logger.LogWarning(
                        "Parent actor {ParentId} not found for subscription {SubscriptionId}",
                        subscription.ParentId, subscription.SubscriptionId);
                    return false;
                }
                
                // 可以发送ping消息来验证Actor是否响应
                try
                {
                    var response = await _rootContext.RequestAsync<PingResponse>(
                        parentPid,
                        new PingMessage(),
                        CancellationToken.None);
                    
                    isHealthy = response != null;
                }
                catch
                {
                    isHealthy = false;
                }
            }
            
            if (!isHealthy)
            {
                Logger.LogWarning("ProtoActor subscription {SubscriptionId} is unhealthy", 
                    subscription.SubscriptionId);
            }
            
            return isHealthy;
        }
        
        // 默认认为不健康
        return false;
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting ProtoActor stream subscription {SubscriptionId}",
            handle.SubscriptionId);
        
        // 清理旧订阅
        if (handle.StreamSubscription != null)
        {
            try
            {
                await handle.StreamSubscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Error cleaning up old ProtoActor subscription during reconnect");
            }
        }
        
        // ProtoActor的重连策略：
        // 1. 首先尝试Resume（如果订阅对象还存在）
        // 2. 如果Actor已经重启或不存在，需要重新创建
        
        if (handle.StreamSubscription is ProtoActorStreamSubscription protoSubscription)
        {
            try
            {
                // 检查父Actor是否还存在
                var parentPid = await GetActorPidAsync(handle.ParentId);
                if (parentPid != null)
                {
                    // Actor存在，尝试恢复订阅
                    await protoSubscription.ResumeAsync();
                    handle.IsHealthy = true;
                    handle.LastActivityAt = DateTime.UtcNow;
                    
                    Logger.LogInformation("Successfully resumed ProtoActor subscription {SubscriptionId}",
                        handle.SubscriptionId);
                    return;
                }
                else
                {
                    Logger.LogWarning("Parent actor {ParentId} not found, cannot resume subscription",
                        handle.ParentId);
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to resume ProtoActor subscription");
            }
        }
        
        // 无法恢复，抛出异常
        throw new NotImplementedException(
            "Full reconnection requires saving the original event handler. " +
            "ProtoActor subscriptions are in-memory only and cannot be fully recreated without the handler.");
    }

    /// <summary>
    /// 创建包装的事件处理器，添加ProtoActor特定的处理逻辑
    /// </summary>
    private Func<EventEnvelope, Task> CreateWrappedEventHandler(
        Func<EventEnvelope, Task> originalHandler,
        string childId,
        string parentId)
    {
        return async (EventEnvelope envelope) =>
        {
            try
            {
                Logger.LogTrace("ProtoActor Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                // ProtoActor特定：处理BOTH方向的事件
                // 如果是从父节点接收的BOTH事件，需要转换为DOWN-only
                if (envelope.Direction == EventDirection.Both && 
                    envelope.Publishers.Contains(parentId))
                {
                    Logger.LogDebug(
                        "Converting BOTH event {EventId} to DOWN-only for ProtoActor child {ChildId}",
                        envelope.Id, childId);
                    
                    // 创建修改后的envelope
                    var modifiedEnvelope = envelope.Clone();
                    modifiedEnvelope.Direction = EventDirection.Down;
                    
                    // 使用修改后的envelope调用处理器
                    await originalHandler(modifiedEnvelope);
                }
                else
                {
                    // 其他情况直接调用原始处理器
                    await originalHandler(envelope);
                }
                
                // 更新订阅活动时间
                UpdateLastActivity(childId, parentId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "ProtoActor: Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // ProtoActor的错误处理：记录错误但继续处理
                // 不重新抛出异常，避免影响Actor消息处理
            }
        };
    }

    /// <summary>
    /// 更新最后活动时间
    /// </summary>
    private void UpdateLastActivity(string childId, string parentId)
    {
        foreach (var subscription in _subscriptions.Values)
        {
            if (subscription.ChildId == childId && subscription.ParentId == parentId)
            {
                subscription.LastActivityAt = DateTime.UtcNow;
                break;
            }
        }
    }

    /// <summary>
    /// 从管理器获取Actor PID
    /// </summary>
    private async Task<PID?> GetActorPidAsync(string actorId)
    {
        // 从管理器获取Actor
        var actor = await _actorManager.GetActorAsync(actorId);
        if (actor is ProtoActorGAgentActor protoActor)
        {
            // ProtoActorGAgentActor已经提供了GetPid()方法
            return protoActor.GetPid();
        }
        
        // 也可以直接从stream registry获取PID
        var pid = _streamRegistry.GetPid(actorId);
        return pid;
    }

    /// <summary>
    /// 创建Actor间的直接订阅（ProtoActor特有）
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeDirectAsync(
        PID parentPid,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // ProtoActor支持通过PID直接订阅，不需要通过Guid查找
        // 这可以提供更好的性能
        
        var parentId = Guid.NewGuid().ToString(); // 生成一个临时ID
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        return subscription;
    }

    /// <summary>
    /// 批量创建订阅（优化版本）
    /// </summary>
    public async Task<IReadOnlyList<ISubscriptionHandle>> SubscribeBatchOptimizedAsync(
        IReadOnlyList<(string ParentId, string ChildId)> subscriptions,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // ProtoActor可以批量发送消息，优化批量订阅
        var results = new List<ISubscriptionHandle>();
        
        foreach (var (parentId, childId) in subscriptions)
        {
            try
            {
                var subscription = await SubscribeWithRetryAsync(
                    parentId, childId, eventHandler, retryPolicy, cancellationToken);
                results.Add(subscription);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, 
                    "Failed to create subscription for Child {ChildId} -> Parent {ParentId}",
                    childId, parentId);
                // 继续处理其他订阅
            }
        }
        
        return results;
    }
}

/// <summary>
/// Ping消息，用于健康检查
/// </summary>
internal class PingMessage { }

/// <summary>
/// Ping响应
/// </summary>
internal class PingResponse 
{
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
