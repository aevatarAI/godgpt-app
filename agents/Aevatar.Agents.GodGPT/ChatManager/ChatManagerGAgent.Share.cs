using Aevatar.Agents.Abstractions;
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
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionInfo?.SessionId.ToString()}");
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        if (sessionInfo == null)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, session not found.");
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.ConversationDeleted, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareIds = sessionInfo.GetShareIds();
        if (shareIds.IsNullOrEmpty() || !shareIds.Contains(shareId))
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionId.ToString()}, shareId not found.");
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
