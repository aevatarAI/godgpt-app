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
        
        // Convert Protobuf messages to ChatMessage list
        var chatMessages = chatMessagesProto.ToList();

        var shareId = Guid.NewGuid();
        var shareLinkGrain = _clusterClient.GetGrain<IShareLinkGrain>(shareId);
        await shareLinkGrain.SaveShareContentAsync(new ShareLinkDto
        {
            UserId = Guid.Parse(Id),
            SessionId = sessionId,
            Messages = chatMessages
        });
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

        var shareLinkGrain = _clusterClient.GetGrain<IShareLinkGrain>(shareId);
        var shareLinkDto = await shareLinkGrain.GetShareContentAsync();
        
        // Convert ShareLinkDto to ShareLinkProto (reusing ChatMessageProto from god_chat.proto)
        var proto = new ShareLinkProto
        {
            UserId = shareLinkDto.UserId.ToString(),
            SessionId = shareLinkDto.SessionId.ToString(),
            CreateTime = Timestamp.FromDateTime(DateTime.SpecifyKind(shareLinkDto.CreateTime, DateTimeKind.Utc))
        };
        
        if (shareLinkDto.Messages != null)
        {
            foreach (var msg in shareLinkDto.Messages)
            {
                proto.Messages.Add(new Aevatar.Agents.GodGPT.Protos.GodChat.ChatMessageProto
                {
                    Role = msg.Role ?? string.Empty,
                    Content = msg.Content ?? string.Empty,
                    Timestamp = Timestamp.FromDateTime(DateTime.SpecifyKind(msg.Timestamp, DateTimeKind.Utc)),
                    ChatRole = (int)msg.ChatRole,
                    ImageKeys = { msg.ImageKeys ?? new List<string>() }
                });
            }
        }
        
        return proto;
    }
}

