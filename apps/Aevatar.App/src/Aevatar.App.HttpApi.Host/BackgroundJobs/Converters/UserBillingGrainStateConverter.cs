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

        // MembershipLevel -> business_metadata (NOT ProductName!)
        // MembershipLevel is "Premium" or "Ultimate", stored in TransactionProto.MembershipLevel
        if (je.TryGetProperty("MembershipLevel", out var membershipEl))
        {
            var membership = ConvertToString(membershipEl);
            if (!string.IsNullOrEmpty(membership))
                state.BusinessMetadata["membership_level"] = membership;
        }
        
        // ProductName - try actual product name field
        if (je.TryGetProperty("ProductName", out var productNameEl))
            state.ProductName = ConvertToString(productNameEl);

        // PaymentMode: old system uses "Mode" as string ("payment", "subscription", "setup")
        if (je.TryGetProperty("Mode", out var modeEl))
            state.PaymentMode = ConvertPaymentMode(modeEl);
        else if (je.TryGetProperty("PaymentMode", out var paymentModeEl))
            state.PaymentMode = ConvertPaymentMode(paymentModeEl);

        // BillingCycle: int enum (None=0, Weekly=1, Monthly=2, Quarterly=3, Yearly=4, Lifetime=5)
        // Old data uses "PlanType" field name
        if (je.TryGetProperty("PlanType", out var planTypeEl2))
            state.BillingCycle = ConvertToInt32(planTypeEl2);
        else if (je.TryGetProperty("BillingCycle", out var billingCycleEl))
            state.BillingCycle = ConvertToInt32(billingCycleEl);

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

        // SubscriptionStartDate -> PeriodStart (fallback to first InvoiceDetails if MinValue)
        DateTime? periodStart = null;
        if (je.TryGetProperty("SubscriptionStartDate", out var startEl))
        {
            periodStart = ConvertToDateTime(startEl);
            // DateTime.MinValue (0001-01-01) means no value
            if (periodStart.HasValue && periodStart.Value.Year <= 1) periodStart = null;
        }

        // SubscriptionEndDate -> PeriodEnd (fallback to first InvoiceDetails if MinValue)
        DateTime? periodEnd = null;
        if (je.TryGetProperty("SubscriptionEndDate", out var endEl))
        {
            periodEnd = ConvertToDateTime(endEl);
            // DateTime.MinValue (0001-01-01) means no value
            if (periodEnd.HasValue && periodEnd.Value.Year <= 1) periodEnd = null;
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

        // InvoiceDetails -> Transactions (also try to get period dates from first invoice if missing)
        if (je.TryGetProperty("InvoiceDetails", out var invoicesEl) && invoicesEl.ValueKind == JsonValueKind.Array)
        {
            bool firstInvoice = true;
            foreach (var inv in invoicesEl.EnumerateArray())
            {
                // Pass parent element (je) for fallback values when invoice fields are null
                var trans = ConvertInvoiceToTransaction(inv, je);
                if (trans != null) state.Transactions.Add(trans);
                
                // Try to get period dates from first invoice if not set at top level
                if (firstInvoice && inv.ValueKind == JsonValueKind.Object)
                {
                    if (periodStart == null && inv.TryGetProperty("SubscriptionStartDate", out var invStartEl))
                    {
                        var invStart = ConvertToDateTime(invStartEl);
                        if (invStart.HasValue && invStart.Value.Year > 1) periodStart = invStart;
                    }
                    if (periodEnd == null && inv.TryGetProperty("SubscriptionEndDate", out var invEndEl))
                    {
                        var invEnd = ConvertToDateTime(invEndEl);
                        if (invEnd.HasValue && invEnd.Value.Year > 1) periodEnd = invEnd;
                    }
                    firstInvoice = false;
                }
            }
        }
        
        // Set period dates
        if (periodStart.HasValue) state.PeriodStart = Timestamp.FromDateTime(periodStart.Value.ToUniversalTime());
        if (periodEnd.HasValue) state.PeriodEnd = Timestamp.FromDateTime(periodEnd.Value.ToUniversalTime());

        // Add plan_type to business_metadata from MembershipLevel
        if (je.TryGetProperty("MembershipLevel", out var membershipLevelEl))
        {
            var membershipLevel = ConvertToString(membershipLevelEl);
            if (!string.IsNullOrEmpty(membershipLevel))
            {
                // Map MembershipLevel to plan_type (Premium -> premium, Ultimate -> ultimate)
                state.BusinessMetadata["plan_type"] = membershipLevel.ToLowerInvariant();
            }
        }

        state.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        state.BusinessType = "godgpt";

        return ($"PaymentRecordGAgent:{paymentId}", state);
    }

    private static TransactionProto? ConvertInvoiceToTransaction(JsonElement inv, JsonElement parent)
    {
        if (inv.ValueKind != JsonValueKind.Object) return null;
        var trans = new TransactionProto();

        // InvoiceId -> TransactionId and InvoiceId
        if (inv.TryGetProperty("InvoiceId", out var invoiceIdEl))
        {
            var invoiceId = ConvertToString(invoiceIdEl);
            trans.TransactionId = invoiceId;
            trans.InvoiceId = invoiceId;
            // Also set as ExternalTransactionId if not empty
            if (!string.IsNullOrEmpty(invoiceId))
                trans.ExternalTransactionId = invoiceId;
        }
        
        // Status - try invoice first, then parent
        if (inv.TryGetProperty("Status", out var statusEl) && statusEl.ValueKind == JsonValueKind.Number)
            trans.Status = statusEl.GetInt32();
        else if (parent.TryGetProperty("Status", out var parentStatusEl))
            trans.Status = ConvertToInt32(parentStatusEl);
        
        // Amount - try invoice first, then parent (convert decimal to cents)
        if (inv.TryGetProperty("Amount", out var amountEl) && IsValidNumber(amountEl))
            trans.Amount = ConvertDecimalToCents(amountEl);
        else if (parent.TryGetProperty("Amount", out var parentAmountEl))
            trans.Amount = ConvertDecimalToCents(parentAmountEl);
        
        // Currency - try invoice first, then parent
        var currency = GetStringValue(inv, "Currency");
        if (string.IsNullOrEmpty(currency))
            currency = GetStringValue(parent, "Currency");
        trans.Currency = currency ?? "USD";
        
        // AmountNetTotal -> NetAmount
        if (inv.TryGetProperty("AmountNetTotal", out var netEl) && IsValidNumber(netEl))
            trans.NetAmount = ConvertDecimalToCents(netEl);
        else if (parent.TryGetProperty("AmountNetTotal", out var parentNetEl))
            trans.NetAmount = ConvertDecimalToCents(parentNetEl);
        
        // PurchaseToken
        var purchaseToken = GetStringValue(inv, "PurchaseToken");
        if (string.IsNullOrEmpty(purchaseToken))
            purchaseToken = GetStringValue(parent, "PurchaseToken");
        trans.PurchaseToken = purchaseToken ?? "";

        // SubscriptionStartDate -> PeriodStart
        var periodStart = GetValidDateTime(inv, "SubscriptionStartDate");
        if (periodStart == null) periodStart = GetValidDateTime(parent, "SubscriptionStartDate");
        if (periodStart.HasValue) trans.PeriodStart = Timestamp.FromDateTime(periodStart.Value.ToUniversalTime());
        
        // SubscriptionEndDate -> PeriodEnd
        var periodEnd = GetValidDateTime(inv, "SubscriptionEndDate");
        if (periodEnd == null) periodEnd = GetValidDateTime(parent, "SubscriptionEndDate");
        if (periodEnd.HasValue) trans.PeriodEnd = Timestamp.FromDateTime(periodEnd.Value.ToUniversalTime());
        
        // CreatedAt
        var createdAt = GetValidDateTime(inv, "CreatedAt");
        if (createdAt == null) createdAt = GetValidDateTime(parent, "CreatedAt");
        if (createdAt.HasValue) trans.CreatedAt = Timestamp.FromDateTime(createdAt.Value.ToUniversalTime());
        
        // CompletedAt
        var completedAt = GetValidDateTime(inv, "CompletedAt");
        if (completedAt == null) completedAt = GetValidDateTime(parent, "CompletedAt");
        if (completedAt.HasValue) trans.CompletedAt = Timestamp.FromDateTime(completedAt.Value.ToUniversalTime());
        
        // IsTrial
        if (inv.TryGetProperty("IsTrial", out var trialEl))
            trans.IsTrial = trialEl.ValueKind == JsonValueKind.True;
        else if (parent.TryGetProperty("IsTrial", out var parentTrialEl))
            trans.IsTrial = parentTrialEl.ValueKind == JsonValueKind.True;
        
        // TrialCode
        var trialCode = GetStringValue(inv, "TrialCode");
        if (string.IsNullOrEmpty(trialCode))
            trialCode = GetStringValue(parent, "TrialCode");
        trans.TrialCode = trialCode ?? "";
        
        // MembershipLevel - try invoice first, then parent
        var membershipLevel = GetStringValue(inv, "MembershipLevel");
        if (string.IsNullOrEmpty(membershipLevel))
            membershipLevel = GetStringValue(parent, "MembershipLevel");
        trans.MembershipLevel = membershipLevel ?? "";
        
        // PlanType - try invoice first, then parent
        if (inv.TryGetProperty("PlanType", out var planTypeEl) && planTypeEl.ValueKind == JsonValueKind.Number)
            trans.PlanType = planTypeEl.GetInt32();
        else if (parent.TryGetProperty("PlanType", out var parentPlanTypeEl))
            trans.PlanType = ConvertToInt32(parentPlanTypeEl);
        
        // PriceId -> ProductId - try invoice first, then parent
        var productId = GetStringValue(inv, "PriceId");
        if (string.IsNullOrEmpty(productId))
            productId = GetStringValue(parent, "PriceId");
        trans.ProductId = productId ?? "";

        return trans;
    }
    
    // Helper: Get string value from JsonElement
    private static string? GetStringValue(JsonElement el, string propName)
    {
        if (el.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
    
    // Helper: Get valid DateTime (not MinValue)
    private static DateTime? GetValidDateTime(JsonElement el, string propName)
    {
        if (el.TryGetProperty(propName, out var prop))
        {
            var dt = ConvertToDateTime(prop);
            if (dt.HasValue && dt.Value.Year > 1) return dt;
        }
        return null;
    }
    
    // Helper: Check if JsonElement is a valid number
    private static bool IsValidNumber(JsonElement el)
    {
        return el.ValueKind == JsonValueKind.Number;
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

    private static long ConvertDecimalToCents(JsonElement el)
    {
        // Check for null or non-number types first
        if (el.ValueKind != JsonValueKind.Number)
            return 0;
            
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
        if (obj is double dbl) return (long)dbl;
        if (obj is decimal dec) return (long)dec;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number)
        {
            // Try int64 first, fallback to double for floating point numbers
            if (je.TryGetInt64(out var i64)) return i64;
            if (je.TryGetDouble(out var d)) return (long)d;
        }
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
