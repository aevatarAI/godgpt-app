using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.Subscription;

/// <summary>
/// Base subscription manager implementation
/// Provides unified subscription management, retry, and health check mechanism
/// </summary>
public abstract class BaseSubscriptionManager : ISubscriptionManager
{
    protected readonly ILogger Logger;
    protected readonly ConcurrentDictionary<Guid, SubscriptionHandle> _subscriptions = new();
    
    protected BaseSubscriptionManager(ILogger? logger = null)
    {
        Logger = logger ?? NullLogger.Instance;
    }

    public async Task<ISubscriptionHandle> SubscribeWithRetryAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        IRetryPolicy? retryPolicy = null,
        CancellationToken cancellationToken = default)
    {
        retryPolicy ??= RetryPolicyFactory.CreateDefault();
        
        var subscriptionId = Guid.NewGuid();
        var handle = new SubscriptionHandle(subscriptionId, parentId, childId);
        
        Logger.LogDebug("Creating subscription {SubscriptionId}: Child {ChildId} -> Parent {ParentId}",
            subscriptionId, childId, parentId);
        
        Exception? lastException = null;
        
        for (var attempt = 1; attempt <= retryPolicy.MaxRetries + 1; attempt++)
        {
            try
            {
                // Call specific implementation to create subscription
                var streamSubscription = await CreateStreamSubscriptionAsync(
                    parentId, childId, eventHandler, cancellationToken);
                
                handle.StreamSubscription = streamSubscription;
                handle.IsHealthy = true;
                handle.LastActivityAt = DateTime.UtcNow;
                
                _subscriptions[subscriptionId] = handle;
                
                Logger.LogInformation(
                    "Successfully created subscription {SubscriptionId} after {Attempts} attempt(s)",
                    subscriptionId, attempt);
                
                return handle;
            }
            catch (Exception ex)
            {
                lastException = ex;
                handle.RetryCount = attempt;
                
                // Check if should retry
                if (attempt <= retryPolicy.MaxRetries && retryPolicy.ShouldRetry(ex, attempt))
                {
                    var delay = retryPolicy.GetDelay(attempt);
                    
                    Logger.LogWarning(ex,
                        "Failed to create subscription on attempt {Attempt}/{MaxRetries}. Retrying after {Delay}ms",
                        attempt, retryPolicy.MaxRetries + 1, delay.TotalMilliseconds);
                    
                    await Task.Delay(delay, cancellationToken);
                }
                else
                {
                    // Should not retry, break loop
                    break;
                }
            }
        }
        
        // All retries failed
        var errorMessage = $"Failed to create subscription after {retryPolicy.MaxRetries + 1} attempts";
        Logger.LogError(lastException, errorMessage);
        throw new InvalidOperationException(errorMessage, lastException);
    }

    public async Task<bool> IsSubscriptionHealthyAsync(ISubscriptionHandle subscription)
    {
        if (subscription == null)
        {
            return false;
        }
        
        // Check if in management list
        if (!_subscriptions.ContainsKey(subscription.SubscriptionId))
        {
            return false;
        }
        
        // Call specific implementation to check health status
        var isHealthy = await CheckStreamHealthAsync(subscription);
        
        // Update health status
        if (_subscriptions.TryGetValue(subscription.SubscriptionId, out var handle))
        {
            handle.IsHealthy = isHealthy;
            if (isHealthy)
            {
                handle.LastActivityAt = DateTime.UtcNow;
            }
        }
        
        return isHealthy;
    }

    public async Task ReconnectSubscriptionAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default)
    {
        if (subscription == null)
        {
            throw new ArgumentNullException(nameof(subscription));
        }
        
        if (!_subscriptions.TryGetValue(subscription.SubscriptionId, out var handle))
        {
            throw new InvalidOperationException($"Subscription {subscription.SubscriptionId} not found");
        }
        
        Logger.LogInformation("Reconnecting subscription {SubscriptionId}", subscription.SubscriptionId);
        
        try
        {
            // Try cleaning up old connection first
            if (handle.StreamSubscription != null)
            {
                await handle.StreamSubscription.UnsubscribeAsync();
            }
            
            // Recreate subscription
            // Note: Need to save original eventHandler, but current design doesn't save it
            // In actual use, may need to save eventHandler in SubscriptionHandle
            await ReconnectStreamAsync(handle, cancellationToken);
            
            handle.IsHealthy = true;
            handle.LastActivityAt = DateTime.UtcNow;
            
            Logger.LogInformation("Successfully reconnected subscription {SubscriptionId}",
                subscription.SubscriptionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to reconnect subscription {SubscriptionId}",
                subscription.SubscriptionId);
            
            handle.IsHealthy = false;
            throw;
        }
    }

    public async Task UnsubscribeAsync(
        ISubscriptionHandle subscription,
        CancellationToken cancellationToken = default)
    {
        if (subscription == null)
        {
            return;
        }
        
        if (_subscriptions.TryRemove(subscription.SubscriptionId, out var handle))
        {
            Logger.LogDebug("Unsubscribing {SubscriptionId}", subscription.SubscriptionId);
            
            try
            {
                if (handle.StreamSubscription != null)
                {
                    await handle.StreamSubscription.UnsubscribeAsync();
                }
                
                Logger.LogInformation("Successfully unsubscribed {SubscriptionId}",
                    subscription.SubscriptionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during unsubscribe of {SubscriptionId}",
                    subscription.SubscriptionId);
            }
        }
    }

    public Task<IReadOnlyList<ISubscriptionHandle>> GetActiveSubscriptionsAsync()
    {
        var activeSubscriptions = _subscriptions.Values
            .Where(s => s.IsHealthy)
            .Cast<ISubscriptionHandle>()
            .ToList();
        
        return Task.FromResult<IReadOnlyList<ISubscriptionHandle>>(activeSubscriptions);
    }

    /// <summary>
    /// Create stream subscription (implemented by specific runtime)
    /// </summary>
    protected abstract Task<IMessageStreamSubscription?> CreateStreamSubscriptionAsync(
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        CancellationToken cancellationToken);

    /// <summary>
    /// Check stream health status (implemented by specific runtime)
    /// </summary>
    protected abstract Task<bool> CheckStreamHealthAsync(ISubscriptionHandle subscription);

    /// <summary>
    /// Reconnect stream (implemented by specific runtime)
    /// </summary>
    protected abstract Task ReconnectStreamAsync(
        SubscriptionHandle handle,
        CancellationToken cancellationToken);

    /// <summary>
    /// Internal subscription handle implementation
    /// </summary>
    protected class SubscriptionHandle : ISubscriptionHandle
    {
        public SubscriptionHandle(Guid subscriptionId, string parentId, string childId)
        {
            SubscriptionId = subscriptionId;
            ParentId = parentId;
            ChildId = childId;
            CreatedAt = DateTime.UtcNow;
            LastActivityAt = CreatedAt;
        }

        public Guid SubscriptionId { get; }
        public string ParentId { get; }
        public string ChildId { get; }
        public DateTime CreatedAt { get; }
        public DateTime LastActivityAt { get; set; }
        public bool IsHealthy { get; set; }
        public int RetryCount { get; set; }
        public IMessageStreamSubscription? StreamSubscription { get; set; }
    }
}

/// <summary>
/// Subscription manager extension methods
/// </summary>
public static class SubscriptionManagerExtensions
{
    /// <summary>
    /// Subscribe and automatically manage health status
    /// </summary>
    public static async Task<ISubscriptionHandle> SubscribeWithHealthCheckAsync(
        this ISubscriptionManager manager,
        string parentId,
        string childId,
        Func<EventEnvelope, Task> eventHandler,
        TimeSpan healthCheckInterval,
        CancellationToken cancellationToken = default)
    {
        var subscription = await manager.SubscribeWithRetryAsync(
            parentId, childId, eventHandler, cancellationToken: cancellationToken);

        // Start health check task
        _ = Task.Run(async () =>
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(healthCheckInterval, cancellationToken);

                if (!await manager.IsSubscriptionHealthyAsync(subscription))
                {
                    try
                    {
                        await manager.ReconnectSubscriptionAsync(subscription, cancellationToken);
                    }
                    catch
                    {
                        // Reconnection failed, try again next time
                    }
                }
            }
        }, cancellationToken);

        return subscription;
    }

    /// <summary>
    /// Batch unsubscribe
    /// </summary>
    public static async Task UnsubscribeAllAsync(
        this ISubscriptionManager manager,
        CancellationToken cancellationToken = default)
    {
        var subscriptions = await manager.GetActiveSubscriptionsAsync();

        await Task.WhenAll(
            subscriptions.Select(s => manager.UnsubscribeAsync(s, cancellationToken))
        );
    }
}