using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    protected override void TransitionState(ChatManagerStateProto state, IMessage @event)
    {
        switch (@event)
        {
            case SetRegisteredAtUtcEvent setRegisteredAtUtcEvent:
                state.RegisteredAtUtc = setRegisteredAtUtcEvent.RegisteredAtUtc;
                break;
            case CreateSessionInfoEvent @createSessionInfo:
                if (state.SessionInfoList.IsNullOrEmpty() && state.RegisteredAtUtc == null)
                {
                    state.RegisteredAtUtc = DateTime.UtcNow.ToProtoTimestamp();
                }

                state.SessionInfoList.Add(new SessionInfoProto()
                {
                    SessionId = @createSessionInfo.SessionId,
                    Title = @createSessionInfo.Title,
                    CreateAt = @createSessionInfo.CreateAt,
                    Guider = @createSessionInfo.Guider
                });
                break;
            case DeleteSessionEvent @deleteSessionEventLog:
                var deleteSession = state.GetSession(@deleteSessionEventLog.SessionId);
                if (deleteSession != null && deleteSession.HasShareIds())
                {
                    state.CurrentShareCount -= deleteSession.GetShareIds().Count;
                }

                var toRemove = state.SessionInfoList.FirstOrDefault(f => f.SessionId == @deleteSessionEventLog.SessionId);
                if (toRemove != null) state.SessionInfoList.Remove(toRemove);
                break;
            case CleanExpiredSessionsEvent @cleanExpiredSessionsEventLog:
                var cleanBefore = @cleanExpiredSessionsEventLog.CleanBefore?.ToDateTime() ?? DateTime.MinValue;
                var expiredSessionIds = state.SessionInfoList
                    .Where(s => (s.CreateAt?.ToDateTime() ?? DateTime.MinValue) <= cleanBefore &&
                                string.IsNullOrEmpty(s.Title))
                    .Select(s => s.SessionId)
                    .ToList();

                foreach (var expiredSessionId in expiredSessionIds)
                {
                    var expiredSession = state.GetSession(expiredSessionId);
                    if (expiredSession != null && expiredSession.HasShareIds())
                    {
                        state.CurrentShareCount -= expiredSession.GetShareIds().Count;
                    }
                }

                var sessionsToRemove = state.SessionInfoList.Where(s => expiredSessionIds.Contains(s.SessionId)).ToList();
                foreach (var s in sessionsToRemove) state.SessionInfoList.Remove(s);
                break;
            case RenameTitleEvent @renameTitleEventLog:
                Logger.LogDebug(
                    $"[ChatGAgentManager][RenameChatTitleEvent] event:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                var sessionInfo = state.SessionInfoList.First(f => f.SessionId == @renameTitleEventLog.SessionId);
                Logger.LogDebug(
                    $"[ChatGAgentManager][RenameChatTitleEvent] event exist:{JsonConvert.SerializeObject(@renameTitleEventLog)}");
                sessionInfo.Title = @renameTitleEventLog.Title;
                // Note: RepeatedField is modified in-place, no need to reassign
                break;
            case ClearAllEvent:
                state.SessionInfoList.Clear();
                state.CurrentShareCount = 0;
                break;
            case GenerateChatShareContentEvent generateChatShareContentLogEvent:
                var session = state.GetSession(generateChatShareContentLogEvent.SessionId);
                if (session == null)
                {
                    Logger.LogDebug(
                        $"[ChatGAgentManager][GenerateChatShareContentEvent] session not fuound: {generateChatShareContentLogEvent.SessionId.ToString()}");
                    break;
                }

                state.CurrentShareCount += 1;
                // SessionInfoProto has single ShareId field
                session.AddShareId(Guid.Parse(generateChatShareContentLogEvent.ShareId));
                break;
            case SetMaxShareCountEvent setMaxShareCountLogEvent:
                state.MaxShareCount = setMaxShareCountLogEvent.MaxShareCount;
                break;
            case InitializeNewUserStatusEvent initializeNewUserStatusLogEvent:
                state.IsFirstConversation = initializeNewUserStatusLogEvent.IsFirstConversation;
                state.UserId = initializeNewUserStatusLogEvent.UserId;
                if (initializeNewUserStatusLogEvent.RegisteredAtUtc != null)
                {
                    state.RegisteredAtUtc = initializeNewUserStatusLogEvent.RegisteredAtUtc;
                }

                state.MaxShareCount = initializeNewUserStatusLogEvent.MaxShareCount;
                break;
        }
    }
}

