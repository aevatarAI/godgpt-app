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
/// 
/// IMPORTANT: PaymentHistory items are also converted to PaymentRecordStateProto via AdditionalPaymentRecords property.
/// </summary>
public class UserBillingGrainStateConverter : IStateConverter
{
    /// <summary>
    /// Additional PaymentRecordStateProto generated from PaymentHistory.
    /// Key: AgentId (PaymentRecordGAgent:{PaymentGrainId})
    /// Value: PaymentRecordStateProto
    /// </summary>
    public List<(string AgentId, PaymentRecordStateProto State)> AdditionalPaymentRecords { get; } = new();

    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        // Clear previous records
        AdditionalPaymentRecords.Clear();
        
        if (oldState == null)
            return new PaymentIndexStateProto();

        var newState = new PaymentIndexStateProto();

        // UserId - not in grain state, will be set from agent ID during migration
        // Note: Grain state doesn't have UserId, so we leave it empty here

        // UserId - try to get from state
        string? userId = null;
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            userId = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userId))
                newState.UserId = userId;
        }

        // CustomerId (Stripe platform = 0)
        if (oldState.TryGetValue("CustomerId", out var customerIdObj))
        {
            var customerId = ConvertToString(customerIdObj);
            if (!string.IsNullOrEmpty(customerId))
                newState.PlatformCustomers["0"] = customerId; // Stripe = 0
        }

        // PaymentHistory - convert to ActiveSubscriptions and PaymentRecordStateProto
        if (oldState.TryGetValue("PaymentHistory", out var paymentHistoryObj))
        {
            ConvertPaymentHistory(paymentHistoryObj, newState, userId);
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

    private void ConvertPaymentHistory(object? paymentHistoryObj, PaymentIndexStateProto newState, string? userId)
    {
        if (paymentHistoryObj == null) return;

        if (paymentHistoryObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                var subscription = ConvertToActiveSubscription(item);
                if (subscription != null)
                    newState.ActiveSubscriptions.Add(subscription);
                
                // Also generate PaymentRecordStateProto
                var paymentRecord = ConvertToPaymentRecordState(item, userId);
                if (paymentRecord != null)
                    AdditionalPaymentRecords.Add(paymentRecord.Value);
            }
        }
        else if (paymentHistoryObj is IEnumerable<object> paymentHistory)
        {
            foreach (var payment in paymentHistory)
            {
                var subscription = ConvertToActiveSubscription(payment);
                if (subscription != null)
                    newState.ActiveSubscriptions.Add(subscription);
                
                // Also generate PaymentRecordStateProto
                var paymentRecord = ConvertToPaymentRecordState(payment, userId);
                if (paymentRecord != null)
                    AdditionalPaymentRecords.Add(paymentRecord.Value);
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

    /// <summary>
    /// Convert PaymentSummary to PaymentRecordStateProto
    /// </summary>
    private static (string AgentId, PaymentRecordStateProto State)? ConvertToPaymentRecordState(object? obj, string? defaultUserId)
    {
        if (obj is not JsonElement je || je.ValueKind != JsonValueKind.Object)
            return null;

        var state = new PaymentRecordStateProto();
        string? paymentId = null;

        // PaymentGrainId -> PaymentId
        if (je.TryGetProperty("PaymentGrainId", out var grainIdEl))
        {
            var grainId = ConvertToString(grainIdEl);
            if (Guid.TryParse(grainId, out var guid))
            {
                paymentId = guid.ToString("D");
                state.PaymentId = paymentId;
            }
        }
        if (string.IsNullOrEmpty(paymentId)) return null;

        // UserId
        if (je.TryGetProperty("UserId", out var userIdEl))
        {
            var uid = ConvertToString(userIdEl);
            if (Guid.TryParse(uid, out var guid)) state.UserId = guid.ToString("D");
        }
        else if (!string.IsNullOrEmpty(defaultUserId)) state.UserId = defaultUserId;

        // OrderId -> ExternalOrderId
        if (je.TryGetProperty("OrderId", out var orderIdEl))
            state.ExternalOrderId = ConvertToString(orderIdEl);

        // SubscriptionId
        if (je.TryGetProperty("SubscriptionId", out var subIdEl))
            state.SubscriptionId = ConvertToString(subIdEl);

        // Platform
        if (je.TryGetProperty("Platform", out var platformEl))
            state.Platform = ConvertToInt32(platformEl);

        // AppStoreEnvironment -> Environment
        if (je.TryGetProperty("AppStoreEnvironment", out var envEl))
            state.Environment = ConvertToString(envEl);

        // PriceId -> ProductId
        if (je.TryGetProperty("PriceId", out var priceIdEl))
            state.ProductId = ConvertToString(priceIdEl);

        // MembershipLevel -> ProductName
        if (je.TryGetProperty("MembershipLevel", out var membershipEl))
        {
            var membership = ConvertToString(membershipEl);
            state.ProductName = membership;
            if (!string.IsNullOrEmpty(membership))
                state.BusinessMetadata["membership_level"] = membership;
        }

        // Amount (decimal -> cents)
        if (je.TryGetProperty("Amount", out var amountEl))
            state.Amount = ConvertDecimalToCents(amountEl);

        // Currency
        if (je.TryGetProperty("Currency", out var currencyEl))
            state.Currency = ConvertToString(currencyEl);
        else state.Currency = "USD";

        // AmountNetTotal -> NetAmount
        if (je.TryGetProperty("AmountNetTotal", out var netEl))
            state.NetAmount = ConvertDecimalToCents(netEl);

        // Status
        if (je.TryGetProperty("Status", out var statusEl))
            state.Status = ConvertToInt32(statusEl);

        // SubscriptionStartDate -> PeriodStart
        if (je.TryGetProperty("SubscriptionStartDate", out var startEl))
        {
            var dt = ConvertToDateTime(startEl);
            if (dt.HasValue) state.PeriodStart = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // SubscriptionEndDate -> PeriodEnd
        if (je.TryGetProperty("SubscriptionEndDate", out var endEl))
        {
            var dt = ConvertToDateTime(endEl);
            if (dt.HasValue) state.PeriodEnd = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // CreatedAt
        if (je.TryGetProperty("CreatedAt", out var createdEl))
        {
            var dt = ConvertToDateTime(createdEl);
            if (dt.HasValue) state.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // CompletedAt
        if (je.TryGetProperty("CompletedAt", out var completedEl))
        {
            var dt = ConvertToDateTime(completedEl);
            if (dt.HasValue) state.CompletedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // InvoiceDetails -> Transactions
        if (je.TryGetProperty("InvoiceDetails", out var invoicesEl) && invoicesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var inv in invoicesEl.EnumerateArray())
            {
                var trans = ConvertInvoiceToTransaction(inv);
                if (trans != null) state.Transactions.Add(trans);
            }
        }

        state.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        state.BusinessType = "godgpt";

        return ($"PaymentRecordGAgent:{paymentId}", state);
    }

    private static TransactionProto? ConvertInvoiceToTransaction(JsonElement inv)
    {
        if (inv.ValueKind != JsonValueKind.Object) return null;
        var trans = new TransactionProto();

        if (inv.TryGetProperty("InvoiceId", out var invoiceIdEl))
        {
            trans.TransactionId = ConvertToString(invoiceIdEl);
            trans.InvoiceId = ConvertToString(invoiceIdEl);
        }
        if (inv.TryGetProperty("Status", out var statusEl)) trans.Status = ConvertToInt32(statusEl);
        if (inv.TryGetProperty("Amount", out var amountEl)) trans.Amount = ConvertDecimalToCents(amountEl);
        if (inv.TryGetProperty("Currency", out var currencyEl)) trans.Currency = ConvertToString(currencyEl);
        if (inv.TryGetProperty("AmountNetTotal", out var netEl)) trans.NetAmount = ConvertDecimalToCents(netEl);
        if (inv.TryGetProperty("PurchaseToken", out var tokenEl)) trans.PurchaseToken = ConvertToString(tokenEl);
        
        if (inv.TryGetProperty("SubscriptionStartDate", out var startEl))
        {
            var dt = ConvertToDateTime(startEl);
            if (dt.HasValue) trans.PeriodStart = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        if (inv.TryGetProperty("SubscriptionEndDate", out var endEl))
        {
            var dt = ConvertToDateTime(endEl);
            if (dt.HasValue) trans.PeriodEnd = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        if (inv.TryGetProperty("CreatedAt", out var createdEl))
        {
            var dt = ConvertToDateTime(createdEl);
            if (dt.HasValue) trans.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        if (inv.TryGetProperty("CompletedAt", out var completedEl))
        {
            var dt = ConvertToDateTime(completedEl);
            if (dt.HasValue) trans.CompletedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        if (inv.TryGetProperty("IsTrial", out var trialEl)) trans.IsTrial = trialEl.ValueKind == JsonValueKind.True;
        if (inv.TryGetProperty("TrialCode", out var codeEl)) trans.TrialCode = ConvertToString(codeEl);

        return trans;
    }

    private static long ConvertDecimalToCents(JsonElement el)
    {
        if (el.TryGetDouble(out var d))
        {
            // If has decimal places or < 100, treat as dollars
            if (Math.Abs(d - Math.Floor(d)) > 0.0001 || d < 100)
                return (long)Math.Round(d * 100);
            return (long)d;
        }
        return 0;
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
