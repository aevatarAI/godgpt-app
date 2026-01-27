using Aevatar.Agents.Abstractions;
using Aevatar.Payment.Agents.Protos;

namespace Aevatar.Payment.Agents;

/// <summary>
/// Order-level payment record agent - manages complete lifecycle of a single payment.
/// ID format: payment_{platform}_{subscriptionId} (for direct webhook routing)
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IPaymentRecordGAgent : IGAgent
{
    // ========== Initialization ==========
    
    /// <summary>
    /// Initialize the payment record (called once when creating)
    /// Uses Protobuf type for RPC compatibility.
    /// </summary>
    Task InitializeAsync(CreatePaymentRequestProto request);
    
    /// <summary>
    /// Check if this agent has been initialized
    /// </summary>
    Task<bool> IsInitializedAsync();
    
    // ========== Query ==========
    
    /// <summary>
    /// Get complete payment record state (Protobuf for RPC compatibility)
    /// </summary>
    Task<PaymentRecordStateProto> GetRecordStateAsync();
    
    /// <summary>
    /// Get current status
    /// </summary>
    Task<PaymentStatus> GetStatusAsync();
    
    /// <summary>
    /// Get user ID (for routing back to index)
    /// </summary>
    Task<string> GetUserIdAsync();
    
    /// <summary>
    /// Get callback agent ID for point-to-point notification (optional)
    /// </summary>
    Task<Guid?> GetCallbackAgentIdAsync();
    
    // ========== Status Update ==========
    
    /// <summary>
    /// Update payment status
    /// </summary>
    Task UpdateStatusAsync(PaymentStatus status, string? reason = null);
    
    /// <summary>
    /// Update subscription period
    /// </summary>
    Task UpdatePeriodAsync(DateTime periodStart, DateTime periodEnd);
    
    /// <summary>
    /// Update platform subscription ID (called by webhook when real subscriptionId is available)
    /// </summary>
    Task UpdateSubscriptionIdAsync(string subscriptionId);
    
    /// <summary>
    /// Mark payment as completed
    /// </summary>
    Task CompleteAsync();
    
    /// <summary>
    /// Cancel the payment/subscription
    /// </summary>
    Task CancelAsync(string? reason = null);
    
    // ========== Transaction Management ==========
    
    /// <summary>
    /// Add a new transaction (renewal, refund, etc.)
    /// </summary>
    Task<string> AddTransactionAsync(Transaction transaction);
    
    /// <summary>
    /// Update transaction status
    /// </summary>
    Task UpdateTransactionStatusAsync(string transactionId, PaymentStatus status);
    
    // ========== Renewal Processing ==========
    
    /// <summary>
    /// Process subscription renewal
    /// Uses Protobuf type for RPC compatibility.
    /// </summary>
    Task ProcessRenewalAsync(Protos.RenewalInfoProto renewal);
    
    // ========== Refund Processing ==========
    
    /// <summary>
    /// Process full refund
    /// Uses Protobuf type for RPC compatibility.
    /// </summary>
    Task ProcessRefundAsync(Protos.RefundInfoProto refund);
    
    /// <summary>
    /// Process partial refund for a specific transaction
    /// </summary>
    Task ProcessPartialRefundAsync(string transactionId, long refundAmount, string reason);
    
    // ========== Callback Notification ==========
    
    /// <summary>
    /// Notify callback agent about payment completion (if configured).
    /// This uses point-to-point SendTo for direct delivery.
    /// </summary>
    Task NotifyCallbackAgentAsync(Protos.PaymentCompletedEvent evt);
    
    /// <summary>
    /// Notify callback agent about payment failure (if configured).
    /// </summary>
    Task NotifyCallbackAgentAsync(Protos.PaymentFailedEvent evt);
    
    /// <summary>
    /// Notify callback agent about refund completion (if configured).
    /// </summary>
    Task NotifyCallbackAgentAsync(Protos.RefundCompletedEvent evt);
}

// Note: RenewalInfo and RefundInfo are now defined as Protobuf messages
// (RenewalInfoProto and RefundInfoProto) in payment_record.proto
// Use Protos.RenewalInfoProto and Protos.RefundInfoProto instead

