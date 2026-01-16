using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.Invitation;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// Invitation State converter
/// </summary>
public class InvitationStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?>? oldState)
    {
        if (oldState == null)
            return new InvitationState();

        var newState = new InvitationState();

        if (oldState.TryGetValue("InviterId", out var inviterIdObj))
        {
            var inviterIdStr = ConvertToString(inviterIdObj);
            if (!string.IsNullOrEmpty(inviterIdStr) && Guid.TryParse(inviterIdStr, out var guid))
                newState.InviterId = guid.ToString("D");
        }

        if (oldState.TryGetValue("CurrentInviteCode", out var inviteCodeObj))
            newState.CurrentInviteCode = ConvertToString(inviteCodeObj);

        if (oldState.TryGetValue("Invitees", out var inviteesObj) && inviteesObj != null)
        {
            var inviteesDict = ConvertToDictionary(inviteesObj);
            if (inviteesDict != null)
            {
                foreach (var kvp in inviteesDict)
                {
                    var inviteeInfo = ConvertInviteeInfo(kvp.Value);
                    if (inviteeInfo != null)
                        newState.Invitees[kvp.Key] = inviteeInfo;
                }
            }
        }

        if (oldState.TryGetValue("TotalInvites", out var totalInvitesObj))
            newState.TotalInvites = ConvertToInt32(totalInvitesObj);

        if (oldState.TryGetValue("ValidInvites", out var validInvitesObj))
            newState.ValidInvites = ConvertToInt32(validInvitesObj);

        if (oldState.TryGetValue("TotalCreditsEarned", out var creditsEarnedObj))
            newState.TotalCreditsEarned = ConvertToInt32(creditsEarnedObj);

        if (oldState.TryGetValue("RewardHistory", out var rewardHistoryObj) && rewardHistoryObj != null)
        {
            var rewardList = ConvertToList(rewardHistoryObj);
            if (rewardList != null)
            {
                foreach (var rewardObj in rewardList)
                {
                    var reward = ConvertRewardRecord(rewardObj);
                    if (reward != null)
                        newState.RewardHistory.Add(reward);
                }
            }
        }

        if (oldState.TryGetValue("LastRewardTierUpdate", out var lastUpdateObj))
        {
            var dt = ConvertToDateTime(lastUpdateObj);
            if (dt.HasValue)
                newState.LastRewardTierUpdate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("TotalCreditsFromX", out var creditsFromXObj))
            newState.TotalCreditsFromX = ConvertToInt32(creditsFromXObj);

        return newState;
    }

    private InviteeInfo? ConvertInviteeInfo(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var info = new InviteeInfo();

        if (dict.TryGetValue("InviteeId", out var inviteeIdObj))
        {
            var inviteeIdStr = ConvertToString(inviteeIdObj);
            if (!string.IsNullOrEmpty(inviteeIdStr) && Guid.TryParse(inviteeIdStr, out var guid))
                info.InviteeId = guid.ToString("D");
        }

        if (dict.TryGetValue("InvitedAt", out var invitedAtObj))
        {
            var dt = ConvertToDateTime(invitedAtObj);
            if (dt.HasValue)
                info.InvitedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("HasCompletedChat", out var hasChatObj))
            info.HasCompletedChat = ConvertToBool(hasChatObj);

        if (dict.TryGetValue("HasPaid", out var hasPaidObj))
            info.HasPaid = ConvertToBool(hasPaidObj);

        if (dict.TryGetValue("PaidPlan", out var paidPlanObj))
            info.PaidPlan = ConvertToQuotaPlanType(paidPlanObj);

        if (dict.TryGetValue("PaidAt", out var paidAtObj))
        {
            var dt = ConvertToDateTime(paidAtObj);
            if (dt.HasValue)
                info.PaidAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("RewardIssued", out var rewardIssuedObj))
            info.RewardIssued = ConvertToBool(rewardIssuedObj);

        if (dict.TryGetValue("IsValid", out var isValidObj))
            info.IsValid = ConvertToBool(isValidObj);

        return info;
    }

    private RewardRecord? ConvertRewardRecord(object? obj)
    {
        if (obj == null) return null;
        var dict = ConvertToDictionary(obj);
        if (dict == null) return null;

        var record = new RewardRecord();

        if (dict.TryGetValue("InviteeId", out var inviteeIdObj))
        {
            var inviteeIdStr = ConvertToString(inviteeIdObj);
            if (!string.IsNullOrEmpty(inviteeIdStr) && Guid.TryParse(inviteeIdStr, out var guid))
                record.InviteeId = guid.ToString("D");
        }

        if (dict.TryGetValue("Credits", out var creditsObj))
            record.Credits = ConvertToInt32(creditsObj);

        if (dict.TryGetValue("RewardType", out var rewardTypeObj))
            record.RewardType = ConvertToRewardType(rewardTypeObj);

        if (dict.TryGetValue("IssuedAt", out var issuedAtObj))
        {
            var dt = ConvertToDateTime(issuedAtObj);
            if (dt.HasValue)
                record.IssuedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("IsScheduled", out var isScheduledObj))
            record.IsScheduled = ConvertToBool(isScheduledObj);

        if (dict.TryGetValue("ScheduledDate", out var scheduledDateObj))
        {
            var dt = ConvertToDateTime(scheduledDateObj);
            if (dt.HasValue)
                record.ScheduledDate = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (dict.TryGetValue("InvoiceId", out var invoiceIdObj))
            record.InvoiceId = ConvertToString(invoiceIdObj);

        if (dict.TryGetValue("TweetId", out var tweetIdObj))
            record.TweetId = ConvertToString(tweetIdObj);

        return record;
    }

    private QuotaPlanType ConvertToQuotaPlanType(object? obj)
    {
        if (obj == null) return QuotaPlanType.None;
        if (obj is int i) return (QuotaPlanType)i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (QuotaPlanType)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (QuotaPlanType)result;
        return QuotaPlanType.None;
    }

    private RewardType ConvertToRewardType(object? obj)
    {
        if (obj == null) return RewardType.FirstInviteReward;
        if (obj is int i) return (RewardType)i;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Number) return (RewardType)je.GetInt32();
        if (int.TryParse(obj.ToString(), out var result)) return (RewardType)result;
        return RewardType.FirstInviteReward;
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

    private List<object>? ConvertToList(object? obj)
    {
        if (obj == null) return null;
        if (obj is List<object> existingList) return existingList;
        if (obj is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var newList = new List<object>();
            foreach (var item in je.EnumerateArray())
                newList.Add(item);
            return newList;
        }
        return null;
    }
}
