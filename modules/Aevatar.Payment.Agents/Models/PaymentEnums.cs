namespace Aevatar.Payment.Agents;

/// <summary>
/// Supported payment platforms
/// </summary>
public enum PaymentPlatform
{
    Stripe = 0,
    AppStore = 1,
    GooglePlay = 2,
    // Future extensions
    PayPal = 3,
    Alipay = 4,
    WechatPay = 5
}

/// <summary>
/// Payment status
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
/// Payment mode
/// </summary>
public enum PaymentMode
{
    /// <summary>Recurring subscription</summary>
    Subscription = 0,
    /// <summary>One-time purchase</summary>
    OneTime = 1,
    /// <summary>Consumable (can be purchased multiple times)</summary>
    Consumable = 2
}

/// <summary>
/// Billing cycle for subscriptions
/// </summary>
public enum BillingCycle
{
    None = 0,
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
    Lifetime = 5
}

/// <summary>
/// Transaction type
/// </summary>
public enum TransactionType
{
    /// <summary>Initial purchase</summary>
    Initial = 0,
    /// <summary>Auto renewal</summary>
    Renewal = 1,
    /// <summary>Upgrade to higher tier</summary>
    Upgrade = 2,
    /// <summary>Downgrade to lower tier</summary>
    Downgrade = 3,
    /// <summary>Resubscribe after cancellation</summary>
    Resubscribe = 4,
    /// <summary>Full refund</summary>
    Refund = 5,
    /// <summary>Partial refund</summary>
    PartialRefund = 6,
    /// <summary>Manual adjustment</summary>
    Adjustment = 7
}

