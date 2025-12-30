using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    public async Task<Guid> GenerateChatShareContentAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}");
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        if (State.CurrentShareCount >= State.MaxShareCount)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, Exceed the maximum sharing limit. {State.CurrentShareCount}");
            var parameters = new Dictionary<string, string>
            {
                ["MaxShareCount"] = State.MaxShareCount.ToString()
            };
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.SharesReached, language, parameters);

            throw new UserFriendlyException(localizedMessage);
        }

        var chatMessagesProto = await GetSessionMessageListAsync(sessionId);
        if (chatMessagesProto == null || chatMessagesProto.Messages.Count == 0)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, chatMessages is null");
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidSession, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareId = Guid.NewGuid();
        
        // Use new ShareLinkGAgent instead of Orleans Grain
        var shareLinkActor = await _actorFactory.CreateGAgentActorAsync<ShareLinkGAgent>(shareId.ToString());
        var shareLink = shareLinkActor.As<IShareLinkGAgent>();
        
        // Build ShareLinkProto directly
        var shareLinkProto = new ShareLinkProto
        {
            UserId = Id,
            SessionId = sessionId.ToString(),
            CreateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        shareLinkProto.Messages.AddRange(chatMessagesProto.Messages);
        
        await shareLink.SaveShareContentAsync(shareLinkProto);
        
        Logger.LogDebug(
            $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, save success");
        RaiseEvent(new GenerateChatShareContentEvent
        {
            SessionId = sessionId.ToString(),
            ShareId = shareId.ToString()
        });

        await ConfirmEventsAsync();
        return shareId;
    }

    public async Task<ShareLinkProto> GetChatShareContentAsync(Guid sessionId, Guid shareId)
    {
        // Enhanced diagnostic logging for share link issues
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] START - UserId: {UserId}, SessionId: {SessionId}, ShareId: {ShareId}, " +
            "State.SessionCount: {SessionCount}, State.CurrentShareCount: {CurrentShareCount}",
            Id, sessionId, shareId, State.SessionInfoList.Count, State.CurrentShareCount);
        
        // Log all sessions in state for debugging
        foreach (var s in State.SessionInfoList.Take(10))
        {
            Logger.LogInformation(
                "[ChatGAgentManager][GetChatShareContentAsync] Session in State: SessionId={SId}, Title={Title}, ShareId={ShareId}",
                s.SessionId, s.Title ?? "(empty)", s.ShareId ?? "(none)");
        }
        if (State.SessionInfoList.Count > 10)
        {
            Logger.LogInformation("[ChatGAgentManager][GetChatShareContentAsync] ... and {More} more sessions", 
                State.SessionInfoList.Count - 10);
        }
        
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] GetSession result: Found={Found}, SessionId={FoundSessionId}",
            sessionInfo != null, sessionInfo?.SessionId ?? "(null)");
        
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        if (sessionInfo == null)
        {
            Logger.LogWarning(
                "[ChatGAgentManager][GetChatShareContentAsync] FAIL - Session NOT FOUND in State. " +
                "RequestedSessionId: {SessionId}, AvailableSessionIds: [{AvailableIds}]",
                sessionId, string.Join(", ", State.SessionInfoList.Select(s => s.SessionId).Take(5)));
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareIds = sessionInfo.GetShareIds();
        Logger.LogInformation(
            "[ChatGAgentManager][GetChatShareContentAsync] ShareId check - StoredShareId: {StoredShareId}, " +
            "RequestedShareId: {RequestedShareId}, Match: {Match}",
            sessionInfo.ShareId ?? "(empty)", shareId, shareIds.Contains(shareId));
        
        if (shareIds.IsNullOrEmpty() || !shareIds.Contains(shareId))
        {
            Logger.LogWarning(
                "[ChatGAgentManager][GetChatShareContentAsync] FAIL - ShareId NOT FOUND. " +
                "StoredShareId: {StoredShareId}, RequestedShareId: {RequestedShareId}",
                sessionInfo.ShareId ?? "(empty)", shareId);
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted, language);
            throw new UserFriendlyException(localizedMessage);
        }

        // Use new ShareLinkGAgent instead of Orleans Grain
        var shareLinkActor = await _actorFactory.CreateGAgentActorAsync<ShareLinkGAgent>(shareId.ToString());
        var shareLink = shareLinkActor.As<IShareLinkGAgent>();
        
        return await shareLink.GetShareContentAsync();
    }
}
