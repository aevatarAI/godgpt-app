namespace Aevatar.Payment.Abstractions;

/// <summary>
/// Payment provider strategy interface.
/// Each payment platform (Stripe, Apple, Google) implements this interface.
/// </summary>
public interface IPaymentProvider
{
    /// <summary>
    /// The payment platform this provider handles
    /// </summary>
    PaymentPlatform Platform { get; }

    /// <summary>
    /// Get available products for this platform
    /// </summary>
    Task<List<ProductDto>> GetProductsAsync(CancellationToken ct = default);

    /// <summary>
    /// Create a subscription/purchase
    /// </summary>
    Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest request, CancellationToken ct = default);

    /// <summary>
    /// Verify a transaction/receipt
    /// </summary>
    Task<VerificationResult> VerifyTransactionAsync(VerificationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Handle webhook callback from the payment platform
    /// </summary>
    Task<WebhookResult> HandleWebhookAsync(WebhookRequest request, CancellationToken ct = default);

    /// <summary>
    /// Cancel an existing subscription
    /// </summary>
    Task<CancellationResult> CancelSubscriptionAsync(CancellationRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get subscription status from payment platform
    /// </summary>
    Task<SubscriptionStatusResult> GetSubscriptionStatusAsync(string subscriptionId, CancellationToken ct = default);

    // ========== Customer Operations (Stripe-specific, optional for other platforms) ==========

    /// <summary>
    /// Get or create a customer on the payment platform.
    /// Primarily used by Stripe. Other platforms may return NotSupported.
    /// </summary>
    Task<CustomerResult> GetOrCreateCustomerAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult(new CustomerResult { Success = false, ErrorMessage = "Not supported by this platform" });

    /// <summary>
    /// Get customer session for mobile SDK (EphemeralKey for Stripe).
    /// Primarily used by Stripe. Other platforms may return NotSupported.
    /// </summary>
    Task<CustomerSessionResult> GetCustomerSessionAsync(Guid userId, string customerId, CancellationToken ct = default)
        => Task.FromResult(new CustomerSessionResult { Success = false, ErrorMessage = "Not supported by this platform" });

    /// <summary>
    /// Create PaymentSheet for mobile app embedded payment.
    /// Primarily used by Stripe. Other platforms may return NotSupported.
    /// </summary>
    Task<PaymentSheetResult> CreatePaymentSheetAsync(PaymentSheetRequest request, CancellationToken ct = default)
        => Task.FromResult(new PaymentSheetResult { Success = false, ErrorMessage = "Not supported by this platform" });
}

#region Request/Response DTOs

/// <summary>
/// Product information from payment platform
/// </summary>
public class ProductDto
{
    public string ProductId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "USD";
    
    /// <summary>
    /// Subscription tier level (Free/Basic/Premium/Enterprise)
    /// </summary>
    public PlanType PlanType { get; set; }
    
    /// <summary>
    /// Billing cycle (Daily/Weekly/Monthly/Yearly)
    /// </summary>
    public BillingCycle BillingCycle { get; set; }
    
    public bool IsActive { get; set; } = true;
    public Dictionary<string, string> Metadata { get; set; } = new();
}

/// <summary>
/// Subscription creation request
/// </summary>
public class SubscriptionRequest
{
    public Guid UserId { get; set; }
    public string ProductId { get; set; } = string.Empty;
    public string? CustomerId { get; set; }
    public string? SuccessUrl { get; set; }
    public string? CancelUrl { get; set; }
    public string? TransactionId { get; set; }
    public string? ReceiptData { get; set; }
    public bool IsSandbox { get; set; }
    public Dictionary<string, string> Metadata { get; set; } = new();
    
    // Stripe-specific
    public string? Mode { get; set; }
    public string? UiMode { get; set; }
    public string? CouponCode { get; set; }
    public int TrialDays { get; set; }
}

/// <summary>
/// Subscription creation result
/// </summary>
public class SubscriptionResult
{
    public bool Success { get; set; }
    public string? SubscriptionId { get; set; }
    public string? SessionUrl { get; set; }
    public string? CustomerId { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? ErrorMessage { get; set; }
    public PaymentStatus Status { get; set; }
    public Dictionary<string, object> AdditionalData { get; set; } = new();
}

/// <summary>
/// Transaction verification request
/// </summary>
public class VerificationRequest
{
    public Guid UserId { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public string? ReceiptData { get; set; }
    public string? PurchaseToken { get; set; }
    public bool IsSandbox { get; set; }
}

/// <summary>
/// Webhook request from payment platform
/// </summary>
public class WebhookRequest
{
    public string Payload { get; set; } = string.Empty;
    public string? Signature { get; set; }
    public Dictionary<string, string> Headers { get; set; } = new();
}

/// <summary>
/// Webhook processing result from payment providers
/// </summary>
public class WebhookResult
{
    // ========== Common Fields ==========
    public bool Success { get; set; }
    public string? EventType { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShouldProcess { get; set; } = true;
    
    // ========== Business Lookup Key ==========
    
    /// <summary>
    /// User ID extracted from metadata
    /// </summary>
    public Guid? UserId { get; set; }
    
    /// <summary>
    /// Business order ID (from metadata). This is the STABLE key for finding 
    /// PaymentRecordGAgent across all webhook events.
    /// - Set in checkout session metadata as "order_id"
    /// - Extracted from subscription/invoice metadata in webhooks
    /// </summary>
    public string? OrderId { get; set; }
    
    // ========== Platform-Specific IDs ==========
    
    /// <summary>
    /// Platform subscription ID for API operations (cancel, update, etc.)
    /// - Stripe: sub_xxx
    /// - Apple: original_transaction_id
    /// - Google: purchase_token or original_transaction_id
    /// </summary>
    public string? SubscriptionId { get; set; }
    
    /// <summary>
    /// Current transaction/invoice ID for logging and tracking
    /// - Stripe: invoice.Id (in_xxx)
    /// - Apple: transaction_id
    /// - Google: transaction_id
    /// </summary>
    public string? TransactionId { get; set; }
    
    // ========== Payment Status ==========
    
    public PaymentStatus? NewStatus { get; set; }
    
    /// <summary>
    /// Product identifier for tier lookup
    /// - Stripe: price_id
    /// - Apple/Google: product_id
    /// </summary>
    public string? ProductId { get; set; }
    
    /// <summary>
    /// Whether this is a renewal payment (not first-time subscription)
    /// </summary>
    public bool IsRenewal { get; set; }
    
    // ========== Verification (Optional) ==========
    
    public VerificationResult? VerificationResult { get; set; }
}

/// <summary>
/// Cancellation request
/// </summary>
public class CancellationRequest
{
    public Guid UserId { get; set; }
    public string SubscriptionId { get; set; } = string.Empty;
    public string? Reason { get; set; }
    public bool Immediate { get; set; }
}

/// <summary>
/// Cancellation result
/// </summary>
public class CancellationResult
{
    public bool Success { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public string? ErrorMessage { get; set; }
}

// ========== Customer Operations DTOs ==========

/// <summary>
/// Customer creation/retrieval result
/// </summary>
public class CustomerResult
{
    public bool Success { get; set; }
    public string? CustomerId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Customer session result (for mobile SDK)
/// </summary>
public class CustomerSessionResult
{
    public bool Success { get; set; }
    public string? CustomerId { get; set; }
    public string? EphemeralKey { get; set; }
    public string? PublishableKey { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// PaymentSheet request (for mobile app embedded payment)
/// </summary>
public class PaymentSheetRequest
{
    public Guid UserId { get; set; }
    public string CustomerId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public string? OrderId { get; set; }
    public string? PriceId { get; set; }
    public List<string>? PaymentMethodTypes { get; set; }
}

/// <summary>
/// PaymentSheet result
/// </summary>
public class PaymentSheetResult
{
    public bool Success { get; set; }
    public string? PaymentIntentClientSecret { get; set; }
    public string? EphemeralKey { get; set; }
    public string? CustomerId { get; set; }
    public string? PublishableKey { get; set; }
    public string? ErrorMessage { get; set; }
}

#endregion
