namespace Aevatar.Payment.Abstractions;

/// <summary>
/// Payment service interface - orchestrates payment providers
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// Get products for a specific platform
    /// </summary>
    Task<List<ProductDto>> GetProductsAsync(PaymentPlatform platform, CancellationToken ct = default);

    /// <summary>
    /// Create a subscription using the appropriate provider
    /// </summary>
    Task<SubscriptionResult> CreateSubscriptionAsync(
        Guid userId, 
        PaymentPlatform platform, 
        SubscriptionRequest request, 
        CancellationToken ct = default);

    /// <summary>
    /// Verify a transaction
    /// </summary>
    Task<VerificationResult> VerifyTransactionAsync(
        Guid userId,
        PaymentPlatform platform,
        VerificationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Handle webhook from a payment platform
    /// </summary>
    Task<WebhookResult> HandleWebhookAsync(
        PaymentPlatform platform,
        WebhookRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Cancel a subscription
    /// </summary>
    Task<CancellationResult> CancelSubscriptionAsync(
        Guid userId,
        PaymentPlatform platform,
        CancellationRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Get user's active subscription status across all platforms
    /// </summary>
    Task<UserSubscriptionStatus> GetUserSubscriptionStatusAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Get payment history for a user
    /// </summary>
    Task<List<PaymentHistoryItem>> GetPaymentHistoryAsync(
        Guid userId,
        int page = 1,
        int pageSize = 10,
        CancellationToken ct = default);

    // ========== Customer Operations (Stripe-specific) ==========

    /// <summary>
    /// Get or create a Stripe customer for a user.
    /// Uses PaymentIndexGAgent to store/retrieve customer ID.
    /// </summary>
    Task<CustomerSessionResult> GetStripeCustomerAsync(
        Guid userId,
        CancellationToken ct = default);

    /// <summary>
    /// Create PaymentSheet for mobile app embedded payment.
    /// </summary>
    Task<PaymentSheetResult> CreatePaymentSheetAsync(
        Guid userId,
        PaymentSheetRequest request,
        CancellationToken ct = default);
}
