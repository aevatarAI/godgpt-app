using Aevatar.Agents.Abstractions;
using Aevatar.Payment.Agents.Protos;

namespace Aevatar.Payment.Agents;

/// <summary>
/// User-level payment index agent - manages active subscriptions and platform customer IDs.
/// ID format: UserId (Guid)
/// 
/// Design principles:
/// - Keep extremely lightweight, no historical data storage
/// - Acts as event hub for business layer (PublishAsync Down to children)
/// - Business agents register via IGAgentActorManager.LinkParentChildAsync
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IPaymentIndexGAgent : IGAgent
{
    // ========== Event Broadcasting (Down to Business Agents) ==========
    
    /// <summary>
    /// Notify payment completed - broadcasts to all registered business agents.
    /// Business agents should register via IGAgentActorManager.LinkParentChildAsync.
    /// </summary>
    Task NotifyPaymentCompletedAsync(PaymentCompletedEvent evt);
    
    /// <summary>
    /// Notify payment failed - broadcasts to all registered business agents.
    /// </summary>
    Task NotifyPaymentFailedAsync(PaymentFailedEvent evt);
    
    /// <summary>
    /// Notify refund completed - broadcasts to all registered business agents.
    /// </summary>
    Task NotifyRefundCompletedAsync(RefundCompletedEvent evt);
    
    // ========== Platform Customer ID ==========
    
    /// <summary>
    /// Get platform customer ID (e.g., Stripe customer ID)
    /// Returns empty string if not found (nullable not supported by RPC)
    /// </summary>
    Task<string> GetPlatformCustomerIdAsync(PaymentPlatform platform);
    
    /// <summary>
    /// Set platform customer ID
    /// </summary>
    Task SetPlatformCustomerIdAsync(PaymentPlatform platform, string customerId);
    
    // ========== Active Subscription Management ==========
    
    /// <summary>
    /// Add an active subscription to the index (uses Protobuf type for RPC)
    /// </summary>
    Task AddActiveSubscriptionAsync(ActiveSubscriptionProto subscription);
    
    /// <summary>
    /// Update subscription period end time
    /// </summary>
    Task UpdateSubscriptionPeriodEndAsync(string paymentId, DateTime periodEnd);
    
    /// <summary>
    /// Remove an active subscription (when cancelled or expired)
    /// </summary>
    Task RemoveActiveSubscriptionAsync(string paymentId);
    
    // ========== Query (Active Subscriptions Only, Fast) ==========
    
    /// <summary>
    /// Get all active subscriptions (returns Protobuf wrapper for RPC)
    /// </summary>
    Task<ActiveSubscriptionListResponse> GetActiveSubscriptionsAsync();
    
    /// <summary>
    /// Get active subscriptions by business type (returns Protobuf wrapper for RPC)
    /// </summary>
    Task<ActiveSubscriptionListResponse> GetActiveSubscriptionsByBusinessAsync(string businessType);
    
    /// <summary>
    /// Check if user has any active subscription
    /// </summary>
    Task<bool> HasActiveSubscriptionAsync(string? businessType = null);
    
    // ========== Simple Statistics ==========
    
    /// <summary>
    /// Get total payment count
    /// </summary>
    Task<int> GetTotalPaymentCountAsync();
    
    /// <summary>
    /// Increment payment count (called when new payment is created)
    /// </summary>
    Task IncrementPaymentCountAsync();
    
    // ========== Management ==========
    
    /// <summary>
    /// Clear all data (for testing/reset)
    /// </summary>
    Task ClearAllAsync();
}

