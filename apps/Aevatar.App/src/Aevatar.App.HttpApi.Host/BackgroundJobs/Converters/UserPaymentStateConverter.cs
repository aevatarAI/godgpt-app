using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserPaymentState converter - converts old Orleans UserPaymentState grain state to new PaymentRecordStateProto
/// </summary>
public class UserPaymentStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new PaymentRecordStateProto();

        var newState = new PaymentRecordStateProto();

        // PaymentId: Guid -> string (matches Agent ID)
        if (oldState.TryGetValue("PaymentId", out var paymentIdObj))
        {
            var paymentIdStr = ConvertToString(paymentIdObj);
            if (!string.IsNullOrEmpty(paymentIdStr))
                newState.PaymentId = paymentIdStr;
        }

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // ExternalOrderId: string
        if (oldState.TryGetValue("ExternalOrderId", out var externalOrderIdObj))
            newState.ExternalOrderId = ConvertToString(externalOrderIdObj);

        // SubscriptionId: string
        if (oldState.TryGetValue("SubscriptionId", out var subscriptionIdObj))
            newState.SubscriptionId = ConvertToString(subscriptionIdObj);

        // BusinessType: string
        if (oldState.TryGetValue("BusinessType", out var businessTypeObj))
            newState.BusinessType = ConvertToString(businessTypeObj);

        // BusinessId: string
        if (oldState.TryGetValue("BusinessId", out var businessIdObj))
            newState.BusinessId = ConvertToString(businessIdObj);

        // Platform: int (enum)
        if (oldState.TryGetValue("Platform", out var platformObj))
            newState.Platform = ConvertToInt32(platformObj);

        // Environment: string
        if (oldState.TryGetValue("Environment", out var environmentObj))
            newState.Environment = ConvertToString(environmentObj);

        // CustomerId: string
        if (oldState.TryGetValue("CustomerId", out var customerIdObj))
            newState.CustomerId = ConvertToString(customerIdObj);

        // ProductId: string
        if (oldState.TryGetValue("ProductId", out var productIdObj))
            newState.ProductId = ConvertToString(productIdObj);

        // PriceId: string
        if (oldState.TryGetValue("PriceId", out var priceIdObj))
            newState.PriceId = ConvertToString(priceIdObj);

        // ProductName: string
        if (oldState.TryGetValue("ProductName", out var productNameObj))
            newState.ProductName = ConvertToString(productNameObj);

        // PaymentMode: int (enum)
        if (oldState.TryGetValue("PaymentMode", out var paymentModeObj))
            newState.PaymentMode = ConvertToInt32(paymentModeObj);

        // BillingCycle: int (enum)
        if (oldState.TryGetValue("BillingCycle", out var billingCycleObj))
            newState.BillingCycle = ConvertToInt32(billingCycleObj);

        // PeriodStart: DateTime -> Timestamp
        if (oldState.TryGetValue("PeriodStart", out var periodStartObj))
        {
            var dt = ConvertToDateTime(periodStartObj);
            if (dt.HasValue)
                newState.PeriodStart = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // PeriodEnd: DateTime -> Timestamp
        if (oldState.TryGetValue("PeriodEnd", out var periodEndObj))
        {
            var dt = ConvertToDateTime(periodEndObj);
            if (dt.HasValue)
                newState.PeriodEnd = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // Amount: long
        if (oldState.TryGetValue("Amount", out var amountObj))
            newState.Amount = ConvertToInt64(amountObj);

        // Currency: string
        if (oldState.TryGetValue("Currency", out var currencyObj))
            newState.Currency = ConvertToString(currencyObj);

        // NetAmount: long?
        if (oldState.TryGetValue("NetAmount", out var netAmountObj))
            newState.NetAmount = ConvertToInt64(netAmountObj);

        // Status: int (enum)
        if (oldState.TryGetValue("Status", out var statusObj))
            newState.Status = ConvertToInt32(statusObj);

        // CreatedAt: DateTime -> Timestamp
        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // CompletedAt: DateTime? -> Timestamp?
        if (oldState.TryGetValue("CompletedAt", out var completedAtObj))
        {
            var dt = ConvertToDateTime(completedAtObj);
            if (dt.HasValue)
                newState.CompletedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // Transactions: List<Transaction> -> repeated TransactionProto
        if (oldState.TryGetValue("Transactions", out var transactionsObj) && transactionsObj != null)
        {
            var transactionList = ConvertToList(transactionsObj);
            if (transactionList != null)
            {
                foreach (var transObj in transactionList)
                {
                    var transaction = ConvertTransaction(transObj);
                    if (transaction != null)
                        newState.Transactions.Add(transaction);
                }
            }
        }

        // LastUpdated: DateTime -> Timestamp
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

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) 
            return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number)
            return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private static long ConvertToInt64(object? obj)
    {
        if (obj == null) return 0;
        if (obj is long l) return l;
        if (obj is int i) return i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number)
            return je.GetInt64();
        if (long.TryParse(obj.ToString(), out var result)) return result;
        return 0;
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

    private static List<object>? ConvertToList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<object> list) return list;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var result = new List<object>();
            foreach (var item in je.EnumerateArray())
                result.Add(item);
            return result;
        }
        return null;
    }

    private static TransactionProto? ConvertTransaction(object? transObj)
    {
        if (transObj == null) return null;

        var trans = new TransactionProto();

        if (transObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            if (je.TryGetProperty("TransactionId", out var transactionId))
                trans.TransactionId = ConvertToString(transactionId);
            if (je.TryGetProperty("ExternalTransactionId", out var externalTransactionId))
                trans.ExternalTransactionId = ConvertToString(externalTransactionId);
            if (je.TryGetProperty("TransactionType", out var transactionType))
                trans.TransactionType = ConvertToInt32(transactionType);
            if (je.TryGetProperty("Status", out var status))
                trans.Status = ConvertToInt32(status);
            if (je.TryGetProperty("Amount", out var amount))
                trans.Amount = ConvertToInt64(amount);
            if (je.TryGetProperty("Currency", out var currency))
                trans.Currency = ConvertToString(currency);
            if (je.TryGetProperty("CreatedAt", out var createdAt))
            {
                var dt = ConvertToDateTime(createdAt);
                if (dt.HasValue)
                    trans.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
        }

        return trans;
    }
}
