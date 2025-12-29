using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

public interface IGodChat : IGAgent
{
    Task<string> GodChatAsync(string llm, string message, ExecutionPromptSettings? promptSettings = null);
    Task InitAsync(Guid ChatManagerGuid);
    
    /// <summary>
    /// Configure the GodChat agent with Protobuf config
    /// </summary>
    Task ConfigAsync(GodChatConfig config);
    
    /// <summary>
    /// Start streaming chat with Protobuf input (for RPC calls)
    /// </summary>
    Task StartStreamChatAsync(StartStreamChatInputProto input);

    Task<string> GodStreamChatAsync(Guid sessionId, string llm, bool streamingModeEnabled, string message,
        string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false, string? region = null,
        bool addToHistory = true, List<string>? images = null, DateTime? userLocalTime = null, string? userTimeZoneId = null);

    [ReadOnly]
    Task<ChatMessageListProto> GetChatMessageAsync();
    
    [ReadOnly]
    Task<ChatMessageWithMetaListProto> GetChatMessageWithMetaAsync();

    [Obsolete("Recommend using StartStreamChatAsync")]
    Task StreamChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null,
        List<string>? images = null);

    Task StreamVoiceChatWithSessionAsync(Guid sessionId, string sysmLLM, string? voiceData, string fileName, string chatId,
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null, VoiceLanguageEnum voiceLanguage = VoiceLanguageEnum.English, double voiceDurationSeconds = 0.0);
    
    Task SetUserProfileAsync(UserProfileDto? userProfileDto);
    Task<UserProfileDto?> GetUserProfileAsync();

    Task ChatMessageCallbackAsync(AIChatContextProto? aiChatContextProto,
        AIExceptionEnum aiExceptionEnum, string? errorMessage,
        AIStreamChatContentProto? aiStreamChatContent);
    
    Task<ChatMessageListProto> ChatWithHistory(Guid sessionId, string systemLLM, string content, string chatId, 
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);
    
    Task<ChatMessageListProto> ChatWithoutHistoryAsync(Guid sessionId, string systemLLM, string content, string chatId, 
        ExecutionPromptSettings promptSettings = null, bool isHttpRequest = false, string? region = null);


    [ReadOnly]
    Task<DateTime?> GetFirstChatTimeAsync();

    [ReadOnly]
    Task<DateTime?> GetLastChatTimeAsync();
    
    /// <summary>
    /// Update proxy initialization status (called by AIAgentStatusProxy)
    /// </summary>
    Task UpdateProxyInitStatusAsync(string proxyId, ProxyInitStatus status);
}