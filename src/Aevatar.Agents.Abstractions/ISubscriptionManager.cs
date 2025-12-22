using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions;

// ============================================================
//  Subscription Manager - Parent-Child Relationship Management
//
//  Design Notes:
//  - ISubscriptionManager: High-level subscription lifecycle
//    management with retry, health check, and reconnection
//  - ISubscriptionHandle: Represents an active subscription
//    with metadata (parent/child IDs, health status)
//  - IMessageStreamSubscription: Low-level stream subscription
//    handle returned by IMessageStream.SubscribeAsync()
//
//  Relationship:
//  ┌─────────────────────────────────────────────────────┐
//  │          ISubscriptionManager                        │
//  │   (orchestrates subscriptions with retry/health)    │
//  │                      │                              │
//  │                      ▼                              │
//  │          ISubscriptionHandle                        │
//  │   (tracks parent-child relationship + metadata)     │
//  │                      │                              │
//  │                      ▼                              │
//  │       IMessageStreamSubscription                    │
//  │   (actual stream subscription, owned by handle)     │
//  └─────────────────────────────────────────────────────┘
//
//  Usage:
//  - Use ISubscriptionManager for robust parent-child subscriptions
//  - Use IMessageStream.SubscribeAsync directly for simple cases
//  - ISubscriptionHandle.StreamSubscription provides access to
//    underlying stream subscription when needed
// ============================================================

/// <summary>
/// Unified subscription manager interface.
/// Provides parent-child subscription management with retry strategy and health checking.
/// </summary>
public interface ISubscriptionManager
{
    /// <summary>
    /// 创建订阅（带重试策略）
    /// </summary>
    /// <param name="parentId">父节点ID</param>
    /// <param name="childId">子节点ID</param>
    /// <param name="eventHandler">事件处理器</param>
    /// <param name="retryPolicy">重试策略</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>订阅句柄</returns>
    Task<ISubscriptionHandle> SubscribeWithRetryAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查订阅健康状态
    /// </summary>
    /// <param name="subscription">订阅句柄</param>
    /// <returns>是否健康</returns>
    Task<bool> IsSubscriptionHealthyAsync(ISubscriptionHandle subscription);

    /// <summary>
    /// 重新连接订阅
    /// </summary>
    /// <param name="subscription">订阅句柄</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task ReconnectSubscriptionAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消订阅
    /// </summary>
    /// <param name="subscription">订阅句柄</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UnsubscribeAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有活跃订阅
    /// </summary>
    /// <returns>活跃订阅列表</returns>
    Task<IReadOnlyList<ISubscriptionHandle>> GetActiveSubscriptionsAsync();
}

/// <summary>
/// 订阅句柄
/// </summary>
public interface ISubscriptionHandle
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    Guid SubscriptionId { get; }

    /// <summary>
    /// 父节点ID
    /// </summary>
    string ParentId { get; }

    /// <summary>
    /// 子节点ID
    /// </summary>
    string ChildId { get; }

    /// <summary>
    /// 订阅创建时间
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// 最后活动时间
    /// </summary>
    DateTime LastActivityAt { get; }

    /// <summary>
    /// 是否健康
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// 重试次数
    /// </summary>
    int RetryCount { get; }

    /// <summary>
    /// 底层的流订阅（如果有）
    /// </summary>
    IMessageStreamSubscription? StreamSubscription { get; }
}

/// <summary>
/// 重试策略接口
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// 最大重试次数
    /// </summary>
    int MaxRetries { get; }

    /// <summary>
    /// 计算下次重试的延迟
    /// </summary>
    /// <param name="attemptNumber">当前尝试次数</param>
    /// <returns>延迟时间</returns>
    TimeSpan GetDelay(int attemptNumber);

    /// <summary>
    /// 是否应该重试
    /// </summary>
    /// <param name="exception">异常</param>
    /// <param name="attemptNumber">当前尝试次数</param>
    /// <returns>是否重试</returns>
    bool ShouldRetry(Exception exception, int attemptNumber);
}

/// <summary>
/// 订阅健康状态
/// </summary>
public enum SubscriptionHealth
{
    /// <summary>
    /// 健康
    /// </summary>
    Healthy,

    /// <summary>
    /// 降级（部分功能可用）
    /// </summary>
    Degraded,

    /// <summary>
    /// 不健康
    /// </summary>
    Unhealthy,

    /// <summary>
    /// 断开连接
    /// </summary>
    Disconnected
}

