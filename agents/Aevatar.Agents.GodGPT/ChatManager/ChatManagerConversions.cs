using Aevatar.Agents.GodGPT.Protos.ChatManager;
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
        // Copy all ShareIds to proto
        if (info.ShareIds != null && info.ShareIds.Any())
        {
            proto.ShareIds.AddRange(info.ShareIds.Select(id => id.ToString()));
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
            ShareIds = proto.ShareIds.Select(id => Guid.Parse(id)).ToList()
        };
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

    // Note: UserDeviceInfoProto (V1) conversions removed - type does not exist
    // Only UserDeviceInfoV2Proto is available in daily_push_user.proto


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
        if (proto.ShareIds == null || proto.ShareIds.Count == 0) return new List<Guid>();
        return proto.ShareIds.Select(id => Guid.Parse(id)).ToList();
    }

    /// <summary>
    /// Check if session has any ShareIds
    /// </summary>
    public static bool HasShareIds(this SessionInfoProto proto)
    {
        return proto.ShareIds != null && proto.ShareIds.Count > 0;
    }

    /// <summary>
    /// Add a ShareId to SessionInfoProto (appends to list, supports multiple share links)
    /// </summary>
    public static void AddShareId(this SessionInfoProto proto, Guid shareId)
    {
        proto.ShareIds.Add(shareId.ToString());
    }

    /// <summary>
    /// Get the first ShareId (for backward compatibility in display)
    /// </summary>
    public static string? GetFirstShareId(this SessionInfoProto proto)
    {
        return proto.ShareIds.FirstOrDefault();
    }

    // =============================================================================
    // Response Event Conversions (for PublishAsync)
    // =============================================================================

    public static ResponseCreateGodProto ToProto(this ResponseCreateGod response)
    {
        return new ResponseCreateGodProto
        {
            ResponseType = (int)response.ResponseType,
            SessionId = response.SessionId.ToString(),
            SessionVersion = response.SessionVersion ?? ""
        };
    }

    public static ResponseStreamGodChatProto ToProto(this ResponseStreamGodChat response)
    {
        var proto = new ResponseStreamGodChatProto
        {
            ResponseType = (int)response.ResponseType,
            Response = response.Response ?? "",
            NewTitle = response.NewTitle ?? "",
            ChatId = response.ChatId ?? "",
            IsLastChunk = response.IsLastChunk,
            SerialNumber = response.SerialNumber,
            SessionId = response.SessionId.ToString()
        };
        if (response.AudioData != null)
        {
            proto.AudioData = Google.Protobuf.ByteString.CopyFrom(response.AudioData);
        }
        return proto;
    }

    public static ResponseStreamGodChat FromProto(this ResponseStreamGodChatProto proto)
    {
        var response = new ResponseStreamGodChat
        {
            ResponseType = (ResponseType)proto.ResponseType,
            Response = proto.Response,
            NewTitle = proto.NewTitle,
            ChatId = proto.ChatId,
            IsLastChunk = proto.IsLastChunk,
            SerialNumber = proto.SerialNumber,
            SessionId = Guid.TryParse(proto.SessionId, out var sessionId) ? sessionId : Guid.Empty
        };
        if (proto.AudioData != null && proto.AudioData.Length > 0)
        {
            response.AudioData = proto.AudioData.ToByteArray();
        }
        return response;
    }
}
