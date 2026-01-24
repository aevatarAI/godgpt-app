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

        // PaymentId: Guid -> string (matches Agent ID format with dashes)
        // Try both "PaymentId" and "Id" (old DTO used "Id")
        if (oldState.TryGetValue("PaymentId", out var paymentIdObj) || 
            oldState.TryGetValue("Id", out paymentIdObj))
        {
            var paymentIdStr = ConvertToString(paymentIdObj);
            if (!string.IsNullOrEmpty(paymentIdStr) && Guid.TryParse(paymentIdStr, out var guid))
                newState.PaymentId = guid.ToString("D"); // With dashes: xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx
        }

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // ExternalOrderId: string (old DTO used "OrderId")
        if (oldState.TryGetValue("ExternalOrderId", out var externalOrderIdObj) ||
            oldState.TryGetValue("OrderId", out externalOrderIdObj))
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

       
        // PriceId: string
        if (oldState.TryGetValue("PriceId", out var priceIdObj))
            newState.ProductId = ConvertToString(priceIdObj);

        // ProductName: string (or ProductId if no ProductName)
        if (oldState.TryGetValue("ProductName", out var productNameObj))
            newState.ProductName = ConvertToString(productNameObj);
        else if (!string.IsNullOrEmpty(newState.ProductId))
            newState.ProductName = newState.ProductId; // Fallback

        // PaymentMode: int (enum) - old DTO used "Mode" as string
        if (oldState.TryGetValue("PaymentMode", out var paymentModeObj) ||
            oldState.TryGetValue("Mode", out paymentModeObj))
            newState.PaymentMode = ConvertPaymentMode(paymentModeObj);

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

        // NetAmount: long? - old DTO used "AmountNetTotal"
        if (oldState.TryGetValue("NetAmount", out var netAmountObj) ||
            oldState.TryGetValue("AmountNetTotal", out netAmountObj))
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
        // Also try "InvoiceDetails" (old DTO name)
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
        else if (oldState.TryGetValue("InvoiceDetails", out var invoiceDetailsObj) && invoiceDetailsObj != null)
        {
            // Old format: InvoiceDetails -> Transactions
            var invoiceList = ConvertToList(invoiceDetailsObj);
            if (invoiceList != null)
            {
                foreach (var invObj in invoiceList)
                {
                    var transaction = ConvertInvoiceDetailToTransaction(invObj);
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

        // ========== Trial info stored in business_metadata ==========
        
        // IsTrial: bool (at payment level)
        if (oldState.TryGetValue("IsTrial", out var isTrialObj))
        {
            var isTrial = ConvertToBool(isTrialObj);
            if (isTrial)
                newState.BusinessMetadata["is_trial"] = "true";
        }

        // TrialCode: string (at payment level)
        if (oldState.TryGetValue("TrialCode", out var trialCodeObj))
        {
            var trialCode = ConvertToString(trialCodeObj);
            if (!string.IsNullOrEmpty(trialCode))
                newState.BusinessMetadata["trial_code"] = trialCode;
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

    private static bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.True) return true;
            if (je.ValueKind == JsonValueKind.False) return false;
        }
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    /// <summary>
    /// Convert PaymentMode - handles both int and string ("SUBSCRIPTION", "PAYMENT", etc.)
    /// </summary>
    private static int ConvertPaymentMode(object? obj)
    {
        if (obj == null) return 0;
        
        // If already int, return as-is
        if (obj is int i) return i;
        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number)
                return je.GetInt32();
            if (je.ValueKind == JsonValueKind.String)
            {
                var str = je.GetString()?.ToUpperInvariant();
                return str switch
                {
                    "SUBSCRIPTION" => 0,  // Subscription = 0
                    "PAYMENT" or "ONE_TIME" or "ONETIME" => 1,  // OneTime = 1
                    "SETUP" or "CONSUMABLE" => 2,  // Consumable = 2
                    _ => 0
                };
            }
        }
        
        // Try parse as string
        var strVal = obj.ToString()?.ToUpperInvariant();
        if (strVal != null)
        {
            return strVal switch
            {
                "SUBSCRIPTION" => 0,
                "PAYMENT" or "ONE_TIME" or "ONETIME" => 1,
                "SETUP" or "CONSUMABLE" => 2,
                _ => int.TryParse(strVal, out var intResult) ? intResult : 0
            };
        }
        return 0;
    }

    /// <summary>
    /// Convert to Int64 (cents). Old data may be decimal (dollars), new data is int64 (cents).
    /// If value has decimal places, assume it's dollars and convert to cents (* 100).
    /// </summary>
    private static long ConvertToInt64(object? obj)
    {
        if (obj == null) return 0;
        if (obj is long l) return l;
        if (obj is int i) return i;
        if (obj is decimal d) return ConvertDecimalToCents(d);
        if (obj is double dbl) return ConvertDoubleToCents(dbl);
        
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            // Try to get as double first (handles both integer and decimal values)
            if (je.TryGetDouble(out var doubleVal))
            {
                return ConvertDoubleToCents(doubleVal);
            }
            // Fallback to int64 (should not reach here normally)
            if (je.TryGetInt64(out var longVal))
                return longVal;
        }
        
        // Try parse as decimal string (e.g., "19.99")
        if (decimal.TryParse(obj.ToString(), out var decimalResult))
            return ConvertDecimalToCents(decimalResult);
            
        if (long.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }
    
    /// <summary>
    /// Convert decimal dollars to cents. If value looks like cents already (no decimal places), return as-is.
    /// </summary>
    private static long ConvertDecimalToCents(decimal value)
    {
        // If value has decimal places, it's likely dollars - convert to cents
        // If value is a whole number > 100, it's likely already in cents
        if (value == Math.Floor(value) && value >= 100)
            return (long)value; // Already in cents
        return (long)Math.Round(value * 100);
    }
    
    /// <summary>
    /// Convert double dollars to cents.
    /// </summary>
    private static long ConvertDoubleToCents(double value)
    {
        // If value has decimal places, it's likely dollars - convert to cents
        // If value is a whole number > 100, it's likely already in cents
        if (Math.Abs(value - Math.Floor(value)) < 0.0001 && value >= 100)
            return (long)value; // Already in cents
        return (long)Math.Round(value * 100);
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
            // Handle IsTrial and TrialCode
            if (je.TryGetProperty("IsTrial", out var isTrial))
                trans.IsTrial = isTrial.ValueKind == JsonValueKind.True;
            if (je.TryGetProperty("TrialCode", out var trialCode))
                trans.TrialCode = ConvertToString(trialCode);
        }

        return trans;
    }

    /// <summary>
    /// Convert old InvoiceDetail to TransactionProto
    /// Old format: { InvoiceId, Status, CreatedAt, CompletedAt, Amount, AmountNetTotal, Discounts, IsTrial, TrialCode }
    /// </summary>
    private static TransactionProto? ConvertInvoiceDetailToTransaction(object? invObj)
    {
        if (invObj == null) return null;

        var trans = new TransactionProto();

        if (invObj is JsonElement je && je.ValueKind == JsonValueKind.Object)
        {
            // InvoiceId -> TransactionId (or InvoiceId field)
            if (je.TryGetProperty("InvoiceId", out var invoiceId))
            {
                trans.TransactionId = ConvertToString(invoiceId);
                trans.InvoiceId = ConvertToString(invoiceId);
            }
            if (je.TryGetProperty("Status", out var status))
                trans.Status = ConvertToInt32(status);
            if (je.TryGetProperty("Amount", out var amount))
                trans.Amount = ConvertToInt64(amount);
            if (je.TryGetProperty("AmountNetTotal", out var netAmount))
                trans.NetAmount = ConvertToInt64(netAmount);
            if (je.TryGetProperty("CreatedAt", out var createdAt))
            {
                var dt = ConvertToDateTime(createdAt);
                if (dt.HasValue)
                    trans.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
            if (je.TryGetProperty("CompletedAt", out var completedAt))
            {
                var dt = ConvertToDateTime(completedAt);
                if (dt.HasValue)
                    trans.CompletedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
            }
            // Handle IsTrial and TrialCode
            if (je.TryGetProperty("IsTrial", out var isTrial))
                trans.IsTrial = isTrial.ValueKind == JsonValueKind.True;
            if (je.TryGetProperty("TrialCode", out var trialCode))
                trans.TrialCode = ConvertToString(trialCode);
            
            // Convert Discounts to Promotions
            if (je.TryGetProperty("Discounts", out var discounts) && 
                discounts.ValueKind == JsonValueKind.Array)
            {
                foreach (var disc in discounts.EnumerateArray())
                {
                    var promo = ConvertDiscountToPromotion(disc);
                    if (promo != null)
                        trans.Promotions.Add(promo);
                }
            }
        }

        return trans;
    }

    /// <summary>
    /// Convert old Discount to PromotionProto
    /// Old format: { CouponId, DiscountId, AmountOff, PercentOff }
    /// </summary>
    private static PromotionProto? ConvertDiscountToPromotion(JsonElement disc)
    {
        if (disc.ValueKind != JsonValueKind.Object) return null;

        var promo = new PromotionProto();

        if (disc.TryGetProperty("CouponId", out var couponId))
        {
            promo.PromotionId = ConvertToString(couponId);
            promo.Code = ConvertToString(couponId);
        }
        if (disc.TryGetProperty("DiscountId", out var discountId))
            promo.PromotionId = ConvertToString(discountId);
        if (disc.TryGetProperty("AmountOff", out var amountOff))
            promo.AmountOff = ConvertToInt64(amountOff);
        if (disc.TryGetProperty("PercentOff", out var percentOff))
        {
            var pct = ConvertToInt32(percentOff);
            if (pct > 0)
                promo.PercentOff = pct;
        }
        
        promo.PromotionType = "coupon";

        return promo;
    }
}
