using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Microsoft.Extensions.Logging;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Subscription;

/// <summary>
/// Orleans运行时的订阅管理器实现
/// </summary>
public class OrleansSubscriptionManager : BaseSubscriptionManager
{
    private readonly IStreamProvider _streamProvider;
    private readonly string _streamNamespace;
    
    public OrleansSubscriptionManager(
        IStreamProvider streamProvider,
        string streamNamespace = AevatarAgentsOrleansConstants.StreamNamespace,
        ILogger<OrleansSubscriptionManager>? logger = null)
        : base(logger)
    {
        _streamProvider = streamProvider ?? throw new ArgumentNullException(nameof(streamProvider));
        _streamNamespace = streamNamespace;
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating Orleans stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        try
        {
            // 获取父节点的Orleans stream
            var streamId = StreamId.Create(_streamNamespace, parentId);
            var parentStream = _streamProvider.GetStream<byte[]>(streamId);
            
            // 包装为OrleansMessageStream
            var messageStream = new OrleansMessageStream(parentId, parentStream);
            
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
                
                // Orleans特定：检查是否是从父stream接收的BOTH事件
                // 需要特殊处理以防止循环
                if (envelope.Direction == EventDirection.Both)
                {
                    // 如果Publishers列表已包含父节点，说明这是从父stream来的
                    if (envelope.Publishers.Contains(parentId))
                    {
                        Logger.LogTrace("BOTH event {EventId} from parent stream, will be converted to DOWN-only",
                            envelope.Id);
                        // 注意：实际的方向转换应该在处理器中进行
                    }
                }
                
                return true;
            };
            
            // 包装事件处理器，添加Orleans特定的处理逻辑
            var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
            
            // 创建订阅
            var subscription = await messageStream.SubscribeAsync<EventEnvelope>(
                wrappedHandler,
                filter,
                cancellationToken);
            
            Logger.LogInformation(
                "Successfully created Orleans stream subscription for Child {ChildId} -> Parent {ParentId}",
                childId, parentId);
            
            return subscription;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "Failed to create Orleans stream subscription for Child {ChildId} -> Parent {ParentId}",
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
        
        // Orleans的订阅健康状态主要通过StreamSubscriptionHandle的IsActive属性判断
        if (subscription.StreamSubscription is OrleansMessageStreamSubscription orleansSubscription)
        {
            var isHealthy = orleansSubscription.IsActive;
            
            if (!isHealthy)
            {
                Logger.LogWarning("Orleans subscription {SubscriptionId} is inactive", 
                    subscription.SubscriptionId);
            }
            
            return isHealthy;
        }
        
        // 如果不是Orleans订阅类型，尝试通过其他方式判断
        try
        {
            // 可以尝试获取stream来验证连接
            var streamId = StreamId.Create(_streamNamespace, subscription.ParentId);
            var stream = _streamProvider.GetStream<byte[]>(streamId);
            
            // 如果能成功获取stream，认为是健康的
            return stream != null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "Failed to check health for Orleans subscription {SubscriptionId}",
                subscription.SubscriptionId);
            return false;
        }
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting Orleans stream subscription {SubscriptionId}",
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
                Logger.LogWarning(ex, "Error cleaning up old Orleans subscription during reconnect");
            }
        }
        
        // Orleans的重连策略：
        // 1. 尝试使用ResumeAsync恢复订阅
        // 2. 如果失败，重新创建订阅
        
        if (handle.StreamSubscription is OrleansMessageStreamSubscription orleansSubscription)
        {
            try
            {
                // 尝试恢复订阅
                await orleansSubscription.ResumeAsync();
                handle.IsHealthy = true;
                handle.LastActivityAt = DateTime.UtcNow;
                
                Logger.LogInformation("Successfully resumed Orleans subscription {SubscriptionId}",
                    handle.SubscriptionId);
                return;
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to resume Orleans subscription, will recreate");
            }
        }
        
        // 如果恢复失败或不是Orleans订阅，抛出异常
        // 因为我们需要原始的eventHandler来重新创建订阅
        throw new NotImplementedException(
            "Full reconnection requires saving the original event handler. " +
            "Consider using ResumeAsync() for Orleans subscriptions or storing the handler in SubscriptionHandle.");
    }

    /// <summary>
    /// 创建包装的事件处理器，添加Orleans特定的处理逻辑
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
                Logger.LogTrace("Orleans Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                // Orleans特定：处理BOTH方向的事件
                // 如果是从父stream接收的BOTH事件，需要转换为DOWN-only
                if (envelope.Direction == EventDirection.Both && 
                    envelope.Publishers.Contains(parentId))
                {
                    Logger.LogDebug(
                        "Converting BOTH event {EventId} to DOWN-only for Orleans child {ChildId}",
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
                    "Orleans: Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // Orleans的错误处理策略：不重新抛出异常，避免影响stream
                // 错误已记录，继续处理后续事件
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
    /// 创建持久化的订阅（Orleans支持持久化订阅）
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeWithPersistenceAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        string? subscriptionId = null,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        // Orleans支持使用特定的subscriptionId创建持久化订阅
        // 这样在系统重启后可以恢复订阅
        
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        // TODO: 可以将订阅信息保存到Orleans存储中
        // 以支持系统重启后的自动恢复
        
        return subscription;
    }

    /// <summary>
    /// 批量创建订阅
    /// </summary>
    public async Task<IReadOnlyList<ISubscriptionHandle>> SubscribeBatchAsync(
        IReadOnlyList<(string ParentId, string ChildId)> subscriptions,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var tasks = subscriptions.Select(s => 
            SubscribeWithRetryAsync(s.ParentId, s.ChildId, eventHandler, retryPolicy, cancellationToken));
        
        var results = await Task.WhenAll(tasks);
        return results;
    }
}

