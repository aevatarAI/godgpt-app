using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserBilling State converter
/// Converts old UserBillingGAgent state to new PaymentIndexGAgent state
/// Note: UserBillingGAgent was split into PaymentIndexGAgent (user-level) and PaymentRecordGAgent (order-level)
/// PaymentHistory items are also converted to PaymentRecordStateProto via AdditionalPaymentRecords property.
/// </summary>
public class UserBillingStateConverter : IStateConverter
{
    /// <summary>
    /// Additional PaymentRecordStateProto generated from PaymentHistory.
    /// </summary>
    public List<(string AgentId, PaymentRecordStateProto State)> AdditionalPaymentRecords { get; } = new();

    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        AdditionalPaymentRecords.Clear();
        
        if (oldState == null)
            return new PaymentIndexStateProto();

        var newState = new PaymentIndexStateProto();

        // UserId
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // Platform customer IDs (e.g., Stripe CustomerId, Apple CustomerId)
        if (oldState.TryGetValue("CustomerId", out var customerIdObj))
        {
            // Assume Stripe platform (0) if not specified
            var customerId = ConvertToString(customerIdObj);
            if (!string.IsNullOrEmpty(customerId))
                newState.PlatformCustomers["0"] = customerId; // Stripe = 0
        }

        // Apple Customer ID (if exists)
        if (oldState.TryGetValue("AppleCustomerId", out var appleCustomerIdObj))
        {
            var customerId = ConvertToString(appleCustomerIdObj);
            if (!string.IsNullOrEmpty(customerId))
                newState.PlatformCustomers["1"] = customerId; // AppStore = 1
        }

        // Google Play Customer ID (if exists)
        if (oldState.TryGetValue("GoogleCustomerId", out var googleCustomerIdObj))
        {
            var customerId = ConvertToString(googleCustomerIdObj);
            if (!string.IsNullOrEmpty(customerId))
                newState.PlatformCustomers["2"] = customerId; // GooglePlay = 2
        }

        // Active subscriptions (if exists as list or dictionary)
        if (oldState.TryGetValue("ActiveSubscriptions", out var subscriptionsObj))
        {
            ConvertActiveSubscriptions(subscriptionsObj, newState);
        }
        else if (oldState.TryGetValue("Subscriptions", out var subscriptionsObj2))
        {
            ConvertActiveSubscriptions(subscriptionsObj2, newState);
        }

        // PaymentHistory - convert to PaymentRecordStateProto
        if (oldState.TryGetValue("PaymentHistory", out var paymentHistoryObj))
        {
            ConvertPaymentHistory(paymentHistoryObj, newState, newState.UserId);
        }

        // Total payment count
        if (oldState.TryGetValue("TotalPaymentCount", out var totalCountObj))
            newState.TotalPaymentCount = ConvertToInt32(totalCountObj);
        else if (oldState.TryGetValue("PaymentCount", out var paymentCountObj))
            newState.TotalPaymentCount = ConvertToInt32(paymentCountObj);

        // Active subscription count
        newState.ActiveSubscriptionCount = newState.ActiveSubscriptions.Count;

        // Last updated
        if (oldState.TryGetValue("LastUpdated", out var lastUpdatedObj))
        {
            var dt = ConvertToDateTime(lastUpdatedObj);
            if (dt.HasValue)
                newState.LastUpdated = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }
        else
        {
            newState.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        return newState;
    }

    private void ConvertPaymentHistory(object? historyObj, PaymentIndexStateProto newState, string? userId)
    {
        if (historyObj == null) return;

        if (historyObj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in je.EnumerateArray())
            {
                // Add to active subscriptions
                var sub = ConvertToActiveSubscription(item);
                if (sub != null) newState.ActiveSubscriptions.Add(sub);

                // Generate PaymentRecordStateProto
                var record = ConvertToPaymentRecordState(item, userId);
                if (record != null) AdditionalPaymentRecords.Add(record.Value);
            }
        }
    }

    private static (string AgentId, PaymentRecordStateProto State)? ConvertToPaymentRecordState(JsonElement je, string? defaultUserId)
    {
        if (je.ValueKind != JsonValueKind.Object) return null;

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
            if (Math.Abs(d - Math.Floor(d)) > 0.0001 || d < 100)
                return (long)Math.Round(d * 100);
            return (long)d;
        }
        return 0;
    }

    private static void ConvertActiveSubscriptions(object? subscriptionsObj, PaymentIndexStateProto newState)
    {
        if (subscriptionsObj == null) return;

        if (subscriptionsObj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in je.EnumerateArray())
                {
                    var subscription = ConvertToActiveSubscription(item);
                    if (subscription != null)
                        newState.ActiveSubscriptions.Add(subscription);
                }
            }
            else if (je.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in je.EnumerateObject())
                {
                    var subscription = ConvertToActiveSubscription(prop.Value);
                    if (subscription != null)
                        newState.ActiveSubscriptions.Add(subscription);
                }
            }
        }
        else if (subscriptionsObj is IEnumerable<object> subscriptions)
        {
            foreach (var sub in subscriptions)
            {
                var subscription = ConvertToActiveSubscription(sub);
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
            // PaymentId (required)
            if (je.TryGetProperty("PaymentId", out var paymentIdEl) || je.TryGetProperty("paymentId", out paymentIdEl))
                subscription.PaymentId = ConvertToString(paymentIdEl);
            else if (je.TryGetProperty("Id", out var idEl) || je.TryGetProperty("id", out idEl))
                subscription.PaymentId = ConvertToString(idEl);

            // Business type
            if (je.TryGetProperty("BusinessType", out var businessTypeEl) || je.TryGetProperty("businessType", out businessTypeEl))
                subscription.BusinessType = ConvertToString(businessTypeEl);
            else
                subscription.BusinessType = "godgpt"; // Default

            // Business ID
            if (je.TryGetProperty("BusinessId", out var businessIdEl) || je.TryGetProperty("businessId", out businessIdEl))
                subscription.BusinessId = ConvertToString(businessIdEl);

            // Platform
            if (je.TryGetProperty("Platform", out var platformEl) || je.TryGetProperty("platform", out platformEl))
                subscription.Platform = ConvertToInt32(platformEl);

            // Product name
            if (je.TryGetProperty("ProductName", out var productNameEl) || je.TryGetProperty("productName", out productNameEl))
                subscription.ProductName = ConvertToString(productNameEl);

            // Amount
            if (je.TryGetProperty("Amount", out var amountEl) || je.TryGetProperty("amount", out amountEl))
                subscription.Amount = ConvertToInt64(amountEl);

            // Currency
            if (je.TryGetProperty("Currency", out var currencyEl) || je.TryGetProperty("currency", out currencyEl))
                subscription.Currency = ConvertToString(currencyEl);
            else
                subscription.Currency = "USD"; // Default

            // Period end
            if (je.TryGetProperty("PeriodEnd", out var periodEndEl) || je.TryGetProperty("periodEnd", out periodEndEl))
            {
                var dt = ConvertToDateTime(periodEndEl);
                if (dt.HasValue)
                    subscription.PeriodEnd = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }

            // Created at
            if (je.TryGetProperty("CreatedAt", out var createdAtEl) || je.TryGetProperty("createdAt", out createdAtEl))
            {
                var dt = ConvertToDateTime(createdAtEl);
                if (dt.HasValue)
                    subscription.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
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
