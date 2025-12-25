using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Core.Abstractions;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using GodGPT.GAgents.SpeechChat;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public interface IChatManagerGAgent : IGAgent
{
    Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null,
        string? guider = null, DateTime? userLocalTime = null);
    Task<Tuple<string,string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content, ExecutionPromptSettings promptSettings = null);
    [ReadOnly]
    Task<SessionListProto> GetSessionListAsync();
    [ReadOnly]
    Task<bool> IsUserSessionAsync(Guid sessionId);
    [ReadOnly]
    Task<ChatMessageListProto> GetSessionMessageListAsync(Guid sessionId);
    [ReadOnly]
    Task<List<ChatMessageWithMetaDto>> GetSessionMessageListWithMetaAsync(Guid sessionId);
    [ReadOnly]
    Task<SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid sessionId);
    Task<Guid> DeleteSessionAsync(Guid sessionId);
    Task<Guid> RenameSessionAsync(Guid sessionId, string title);
    Task<UserProfileDto> GetLastSessionUserProfileAsync();
    Task<Guid> ClearAllAsync();
    Task RenameChatTitleAsync(Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent @event);
    Task<Guid> GenerateChatShareContentAsync(Guid sessionId);
    [ReadOnly]
    Task<ShareLinkDto> GetChatShareContentAsync(Guid sessionId, Guid shareId);

    /// <summary>
    /// Search sessions by keyword with fuzzy matching
    /// </summary>
    /// <param name="keyword">Search keyword</param>
    /// <param name="maxResults">Maximum number of results to return (default: 1000)</param>
    /// <returns>List of matching sessions with content preview</returns>
    [ReadOnly]
    Task<SessionListProto> SearchSessionsAsync(string keyword, int maxResults = 1000);
    
}