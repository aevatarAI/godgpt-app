using Aevatar.Agents.GodGPT.Protos.ChatManager;
using GodGPT.GAgents.DailyPush;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Application.Grains.Agents.ChatManager;

/// <summary>
/// Conversion helpers for ChatManagerGAgent migration
/// Converts between C# DTOs and Protobuf types
/// </summary>
public static class ChatManagerConversions
{
    // =============================================================================
    // SessionInfo Conversions
    // =============================================================================

    public static SessionInfoProto ToProto(this SessionInfo info)
    {
        var proto = new SessionInfoProto
        {
            SessionId = info.SessionId.ToString(),
            Title = info.Title ?? "",
            CreateAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.CreateAt, DateTimeKind.Utc)),
            Guider = info.Guider ?? ""
        };
        // ShareIds is a list in C#, take first one if exists
        if (info.ShareIds != null && info.ShareIds.Any())
        {
            proto.ShareId = info.ShareIds.First().ToString();
        }
        return proto;
    }

    public static SessionInfo FromProto(this SessionInfoProto proto)
    {
        var session = new SessionInfo
        {
            SessionId = Guid.Parse(proto.SessionId),
            Title = proto.Title,
            CreateAt = proto.CreateAt?.ToDateTime() ?? DateTime.MinValue,
            Guider = string.IsNullOrEmpty(proto.Guider) ? null : proto.Guider,
            ShareIds = new List<Guid>()
        };
        if (!string.IsNullOrEmpty(proto.ShareId))
        {
            session.ShareIds.Add(Guid.Parse(proto.ShareId));
        }
        return session;
    }

    public static List<SessionInfoProto> ToProtoList(this List<SessionInfo> list)
    {
        return list.Select(s => s.ToProto()).ToList();
    }

    public static List<SessionInfo> FromProtoList(this IEnumerable<SessionInfoProto> protos)
    {
        return protos.Select(p => p.FromProto()).ToList();
    }

    // =============================================================================
    // UserDeviceInfo Conversions
    // =============================================================================

    public static UserDeviceInfoProto ToProto(this UserDeviceInfo info)
    {
        var proto = new UserDeviceInfoProto
        {
            PushToken = info.PushToken ?? "",
            DeviceType = "",  // UserDeviceInfo doesn't have DeviceType
            RegisteredAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.RegisteredAt, DateTimeKind.Utc)),
            LastActiveAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastTokenUpdate, DateTimeKind.Utc)),
            IsPushEnabled = info.PushEnabled,
            Timezone = info.TimeZoneId ?? ""
        };
        return proto;
    }

    public static UserDeviceInfo FromProto(this UserDeviceInfoProto proto)
    {
        return new UserDeviceInfo
        {
            DeviceId = "",  // Not in proto
            PushToken = proto.PushToken,
            TimeZoneId = proto.Timezone,
            PushLanguage = "en",
            PushEnabled = proto.IsPushEnabled,
            RegisteredAt = proto.RegisteredAt?.ToDateTime() ?? DateTime.MinValue,
            LastTokenUpdate = proto.LastActiveAt?.ToDateTime() ?? DateTime.MinValue
        };
    }

    // =============================================================================
    // UserDeviceInfoV2 Conversions
    // =============================================================================

    public static UserDeviceInfoV2Proto ToProto(this UserDeviceInfoV2 info)
    {
        var proto = new UserDeviceInfoV2Proto
        {
            PushToken = info.PushToken ?? "",
            DeviceType = info.DeviceType ?? "",
            RegisteredAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.RegisteredAt, DateTimeKind.Utc)),
            LastActiveAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastActiveAt, DateTimeKind.Utc)),
            IsPushEnabled = info.IsPushEnabled,
            Timezone = info.Timezone ?? "",
            ConsecutiveFailures = info.ConsecutiveFailures,
            PushStatus = (int)info.PushStatus
        };
        if (info.LocalTime.HasValue)
        {
            proto.LocalTime = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LocalTime.Value, DateTimeKind.Utc));
        }
        if (info.LastFailureAt.HasValue)
        {
            proto.LastFailureAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastFailureAt.Value, DateTimeKind.Utc));
        }
        if (info.LastSuccessAt.HasValue)
        {
            proto.LastSuccessAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastSuccessAt.Value, DateTimeKind.Utc));
        }
        if (info.TokenUpdatedAt.HasValue)
        {
            proto.TokenUpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.TokenUpdatedAt.Value, DateTimeKind.Utc));
        }
        return proto;
    }

    public static UserDeviceInfoV2 FromProto(this UserDeviceInfoV2Proto proto)
    {
        return new UserDeviceInfoV2
        {
            PushToken = proto.PushToken,
            DeviceType = proto.DeviceType,
            RegisteredAt = proto.RegisteredAt?.ToDateTime() ?? DateTime.MinValue,
            LastActiveAt = proto.LastActiveAt?.ToDateTime() ?? DateTime.MinValue,
            IsPushEnabled = proto.IsPushEnabled,
            LocalTime = proto.LocalTime?.ToDateTime(),
            Timezone = proto.Timezone,
            ConsecutiveFailures = proto.ConsecutiveFailures,
            LastFailureAt = proto.LastFailureAt?.ToDateTime(),
            LastSuccessAt = proto.LastSuccessAt?.ToDateTime(),
            PushStatus = (DevicePushStatus)proto.PushStatus,
            TokenUpdatedAt = proto.TokenUpdatedAt?.ToDateTime()
        };
    }

    // =============================================================================
    // Dictionary Conversions
    // =============================================================================

    public static Dictionary<string, UserDeviceInfoProto> ToProtoDict(this Dictionary<string, UserDeviceInfo> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToProto());
    }

    public static Dictionary<string, UserDeviceInfo> FromProtoDict(this IDictionary<string, UserDeviceInfoProto> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FromProto());
    }

    public static Dictionary<string, UserDeviceInfoV2Proto> ToProtoDict(this Dictionary<string, UserDeviceInfoV2> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToProto());
    }

    public static Dictionary<string, UserDeviceInfoV2> FromProtoDict(this IDictionary<string, UserDeviceInfoV2Proto> dict)
    {
        return dict.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.FromProto());
    }

    // =============================================================================
    // VoiceLanguageEnum Conversions
    // =============================================================================

    public static int ToProtoInt(this VoiceLanguageEnum voiceLanguage)
    {
        return (int)voiceLanguage;
    }

    public static VoiceLanguageEnum FromProtoInt(int value)
    {
        return (VoiceLanguageEnum)value;
    }

    // =============================================================================
    // Timestamp Helpers
    // =============================================================================

    public static Timestamp ToTimestamp(this DateTime dateTime)
    {
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime, DateTimeKind.Utc));
    }

    public static Timestamp? ToTimestampNullable(this DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc));
    }

    // =============================================================================
    // State Extension Methods
    // =============================================================================

    public static SessionInfoProto? GetSession(this ChatManagerStateProto state, Guid sessionId)
    {
        return state.SessionInfoList.FirstOrDefault(s => s.SessionId == sessionId.ToString());
    }

    public static SessionInfoProto? GetSession(this ChatManagerStateProto state, string sessionId)
    {
        return state.SessionInfoList.FirstOrDefault(s => s.SessionId == sessionId);
    }
}
