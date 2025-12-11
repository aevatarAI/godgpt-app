using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Payment.Agents;

/// <summary>
/// User-level payment index agent - manages active subscriptions and platform customer IDs.
/// Acts as event hub for business layer - publishes events Down to registered children.
/// Keeps state extremely lightweight (< 1KB).
/// </summary>
public class PaymentIndexGAgent : GAgentBase<PaymentIndexStateProto>, IPaymentIndexGAgent
{
    public ILogger<PaymentIndexGAgent> PaymentLogger { get; set; } = NullLogger<PaymentIndexGAgent>.Instance;

    public PaymentIndexGAgent() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"PaymentIndex: {State.ActiveSubscriptionCount} active subscriptions");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        if (string.IsNullOrEmpty(State.UserId))
        {
            State.UserId = Id.ToString();
        }
    }

    // ========== Event Broadcasting (Down to Business Agents) ==========

    public async Task NotifyPaymentCompletedAsync(PaymentCompletedEvent evt)
    {
        PaymentLogger.LogInformation(
            "[PaymentIndexGAgent] Broadcasting PaymentCompleted to children: user={UserId}, payment={PaymentId}",
            Id, evt.Context?.PaymentId);

        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task NotifyPaymentFailedAsync(PaymentFailedEvent evt)
    {
        PaymentLogger.LogWarning(
            "[PaymentIndexGAgent] Broadcasting PaymentFailed to children: user={UserId}, error={ErrorCode}",
            Id, evt.ErrorCode);

        await PublishAsync(evt, EventDirection.Down);
    }

    public async Task NotifyRefundCompletedAsync(RefundCompletedEvent evt)
    {
        PaymentLogger.LogInformation(
            "[PaymentIndexGAgent] Broadcasting RefundCompleted to children: user={UserId}, refund={RefundId}",
            Id, evt.RefundId);

        await PublishAsync(evt, EventDirection.Down);
    }

    // ========== Event Sourcing: TransitionState ==========

    protected override void TransitionState(PaymentIndexStateProto state, IMessage evt)
    {
        switch (evt)
        {
            case PlatformCustomerUpdatedEvent e:
                state.PlatformCustomers[e.Platform.ToString()] = e.CustomerId;
                break;
                
            case ActiveSubscriptionAddedEvent e:
                state.ActiveSubscriptions.Add(e.Subscription);
                state.ActiveSubscriptionCount = state.ActiveSubscriptions.Count;
                break;
                
            case ActiveSubscriptionRemovedEvent e:
                var toRemove = state.ActiveSubscriptions.FirstOrDefault(s => s.PaymentId == e.PaymentId);
                if (toRemove != null)
                {
                    state.ActiveSubscriptions.Remove(toRemove);
                    state.ActiveSubscriptionCount = state.ActiveSubscriptions.Count;
                }
                break;
                
            case SubscriptionPeriodEndUpdatedEvent e:
                var sub = state.ActiveSubscriptions.FirstOrDefault(s => s.PaymentId == e.PaymentId);
                if (sub != null)
                {
                    sub.PeriodEnd = e.NewPeriodEnd;
                }
                break;
                
            case PaymentCountIncrementedEvent e:
                state.TotalPaymentCount = e.NewCount;
                break;
                
            case IndexClearedEvent e:
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
        PaymentLogger.LogInformation(
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

    public async Task AddActiveSubscriptionAsync(ActiveSubscription subscription)
    {
        PaymentLogger.LogInformation(
            "[PaymentIndexGAgent] Adding active subscription {PaymentId} for user {UserId}",
            subscription.PaymentId, Id);

        RaiseEvent(new ActiveSubscriptionAddedEvent
        {
            Subscription = ToProto(subscription)
        });

        await ConfirmEventsAsync();
    }

    public async Task UpdateSubscriptionPeriodEndAsync(string paymentId, DateTime periodEnd)
    {
        PaymentLogger.LogInformation(
            "[PaymentIndexGAgent] Updating period end for {PaymentId} to {PeriodEnd}",
            paymentId, periodEnd);

        RaiseEvent(new SubscriptionPeriodEndUpdatedEvent
        {
            PaymentId = paymentId,
            NewPeriodEnd = Timestamp.FromDateTime(periodEnd.ToUniversalTime())
        });

        await ConfirmEventsAsync();
    }

    public async Task RemoveActiveSubscriptionAsync(string paymentId)
    {
        PaymentLogger.LogInformation(
            "[PaymentIndexGAgent] Removing active subscription {PaymentId} for user {UserId}",
            paymentId, Id);

        RaiseEvent(new ActiveSubscriptionRemovedEvent
        {
            PaymentId = paymentId
        });

        await ConfirmEventsAsync();
    }

    // ========== Query ==========

    public Task<List<ActiveSubscription>> GetActiveSubscriptionsAsync()
    {
        var result = State.ActiveSubscriptions
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow)
            .Select(FromProto)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<List<ActiveSubscription>> GetActiveSubscriptionsByBusinessAsync(string businessType)
    {
        var result = State.ActiveSubscriptions
            .Where(s => s.BusinessType == businessType)
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow)
            .Select(FromProto)
            .ToList();

        return Task.FromResult(result);
    }

    public Task<bool> HasActiveSubscriptionAsync(string? businessType = null)
    {
        var query = State.ActiveSubscriptions
            .Where(s => s.PeriodEnd == null || s.PeriodEnd.ToDateTime() > DateTime.UtcNow);

        if (!string.IsNullOrEmpty(businessType))
        {
            query = query.Where(s => s.BusinessType == businessType);
        }

        return Task.FromResult(query.Any());
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
        PaymentLogger.LogWarning("[PaymentIndexGAgent] Clearing all data for user {UserId}", Id);

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

