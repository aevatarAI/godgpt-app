namespace Aevatar.Payment.Agents;

/// <summary>
/// Active subscription info for Index Agent
/// </summary>
public class ActiveSubscription
{
    /// <summary>PaymentRecordGAgent ID</summary>
    public string PaymentId { get; set; } = string.Empty;
    
    /// <summary>Business type (godgpt/course/api)</summary>
    public string BusinessType { get; set; } = string.Empty;
    
    /// <summary>Business entity ID</summary>
    public string BusinessId { get; set; } = string.Empty;
    
    /// <summary>Payment platform</summary>
    public PaymentPlatform Platform { get; set; }
    
    /// <summary>Product display name</summary>
    public string ProductName { get; set; } = string.Empty;
    
    /// <summary>Amount in smallest unit (e.g., cents)</summary>
    public long Amount { get; set; }
    
    /// <summary>Currency code (USD, EUR, etc.)</summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>Subscription period end time</summary>
    public DateTime PeriodEnd { get; set; }
    
    /// <summary>Created time</summary>
    public DateTime CreatedAt { get; set; }
}

