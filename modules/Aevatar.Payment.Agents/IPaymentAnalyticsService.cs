namespace Aevatar.Payment.Agents;

/// <summary>
/// Payment analytics service interface for GA4 reporting.
/// Uses string for platform to avoid cross-project enum dependencies.
/// </summary>
public interface IPaymentAnalyticsService
{
    /// <summary>
    /// Report a purchase event to GA4
    /// </summary>
    /// <param name="platform">Payment platform name (e.g., "Stripe", "AppStore", "GooglePlay")</param>
    /// <param name="transactionId">Unique transaction ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="amount">Purchase amount</param>
    /// <param name="currency">Currency code (e.g., USD)</param>
    /// <param name="isRenewal">Whether this is a subscription renewal</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if reported successfully</returns>
    Task<bool> ReportPurchaseAsync(
        string platform,
        string transactionId,
        string userId,
        decimal amount,
        string currency,
        bool isRenewal = false,
        CancellationToken ct = default);

    /// <summary>
    /// Report a refund event to GA4
    /// </summary>
    /// <param name="platform">Payment platform name</param>
    /// <param name="transactionId">Original transaction ID</param>
    /// <param name="userId">User ID</param>
    /// <param name="refundAmount">Refund amount</param>
    /// <param name="currency">Currency code</param>
    /// <param name="reason">Refund reason</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if reported successfully</returns>
    Task<bool> ReportRefundAsync(
        string platform,
        string transactionId,
        string userId,
        decimal refundAmount,
        string currency,
        string reason,
        CancellationToken ct = default);
}
