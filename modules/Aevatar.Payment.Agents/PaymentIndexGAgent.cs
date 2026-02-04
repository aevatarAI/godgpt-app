using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Payment.Agents;

/// <summary>
/// User-level payment index agent - manages active subscriptions and platform customer IDs.
/// Acts as event hub for business layer - publishes events Down to registered children.
/// Keeps state extremely lightweight.
/// </summary>
public class PaymentIndexGAgent : GAgentBase<PaymentIndexStateProto>, IPaymentIndexGAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Payment Index Agent for user {State.UserId}, " +
                               $"subscriptions: {State.ActiveSubscriptionCount}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize UserId through Event Sourcing if not set
        // Store complete Agent Id for consistency and direct Agent lookup
        if (string.IsNullOrEmpty(State.UserId))
        {
            RaiseEvent(new PaymentIndexInitializedEvent
            {
                UserId = Id,
                InitializedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await ConfirmEventsAsync();
        }
    }

    // ========== Event Broadcasting (Down to Business Agents) ==========

    public async Task NotifyPaymentCompletedAsync(PaymentCompletedEvent evt)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Broadcasting PaymentCompleted to children: user={UserId}, payment={PaymentId}",
            Id, evt.Context?.PaymentId);

        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task NotifyPaymentFailedAsync(PaymentFailedEvent evt)
    {
        Logger.LogWarning(
            "[PaymentIndexGAgent] Broadcasting PaymentFailed to children: user={UserId}, error={ErrorCode}",
            Id, evt.ErrorCode);

        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task NotifyRefundCompletedAsync(RefundCompletedEvent evt)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Broadcasting RefundCompleted to children: user={UserId}, refund={RefundId}",
            Id, evt.RefundId);

        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task NotifyPaymentCancelledAsync(PaymentCancelledEvent evt)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Broadcasting PaymentCancelled to children: user={UserId}, payment={PaymentId}, reason={Reason}",
            Id, evt.Context?.PaymentId, evt.Reason);

        await PublishAsync(evt, EventDirection.Down);
    }

    // ========== Event Sourcing: TransitionState ==========

    protected override void TransitionState(PaymentIndexStateProto state, IMessage evt)
    {
        switch (evt)
        {
            case PaymentIndexInitializedEvent e:
                state.UserId = e.UserId;
                break;
                
            case PlatformCustomerUpdatedEvent e:
                state.PlatformCustomers[e.Platform.ToString()] = e.CustomerId;
                break;
                
            case ActiveSubscriptionAddedEvent e:
                state.ActiveSubscriptions.Add(e.Subscription);
                state.ActiveSubscriptionCount = state.ActiveSubscriptions.Count;
                state.TotalPaymentCount++; // Auto-increment when adding subscription
                break;
                
            case ActiveSubscriptionRemovedEvent e:
                // Update status to Cancelled instead of removing (preserves history)
                var toCancel = state.ActiveSubscriptions.FirstOrDefault(s => s.PaymentId == e.PaymentId);
                if (toCancel != null)
                {
                    toCancel.Status = 8; // PaymentStatus.Cancelled
                }
                break;
                
            case SubscriptionPeriodEndUpdatedEvent e:
                var sub = state.ActiveSubscriptions.FirstOrDefault(s => s.PaymentId == e.PaymentId);
                if (sub != null)
                {
                    sub.PeriodEnd = e.NewPeriodEnd;
                }
                break;
                
            case SubscriptionIdUpdatedEvent e:
                var subToUpdate = state.ActiveSubscriptions.FirstOrDefault(s => s.PaymentId == e.PaymentId);
                if (subToUpdate != null)
                {
                    subToUpdate.SubscriptionId = e.SubscriptionId;
                }
                break;
                
            case PaymentCountIncrementedEvent e:
                state.TotalPaymentCount = e.NewCount;
                break;
                
            case IndexClearedEvent:
                state.PlatformCustomers.Clear();
                state.ActiveSubscriptions.Clear();
                state.TotalPaymentCount = 0;
                state.ActiveSubscriptionCount = 0;
                break;
        }
        
        state.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
    }

    // ========== Platform Customer ID ==========

    public Task<string?> GetPlatformCustomerIdAsync(PaymentPlatform platform)
    {
        var key = ((int)platform).ToString();
        return Task.FromResult(
            State.PlatformCustomers.TryGetValue(key, out var customerId) 
                ? customerId 
                : null);
    }

    public async Task SetPlatformCustomerIdAsync(PaymentPlatform platform, string customerId)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Setting {Platform} customer ID for user {UserId}",
            platform, Id);

        RaiseEvent(new PlatformCustomerUpdatedEvent
        {
            Platform = (int)platform,
            CustomerId = customerId
        });

        await ConfirmEventsAsync();
    }

    // ========== Active Subscription Management ==========

    public async Task AddActiveSubscriptionAsync(ActiveSubscriptionProto subscription)
    {
        // Idempotent: skip if already exists
        if (State.ActiveSubscriptions.Any(s => s.PaymentId == subscription.PaymentId))
        {
            Logger.LogDebug(
                "[PaymentIndexGAgent] Subscription {PaymentId} already exists, skipping",
                subscription.PaymentId);
            return;
        }
        
        Logger.LogInformation(
            "[PaymentIndexGAgent] Adding active subscription {PaymentId} for user {UserId}",
            subscription.PaymentId, Id);

        // Event handler auto-increments TotalPaymentCount
        RaiseEvent(new ActiveSubscriptionAddedEvent
        {
            Subscription = subscription
        });

        await ConfirmEventsAsync();
    }

    public async Task UpdateSubscriptionPeriodEndAsync(string paymentId, DateTime periodEnd)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Updating period end for {PaymentId} to {PeriodEnd}",
            paymentId, periodEnd);

        RaiseEvent(new SubscriptionPeriodEndUpdatedEvent
        {
            PaymentId = paymentId,
            NewPeriodEnd = Timestamp.FromDateTime(periodEnd.ToUniversalTime())
        });

        await ConfirmEventsAsync();
    }

    public async Task UpdateSubscriptionIdAsync(string paymentId, string subscriptionId)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Updating SubscriptionId for {PaymentId} to {SubscriptionId}",
            paymentId, subscriptionId);

        RaiseEvent(new SubscriptionIdUpdatedEvent
        {
            PaymentId = paymentId,
            SubscriptionId = subscriptionId
        });

        await ConfirmEventsAsync();
    }

    public async Task RemoveActiveSubscriptionAsync(string paymentId)
    {
        Logger.LogInformation(
            "[PaymentIndexGAgent] Marking subscription {PaymentId} as Cancelled for user {UserId}",
            paymentId, Id);

        RaiseEvent(new ActiveSubscriptionRemovedEvent
        {
            PaymentId = paymentId
        });

        await ConfirmEventsAsync();
    }

    // ========== Query ==========

    public Task<ActiveSubscriptionListResponse> GetActiveSubscriptionsAsync()
    {
        var response = new ActiveSubscriptionListResponse();
        // Filter: not cancelled (status != 8) AND not expired
        // Status 0 = default (active), 5 = Completed (active)
        // Status 8 = Cancelled, 9 = Expired, 10 = Refunded - should be filtered
        var activeSubscriptions = State.ActiveSubscriptions
            .Where(s => s.Status < 8) // Exclude Cancelled(8), Expired(9), Refunded(10)
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow);
        
        response.Subscriptions.AddRange(activeSubscriptions);
        return Task.FromResult(response);
    }

    public Task<ActiveSubscriptionListResponse> GetAllSubscriptionsAsync()
    {
        var response = new ActiveSubscriptionListResponse();
        response.Subscriptions.AddRange(State.ActiveSubscriptions);
        return Task.FromResult(response);
    }

    public Task<ActiveSubscriptionListResponse> GetActiveSubscriptionsByBusinessAsync(string businessType)
    {
        var response = new ActiveSubscriptionListResponse();
        // Filter: not cancelled (status < 8) AND not expired AND matching business type
        var activeSubscriptions = State.ActiveSubscriptions
            .Where(s => s.BusinessType == businessType)
            .Where(s => s.Status < 8) // Exclude Cancelled(8), Expired(9), Refunded(10)
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow);
        
        response.Subscriptions.AddRange(activeSubscriptions);
        return Task.FromResult(response);
    }

    public Task<bool> HasActiveSubscriptionAsync(string? businessType = null)
    {
        var query = State.ActiveSubscriptions
            .Where(s => s.Status < 8) // Exclude Cancelled(8), Expired(9), Refunded(10)
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow);

        if (!string.IsNullOrEmpty(businessType))
        {
            query = query.Where(s => s.BusinessType == businessType);
        }

        return Task.FromResult(query.Any());
    }

    public Task<string?> GetPaymentIdBySubscriptionIdAsync(string subscriptionId)
    {
        if (string.IsNullOrEmpty(subscriptionId))
        {
            return Task.FromResult<string?>(null);
        }

        var subscription = State.ActiveSubscriptions
            .FirstOrDefault(s => s.SubscriptionId == subscriptionId);

        return Task.FromResult(subscription?.PaymentId);
    }

    // ========== Statistics ==========

    public Task<int> GetTotalPaymentCountAsync()
    {
        return Task.FromResult(State.TotalPaymentCount);
    }

    public async Task IncrementPaymentCountAsync()
    {
        RaiseEvent(new PaymentCountIncrementedEvent
        {
            NewCount = State.TotalPaymentCount + 1
        });

        await ConfirmEventsAsync();
    }

    // ========== Management ==========

    public async Task ClearAllAsync()
    {
        Logger.LogWarning("[PaymentIndexGAgent] Clearing all data for user {UserId}", Id);

        RaiseEvent(new IndexClearedEvent
        {
            ClearedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
    }

    // ========== Proto Conversions ==========

    private static ActiveSubscriptionProto ToProto(ActiveSubscription sub)
    {
        return new ActiveSubscriptionProto
        {
            PaymentId = sub.PaymentId,
            BusinessType = sub.BusinessType,
            BusinessId = sub.BusinessId,
            Platform = (int)sub.Platform,
            ProductName = sub.ProductName,
            Amount = sub.Amount,
            Currency = sub.Currency,
            PeriodEnd = Timestamp.FromDateTime(sub.PeriodEnd.ToUniversalTime()),
            CreatedAt = Timestamp.FromDateTime(sub.CreatedAt.ToUniversalTime())
        };
    }

    private static ActiveSubscription FromProto(ActiveSubscriptionProto proto)
    {
        return new ActiveSubscription
        {
            PaymentId = proto.PaymentId,
            BusinessType = proto.BusinessType,
            BusinessId = proto.BusinessId,
            Platform = (PaymentPlatform)proto.Platform,
            ProductName = proto.ProductName,
            Amount = proto.Amount,
            Currency = proto.Currency,
            PeriodEnd = proto.PeriodEnd?.ToDateTime() ?? DateTime.MaxValue,
            CreatedAt = proto.CreatedAt?.ToDateTime() ?? DateTime.UtcNow
        };
    }
}
