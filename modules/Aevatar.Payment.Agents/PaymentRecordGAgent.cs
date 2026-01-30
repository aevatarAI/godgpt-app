using Aevatar.Agents.Core;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Payment.Agents;

/// <summary>
/// Order-level payment record agent - manages complete lifecycle of a single payment.
/// ID format: payment_{platform}_{subscriptionId}
/// </summary>
public class PaymentRecordGAgent : GAgentBase<PaymentRecordStateProto>, IPaymentRecordGAgent
{
    public PaymentRecordGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"PaymentRecord: {State.PaymentId} [{(PaymentStatus)State.Status}]");
    }

    // ========== Event Sourcing: TransitionState ==========

    protected override void TransitionState(PaymentRecordStateProto state, IMessage evt)
    {
        switch (evt)
        {
            case PaymentRecordInitializedEvent e:
                CopyFromProto(state, e.Record);
                break;
                
            case RecordStatusChangedEvent e:
                state.Status = e.NewStatus;
                // When cancelled/expired, also update the last transaction status
                // This matches old code behavior where InvoiceDetail.Status was updated on cancel
                if (e.NewStatus == (int)PaymentStatus.Cancelled || e.NewStatus == (int)PaymentStatus.Expired)
                {
                    var lastTxn = state.Transactions
                        .Where(t => t.Status == (int)PaymentStatus.Completed)
                        .OrderByDescending(t => t.CreatedAt)
                        .FirstOrDefault();
                    if (lastTxn != null)
                    {
                        lastTxn.Status = e.NewStatus;
                    }
                }
                break;
                
            case RecordPeriodUpdatedEvent e:
                state.PeriodStart = e.NewPeriodStart;
                state.PeriodEnd = e.NewPeriodEnd;
                break;
                
            case RecordCompletedEvent e:
                state.Status = (int)PaymentStatus.Completed;
                state.CompletedAt = e.CompletedAt;
                break;
                
            case TransactionAddedEvent e:
                state.Transactions.Add(e.Transaction);
                break;
                
            case TransactionStatusChangedEvent e:
                var txn = state.Transactions.FirstOrDefault(t => t.TransactionId == e.TransactionId);
                if (txn != null)
                {
                    txn.Status = e.NewStatus;
                    txn.CompletedAt = e.ChangedAt;
                }
                break;
                
            case RenewalProcessedEvent e:
                state.Transactions.Add(e.RenewalTransaction);
                state.PeriodEnd = e.NewPeriodEnd;
                // Update main record's product info (matches old code: existingSubscription.PlanType = appleProduct.PlanType)
                if (!string.IsNullOrEmpty(e.NewProductId))
                    state.ProductId = e.NewProductId;
                if (e.NewBillingCycle != 0)
                    state.BillingCycle = e.NewBillingCycle;
                break;
                
            case RefundProcessedEvent e:
                var refundTxn = state.Transactions.FirstOrDefault(t => t.TransactionId == e.TransactionId);
                if (refundTxn != null)
                {
                    refundTxn.Status = (int)PaymentStatus.Refunded;
                }
                // Match old code: only update main status if refunding the LATEST transaction
                // Old code: if (invoiceDetail == invoiceDetails.LastOrDefault()) { paymentSummary.Status = Refunded; }
                var latestTxn = state.Transactions
                    .OrderByDescending(t => t.CreatedAt)
                    .FirstOrDefault();
                if (latestTxn?.TransactionId == e.TransactionId)
                {
                    state.Status = (int)PaymentStatus.Refunded;
                }
                break;
            
            case RecordClearedEvent:
                // Clear all data
                state.PaymentId = string.Empty;
                state.UserId = string.Empty;
                state.ExternalOrderId = string.Empty;
                state.SubscriptionId = string.Empty;
                state.BusinessType = string.Empty;
                state.BusinessId = string.Empty;
                state.ProductId = string.Empty;
                state.PriceId = string.Empty;
                state.ProductName = string.Empty;
                state.CustomerId = string.Empty;
                state.Status = (int)PaymentStatus.None;
                state.Amount = 0;
                state.NetAmount = 0;
                state.Currency = string.Empty;
                state.Transactions.Clear();
                state.BusinessMetadata.Clear();
                break;
        }
        
        state.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    // ========== Initialization ==========

    public async Task InitializeAsync(CreatePaymentRequestProto request)
    {
        if (!string.IsNullOrEmpty(State.PaymentId))
        {
            Logger.LogWarning(
                "[PaymentRecordGAgent] Agent {Id} already initialized, ignoring", Id);
            return;
        }

        // Use business paymentId if provided, otherwise fallback to Agent ID
        var paymentId = !string.IsNullOrEmpty(request.PaymentId) 
            ? request.PaymentId 
            : Id.ToString();
            
        Logger.LogInformation(
            "[PaymentRecordGAgent] Initializing payment {PaymentId} for user {UserId}",
            paymentId, request.UserId);

        var record = new PaymentRecordStateProto
        {
            PaymentId = paymentId,
            UserId = request.UserId,
            ExternalOrderId = request.ExternalOrderId ?? string.Empty,
            SubscriptionId = request.SubscriptionId ?? string.Empty,
            BusinessType = request.BusinessType,
            BusinessId = request.BusinessId,
            Platform = request.Platform,
            Environment = request.Environment ?? "Production",
            CustomerId = request.CustomerId ?? string.Empty,
            ProductId = request.ProductId ?? string.Empty,
            PriceId = request.PriceId ?? string.Empty,
            ProductName = request.ProductName ?? string.Empty,
            PaymentMode = request.PaymentMode,
            BillingCycle = request.BillingCycle,
            Amount = request.Amount,
            Currency = request.Currency ?? "USD",
            Status = (int)PaymentStatus.Pending,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (request.NetAmount > 0)
            record.NetAmount = request.NetAmount;
        if (request.PeriodStart != null)
            record.PeriodStart = request.PeriodStart;
        if (request.PeriodEnd != null)
            record.PeriodEnd = request.PeriodEnd;

        foreach (var kv in request.BusinessMetadata)
        {
            record.BusinessMetadata[kv.Key] = kv.Value;
        }

        // Set callback agent ID if provided
        if (!string.IsNullOrEmpty(request.CallbackAgentId))
        {
            record.CallbackAgentId = request.CallbackAgentId;
        }

        // Note: InitialTransaction is not included in Proto version 
        // It should be added via AddTransactionAsync after initialization

        RaiseEvent(new PaymentRecordInitializedEvent { Record = record });
        await ConfirmEventsAsync();
    }

    public Task<bool> IsInitializedAsync()
    {
        return Task.FromResult(!string.IsNullOrEmpty(State.PaymentId));
    }

    // ========== Query ==========

    public Task<PaymentRecordStateProto> GetRecordStateAsync()
    {
        return Task.FromResult(State);
    }

    public Task<PaymentStatus> GetStatusAsync()
    {
        return Task.FromResult((PaymentStatus)State.Status);
    }

    public Task<string> GetUserIdAsync()
    {
        return Task.FromResult(State.UserId);
    }

    public Task<Guid?> GetCallbackAgentIdAsync()
    {
        if (string.IsNullOrEmpty(State.CallbackAgentId))
            return Task.FromResult<Guid?>(null);
        
        return Guid.TryParse(State.CallbackAgentId, out var id) 
            ? Task.FromResult<Guid?>(id) 
            : Task.FromResult<Guid?>(null);
    }

    // ========== Status Update ==========

    public async Task UpdateStatusAsync(PaymentStatus status, string? reason = null)
    {
        Logger.LogInformation(
            "[PaymentRecordGAgent] Updating status from {OldStatus} to {NewStatus}",
            (PaymentStatus)State.Status, status);

        RaiseEvent(new RecordStatusChangedEvent
        {
            OldStatus = State.Status,
            NewStatus = (int)status,
            ChangedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Reason = reason ?? string.Empty
        });

        await ConfirmEventsAsync();
    }

    public async Task UpdatePeriodAsync(DateTime periodStart, DateTime periodEnd)
    {
        Logger.LogInformation(
            "[PaymentRecordGAgent] Updating period to {Start} - {End}",
            periodStart, periodEnd);

        RaiseEvent(new RecordPeriodUpdatedEvent
        {
            NewPeriodStart = Timestamp.FromDateTime(periodStart.ToUniversalTime()),
            NewPeriodEnd = Timestamp.FromDateTime(periodEnd.ToUniversalTime())
        });

        await ConfirmEventsAsync();
    }

    public async Task UpdateSubscriptionIdAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            Logger.LogWarning("[PaymentRecordGAgent] Attempted to update with empty subscriptionId");
            return;
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Updating subscriptionId from {OldId} to {NewId}",
            State.SubscriptionId, subscriptionId);

        State.SubscriptionId = subscriptionId;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        
        await Task.CompletedTask;
    }

    public async Task CompleteAsync()
    {
        // Only block if refunded (user got money back)
        // Allow re-activation from Cancelled/Expired for Apple/Google resubscription scenarios
        var currentStatus = (PaymentStatus)State.Status;
        if (currentStatus == PaymentStatus.Refunded)
        {
            Logger.LogWarning(
                "[PaymentRecordGAgent] Cannot complete payment {PaymentId} - already Refunded",
                State.PaymentId);
            return;
        }
        
        // Log reactivation for monitoring
        if (currentStatus == PaymentStatus.Cancelled || currentStatus == PaymentStatus.Expired)
        {
            Logger.LogInformation(
                "[PaymentRecordGAgent] Reactivating payment {PaymentId} from {Status}",
                State.PaymentId, currentStatus);
        }
        else
        {
            Logger.LogInformation("[PaymentRecordGAgent] Marking payment as completed");
        }

        RaiseEvent(new RecordCompletedEvent
        {
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
    }

    public async Task CancelAsync(string? reason = null)
    {
        Logger.LogInformation("[PaymentRecordGAgent] Cancelling payment: {Reason}", reason);

        RaiseEvent(new RecordStatusChangedEvent
        {
            OldStatus = State.Status,
            NewStatus = (int)PaymentStatus.Cancelled,
            ChangedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Reason = reason ?? string.Empty
        });

        await ConfirmEventsAsync();
    }

    // ========== Transaction Management ==========

    public async Task<string> AddTransactionAsync(Transaction transaction)
    {
        if (string.IsNullOrEmpty(transaction.TransactionId))
        {
            transaction.TransactionId = Guid.NewGuid().ToString();
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Adding transaction {TransactionId} type {Type}",
            transaction.TransactionId, transaction.TransactionType);

        RaiseEvent(new TransactionAddedEvent
        {
            Transaction = ToProto(transaction)
        });

        await ConfirmEventsAsync();
        return transaction.TransactionId;
    }

    public async Task UpdateTransactionStatusAsync(string transactionId, PaymentStatus status)
    {
        Logger.LogInformation(
            "[PaymentRecordGAgent] Updating transaction {TransactionId} to {Status}",
            transactionId, status);

        RaiseEvent(new TransactionStatusChangedEvent
        {
            TransactionId = transactionId,
            NewStatus = (int)status,
            ChangedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
    }

    // ========== Transaction Processing ==========

    public async Task ProcessRenewalAsync(RenewalInfoProto renewal)
    {
        Logger.LogInformation(
            "[PaymentRecordGAgent] Processing transaction, period end: {PeriodEnd}",
            renewal.PeriodEnd?.ToDateTime());

        var transaction = new TransactionProto
        {
            TransactionId = Guid.NewGuid().ToString(),
            ExternalTransactionId = renewal.ExternalTransactionId ?? string.Empty,
            InvoiceId = renewal.InvoiceId ?? string.Empty,
            TransactionType = (int)TransactionType.Renewal,
            Status = (int)PaymentStatus.Completed,
            Amount = renewal.Amount,
            Currency = renewal.Currency,
            PeriodStart = renewal.PeriodStart,
            PeriodEnd = renewal.PeriodEnd,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            IsTrial = renewal.IsTrial,
            TrialCode = renewal.TrialCode ?? string.Empty,
            // Product info per transaction (matches old InvoiceDetail.PriceId/PlanType)
            ProductId = renewal.ProductId ?? string.Empty,
            PlanType = renewal.PlanType,
            MembershipLevel = renewal.MembershipLevel ?? string.Empty
        };

        foreach (var promo in renewal.Promotions)
        {
            transaction.Promotions.Add(promo);
        }

        RaiseEvent(new RenewalProcessedEvent
        {
            RenewalTransaction = transaction,
            NewPeriodEnd = renewal.PeriodEnd,
            // Update main record's product info (matches old code: existingSubscription.PlanType = appleProduct.PlanType)
            NewProductId = renewal.ProductId ?? string.Empty,
            NewBillingCycle = renewal.PlanType
        });

        await ConfirmEventsAsync();
    }

    // ========== Refund Processing ==========

    public async Task ProcessRefundAsync(RefundInfoProto refund)
    {
        var transactionId = refund.TransactionId;
        
        // If no specific transaction, refund the latest completed one
        if (string.IsNullOrEmpty(transactionId))
        {
            var latestCompleted = State.Transactions
                .Where(t => t.Status == (int)PaymentStatus.Completed)
                .OrderByDescending(t => t.CreatedAt)
                .FirstOrDefault();
            transactionId = latestCompleted?.TransactionId;
        }

        if (string.IsNullOrEmpty(transactionId))
        {
            Logger.LogWarning("[PaymentRecordGAgent] No transaction to refund");
            return;
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Processing refund for transaction {TransactionId}",
            transactionId);

        RaiseEvent(new RefundProcessedEvent
        {
            TransactionId = transactionId,
            RefundAmount = refund.RefundAmount,
            Reason = refund.Reason,
            RefundedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
    }

    public Task ProcessPartialRefundAsync(string transactionId, long refundAmount, string reason)
    {
        return ProcessRefundAsync(new RefundInfoProto
        {
            TransactionId = transactionId,
            RefundAmount = refundAmount,
            Reason = reason
        });
    }

    // ========== Management ==========

    public async Task ClearAsync()
    {
        // Idempotency check: skip if already cleared or never initialized
        if (string.IsNullOrEmpty(State.PaymentId) || State.Status == (int)PaymentStatus.None)
        {
            Logger.LogInformation(
                "[PaymentRecordGAgent] Skipping clear - already cleared or not initialized. PaymentId={PaymentId}, Status={Status}",
                State.PaymentId, State.Status);
            return;
        }
        
        Logger.LogWarning("[PaymentRecordGAgent] Clearing all data for payment {PaymentId}", Id);

        RaiseEvent(new RecordClearedEvent
        {
            ClearedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
    }

    // ========== Proto Conversions ==========

    private static void CopyFromProto(PaymentRecordStateProto target, PaymentRecordStateProto source)
    {
        target.PaymentId = source.PaymentId;
        target.UserId = source.UserId;
        target.ExternalOrderId = source.ExternalOrderId;
        target.SubscriptionId = source.SubscriptionId;
        target.BusinessType = source.BusinessType;
        target.BusinessId = source.BusinessId;
        target.Platform = source.Platform;
        target.Environment = source.Environment;
        target.CustomerId = source.CustomerId;
        target.ProductId = source.ProductId;
        target.PriceId = source.PriceId;
        target.ProductName = source.ProductName;
        target.PaymentMode = source.PaymentMode;
        target.BillingCycle = source.BillingCycle;
        target.PeriodStart = source.PeriodStart;
        target.PeriodEnd = source.PeriodEnd;
        target.Amount = source.Amount;
        target.Currency = source.Currency;
        target.NetAmount = source.NetAmount;
        target.Status = source.Status;
        target.CreatedAt = source.CreatedAt;
        target.CompletedAt = source.CompletedAt;
        target.LastUpdated = source.LastUpdated;
        
        target.BusinessMetadata.Clear();
        foreach (var kv in source.BusinessMetadata)
        {
            target.BusinessMetadata[kv.Key] = kv.Value;
        }
        
        target.Transactions.Clear();
        target.Transactions.AddRange(source.Transactions);
    }

    private static PaymentRecord FromProto(PaymentRecordStateProto proto)
    {
        return new PaymentRecord
        {
            PaymentId = proto.PaymentId,
            UserId = proto.UserId,
            ExternalOrderId = proto.ExternalOrderId,
            SubscriptionId = proto.SubscriptionId,
            BusinessType = proto.BusinessType,
            BusinessId = proto.BusinessId,
            BusinessMetadata = proto.BusinessMetadata.ToDictionary(kv => kv.Key, kv => kv.Value),
            Platform = (PaymentPlatform)proto.Platform,
            Environment = proto.Environment,
            CustomerId = proto.CustomerId,
            ProductId = proto.ProductId,
            PriceId = proto.PriceId,
            ProductName = proto.ProductName,
            PaymentMode = (PaymentMode)proto.PaymentMode,
            BillingCycle = (BillingCycle)proto.BillingCycle,
            PeriodStart = proto.PeriodStart?.ToDateTime(),
            PeriodEnd = proto.PeriodEnd?.ToDateTime(),
            Amount = proto.Amount,
            Currency = proto.Currency,
            NetAmount = proto.HasNetAmount ? proto.NetAmount : null,
            Status = (PaymentStatus)proto.Status,
            CreatedAt = proto.CreatedAt?.ToDateTime() ?? DateTime.UtcNow,
            CompletedAt = proto.CompletedAt?.ToDateTime(),
            Transactions = proto.Transactions.Select(FromProto).ToList()
        };
    }

    private static Transaction FromProto(TransactionProto proto)
    {
        return new Transaction
        {
            TransactionId = proto.TransactionId,
            ExternalTransactionId = proto.ExternalTransactionId,
            InvoiceId = proto.InvoiceId,
            PurchaseToken = proto.PurchaseToken,
            TransactionType = (TransactionType)proto.TransactionType,
            Status = (PaymentStatus)proto.Status,
            Amount = proto.Amount,
            Currency = proto.Currency,
            NetAmount = proto.HasNetAmount ? proto.NetAmount : null,
            PeriodStart = proto.PeriodStart?.ToDateTime(),
            PeriodEnd = proto.PeriodEnd?.ToDateTime(),
            CreatedAt = proto.CreatedAt?.ToDateTime() ?? DateTime.UtcNow,
            CompletedAt = proto.CompletedAt?.ToDateTime(),
            Promotions = proto.Promotions.Select(FromProto).ToList(),
            IsTrial = proto.IsTrial,
            TrialCode = proto.TrialCode,
            Metadata = proto.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value),
            // Product info per transaction
            ProductId = proto.ProductId,
            PlanType = proto.PlanType,
            MembershipLevel = proto.MembershipLevel
        };
    }

    private static Promotion FromProto(PromotionProto proto)
    {
        return new Promotion
        {
            PromotionId = proto.PromotionId,
            PromotionType = proto.PromotionType,
            Code = proto.Code,
            Name = proto.Name,
            AmountOff = proto.HasAmountOff ? proto.AmountOff : null,
            PercentOff = proto.HasPercentOff ? proto.PercentOff : null,
            Metadata = proto.Metadata.ToDictionary(kv => kv.Key, kv => kv.Value)
        };
    }

    private static TransactionProto ToProto(Transaction txn)
    {
        var proto = new TransactionProto
        {
            TransactionId = txn.TransactionId,
            ExternalTransactionId = txn.ExternalTransactionId ?? string.Empty,
            InvoiceId = txn.InvoiceId ?? string.Empty,
            PurchaseToken = txn.PurchaseToken ?? string.Empty,
            TransactionType = (int)txn.TransactionType,
            Status = (int)txn.Status,
            Amount = txn.Amount,
            Currency = txn.Currency,
            IsTrial = txn.IsTrial,
            TrialCode = txn.TrialCode ?? string.Empty,
            CreatedAt = Timestamp.FromDateTime(txn.CreatedAt.ToUniversalTime()),
            // Product info per transaction
            ProductId = txn.ProductId ?? string.Empty,
            PlanType = txn.PlanType,
            MembershipLevel = txn.MembershipLevel ?? string.Empty
        };

        if (txn.NetAmount.HasValue)
            proto.NetAmount = txn.NetAmount.Value;
        if (txn.PeriodStart.HasValue)
            proto.PeriodStart = Timestamp.FromDateTime(txn.PeriodStart.Value.ToUniversalTime());
        if (txn.PeriodEnd.HasValue)
            proto.PeriodEnd = Timestamp.FromDateTime(txn.PeriodEnd.Value.ToUniversalTime());
        if (txn.CompletedAt.HasValue)
            proto.CompletedAt = Timestamp.FromDateTime(txn.CompletedAt.Value.ToUniversalTime());

        foreach (var kv in txn.Metadata)
        {
            proto.Metadata[kv.Key] = kv.Value;
        }

        foreach (var promo in txn.Promotions)
        {
            proto.Promotions.Add(ToProto(promo));
        }

        return proto;
    }

    private static PromotionProto ToProto(Promotion promo)
    {
        var proto = new PromotionProto
        {
            PromotionId = promo.PromotionId,
            PromotionType = promo.PromotionType,
            Code = promo.Code ?? string.Empty,
            Name = promo.Name ?? string.Empty
        };

        if (promo.AmountOff.HasValue)
            proto.AmountOff = promo.AmountOff.Value;
        if (promo.PercentOff.HasValue)
            proto.PercentOff = promo.PercentOff.Value;

        foreach (var kv in promo.Metadata)
        {
            proto.Metadata[kv.Key] = kv.Value;
        }

        return proto;
    }

    // ========== Callback Notification (Point-to-Point) ==========
    // Note: Using different method names instead of overloads to avoid RPC proxy confusion

    public async Task NotifyPaymentCompletedToCallbackAsync(PaymentCompletedEvent evt)
    {
        var callbackId = await GetCallbackAgentIdAsync();
        if (callbackId == null)
        {
            Logger.LogDebug("[PaymentRecordGAgent] No callback agent configured, skipping notification");
            return;
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Sending PaymentCompleted to callback agent {CallbackAgentId}",
            callbackId);

        await SendToAsync(callbackId.Value.ToString(), evt);
    }

    public async Task NotifyPaymentFailedToCallbackAsync(PaymentFailedEvent evt)
    {
        var callbackId = await GetCallbackAgentIdAsync();
        if (callbackId == null)
        {
            Logger.LogDebug("[PaymentRecordGAgent] No callback agent configured, skipping notification");
            return;
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Sending PaymentFailed to callback agent {CallbackAgentId}",
            callbackId);

        await SendToAsync(callbackId.Value.ToString(), evt);
    }

    public async Task NotifyRefundCompletedToCallbackAsync(RefundCompletedEvent evt)
    {
        var callbackId = await GetCallbackAgentIdAsync();
        if (callbackId == null)
        {
            Logger.LogDebug("[PaymentRecordGAgent] No callback agent configured, skipping notification");
            return;
        }

        Logger.LogInformation(
            "[PaymentRecordGAgent] Sending RefundCompleted to callback agent {CallbackAgentId}",
            callbackId);

        await SendToAsync(callbackId.Value.ToString(), evt);
    }
}

