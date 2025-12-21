using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Common.Constants;
using Google.Protobuf.WellKnownTypes;
using GodGPT.GAgents.SpeechChat;
// Use the compatibility layer ChatMessage
using ChatMessage = Aevatar.GAgents.AI.Abstractions.ChatMessage;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Conversion helpers for GodChatGAgent migration
/// Converts between C# DTOs and Protobuf types
/// </summary>
public static class GodChatConversions
{
    // =============================================================================
    // UserProfile Conversions
    // =============================================================================
    
    public static UserProfileProto? ToProto(this UserProfile? profile)
    {
        if (profile == null) return null;
        return new UserProfileProto
        {
            Gender = profile.Gender ?? "",
            BirthDate = Timestamp.FromDateTime(DateTime.SpecifyKind(profile.BirthDate, DateTimeKind.Utc)),
            BirthPlace = profile.BirthPlace ?? "",
            FullName = profile.FullName ?? ""
        };
    }
    
    public static UserProfile? FromProto(this UserProfileProto? proto)
    {
        if (proto == null) return null;
        return new UserProfile
        {
            Gender = proto.Gender,
            BirthDate = proto.BirthDate?.ToDateTime() ?? DateTime.MinValue,
            BirthPlace = proto.BirthPlace,
            FullName = proto.FullName
        };
    }
    
    // =============================================================================
    // ChatMessageMeta Conversions
    // =============================================================================
    
    public static ChatMessageMetaProto ToProto(this ChatMessageMeta meta)
    {
        return new ChatMessageMetaProto
        {
            IsVoiceMessage = meta.IsVoiceMessage,
            VoiceLanguage = (int)meta.VoiceLanguage,
            VoiceParseSuccess = meta.VoiceParseSuccess,
            VoiceParseErrorMessage = meta.VoiceParseErrorMessage ?? "",
            VoiceDurationSeconds = meta.VoiceDurationSeconds
        };
    }
    
    public static ChatMessageMeta FromProto(this ChatMessageMetaProto proto)
    {
        return new ChatMessageMeta
        {
            IsVoiceMessage = proto.IsVoiceMessage,
            VoiceLanguage = (VoiceLanguageEnum)proto.VoiceLanguage,
            VoiceParseSuccess = proto.VoiceParseSuccess,
            VoiceParseErrorMessage = string.IsNullOrEmpty(proto.VoiceParseErrorMessage) ? null : proto.VoiceParseErrorMessage,
            VoiceDurationSeconds = proto.VoiceDurationSeconds
        };
    }
    
    public static List<ChatMessageMetaProto> ToProtoList(this List<ChatMessageMeta> metas)
    {
        return metas.Select(m => m.ToProto()).ToList();
    }
    
    public static List<ChatMessageMeta> FromProtoList(this IEnumerable<ChatMessageMetaProto> protos)
    {
        return protos.Select(p => p.FromProto()).ToList();
    }
    
    // =============================================================================
    // ChatMessage Conversions
    // =============================================================================
    
    public static ChatMessageProto ToProto(this ChatMessage msg)
    {
        return new ChatMessageProto
        {
            Id = Guid.NewGuid().ToString(),
            Role = msg.Role ?? "user",
            Content = msg.Content ?? "",
            Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(msg.Timestamp, DateTimeKind.Utc))
        };
    }
    
    public static ChatMessage FromProto(this ChatMessageProto proto)
    {
        return new ChatMessage
        {
            Role = proto.Role,
            Content = proto.Content,
            Timestamp = proto.Timestamp?.ToDateTime() ?? DateTime.UtcNow
        };
    }
    
    public static List<ChatMessageProto> ToProtoList(this List<ChatMessage> messages)
    {
        return messages.Select(m => m.ToProto()).ToList();
    }
    
    public static List<ChatMessage> FromProtoList(this IEnumerable<ChatMessageProto> protos)
    {
        return protos.Select(p => p.FromProto()).ToList();
    }
    
    // =============================================================================
    // RegionProxies Conversions
    // =============================================================================
    
    public static List<RegionProxiesEntryProto> ToProto(this Dictionary<string, List<Guid>> regionProxies)
    {
        return regionProxies.Select(kvp => new RegionProxiesEntryProto
        {
            Region = kvp.Key,
            ProxyIds = { kvp.Value.Select(g => g.ToString()) }
        }).ToList();
    }
    
    public static Dictionary<string, List<Guid>> FromProto(this IEnumerable<RegionProxiesEntryProto> protos)
    {
        return protos.ToDictionary(
            p => p.Region,
            p => p.ProxyIds.Select(Guid.Parse).ToList());
    }
    
    // =============================================================================
    // ProxyInitStatuses Conversions
    // =============================================================================
    
    public static ProxyInitStatusProto ToProto(this ProxyInitStatus status)
    {
        return status switch
        {
            ProxyInitStatus.NotInitialized => ProxyInitStatusProto.ProxyInitStatusNotInitialized,
            ProxyInitStatus.Initializing => ProxyInitStatusProto.ProxyInitStatusInitializing,
            ProxyInitStatus.Initialized => ProxyInitStatusProto.ProxyInitStatusInitialized,
            _ => ProxyInitStatusProto.ProxyInitStatusNotInitialized
        };
    }
    
    public static ProxyInitStatus FromProto(this ProxyInitStatusProto proto)
    {
        return proto switch
        {
            ProxyInitStatusProto.ProxyInitStatusNotInitialized => ProxyInitStatus.NotInitialized,
            ProxyInitStatusProto.ProxyInitStatusInitializing => ProxyInitStatus.Initializing,
            ProxyInitStatusProto.ProxyInitStatusInitialized => ProxyInitStatus.Initialized,
            _ => ProxyInitStatus.NotInitialized
        };
    }
    
    public static List<ProxyInitStatusEntryProto> ToProto(this Dictionary<Guid, ProxyInitStatus> statuses)
    {
        return statuses.Select(kvp => new ProxyInitStatusEntryProto
        {
            ProxyId = kvp.Key.ToString(),
            Status = kvp.Value.ToProto()
        }).ToList();
    }
    
    public static Dictionary<Guid, ProxyInitStatus> FromProto(this IEnumerable<ProxyInitStatusEntryProto> protos)
    {
        return protos.ToDictionary(
            p => Guid.Parse(p.ProxyId),
            p => p.Status.FromProto());
    }
    
    // =============================================================================
    // DateTime/Timestamp Helpers
    // =============================================================================
    
    public static Timestamp? ToProtoTimestamp(this DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        return Timestamp.FromDateTime(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc));
    }
    
    public static DateTime? FromProtoTimestamp(this Timestamp? timestamp)
    {
        return timestamp?.ToDateTime();
    }
    
    // =============================================================================
    // Default ChatMessageMeta
    // =============================================================================
    
    public static ChatMessageMetaProto CreateDefaultMetaProto()
    {
        return new ChatMessageMetaProto
        {
            IsVoiceMessage = false,
            VoiceLanguage = (int)VoiceLanguageEnum.English,
            VoiceParseSuccess = true,
            VoiceParseErrorMessage = "",
            VoiceDurationSeconds = 0.0
        };
    }
    
    public static ChatMessageMeta CreateDefaultMeta()
    {
        return new ChatMessageMeta
        {
            IsVoiceMessage = false,
            VoiceLanguage = VoiceLanguageEnum.English,
            VoiceParseSuccess = true,
            VoiceParseErrorMessage = null,
            VoiceDurationSeconds = 0.0
        };
    }
    
    // =============================================================================
    // ResponseStreamGodChat Conversions
    // =============================================================================
    
    public static ResponseStreamGodChatProto ToProto(this Aevatar.Application.Grains.Agents.ChatManager.ResponseStreamGodChat msg)
    {
        var proto = new ResponseStreamGodChatProto
        {
            ResponseType = msg.ResponseType switch
            {
                Aevatar.Application.Grains.Agents.ChatManager.ResponseType.ChatResponse => ResponseTypeProto.ResponseTypeChatResponse,
                // Map other response types to ChatResponse as fallback
                _ => ResponseTypeProto.ResponseTypeChatResponse
            },
            Response = msg.Response ?? "",
            NewTitle = msg.NewTitle ?? "",
            ChatId = msg.ChatId ?? "",
            IsLastChunk = msg.IsLastChunk,
            SerialNumber = msg.SerialNumber,
            SessionId = msg.SessionId.ToString()
        };
        
        if (msg.AudioData != null)
        {
            proto.AudioData = Google.Protobuf.ByteString.CopyFrom(msg.AudioData);
        }
        
        if (msg.AudioMetadata != null)
        {
            proto.AudioMetadata = new AudioMetadataProto
            {
                DurationSeconds = msg.AudioMetadata.Duration,
                Format = "",  // AudioMetadata doesn't have Format field
                Language = ""  // AudioMetadata doesn't have Language field
            };
        }
        
        return proto;
    }
}

