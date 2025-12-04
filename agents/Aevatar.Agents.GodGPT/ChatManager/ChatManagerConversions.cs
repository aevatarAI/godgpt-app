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
            DeviceType = info.Platform ?? "",  // Platform maps to DeviceType
            RegisteredAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.RegisteredAt, DateTimeKind.Utc)),
            LastActiveAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastActiveAt, DateTimeKind.Utc)),
            IsPushEnabled = info.PushEnabled,
            Timezone = info.TimeZoneId ?? "",
            ConsecutiveFailures = info.ConsecutiveFailures,
            PushStatus = (int)info.Status
        };
        if (info.LastSuccessfulPush.HasValue)
        {
            proto.LastSuccessAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastSuccessfulPush.Value, DateTimeKind.Utc));
        }
        proto.TokenUpdatedAt = Timestamp.FromDateTime(DateTime.SpecifyKind(info.LastTokenUpdate, DateTimeKind.Utc));
        return proto;
    }

    public static UserDeviceInfoV2 FromProto(this UserDeviceInfoV2Proto proto)
    {
        return new UserDeviceInfoV2
        {
            DeviceId = "",  // Not in proto
            UserId = Guid.Empty,  // Not in proto
            PushToken = proto.PushToken,
            TimeZoneId = proto.Timezone,
            PushLanguage = "en",
            PushEnabled = proto.IsPushEnabled,
            RegisteredAt = proto.RegisteredAt?.ToDateTime() ?? DateTime.MinValue,
            LastTokenUpdate = proto.TokenUpdatedAt?.ToDateTime() ?? DateTime.MinValue,
            LastActiveAt = proto.LastActiveAt?.ToDateTime() ?? DateTime.MinValue,
            Platform = proto.DeviceType,
            AppVersion = "",
            StructureVersion = 2,
            Status = (DeviceStatus)proto.PushStatus,
            ConsecutiveFailures = proto.ConsecutiveFailures,
            LastSuccessfulPush = proto.LastSuccessAt?.ToDateTime()
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

    // =============================================================================
    // Timestamp Extension Methods (for Timestamp -> DateTime operations)
    // =============================================================================

    /// <summary>
    /// Get the Date part of a Timestamp (like DateTime.Date)
    /// </summary>
    public static DateTime Date(this Timestamp timestamp)
    {
        return timestamp.ToDateTime().Date;
    }

    /// <summary>
    /// Get the underlying DateTime value from a Timestamp (like DateTime?.Value)
    /// </summary>
    public static DateTime Value(this Timestamp timestamp)
    {
        return timestamp.ToDateTime();
    }

    /// <summary>
    /// Convert Timestamp to DateTime safely (null returns MinValue)
    /// </summary>
    public static DateTime ToDateTimeSafe(this Timestamp? timestamp)
    {
        return timestamp?.ToDateTime() ?? DateTime.MinValue;
    }

    /// <summary>
    /// Check if Timestamp equals a DateTime
    /// </summary>
    public static bool EqualsDateTime(this Timestamp? timestamp, DateTime dateTime)
    {
        if (timestamp == null) return false;
        return timestamp.ToDateTime() == dateTime;
    }

    /// <summary>
    /// Check if Timestamp is less than or equal to DateTime
    /// </summary>
    public static bool LessOrEqualThan(this Timestamp? timestamp, DateTime dateTime)
    {
        if (timestamp == null) return true;
        return timestamp.ToDateTime() <= dateTime;
    }

    /// <summary>
    /// Format Timestamp to string with format (like DateTime.ToString(format))
    /// </summary>
    public static string ToFormattedString(this Timestamp timestamp, string format)
    {
        return timestamp.ToDateTime().ToString(format);
    }

    // =============================================================================
    // String/Guid Extension Methods
    // =============================================================================

    /// <summary>
    /// Parse string to Guid, returns Guid.Empty if null/empty
    /// </summary>
    public static Guid ToGuid(this string? str)
    {
        if (string.IsNullOrEmpty(str)) return Guid.Empty;
        return Guid.Parse(str);
    }

    /// <summary>
    /// Parse string to nullable Guid
    /// </summary>
    public static Guid? ToGuidNullable(this string? str)
    {
        if (string.IsNullOrEmpty(str)) return null;
        return Guid.Parse(str);
    }

    // =============================================================================
    // SessionInfoProto Extension Methods
    // =============================================================================

    /// <summary>
    /// Get ShareIds as List of Guids from SessionInfoProto
    /// </summary>
    public static List<Guid> GetShareIds(this SessionInfoProto proto)
    {
        if (string.IsNullOrEmpty(proto.ShareId)) return new List<Guid>();
        return new List<Guid> { Guid.Parse(proto.ShareId) };
    }

    /// <summary>
    /// Check if session has any ShareIds
    /// </summary>
    public static bool HasShareIds(this SessionInfoProto proto)
    {
        return !string.IsNullOrEmpty(proto.ShareId);
    }

    /// <summary>
    /// Add a ShareId to SessionInfoProto (replaces existing since proto has single ShareId)
    /// </summary>
    public static void AddShareId(this SessionInfoProto proto, Guid shareId)
    {
        proto.ShareId = shareId.ToString();
    }
}
