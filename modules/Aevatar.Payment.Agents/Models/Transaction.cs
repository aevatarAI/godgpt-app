namespace Aevatar.Payment.Agents;

/// <summary>
/// Transaction (Invoice detail for each payment action)
/// </summary>
public class Transaction
{
    // ========== Identifiers ==========
    
    /// <summary>Internal transaction ID</summary>
    public string TransactionId { get; set; } = string.Empty;
    
    /// <summary>Platform transaction ID</summary>
    public string? ExternalTransactionId { get; set; }
    
    /// <summary>Invoice ID (if any)</summary>
    public string? InvoiceId { get; set; }
    
    /// <summary>Google Play purchase token</summary>
    public string? PurchaseToken { get; set; }
    
    // ========== Type and Status ==========
    
    /// <summary>Transaction type</summary>
    public TransactionType TransactionType { get; set; }
    
    /// <summary>Transaction status</summary>
    public PaymentStatus Status { get; set; }
    
    // ========== Amount ==========
    
    /// <summary>Amount in smallest unit</summary>
    public long Amount { get; set; }
    
    /// <summary>Currency code</summary>
    public string Currency { get; set; } = "USD";
    
    /// <summary>Net amount after discounts</summary>
    public long? NetAmount { get; set; }
    
    // ========== Period (for renewal) ==========
    
    /// <summary>Period start (for renewal)</summary>
    public DateTime? PeriodStart { get; set; }
    
    /// <summary>Period end (for renewal)</summary>
    public DateTime? PeriodEnd { get; set; }
    
    // ========== Time ==========
    
    /// <summary>Created time</summary>
    public DateTime CreatedAt { get; set; }
    
    /// <summary>Completed time</summary>
    public DateTime? CompletedAt { get; set; }
    
    // ========== Promotions ==========
    
    /// <summary>Applied promotions</summary>
    public List<Promotion> Promotions { get; set; } = new();
    
    /// <summary>Is trial period</summary>
    public bool IsTrial { get; set; }
    
    /// <summary>Trial code used</summary>
    public string? TrialCode { get; set; }
    
    // ========== Metadata ==========
    
    /// <summary>Additional metadata</summary>
    public Dictionary<string, string> Metadata { get; set; } = new();
}

