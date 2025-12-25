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
    /// Create subscription (with retry policy)
    /// </summary>
    /// <param name="parentId">Parent node ID</param>
    /// <param name="childId">Child node ID</param>
    /// <param name="eventHandler">Event handler</param>
    /// <param name="retryPolicy">Retry policy</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Subscription handle</returns>
    Task<ISubscriptionHandle> SubscribeWithRetryAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check subscription health status
    /// </summary>
    /// <param name="subscription">Subscription handle</param>
    /// <returns>Whether healthy</returns>
    Task<bool> IsSubscriptionHealthyAsync(ISubscriptionHandle subscription);

    /// <summary>
    /// Reconnect subscription
    /// </summary>
    /// <param name="subscription">Subscription handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ReconnectSubscriptionAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unsubscribe
    /// </summary>
    /// <param name="subscription">Subscription handle</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnsubscribeAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active subscriptions
    /// </summary>
    /// <returns>Active subscription list</returns>
    Task<IReadOnlyList<ISubscriptionHandle>> GetActiveSubscriptionsAsync();
}

/// <summary>
/// Subscription handle
/// </summary>
public interface ISubscriptionHandle
{
    /// <summary>
    /// Subscription ID
    /// </summary>
    Guid SubscriptionId { get; }

    /// <summary>
    /// Parent node ID
    /// </summary>
    string ParentId { get; }

    /// <summary>
    /// Child node ID
    /// </summary>
    string ChildId { get; }

    /// <summary>
    /// Subscription creation time
    /// </summary>
    DateTime CreatedAt { get; }

    /// <summary>
    /// Last activity time
    /// </summary>
    DateTime LastActivityAt { get; }

    /// <summary>
    /// Whether healthy
    /// </summary>
    bool IsHealthy { get; }

    /// <summary>
    /// Retry count
    /// </summary>
    int RetryCount { get; }

    /// <summary>
    /// Underlying stream subscription (if any)
    /// </summary>
    IMessageStreamSubscription? StreamSubscription { get; }
}

/// <summary>
/// Retry policy interface
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Maximum retry count
    /// </summary>
    int MaxRetries { get; }

    /// <summary>
    /// Calculate delay for next retry
    /// </summary>
    /// <param name="attemptNumber">Current attempt number</param>
    /// <returns>Delay duration</returns>
    TimeSpan GetDelay(int attemptNumber);

    /// <summary>
    /// Whether should retry
    /// </summary>
    /// <param name="exception">Exception</param>
    /// <param name="attemptNumber">Current attempt number</param>
    /// <returns>Whether to retry</returns>
    bool ShouldRetry(Exception exception, int attemptNumber);
}

/// <summary>
/// Subscription health status
/// </summary>
public enum SubscriptionHealth
{
    /// <summary>
    /// Healthy
    /// </summary>
    Healthy,

    /// <summary>
    /// Degraded (partial functionality available)
    /// </summary>
    Degraded,

    /// <summary>
    /// Unhealthy
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Disconnected
    /// </summary>
    Disconnected
}

