using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Payment.Agents;

/// <summary>
/// Analytics agent for reporting payment events to GA4.
/// ID format: transactionId (each transaction gets its own agent for parallel processing)
/// 
/// This agent subscribes to payment events and asynchronously reports them to GA4.
/// Failure to report does not affect the payment flow.
/// </summary>
public class PaymentAnalyticsGAgent : GAgentBase<PaymentAnalyticsStateProto>, IPaymentAnalyticsGAgent
{
    public ILogger<PaymentAnalyticsGAgent> AnalyticsLogger { get; set; } = NullLogger<PaymentAnalyticsGAgent>.Instance;
    
    // Injected via DI
    public IPaymentAnalyticsService? AnalyticsService { get; set; }

    public PaymentAnalyticsGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"PaymentAnalytics: {State.TransactionId} - Purchase: {State.PurchaseReported}, Refund: {State.RefundReported}");
    }

    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        AnalyticsLogger.LogDebug("[PaymentAnalyticsGAgent] Activated for transaction {TransactionId}", Id);
        return base.OnActivateAsync(ct);
    }

    // ========== Event Handlers ==========

    /// <summary>
    /// Handle payment completed event - report purchase to GA4
    /// </summary>
    [Aevatar.Agents.Abstractions.Attributes.EventHandler]
    public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
    {
        if (State.PurchaseReported)
        {
            AnalyticsLogger.LogDebug("[PaymentAnalyticsGAgent] Purchase already reported for {TransactionId}", evt.TransactionId);
            return;
        }

        AnalyticsLogger.LogInformation("[PaymentAnalyticsGAgent] Reporting purchase for {TransactionId}", evt.TransactionId);

        // Initialize state if needed
        if (string.IsNullOrEmpty(State.TransactionId))
        {
            State.TransactionId = evt.TransactionId;
            State.UserId = evt.Context?.UserId ?? string.Empty;
            State.Platform = evt.Context?.Platform ?? 0;
        }

        var success = await ReportPurchaseAsync(evt);

        if (success)
        {
            RaiseEvent(new PurchaseReportedEvent
            {
                TransactionId = evt.TransactionId,
                ReportedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        else
        {
            RaiseEvent(new ReportFailedEvent
            {
                TransactionId = evt.TransactionId,
                EventType = "purchase",
                ErrorMessage = "Failed to report to GA4",
                RetryCount = State.RetryCount + 1,
                FailedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Handle refund completed event - report refund to GA4
    /// </summary>
    [Aevatar.Agents.Abstractions.Attributes.EventHandler]
    public async Task HandleRefundCompleted(RefundCompletedEvent evt)
    {
        if (State.RefundReported)
        {
            AnalyticsLogger.LogDebug("[PaymentAnalyticsGAgent] Refund already reported for {TransactionId}", evt.OriginalTransactionId);
            return;
        }

        AnalyticsLogger.LogInformation("[PaymentAnalyticsGAgent] Reporting refund for {TransactionId}", evt.OriginalTransactionId);

        var success = await ReportRefundAsync(evt);

        if (success)
        {
            RaiseEvent(new RefundReportedEvent
            {
                TransactionId = evt.OriginalTransactionId,
                ReportedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }
        else
        {
            RaiseEvent(new ReportFailedEvent
            {
                TransactionId = evt.OriginalTransactionId,
                EventType = "refund",
                ErrorMessage = "Failed to report refund to GA4",
                RetryCount = State.RetryCount + 1,
                FailedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
        }

        await ConfirmEventsAsync();
    }

    // ========== Private Methods ==========

    private async Task<bool> ReportPurchaseAsync(PaymentCompletedEvent evt)
    {
        if (AnalyticsService == null)
        {
            AnalyticsLogger.LogWarning("[PaymentAnalyticsGAgent] AnalyticsService not injected, skipping report");
            return true; // Don't block if service not available
        }

        try
        {
            var platformName = ((PaymentPlatform)evt.Context!.Platform).ToString();
            return await AnalyticsService.ReportPurchaseAsync(
                platformName,
                evt.TransactionId,
                evt.Context.UserId,
                0, // Amount not in event, could be enhanced
                "USD", // Currency not in event, could be enhanced
                evt.IsRenewal);
        }
        catch (Exception ex)
        {
            AnalyticsLogger.LogError(ex, "[PaymentAnalyticsGAgent] Exception reporting purchase for {TransactionId}", evt.TransactionId);
            return false;
        }
    }

    private async Task<bool> ReportRefundAsync(RefundCompletedEvent evt)
    {
        if (AnalyticsService == null)
        {
            AnalyticsLogger.LogWarning("[PaymentAnalyticsGAgent] AnalyticsService not injected, skipping report");
            return true;
        }

        try
        {
            var platformName = ((PaymentPlatform)evt.Context!.Platform).ToString();
            return await AnalyticsService.ReportRefundAsync(
                platformName,
                evt.OriginalTransactionId,
                evt.Context.UserId,
                evt.RefundAmount,
                "USD",
                evt.Reason);
        }
        catch (Exception ex)
        {
            AnalyticsLogger.LogError(ex, "[PaymentAnalyticsGAgent] Exception reporting refund for {TransactionId}", evt.OriginalTransactionId);
            return false;
        }
    }

    // ========== Event Sourcing ==========

    protected override void TransitionState(PaymentAnalyticsStateProto state, IMessage evt)
    {
        switch (evt)
        {
            case PurchaseReportedEvent e:
                state.PurchaseReported = true;
                state.LastReportAttempt = e.ReportedAt;
                break;

            case RefundReportedEvent e:
                state.RefundReported = true;
                state.LastReportAttempt = e.ReportedAt;
                break;

            case ReportFailedEvent e:
                state.RetryCount = e.RetryCount;
                state.LastError = e.ErrorMessage;
                state.LastReportAttempt = e.FailedAt;
                break;
        }
    }
}

/// <summary>
/// Interface for PaymentAnalyticsGAgent
/// </summary>
public interface IPaymentAnalyticsGAgent : IGAgent
{
    // EventHandlers are discovered automatically via [EventHandler] attribute
}

