using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserProfile;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volo.Abp;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    #region User Profile

    public async Task<UserProfileDto> GetLastSessionUserProfileAsync()
    {
        var sessionInfo = State.SessionInfoList.LastOrDefault();
        if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.SessionId))
        {
            return new UserProfileDto();
        }

        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(Guid.Parse(sessionInfo.SessionId));
        var godChat = (IGodChat)godChatActor.GetAgent();
        var userProfileDto = await godChat.GetUserProfileAsync();
        return userProfileDto ?? new UserProfileDto();
    }
    
    #endregion
    
    public async Task RenameChatTitleAsync(RenameChatTitleEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] start:{JsonConvert.SerializeObject(@event)}");

        RaiseEvent(new RenameTitleEvent()
        {
            SessionId = @event.SessionId.ToString(),
            Title = @event.Title
        });

        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    public async Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null,
        string? guider = null, DateTime? userLocalTime = null)
    {
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] Start - UserId: {Id}");

        var configuration = await GetConfigurationAsync();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(Guid.NewGuid());
        var godChat = (IGodChat)godChatActor.GetAgent();
        // await RegisterAsync(godChat);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step,time use:{sw.ElapsedMilliseconds}");
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] grainId={godChat.GetGrainId().ToString()}");

        sw.Reset();

        // Add role-specific prompt if guider is provided
        var sysMessage = string.Empty;
        if (!string.IsNullOrEmpty(guider))
        {
            if (guider == SessionGuiderConstants.DailyGuide)
            {
                sysMessage = $"###{SessionGuiderConstants.DailyGuide}###";
            }
            else
            {
                var rolePrompt = GetRolePrompt(guider);
                if (!string.IsNullOrEmpty(rolePrompt))
                {
                    sysMessage = rolePrompt;
                    Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] Added role prompt for guider: {guider}");
                }
            }
        }

        var godChatConfig = new GodChatConfig
        {
            Instructions = sysMessage, 
            MaxHistoryCount = 32,
            LlmSystemLlm = configuration.GetSystemLLM(),  // Now sync call
            StreamingModeEnabled = true, 
            StreamingBufferingSize = 32
        };
        Logger.LogDebug($"[GodChatGAgent][InitializeAsync] Detail : {JsonConvert.SerializeObject(godChatConfig)}");
        await godChat.ConfigAsync(godChatConfig);

        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");

        var sessionId = godChat.Id;
        if (userProfile != null)
        {
            Logger.LogDebug("CreateSessionAsync set user profile. session={0}", sessionId);
            var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(Id);
            var userProfileGAgent = (IUserProfileGAgent)userProfileActor.GetAgent();
            await userProfileGAgent.SetUserProfileAsync(userProfile.Gender, userProfile.BirthDate, userProfile.BirthPlace, userProfile.FullName);
            Logger.LogDebug("CreateSessionAsync set GodChat user profile. session={0}", sessionId);
            await godChat.SetUserProfileAsync(userProfile);
        }

        sw.Reset();

        // Record user activity metrics for retention analysis (before RaiseEvent to ensure proper deduplication)
        await RecordUserActivityMetricsAsync();
        RaiseEvent(new CreateSessionInfoEvent()
        {
            SessionId = sessionId.ToString(),
            Title = "",
            CreateAt = DateTime.UtcNow.ToProtoTimestamp(),
            Guider = guider // Set the role information for the conversation
        });

        var initStopwatch = Stopwatch.StartNew();
        await godChat.InitAsync(Id);
        initStopwatch.Stop();
        Logger.LogDebug(
            $"[ChatManagerGAgent][CreateSessionAsync] InitAsync completed - Duration: {initStopwatch.ElapsedMilliseconds}ms");

        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");
        return godChat.Id;
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        throw new Exception("The method is outdated");
    }

    public async Task<List<SessionInfoDto>> GetSessionListAsync()
    {
        // Clean expired sessions (7 days old and empty title)
        var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
        var hasExpiredSessions = State.SessionInfoList.Any(s =>
            s.CreateAt.LessOrEqualThan(sevenDaysAgo) &&
            string.IsNullOrEmpty(s.Title));

        if (hasExpiredSessions)
        {
            Logger.LogDebug($"[ChatGAgentManager][GetSessionListAsync] Cleaning sessions older than {sevenDaysAgo}");
            RaiseEvent(new CleanExpiredSessionsEvent
            {
                CleanBefore = sevenDaysAgo.ToProtoTimestamp()
            });
            await ConfirmEventsAsync();
        }

        var result = new List<SessionInfoDto>();

        foreach (var item in State.SessionInfoList)
        {
            var createAt = item.CreateAt?.ToDateTime() ?? DateTime.MinValue;
            if (createAt == default)
            {
                createAt = new DateTime(2025, 4, 18);
            }

            result.Add(new SessionInfoDto()
            {
                SessionId = Guid.Parse(item.SessionId),
                Title = item.Title,
                CreateAt = createAt,
                Guider = item.Guider // Include role information in the response
            });
        }

        return result;
    }

    public async Task<bool> IsUserSessionAsync(Guid sessionId)
    {
        var sessionInfo = State.GetSession(sessionId);
        return sessionInfo != null;
    }

    public async Task<List<ChatMessage>> GetSessionMessageListAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {sessionId.ToString()}");
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogDebug(
            $"[ChatGAgentManager][GetSessionMessageListAsync] - session:ID {JsonConvert.SerializeObject(sessionInfo)}");

        if (sessionInfo == null)
        {
            throw new UserFriendlyException($"Unable to load conversation {sessionId}");
        }

        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(Guid.Parse(sessionInfo.SessionId));
        var godChat = (IGodChat)godChatActor.GetAgent();
        return await godChat.GetChatMessageAsync();
    }

    public async Task<List<ChatMessageWithMetaDto>> GetSessionMessageListWithMetaAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}");
        var sessionInfo = State.GetSession(sessionId);

        if (sessionInfo == null)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            var parameters = new Dictionary<string, string>
            {
                ["SessionId"] = sessionId.ToString()
            };
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidConversation, language,
                    parameters);
            Logger.LogWarning(
                $"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - Session not found: {sessionId} ,language:{language}");
            throw new UserFriendlyException(localizedMessage);
        }

        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(Guid.Parse(sessionInfo.SessionId));
        var godChat = (IGodChat)godChatActor.GetAgent();
        var result = await godChat.GetChatMessageWithMetaAsync();

        Logger.LogDebug(
            $"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}, returned {result.Count} messages with audio metadata");
        return result;
    }

    public async Task<SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GetSessionCreationInfoAsync] - session:ID {sessionId.ToString()}");
        var sessionInfo = State.GetSession(sessionId);

        if (sessionInfo == null)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GetSessionCreationInfoAsync] - session not found: {sessionId.ToString()}");
            return null;
        }

        return new SessionCreationInfoDto
        {
            SessionId = Guid.Parse(sessionInfo.SessionId),
            Title = sessionInfo.Title,
            CreateAt = sessionInfo.CreateAt?.ToDateTime() ?? DateTime.MinValue,
            Guider = sessionInfo.Guider
        };
    }

    public async Task<Guid> DeleteSessionAsync(Guid sessionId)
    {
        if (State.GetSession(sessionId) == null)
        {
            return sessionId;
        }

        //Do not clear the content of ShareGrain. When querying, first determine whether the Session exists

        RaiseEvent(new DeleteSessionEvent()
        {
            SessionId = sessionId.ToString()
        });

        await ConfirmEventsAsync();
        return sessionId;
    }

    public async Task<Guid> RenameSessionAsync(Guid sessionId, string title)
    {
        var sessionInfo = State.GetSession(sessionId);
        if (sessionInfo == null || sessionInfo.Title == title)
        {
            return sessionId;
        }

        RaiseEvent(new RenameTitleEvent()
        {
            SessionId = sessionId.ToString(),
            Title = title,
        });

        await ConfirmEventsAsync();
        return sessionId;
    }

    public async Task<Guid> ClearAllAsync()
    {
        //Do not clear the content of ShareGrain. When querying, first determine whether the Session exists
        // Record the event to clear all sessions
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);
        await userQuotaGAgent.ClearAllAsync();

        var userBillingActor = await _actorFactory.CreateGAgentActorAsync<UserBillingGAgent>(Id);
        var userBillingGAgent = (IUserBillingGAgent)userBillingActor.GetAgent();
        await userBillingGAgent.ClearAllAsync();

        var userInfoCollectionGAgent = await GetUserInfoCollectionAgentAsync(Id);
        await userInfoCollectionGAgent.ClearAllAsync();

        var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(Id);
        var userProfileGAgent = (IUserProfileGAgent)userProfileActor.GetAgent();
        await userProfileGAgent.ClearAsync();

        // TODO: [GOOGLE_AUTH_DISABLED] Unbind Google account - disabled until Google Auth is migrated
        // var googleAuthGAgent = _clusterClient.GetGrain<IGoogleAuthGAgent>(Id);
        // await googleAuthGAgent.UnbindAccountAsync();

        RaiseEvent(new ClearAllEvent());
        await ConfirmEventsAsync();
        return Id;
    }
}

