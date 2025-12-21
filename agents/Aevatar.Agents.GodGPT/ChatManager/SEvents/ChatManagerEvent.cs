using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using GodGPT.GAgents.SpeechChat;


namespace Aevatar.Application.Grains.Agents.ChatManager;

/// <summary>
/// Voice content type enum for voice chat responses
/// </summary>
public enum VoiceContentType
{
    /// <summary>
    /// Voice input converted to text content
    /// </summary>
    VoiceToText = 0,
    
    /// <summary>
    /// Voice response content (corresponding to response text)
    /// </summary>
    VoiceResponse = 1
}

[GenerateSerializer]
public class ResponseCreateGod : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.CreateSession;
    [Id(1)] public Guid SessionId { get; set; }
    [Id(2)] public String SessionVersion { get; set; }
}

[GenerateSerializer]
public class ResponseStreamGodChat : ResponseToPublisherEventBase
{
    [Id(0)] public ResponseType ResponseType { get; set; } = ResponseType.ChatResponse;
    [Id(1)] public string Response { get; set; }
    [Id(2)] public string NewTitle { get; set; }
    [Id(3)] public string ChatId { get; set; }
    [Id(4)] public bool IsLastChunk { get; set; }

    [Id(5)] public int SerialNumber { get; set; }

    [Id(6)] public Guid SessionId { get; set; }
    
    /// <summary>
    /// Binary MP3 audio data for voice response
    /// </summary>
    [Id(7)] public byte[]? AudioData { get; set; }
    
    /// <summary>
    /// Audio metadata including duration, format, language etc.
    /// </summary>
    [Id(8)] public AudioMetadata? AudioMetadata { get; set; }
    [Id(9)] public ChatErrorCode ErrorCode { get; set; }
    [Id(10)] public VoiceContentType VoiceContentType { get; set; } = VoiceContentType.VoiceResponse;
    
    /// <summary>
    /// AI-generated conversation suggestions (text chat only, present in last chunk if generated)
    /// </summary>
    [Id(11)] public List<string>? SuggestedItems { get; set; }

    public ResponseStreamGodChatForHttp ConvertToHttpResponse()
    {
        return new ResponseStreamGodChatForHttp()
        {
            Response = Response,
            ChatId = ChatId,
            IsLastChunk = IsLastChunk,
            SerialNumber = SerialNumber,
            SessionId = SessionId,
            AudioData = AudioData,
            AudioMetadata = AudioMetadata,
            ErrorCode = ErrorCode,
            VoiceContentType = VoiceContentType,
            SuggestedItems = SuggestedItems
        };
    }
}

public class ResponseStreamGodChatForHttp
{
    public ResponseType ResponseType { get; set; } = ResponseType.ChatResponse;
    public string Response { get; set; }
    public string ChatId { get; set; }
    public bool IsLastChunk { get; set; }
    public int SerialNumber { get; set; }
    public int SerialChunk { get; set; }
    public Guid SessionId { get; set; }
    
    /// <summary>
    /// Binary MP3 audio data for voice response
    /// </summary>
    public byte[]? AudioData { get; set; }
    
    /// <summary>
    /// Audio metadata including duration, format, language etc.
    /// </summary>
    public AudioMetadata? AudioMetadata { get; set; } 
    public ChatErrorCode ErrorCode { get; set; }
    public VoiceContentType VoiceContentType { get; set; } = VoiceContentType.VoiceResponse;
    
    /// <summary>
    /// AI-generated conversation suggestions (text chat only, present in last chunk if generated)
    /// </summary>
    public List<string>? SuggestedItems { get; set; }

}

[GenerateSerializer]
public class UserProfileDto
{
    [Id(0)] public string Gender { get; set; }
    [Id(1)] public DateTime BirthDate { get; set; }
    [Id(2)] public string BirthPlace { get; set; }
    [Id(3)] public string FullName { get; set; }
    [Id(4)] public CreditsInfoDto Credits { get; set; }
    [Id(5)] public SubscriptionInfoProto Subscription { get; set; }
    [Id(6)] public SubscriptionInfoProto UltimateSubscription { get; set; }
    [Id(7)] public Guid Id { get; set; }
    [Id(8)] public Guid? InviterId { get; set; }
    [Id(9)] public VoiceLanguageEnum VoiceLanguage { get; set; }
    [Id(10)] public bool? IsFirstConversation { get; set; }

}

[GenerateSerializer]
public enum ResponseType
{
    CreateSession = 1,
    ChatResponse = 2,
    SessionListResponse = 3,
    SessionChatHistory = 4,
    SessionDelete = 5,
    SessionRename = 6,
    ClearAll = 7,
    SetUserProfile = 8,
    GetUserProfile = 9
}

[GenerateSerializer]
public class AudioMetadata
{
    /// <summary>
    /// Audio duration in seconds
    /// </summary>
    [Id(0)] public double Duration { get; set; }
    
    /// <summary>
    /// Audio file size in bytes
    /// </summary>
    [Id(1)] public int SizeBytes { get; set; }
    
    /// <summary>
    /// Audio sample rate (16000 Hz)
    /// </summary>
    [Id(2)] public int SampleRate { get; set; }
    
    /// <summary>
    /// Audio bit rate (24000 bps)
    /// </summary>
    [Id(3)] public int BitRate { get; set; }
    
    /// <summary>
    /// Audio format (mp3)
    /// </summary>
    [Id(4)] public string Format { get; set; }
    
    /// <summary>
    /// Language type for voice
    /// </summary>
    [Id(5)] public VoiceLanguageEnum LanguageType { get; set; }
}

[GenerateSerializer]
public class ChatMessageMeta
{
    /// <summary>
    /// Whether this message is a voice message
    /// </summary>
    [Id(0)] public bool IsVoiceMessage { get; set; }
    
    /// <summary>
    /// Voice language if this is a voice message
    /// </summary>
    [Id(1)] public VoiceLanguageEnum VoiceLanguage { get; set; }
    
    /// <summary>
    /// Whether voice parsing was successful
    /// </summary>
    [Id(2)] public bool VoiceParseSuccess { get; set; }
    
    /// <summary>
    /// Error message if voice parsing failed (in English)
    /// </summary>
    [Id(3)] public string? VoiceParseErrorMessage { get; set; }
    
    /// <summary>
    /// Duration of the voice message in seconds (from frontend)
    /// </summary>
    [Id(4)] public double VoiceDurationSeconds { get; set; }
}

public enum ChatErrorCode
{
    Success = 0,
    ParamInvalid = 20001,
    VoiceParsingFailed = 20002,
    InsufficientCredits = 20003,
    RateLimitExceeded = 20004
}