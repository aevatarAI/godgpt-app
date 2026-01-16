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
/// This converter only handles PaymentIndexGAgent conversion. PaymentRecordGAgent conversion may need separate handling.
/// </summary>
public class UserBillingStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
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
