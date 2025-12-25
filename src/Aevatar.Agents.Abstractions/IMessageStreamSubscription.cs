namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream subscription handle
/// Allows managing and canceling subscriptions
/// </summary>
public interface IMessageStreamSubscription : IAsyncDisposable
{
    /// <summary>
    /// Subscription ID
    /// </summary>
    Guid SubscriptionId { get; }
    
    /// <summary>
    /// Associated Stream ID
    /// </summary>
    string StreamId { get; }
    
    /// <summary>
    /// Whether activated
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// Unsubscribe
    /// </summary>
    Task UnsubscribeAsync();
    
    /// <summary>
    /// Resume subscription (for reconnection scenarios)
    /// </summary>
    Task ResumeAsync();
}
