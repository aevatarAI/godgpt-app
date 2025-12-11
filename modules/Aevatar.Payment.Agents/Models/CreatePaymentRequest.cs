namespace Aevatar.Payment.Agents;

/// <summary>
/// Request to create a new payment record
/// </summary>
public class CreatePaymentRequest
{
    // ========== User Info ==========
    
    /// <summary>User ID</summary>
    public string UserId { get; set; } = string.Empty;
    
    /// <summary>External order ID (business layer generated)</summary>
    public string? ExternalOrderId { get; set; }
    
    // ========== Business Context ==========
    
    /// <summary>Business type (godgpt/course/api)</summary>
    public string BusinessType { get; set; } = string.Empty;
    
    /// <summary>Business entity ID</summary>
    public string BusinessId { get; set; } = string.Empty;
    
    /// <summary>Business metadata</summary>
    public Dictionary<string, string> BusinessMetadata { get; set; } = new();
    
    /// <summary>
    /// Callback agent ID for point-to-point notification.
    /// When payment completes, PaymentRecordGAgent will SendTo this agent.
    /// Used for order-level agents that need to receive payment results.
    /// </summary>
    public Guid? CallbackAgentId { get; set; }
    
    // ========== Platform Info ==========
    
    /// <summary>Payment platform</summary>
    public PaymentPlatform Platform { get; set; }
    
    /// <summary>Environment (Production/Sandbox)</summary>
    public string Environment { get; set; } = "Production";
    
    /// <summary>Platform customer ID</summary>
    public string? CustomerId { get; set; }
    
    /// <summary>Platform subscription ID (for webhook routing)</summary>
    public string? SubscriptionId { get; set; }
    
    // ========== Product Info ==========
    
    /// <summary>Platform product ID</summary>
    public string? ProductId { get; set; }
    
    /// <summary>Platform price ID</summary>
    public string? PriceId { get; set; }
    
    /// <summary>Product display name</summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>Payment mode</summary>
    public PaymentMode PaymentMode { get; set; }
    
    // ========== Billing Cycle ==========
    
    /// <summary>Billing cycle for subscription</summary>
    public BillingCycle BillingCycle { get; set; }
    
    /// <summary>Period start</summary>
    public DateTime? PeriodStart { get; set; }
    
    /// <summary>Period end</summary>
    public DateTime? PeriodEnd { get; set; }
    
    // ========== Amount Info ==========
    
    /// <summary>Amount in smallest unit (e.g., cents)</summary>
    public long Amount { get; set; }
    
    /// <summary>Currency code</summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>Net amount after discounts</summary>
    public long? NetAmount { get; set; }
    
    // ========== Initial Transaction ==========
    
    /// <summary>Initial transaction info</summary>
    public Transaction? InitialTransaction { get; set; }
}

