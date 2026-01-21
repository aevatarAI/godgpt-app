using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserBillingGrain State converter
/// Converts old Orleans UserBillingGrain state (OrleansgodgptprodUserBillingState) to new PaymentIndexGAgent state
/// 
/// Note: UserBillingGrain state only contains 4 fields:
/// - CustomerId
/// - PaymentHistory
/// - TotalPayments
/// - RefundedPayments
/// 
/// This converter handles the grain state format, which is simpler than the agent state format.
/// The agent state (UserBillingGAgent) should be migrated after this, and will overwrite/merge with this data.
/// </summary>
public class UserBillingGrainStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new PaymentIndexStateProto();

        var newState = new PaymentIndexStateProto();

        // UserId - not in grain state, will be set from agent ID during migration
        // Note: Grain state doesn't have UserId, so we leave it empty here

        // CustomerId (Stripe platform = 0)
        if (oldState.TryGetValue("CustomerId", out var customerIdObj))
        {
            var customerId = ConvertToString(customerIdObj);
            if (!string.IsNullOrEmpty(customerId))
                newState.PlatformCustomers["0"] = customerId; // Stripe = 0
        }

        // PaymentHistory - convert to ActiveSubscriptions
        if (oldState.TryGetValue("PaymentHistory", out var paymentHistoryObj))
        {
            ConvertPaymentHistory(paymentHistoryObj, newState);
        }

        // TotalPayments -> TotalPaymentCount
        if (oldState.TryGetValue("TotalPayments", out var totalPaymentsObj))
        {
            newState.TotalPaymentCount = ConvertToInt32(totalPaymentsObj);
        }

        // RefundedPayments - not directly mapped, but can be used for validation
        // Note: RefundedPayments is not part of PaymentIndexStateProto, so we skip it

        // Last updated - set to current time since grain state doesn't have this field
        newState.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);

        return newState;
    }

    private static void ConvertPaymentHistory(object? paymentHistoryObj, PaymentIndexStateProto newState)
    {
        if (paymentHistoryObj == null) return;

        if (paymentHistoryObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                var subscription = ConvertToActiveSubscription(item);
                if (subscription != null)
                    newState.ActiveSubscriptions.Add(subscription);
            }
        }
        else if (paymentHistoryObj is IEnumerable<object> paymentHistory)
        {
            foreach (var payment in paymentHistory)
            {
                var subscription = ConvertToActiveSubscription(payment);
                if (subscription != null)
                    newState.ActiveSubscriptions.Add(subscription);
            }
        }
    }

    private static ActiveSubscriptionProto? ConvertToActiveSubscription(object? obj)
    {
        if (obj == null) return null;

        var subscription = new ActiveSubscriptionProto();

        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            // PaymentId - use PaymentGrainId if available, otherwise OrderId
            if (je.TryGetProperty("PaymentGrainId", out var paymentGrainIdEl))
            {
                var grainId = ConvertToString(paymentGrainIdEl);
                if (!string.IsNullOrEmpty(grainId) && Guid.TryParse(grainId, out var guid))
                    subscription.PaymentId = guid.ToString("D");
            }
            else if (je.TryGetProperty("OrderId", out var orderIdEl))
            {
                subscription.PaymentId = ConvertToString(orderIdEl);
            }

            // Platform
            if (je.TryGetProperty("Platform", out var platformEl))
            {
                subscription.Platform = ConvertToInt32(platformEl);
            }

            // Note: ActiveSubscriptionProto doesn't have SubscriptionId field
            // SubscriptionId information is stored in PaymentId (which references PaymentRecordGAgent)

            // Product name - try to derive from PlanType or MembershipLevel
            if (je.TryGetProperty("MembershipLevel", out var membershipLevelEl))
            {
                subscription.ProductName = ConvertToString(membershipLevelEl);
            }
            else if (je.TryGetProperty("PlanType", out var planTypeEl))
            {
                subscription.ProductName = ConvertToString(planTypeEl);
            }

            // Amount
            if (je.TryGetProperty("Amount", out var amountEl))
            {
                subscription.Amount = ConvertToInt64(amountEl);
            }
            else if (je.TryGetProperty("AmountNetTotal", out var amountNetTotalEl))
            {
                subscription.Amount = ConvertToInt64(amountNetTotalEl);
            }

            // Currency
            if (je.TryGetProperty("Currency", out var currencyEl))
            {
                subscription.Currency = ConvertToString(currencyEl);
            }
            else
            {
                subscription.Currency = "USD"; // Default
            }

            // Period end - use SubscriptionEndDate
            if (je.TryGetProperty("SubscriptionEndDate", out var periodEndEl))
            {
                var dt = ConvertToDateTime(periodEndEl);
                if (dt.HasValue)
                    subscription.PeriodEnd = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }

            // Created at - use CreatedAt or SubscriptionStartDate
            if (je.TryGetProperty("CreatedAt", out var createdAtEl))
            {
                var dt = ConvertToDateTime(createdAtEl);
                if (dt.HasValue)
                    subscription.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
            else if (je.TryGetProperty("SubscriptionStartDate", out var startDateEl))
            {
                var dt = ConvertToDateTime(startDateEl);
                if (dt.HasValue)
                    subscription.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }

            // Business type - default to "godgpt"
            subscription.BusinessType = "godgpt";
        }

        // Only return if PaymentId is set
        if (string.IsNullOrEmpty(subscription.PaymentId))
            return null;

        return subscription;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private static long ConvertToInt64(object? obj)
    {
        if (obj == null) return 0L;
        if (obj is long l) return l;
        if (obj is int i) return i;
        if (obj is decimal d) return (long)d;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt64();
        if (long.TryParse(obj.ToString(), out var result)) return result;
        return 0L;
    }

    private static DateTime? ConvertToDateTime(object? obj)
    {
        if (obj == null) return null;
        if (obj is DateTime dt) return dt;
        if (obj is DateTimeOffset dto) return dto.DateTime;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String)
        {
            if (DateTime.TryParse(je.GetString(), out var dt2)) return dt2;
        }
        if (DateTime.TryParse(obj.ToString(), out var dt3)) return dt3;
        return null;
    }
}
