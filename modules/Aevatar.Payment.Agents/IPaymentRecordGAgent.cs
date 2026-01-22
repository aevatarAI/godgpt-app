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
    /// Get complete payment record (local use only - not RPC safe)
    /// </summary>
    Task<PaymentRecord> GetPaymentRecordAsync();
    
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
    
    /// <summary>
    /// Get all transactions
    /// </summary>
    Task<List<Transaction>> GetTransactionsAsync();
    
    /// <summary>
    /// Get specific transaction by ID
    /// </summary>
    Task<Transaction?> GetTransactionAsync(string transactionId);
    
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
    /// </summary>
    Task ProcessRenewalAsync(RenewalInfo renewal);
    
    // ========== Refund Processing ==========
    
    /// <summary>
    /// Process full refund
    /// </summary>
    Task ProcessRefundAsync(RefundInfo refund);
    
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

/// <summary>
/// Renewal information
/// </summary>
public class RenewalInfo
{
    /// <summary>Platform transaction ID</summary>
    public string? ExternalTransactionId { get; set; }
    
    /// <summary>Invoice ID</summary>
    public string? InvoiceId { get; set; }
    
    /// <summary>New period start</summary>
    public DateTime PeriodStart { get; set; }
    
    /// <summary>New period end</summary>
    public DateTime PeriodEnd { get; set; }
    
    /// <summary>Amount in smallest unit</summary>
    public long Amount { get; set; }
    
    /// <summary>Currency code</summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>Applied promotions</summary>
    public List<Promotion> Promotions { get; set; } = new();
    
    /// <summary>Is trial period</summary>
    public bool IsTrial { get; set; }
    
    /// <summary>Trial code</summary>
    public string? TrialCode { get; set; }
}

/// <summary>
/// Refund information
/// </summary>
public class RefundInfo
{
    /// <summary>Transaction ID to refund (if specific)</summary>
    public string? TransactionId { get; set; }
    
    /// <summary>Refund amount in smallest unit</summary>
    public long RefundAmount { get; set; }
    
    /// <summary>Refund reason</summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>Platform refund ID</summary>
    public string? ExternalRefundId { get; set; }
}

