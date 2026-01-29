using System.Diagnostics;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserProfile;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Volo.Abp;
using Aevatar.Agents.Abstractions.Extensions;

namespace Aevatar.Application.Grains.Agents.ChatManager;

public partial class ChatGAgentManager
{
    #region User Profile

    public async Task<UserProfileResponseProto> GetLastSessionUserProfileAsync()
    {
        var sessionInfo = State.SessionInfoList.LastOrDefault();
        if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.SessionId))
        {
            return new UserProfileResponseProto();
        }

        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(sessionInfo.SessionId);
        var godChat = godChatActor.As<IGodChat>();
        var userProfileDto = await godChat.GetUserProfileAsync();
        return userProfileDto?.ToProto() ?? new UserProfileResponseProto();
    }
    
    #endregion
    
    public async Task RenameChatTitleAsync(Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] start:{JsonConvert.SerializeObject(@event)}");

        RaiseEvent(new RenameTitleEvent()
        {
            SessionId = @event.SessionId,
            Title = @event.Title
        });
        await ConfirmEventsAsync();

        Logger.LogDebug($"[ChatGAgentManager][RenameChatTitleEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    public async Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null,
        string? guider = null, DateTime? userLocalTime = null)
    {
        var methodStart = Stopwatch.StartNew();
        Logger.LogInformation("[PERF][ChatGAgentManager] CreateSessionAsync START - UserId: {UserId}", Id);

        var configuration = await GetConfigurationAsync();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        var newSessionId = Guid.NewGuid();
        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(newSessionId.ToString());
        var godChat = godChatActor.As<IGodChat>();
        // await RegisterAsync(godChat);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step,time use:{sw.ElapsedMilliseconds}");
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] grainId={newSessionId.ToString()}");

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
            LlmSystemLlm = await configuration.GetSystemLLMAsync(),
            StreamingModeEnabled = true, 
            StreamingBufferingSize = 32
        };
        Logger.LogDebug($"[GodChatGAgent][InitializeAsync] Detail : {JsonConvert.SerializeObject(godChatConfig)}");
        await godChat.ConfigAsync(godChatConfig);

        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");

        var sessionId = newSessionId; // Use the sessionId we created, not parsing from Actor ID
        if (userProfile != null)
        {
            Logger.LogDebug("CreateSessionAsync set user profile. session={0}", sessionId);
            var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(AgentId.ExtractRawId(Id));
            var userProfileGAgent = userProfileActor.As<IUserProfileGAgent>();
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
            Guider = guider ?? "" // Protobuf string cannot be null
        });
        Logger.LogInformation("[PERF][ChatGAgentManager] CreateSessionAsync ConfirmEventsAsync START");
        await ConfirmEventsAsync();
        Logger.LogInformation("[PERF][ChatGAgentManager] CreateSessionAsync ConfirmEventsAsync END - Duration: {Duration}ms", methodStart.ElapsedMilliseconds);

        var initStopwatch = Stopwatch.StartNew();
        // Extract raw Guid from Agent ID (may be in "AgentType:Guid" format)
        var chatManagerGuid = ExtractGuidFromId(Id);
        await godChat.InitAsync(chatManagerGuid);
        initStopwatch.Stop();
        Logger.LogDebug(
            $"[ChatManagerGAgent][CreateSessionAsync] InitAsync completed - Duration: {initStopwatch.ElapsedMilliseconds}ms");

        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");
        return newSessionId;
    }

    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid sessionId, string sysmLLM, string content,
        ExecutionPromptSettings promptSettings = null)
    {
        throw new Exception("The method is outdated");
    }

    public async Task<SessionListProto> GetSessionListAsync()
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

        var result = new SessionListProto();

        foreach (var item in State.SessionInfoList)
        {
            var sessionProto = new SessionInfoProto
            {
                SessionId = item.SessionId,
                Title = item.Title,
                CreateAt = item.CreateAt ?? Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
                Guider = item.Guider ?? string.Empty
            };
            // Copy all ShareIds
            if (item.ShareIds != null && item.ShareIds.Count > 0)
            {
                sessionProto.ShareIds.AddRange(item.ShareIds);
            }
            result.Sessions.Add(sessionProto);
        }

        return result;
    }

    public async Task<bool> IsUserSessionAsync(Guid sessionId)
    {
        var sessionInfo = State.GetSession(sessionId);
        return sessionInfo != null;
    }

    public async Task<ChatMessageListProto> GetSessionMessageListAsync(Guid sessionId)
    {
        var sw = Stopwatch.StartNew();
        var sessionInfo = State.GetSession(sessionId);

        if (sessionInfo == null)
        {
            throw new InvalidOperationException($"Unable to load conversation {sessionId}");
        }

        // Step A: Create GodChatGAgent Actor
        var stepAStart = sw.ElapsedMilliseconds;
        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(sessionInfo.SessionId);
        var stepAEnd = sw.ElapsedMilliseconds;
        
        var godChat = godChatActor.As<IGodChat>();
        
        // Step B: Call GetChatMessageAsync RPC
        var stepBStart = sw.ElapsedMilliseconds;
        var result = await godChat.GetChatMessageAsync();
        var stepBEnd = sw.ElapsedMilliseconds;
        
        Logger.LogInformation(
            "[PERF][GetMessages] TOTAL={TotalMs}ms - CreateActor={CreateActorMs}ms, GetChatMessage={GetChatMs}ms - SessionId: {SessionId}, MessageCount: {Count}",
            sw.ElapsedMilliseconds, stepAEnd - stepAStart, stepBEnd - stepBStart, sessionId, result?.Messages?.Count ?? 0);
        
        return result;
    }

    public async Task<ChatMessageWithMetaListProto> GetSessionMessageListWithMetaAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}");
        var sessionInfo = State.GetSession(sessionId);

        if (sessionInfo == null)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
            var parameters = new Dictionary<string, string>
            {
                ["SessionId"] = sessionId.ToString()
            };
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidConversation, language,
                    parameters);
            Logger.LogWarning(
                $"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - Session not found: {sessionId} ,language:{language}");
            // Use InvalidOperationException instead of UserFriendlyException for Orleans serialization
            throw new InvalidOperationException(localizedMessage);
        }

        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(sessionInfo.SessionId);
        var godChat = godChatActor.As<IGodChat>();
        var result = await godChat.GetChatMessageWithMetaAsync();

        Logger.LogDebug(
            $"[ChatManagerGAgent][GetSessionMessageListWithMetaAsync] - sessionId: {sessionId}, returned {result.Entries.Count} messages with audio metadata");
        return result;
    }

    public async Task<SessionCreationInfoProto?> GetSessionCreationInfoAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GetSessionCreationInfoAsync] - session:ID {sessionId.ToString()}");
        var sessionInfo = State.GetSession(sessionId);

        if (sessionInfo == null)
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GetSessionCreationInfoAsync] - session not found: {sessionId.ToString()}");
            return null;
        }

        return new SessionCreationInfoProto
        {
            SessionId = sessionInfo.SessionId,
            Title = sessionInfo.Title,
            CreateAt = sessionInfo.CreateAt ?? Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            Guider = sessionInfo.Guider ?? string.Empty
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
        var rawUserId = AgentId.ExtractRawId(Id);
        var userQuotaGAgent = await GetUserQuotaAgentAsync(rawUserId);
        await userQuotaGAgent.ClearAllAsync();

        // Clear payment data (replaces UserBillingGAgent)
        try
        {
            var paymentIndexGAgent = await GetPaymentIndexAgentAsync(AgentId.ExtractRawId(Id));
            
            // Get ALL subscriptions (including expired) and clear their PaymentRecords
            // Fire-and-forget: Orleans Grain is single-threaded, don't block on these
            var allSubscriptions = await paymentIndexGAgent.GetAllSubscriptionsAsync();
            foreach (var subscription in allSubscriptions.Subscriptions)
            {
                var paymentId = subscription.PaymentId;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var paymentRecordGAgent = await GetPaymentRecordAgentAsync(paymentId);
                        await paymentRecordGAgent.ClearAsync();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, "[ChatGAgentManager][ClearAllAsync] PaymentRecordGAgent ClearAsync error paymentId: {PaymentId}", paymentId);
                    }
                });
            }
            
            // Clear the index itself
            await paymentIndexGAgent.ClearAllAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "[ChatGAgentManager][ClearAllAsync] PaymentIndexGAgent ClearAllAsync error userId: {UserId}", Id);
        }

        // Clear invitation data
        try
        {
            var invitationGAgent = await GetInvitationAgentAsync(AgentId.ExtractRawId(Id));
            await invitationGAgent.ClearAllAsync();
        }
        catch (Exception e)
        {
            Logger.LogError(e, "[ChatGAgentManager][ClearAllAsync] InvitationGAgent ClearAllAsync error userId: {UserId}", Id);
        }

        var userInfoCollectionGAgent = await GetUserInfoCollectionAgentAsync(rawUserId);
        await userInfoCollectionGAgent.ClearAllAsync();

        var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(AgentId.ExtractRawId(Id));
        var userProfileGAgent = userProfileActor.As<IUserProfileGAgent>();
        await userProfileGAgent.ClearAsync();

        // TODO: [GOOGLE_AUTH_DISABLED] Unbind Google account - disabled until Google Auth is migrated
        // var googleAuthGAgent = _clusterClient.GetGrain<IGoogleAuthGAgent>(Id);
        // await googleAuthGAgent.UnbindAccountAsync();

        RaiseEvent(new ClearAllEvent());
        await ConfirmEventsAsync();
        return Guid.Parse(AgentId.ExtractRawId(Id));
    }
}

