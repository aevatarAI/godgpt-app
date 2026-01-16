using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// UserFeedback State converter
/// </summary>
public class UserFeedbackStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        if (oldState == null)
            return new UserFeedbackState();

        var newState = new UserFeedbackState();

        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            var userIdStr = ConvertToString(userIdObj);
            if (!string.IsNullOrEmpty(userIdStr) && Guid.TryParse(userIdStr, out var guid))
                newState.UserId = guid.ToString("D");
        }

        if (oldState.TryGetValue("CurrentFeedback", out var currentFeedbackObj))
        {
            var currentFeedback = ConvertFeedbackInfo(currentFeedbackObj);
            if (currentFeedback != null)
                newState.CurrentFeedback = currentFeedback;
        }

        if (oldState.TryGetValue("ArchivedFeedbacks", out var archivedObj) && archivedObj != null)
        {
            var archivedList = ConvertToStringList(archivedObj);
            if (archivedList != null)
                newState.ArchivedFeedbacks.AddRange(archivedList);
        }

        if (oldState.TryGetValue("LastFeedbackTime", out var lastFeedbackObj))
        {
            var dt = ConvertToDateTime(lastFeedbackObj);
            if (dt.HasValue)
                newState.LastFeedbackTime = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("FeedbackCount", out var feedbackCountObj))
            newState.FeedbackCount = ConvertToInt32(feedbackCountObj);

        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("UpdatedAt", out var updatedAtObj))
        {
            var dt = ConvertToDateTime(updatedAtObj);
            if (dt.HasValue)
                newState.UpdatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        return newState;
    }

    private UserFeedbackInfo? ConvertFeedbackInfo(object? obj)
    {
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new UserFeedbackInfo();

        if (dict.TryGetValue("FeedbackId", out var feedbackIdObj))
            info.FeedbackId = ConvertToString(feedbackIdObj);

        if (dict.TryGetValue("FeedbackType", out var feedbackTypeObj))
            info.FeedbackType = ConvertToString(feedbackTypeObj);

        if (dict.TryGetValue("Reasons", out var reasonsObj) && reasonsObj != null)
        {
            var reasonValues = ConvertToIntList(reasonsObj);
            if (reasonValues != null)
            {
                foreach (var reasonValue in reasonValues)
                    info.Reasons.Add((FeedbackReason)reasonValue);
            }
        }

        if (dict.TryGetValue("Response", out var responseObj))
            info.Response = ConvertToString(responseObj);

        if (dict.TryGetValue("ContactRequested", out var contactRequestedObj))
            info.ContactRequested = ConvertToBool(contactRequestedObj);

        if (dict.TryGetValue("Email", out var emailObj))
            info.Email = ConvertToString(emailObj);

        if (dict.TryGetValue("SubmittedAt", out var submittedAtObj))
        {
            var dt = ConvertToDateTime(submittedAtObj);
            if (dt.HasValue)
                info.SubmittedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("ReasonTextsEnglish", out var reasonTextsObj) && reasonTextsObj != null)
        {
            var texts = ConvertToStringList(reasonTextsObj);
            if (texts != null)
                info.ReasonTextsEnglish.AddRange(texts);
        }

        if (dict.TryGetValue("Subscription", out var subscriptionObj))
        {
            var subscription = ConvertSubscriptionInfo(subscriptionObj);
            if (subscription != null)
                info.Subscription = subscription;
        }

        return info;
    }

    private UserSubscriptionInfo? ConvertSubscriptionInfo(object? obj)
    {
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new UserSubscriptionInfo();

        if (dict.TryGetValue("PlanType", out var planTypeObj))
            info.PlanType = ConvertToQuotaPlanType(planTypeObj);

        if (dict.TryGetValue("IsUltimate", out var isUltimateObj))
            info.IsUltimate = ConvertToBool(isUltimateObj);

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

        return info;
    }

    private QuotaPlanType ConvertToQuotaPlanType(object? obj)
    {
        if (obj == null) return QuotaPlanType.None;
        if (obj is int i) return (QuotaPlanType)i;
        if (obj is long l) return (QuotaPlanType)l;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (QuotaPlanType)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (QuotaPlanType)result;
        if (System.Enum.TryParse(obj?.ToString(), true, out QuotaPlanType parsed)) return parsed;
        return QuotaPlanType.None;
    }

    private static Dictionary<string, object?>? ConvertToDictionary(object? obj)
    {
        if (obj == null) return null;
        if (obj is Dictionary<string, object?> dict) return dict;
        if (obj is Dictionary<string, object> dictNonNullable)
            return dictNonNullable.ToDictionary(k => k.Key, v => (object?)v.Value);
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Object)
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(je.GetRawText());
        return null;
    }

    private static List<string>? ConvertToStringList(object? obj)
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
                else if (item.ValueKind != JsonValueKind.Null)
                    resultList.Add(item.ToString());
            }
            return resultList;
        }
        if (obj is IEnumerable<object?> objEnumerable)
        {
            return objEnumerable.Select(item => item?.ToString() ?? string.Empty).ToList();
        }
        return null;
    }

    private static List<int>? ConvertToIntList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<int> intList) return intList;
        if (obj is IEnumerable<int> intEnumerable) return intEnumerable.ToList();
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var resultList = new List<int>();
            foreach (var item in je.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out var number))
                    resultList.Add(number);
                else if (item.ValueKind == JsonValueKind.String && int.TryParse(item.GetString(), out var parsed))
                    resultList.Add(parsed);
            }
            return resultList;
        }
        if (obj is IEnumerable<object?> objEnumerable)
        {
            var resultList = new List<int>();
            foreach (var item in objEnumerable)
            {
                if (item == null) continue;
                if (item is int i) resultList.Add(i);
                else if (int.TryParse(item.ToString(), out var parsed)) resultList.Add(parsed);
            }
            return resultList;
        }
        return null;
    }

    private static string ConvertToString(object? obj)
    {
        if (obj == null) return string.Empty;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.String) return je.GetString() ?? string.Empty;
        return obj.ToString() ?? string.Empty;
    }

    private static bool ConvertToBool(object? obj)
    {
        if (obj == null) return false;
        if (obj is bool b) return b;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.True) return true;
        if (obj is JsonElement je2 && je2.ValueKind == JsonValueKind.False) return false;
        if (bool.TryParse(obj.ToString(), out var result)) return result;
        return false;
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
