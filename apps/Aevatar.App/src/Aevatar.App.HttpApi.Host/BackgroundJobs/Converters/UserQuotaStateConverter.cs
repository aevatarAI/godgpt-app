using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserQuota State converter
/// </summary>
public class UserQuotaStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object>? oldState)
    {
        if (oldState == null)
            return new UserQuotaState();

        var newState = new UserQuotaState();

        // UserId: Guid -> string
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        // Credits: int
        if (oldState.TryGetValue("Credits", out var creditsObj))
            newState.Credits = ConvertToInt32(creditsObj);

        // HasInitialCredits: bool
        if (oldState.TryGetValue("HasInitialCredits", out var hasInitialCreditsObj))
            newState.HasInitialCredits = ConvertToBool(hasInitialCreditsObj);

        // HasShownInitialCreditsToast: bool
        if (oldState.TryGetValue("HasShownInitialCreditsToast", out var hasShownToastObj))
            newState.HasShownInitialCreditsToast = ConvertToBool(hasShownToastObj);

        // Subscription: SubscriptionInfoProto
        if (oldState.TryGetValue("Subscription", out var subscriptionObj))
        {
            var subscription = ConvertSubscriptionInfo(subscriptionObj);
            if (subscription != null)
                newState.Subscription = subscription;
        }

        // RateLimits: Dictionary<string, RateLimitInfoProto> -> map<string, RateLimitInfoProto>
        if (oldState.TryGetValue("RateLimits", out var rateLimitsObj) && rateLimitsObj != null)
        {
            var rateLimitsDict = ConvertToDictionary(rateLimitsObj);
            if (rateLimitsDict != null)
            {
                foreach (var kvp in rateLimitsDict)
                {
                    var rateLimit = ConvertRateLimitInfo(kvp.Value);
                    if (rateLimit != null)
                        newState.RateLimits[kvp.Key] = rateLimit;
                }
            }
        }

        // UltimateSubscription: SubscriptionInfoProto
        if (oldState.TryGetValue("UltimateSubscription", out var ultimateSubscriptionObj))
        {
            var subscription = ConvertSubscriptionInfo(ultimateSubscriptionObj);
            if (subscription != null)
                newState.UltimateSubscription = subscription;
        }

        // CreatedAt: DateTime -> Timestamp
        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        // CanReceiveInviteReward: bool
        if (oldState.TryGetValue("CanReceiveInviteReward", out var canReceiveRewardObj))
            newState.CanReceiveInviteReward = ConvertToBool(canReceiveRewardObj);

        // DailyImageConversation: DailyImageConversationInfoProto
        if (oldState.TryGetValue("DailyImageConversation", out var dailyImageObj))
        {
            var dailyImage = ConvertDailyImageConversationInfo(dailyImageObj);
            if (dailyImage != null)
                newState.DailyImageConversation = dailyImage;
        }

        // FreeTrialInfo: FreeTrialInfoProto (optional)
        if (oldState.TryGetValue("FreeTrialInfo", out var freeTrialObj))
        {
            var freeTrial = ConvertFreeTrialInfo(freeTrialObj);
            if (freeTrial != null)
                newState.FreeTrialInfo = freeTrial;
        }

        // OneTimeTransactionIds: List<string> -> repeated string
        if (oldState.TryGetValue("OneTimeTransactionIds", out var transactionIdsObj) && transactionIdsObj != null)
        {
            var transactionList = ConvertToStringList(transactionIdsObj);
            if (transactionList != null)
            {
                foreach (var id in transactionList)
                    newState.OneTimeTransactionIds.Add(id);
            }
        }

        return newState;
    }

    private SubscriptionInfoProto? ConvertSubscriptionInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new SubscriptionInfoProto();

        if (dict.TryGetValue("IsActive", out var isActiveObj))
            info.IsActive = ConvertToBool(isActiveObj);

        if (dict.TryGetValue("PlanType", out var planTypeObj))
            info.PlanType = ConvertToQuotaPlanType(planTypeObj);

        if (dict.TryGetValue("Status", out var statusObj))
            info.Status = ConvertToQuotaPaymentStatus(statusObj);

        if (dict.TryGetValue("StartDate", out var startDateObj))
        {
            var dt = ConvertToDateTime(startDateObj);
            if (dt.HasValue)
                info.StartDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("EndDate", out var endDateObj))
        {
            var dt = ConvertToDateTime(endDateObj);
            if (dt.HasValue)
                info.EndDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("SubscriptionIds", out var subscriptionIdsObj) && subscriptionIdsObj != null)
        {
            var ids = ConvertToStringList(subscriptionIdsObj);
            if (ids != null)
            {
                foreach (var id in ids)
                    info.SubscriptionIds.Add(id);
            }
        }

        if (dict.TryGetValue("InvoiceIds", out var invoiceIdsObj) && invoiceIdsObj != null)
        {
            var ids = ConvertToStringList(invoiceIdsObj);
            if (ids != null)
            {
                foreach (var id in ids)
                    info.InvoiceIds.Add(id);
            }
        }

        return info;
    }

    private RateLimitInfoProto? ConvertRateLimitInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new RateLimitInfoProto();

        if (dict.TryGetValue("Count", out var countObj))
            info.Count = ConvertToInt32(countObj);

        if (dict.TryGetValue("LastTime", out var lastTimeObj))
        {
            var dt = ConvertToDateTime(lastTimeObj);
            if (dt.HasValue)
                info.LastTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        return info;
    }

    private DailyImageConversationInfoProto? ConvertDailyImageConversationInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new DailyImageConversationInfoProto();

        if (dict.TryGetValue("Count", out var countObj))
            info.Count = ConvertToInt32(countObj);

        if (dict.TryGetValue("LastConversationTime", out var lastTimeObj))
        {
            var dt = ConvertToDateTime(lastTimeObj);
            if (dt.HasValue)
                info.LastConversationTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        return info;
    }

    private FreeTrialInfoProto? ConvertFreeTrialInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new FreeTrialInfoProto();

        if (dict.TryGetValue("FreeTrialCode", out var codeObj))
            info.FreeTrialCode = ConvertToString(codeObj);

        if (dict.TryGetValue("TrialDays", out var daysObj))
            info.TrialDays = ConvertToInt32(daysObj);

        if (dict.TryGetValue("PlanType", out var planTypeObj))
            info.PlanType = ConvertToQuotaPlanType(planTypeObj);

        if (dict.TryGetValue("IsUltimate", out var isUltimateObj))
            info.IsUltimate = ConvertToBool(isUltimateObj);

        if (dict.TryGetValue("TransactionId", out var transactionIdObj))
            info.TransactionId = ConvertToString(transactionIdObj);

        return info;
    }

    private QuotaPlanType ConvertToQuotaPlanType(object? obj)
    {
        if (obj == null) return QuotaPlanType.None;
        if (obj is int i) return (QuotaPlanType)i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (QuotaPlanType)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (QuotaPlanType)result;
        return QuotaPlanType.None;
    }

    private QuotaPaymentStatus ConvertToQuotaPaymentStatus(object? obj)
    {
        if (obj == null) return QuotaPaymentStatus.None;
        if (obj is int i) return (QuotaPaymentStatus)i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (QuotaPaymentStatus)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (QuotaPaymentStatus)result;
        return QuotaPaymentStatus.None;
    }

    private string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
    }

    private int ConvertToInt32(object? obj)
    {
        if (obj == null) return 0;
        if (obj is int i) return i;
        if (obj is long l) return (int)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return result;
        return 0;
    }

    private DateTime? ConvertToDateTime(object? obj)
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

    private Dictionary<string, object>? ConvertToDictionary(object? obj)
    {
        if (obj == null) return null;
        if (obj is Dictionary<string, object> dict) return dict;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object>>(je.GetRawText());
        return null;
    }

    private List<string>? ConvertToStringList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<string> list) return list;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var resultList = new List<string>();
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                    resultList.Add(item.GetString() ?? string.Empty);
            }
            return resultList;
        }
        return null;
    }
}
