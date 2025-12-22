using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Subscription;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Runtime.Local.Subscription;

/// <summary>
/// Local runtime的订阅管理器实现
/// </summary>
public class LocalSubscriptionManager : BaseSubscriptionManager
{
    private readonly LocalMessageStreamRegistry _streamRegistry;
    // 保存原始事件处理器以支持重连
    private readonly Dictionary<string, Func<EventEnvelope, Task>> _eventHandlers = new();
    
    public LocalSubscriptionManager(
        LocalMessageStreamRegistry streamRegistry,
        ILogger<LocalSubscriptionManager>? logger = null)
        : base(logger)
    {
        _streamRegistry = streamRegistry ?? throw new ArgumentNullException(nameof(streamRegistry));
    }

    protected override async Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken)
    {
        Logger.LogDebug("Creating Local stream subscription: Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        // 获取父节点的stream（不自动创建，以便在stream不存在时触发重试）
        var parentStream = _streamRegistry.GetStream(parentId);
        
        if (parentStream == null)
        {
            throw new InvalidOperationException($"Cannot get stream for parent {parentId}");
        }
        
        // 创建订阅，包装事件处理器以添加错误处理
        var wrappedHandler = CreateWrappedEventHandler(eventHandler, childId, parentId);
        
        // 保存原始事件处理器以支持重连（使用wrapped handler）
        _eventHandlers[childId] = wrappedHandler;
        
        // 创建过滤器（可选）
        Func<EventEnvelope, bool>? filter = envelope =>
        {
            // 过滤掉子节点自己发布的事件，避免循环
            if (envelope.PublisherId == childId)
            {
                Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                    envelope.Id, childId);
                return false;
            }
            
            // 其他过滤逻辑...
            return true;
        };
        
        // 订阅父节点的stream
        var subscription = await parentStream.SubscribeAsync<EventEnvelope>(
            wrappedHandler, 
            filter, 
            cancellationToken);
        
        Logger.LogInformation("Successfully created Local stream subscription for Child {ChildId} -> Parent {ParentId}",
            childId, parentId);
        
        return subscription;
    }

    protected override async Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription)
    {
        if (subscription?.StreamSubscription == null)
        {
            return false;
        }
        
        // 对于Local runtime，检查stream是否还在registry中
        var parentStream = _streamRegistry.GetStream(subscription.ParentId);
        if (parentStream == null)
        {
            Logger.LogWarning("Parent stream {ParentId} not found in registry", subscription.ParentId);
            return false;
        }
        
        // 检查订阅是否还活跃
        // 这里可以添加更多的健康检查逻辑
        var isHealthy = subscription.StreamSubscription != null;
        
        if (!isHealthy)
        {
            Logger.LogWarning("Subscription {SubscriptionId} is unhealthy", subscription.SubscriptionId);
        }
        
        return isHealthy;
    }

    protected override async Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken)
    {
        Logger.LogInformation("Reconnecting Local stream subscription {SubscriptionId}",
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
                Logger.LogWarning(ex, "Error cleaning up old subscription during reconnect");
            }
        }
        
        // 重新获取父stream
        var parentStream = _streamRegistry.GetOrCreateStream(handle.ParentId);
        
        // 获取保存的事件处理器
        if (!_eventHandlers.TryGetValue(handle.ChildId, out var wrappedHandler))
        {
            throw new InvalidOperationException(
                $"Event handler for child {handle.ChildId} not found. Cannot reconnect.");
        }
        
        // 创建过滤器（与创建时相同）
        Func<EventEnvelope, bool>? filter = envelope =>
        {
            if (envelope.PublisherId == handle.ChildId)
            {
                Logger.LogTrace("Filtering out self-published event {EventId} for child {ChildId}",
                    envelope.Id, handle.ChildId);
                return false;
            }
            return true;
        };
        
        // 重新订阅
        var newSubscription = await parentStream.SubscribeAsync<EventEnvelope>(
            wrappedHandler,
            filter,
            cancellationToken);
        
        // 更新handle
        handle.StreamSubscription = newSubscription;
        handle.IsHealthy = true;
        handle.LastActivityAt = DateTime.UtcNow;
        
        Logger.LogInformation("Successfully reconnected subscription {SubscriptionId}", 
            handle.SubscriptionId);
    }

    /// <summary>
    /// 清理保存的事件处理器
    /// </summary>
    public void CleanupEventHandler(string childId)
    {
        if (_eventHandlers.Remove(childId))
        {
            Logger.LogDebug("Cleaned up event handler for child {ChildId}", childId);
        }
    }

    /// <summary>
    /// 创建包装的事件处理器，添加错误处理和日志
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
                Logger.LogTrace("Child {ChildId} processing event {EventId} from parent {ParentId}",
                    childId, envelope.Id, parentId);
                
                await originalHandler(envelope);
                
                // 更新订阅活动时间
                UpdateLastActivity(childId, parentId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Error processing event {EventId} in child {ChildId} from parent {ParentId}",
                    envelope.Id, childId, parentId);
                
                // 可以选择是否重新抛出异常
                // throw;
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
    /// 创建带健康检查的订阅
    /// </summary>
    public async Task<ISubscriptionHandle> SubscribeWithHealthMonitoringAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        TimeSpan healthCheckInterval,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        var subscription = await SubscribeWithRetryAsync(
            parentId, childId, eventHandler, retryPolicy, cancellationToken);
        
        // 启动健康监控任务
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(healthCheckInterval, cancellationToken);
                    
                    var isHealthy = await IsSubscriptionHealthyAsync(subscription);
                    
                    if (!isHealthy)
                    {
                        Logger.LogWarning("Subscription {SubscriptionId} is unhealthy, attempting to recover",
                            subscription.SubscriptionId);
                        
                        // 这里可以触发重连或其他恢复操作
                        // await ReconnectSubscriptionAsync(subscription, cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in health monitoring for subscription {SubscriptionId}",
                        subscription.SubscriptionId);
                }
            }
        }, cancellationToken);
        
        return subscription;
    }
}
