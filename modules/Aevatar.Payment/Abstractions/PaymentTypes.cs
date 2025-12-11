namespace Aevatar.Payment.Abstractions;

/// <summary>
/// Supported payment platforms (API layer)
/// </summary>
public enum PaymentPlatform
{
    Stripe = 0,
    AppStore = 1,
    GooglePlay = 2,
    PayPal = 3,
    Alipay = 4,
    WechatPay = 5
}

/// <summary>
/// Payment status (API layer)
/// </summary>
public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4,
    PartialRefunded = 5,
    Cancelled = 6,
    Expired = 7
}

/// <summary>
/// Subscription plan type (API layer)
/// </summary>
public enum PlanType
{
    Free = 0,
    Basic = 1,
    Premium = 2,
    Enterprise = 3
}

/// <summary>
/// Payment mode (API layer)
/// </summary>
public enum PaymentMode
{
    Subscription = 0,
    OneTime = 1,
    Consumable = 2
}

/// <summary>
/// Billing cycle for subscriptions (API layer)
/// </summary>
public enum BillingCycle
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3,
    Quarterly = 4,
    Yearly = 5,
    Lifetime = 6
}

/// <summary>
/// User subscription status result
/// </summary>
public class UserSubscriptionStatus
{
    public bool HasActiveSubscription { get; set; }
    public string? CurrentPlan { get; set; }
    public PaymentPlatform? Platform { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? SubscriptionId { get; set; }
    public bool AutoRenew { get; set; }
    public List<ActiveSubscriptionDto> ActiveSubscriptions { get; set; } = new();
}

/// <summary>
/// Active subscription DTO for API responses
/// </summary>
public class ActiveSubscriptionDto
{
    public string PaymentId { get; set; } = string.Empty;
    public string BusinessType { get; set; } = string.Empty;
    public string BusinessId { get; set; } = string.Empty;
    public PaymentPlatform Platform { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime PeriodEnd { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Payment history item DTO
/// </summary>
public class PaymentHistoryItem
{
    public string PaymentId { get; set; } = string.Empty;
    public PaymentPlatform Platform { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public PaymentStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Subscription status from payment platform
/// </summary>
public class SubscriptionStatusResult
{
    public bool IsActive { get; set; }
    public string? SubscriptionId { get; set; }
    public PaymentStatus Status { get; set; }
    public DateTime? CurrentPeriodEnd { get; set; }
    public bool WillRenew { get; set; }
    public string? ProductId { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Transaction verification result
/// </summary>
public class VerificationResult
{
    public bool IsValid { get; set; }
    public string? ProductId { get; set; }
    public string? TransactionId { get; set; }
    public string? OriginalTransactionId { get; set; }
    public DateTime? PurchaseDate { get; set; }
    public DateTime? ExpiresDate { get; set; }
    public bool AutoRenewing { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public string? Environment { get; set; }
    public string? ErrorMessage { get; set; }
}

