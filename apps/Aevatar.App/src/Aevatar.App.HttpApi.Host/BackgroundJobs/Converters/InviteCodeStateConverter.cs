using System;
using System.Collections.Generic;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos.InviteCode;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs.Converters;

/// <summary>
/// InviteCode State converter
/// </summary>
public class InviteCodeStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object?> oldState)
    {
        if (oldState == null)
            return new InviteCodeState();

        var newState = new InviteCodeState();

        if (oldState.TryGetValue("InviterId", out var inviterIdObj))
            newState.InviterId = ConvertToString(inviterIdObj);

        if (oldState.TryGetValue("CreatedAt", out var createdAtObj))
        {
            var dt = ConvertToDateTime(createdAtObj);
            if (dt.HasValue)
                newState.CreatedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("IsActive", out var isActiveObj))
            newState.IsActive = ConvertToBool(isActiveObj);

        if (oldState.TryGetValue("UsageCount", out var usageCountObj))
            newState.UsageCount = ConvertToInt32(usageCountObj);

        if (oldState.TryGetValue("InviteCode", out var inviteCodeObj))
            newState.InviteCode = ConvertToString(inviteCodeObj);

        if (oldState.TryGetValue("CodeType", out var codeTypeObj))
            newState.CodeType = ConvertToEnum(codeTypeObj, InvitationCodeType.FriendInvitation);

        if (oldState.TryGetValue("BatchId", out var batchIdObj))
            newState.BatchId = ConvertToInt64(batchIdObj);

        if (oldState.TryGetValue("TrialDays", out var trialDaysObj))
            newState.TrialDays = ConvertToInt32(trialDaysObj);

        if (oldState.TryGetValue("ProductId", out var productIdObj))
            newState.ProductId = ConvertToString(productIdObj);

        if (oldState.TryGetValue("PlanType", out var planTypeObj))
            newState.PlanType = ConvertToEnum(planTypeObj, PlanType.None);

        if (oldState.TryGetValue("IsUltimate", out var isUltimateObj))
            newState.IsUltimate = ConvertToBool(isUltimateObj);

        if (oldState.TryGetValue("Platform", out var platformObj))
            newState.Platform = ConvertToEnum(platformObj, PaymentPlatform.Stripe);

        if (oldState.TryGetValue("InviteeId", out var inviteeIdObj))
            newState.InviteeId = ConvertToString(inviteeIdObj);

        if (oldState.TryGetValue("UsedAt", out var usedAtObj))
        {
            var dt = ConvertToDateTime(usedAtObj);
            if (dt.HasValue)
                newState.UsedAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        if (oldState.TryGetValue("SessionUrl", out var sessionUrlObj))
            newState.SessionUrl = ConvertToString(sessionUrlObj);

        if (oldState.TryGetValue("SessionExpiresAt", out var sessionExpiresAtObj))
        {
            var dt = ConvertToDateTime(sessionExpiresAtObj);
            if (dt.HasValue)
                newState.SessionExpiresAt = Timestamp.FromDateTime(dt.Value.ToUniversalTime());
        }

        return newState;
    }

    private static TEnum ConvertToEnum<TEnum>(object? obj, TEnum defaultValue) where TEnum : struct
    {
        if (obj == null) return defaultValue;
        if (obj is TEnum directEnum) return directEnum;

        if (obj is JsonElement je)
        {
            if (je.ValueKind == JsonValueKind.Number && je.TryGetInt32(out var enumInt))
            {
                if (System.Enum.IsDefined(typeof(TEnum), enumInt))
                    return (TEnum)System.Enum.ToObject(typeof(TEnum), enumInt);
            }

            if (je.ValueKind == JsonValueKind.String)
            {
                var enumStr = je.GetString();
                if (!string.IsNullOrWhiteSpace(enumStr) && System.Enum.TryParse(enumStr, true, out TEnum parsed))
                    return parsed;
            }
        }

        var objStr = obj.ToString();
        if (!string.IsNullOrWhiteSpace(objStr) && System.Enum.TryParse(objStr, true, out TEnum result))
            return result;

        if (int.TryParse(objStr, out var intValue) && System.Enum.IsDefined(typeof(TEnum), intValue))
            return (TEnum)System.Enum.ToObject(typeof(TEnum), intValue);

        return defaultValue;
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
