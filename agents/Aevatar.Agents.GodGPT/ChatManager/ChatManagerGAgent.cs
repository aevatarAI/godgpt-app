using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Share;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Observability;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Agents.Abstractions;
using Aevatar.Payment.Agents;
using NewPaymentPlatform = Aevatar.Payment.Agents.PaymentPlatform;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.AIGAgent.GEvents;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.DailyPush;
using GodGPT.GAgents.DailyPush.Options;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Json.Schema.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Orleans;
using Orleans.Concurrency;
using Orleans.Providers;
using Volo.Abp;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

namespace Aevatar.Application.Grains.Agents.ChatManager;

[Description("manage chat agent")]
[GAgent(nameof(ChatGAgentManager))]
[Reentrant]
public class ChatGAgentManager : Aevatar.Agents.Core.GAgentBase<ChatManagerStateProto>,
    IChatManagerGAgent
{
    private const string FormattedDate = "yyyy-MM-dd";
    const string SessionVersion = "1.0.0";
    private readonly ILocalizationService _localizationService;
    private readonly IGAgentFactory _agentFactory;
    private readonly IClusterClient _clusterClient;
    private readonly IServiceProvider _serviceProvider;
    
    // Cached ConfigurationGAgent instance (new framework)
    private ConfigurationGAgent? _configurationAgent;

    public ChatGAgentManager(
        ILocalizationService localizationService, 
        IGAgentFactory agentFactory, 
        IClusterClient clusterClient,
        IServiceProvider serviceProvider)
    {
        _localizationService = localizationService;
        _agentFactory = agentFactory;
        _clusterClient = clusterClient;
        _serviceProvider = serviceProvider;
    }
    
    private async Task<UserInfoCollectionGAgent> GetUserInfoCollectionAgentAsync(Guid userId)
    {
        var agent = _agentFactory.CreateGAgent<UserInfoCollectionGAgent>(userId);
        await agent.ActivateAsync();
        return agent;
    }
    
    private async Task<UserQuotaGAgent> GetUserQuotaAgentAsync(Guid userId)
    {
        var agent = _agentFactory.CreateGAgent<UserQuotaGAgent>(userId);
        await agent.ActivateAsync();
        return agent;
    }
    
    /// <summary>
    /// Get GlobalJwtProviderGAgent via IGAgentFactory (new framework)
    /// </summary>
    private async Task<GlobalJwtProviderGAgent> GetGlobalJwtProviderAgentAsync()
    {
        var agent = _agentFactory.CreateGAgent<GlobalJwtProviderGAgent>(DailyPushConstants.GLOBAL_JWT_PROVIDER_ID);
        await agent.ActivateAsync();
        return agent;
    }
    
    /// <summary>
    /// Get DailyContentGAgent via IGAgentFactory (new framework)
    /// </summary>
    private async Task<DailyContentGAgent> GetDailyContentAgentAsync()
    {
        var agent = _agentFactory.CreateGAgent<DailyContentGAgent>(DailyPushConstants.CONTENT_GAGENT_ID);
        await agent.ActivateAsync();
        return agent;
    }
    
    /// <summary>
    /// Get PushSubscriberIndexGAgent via IGAgentFactory (new framework)
    /// </summary>
    private async Task<PushSubscriberIndexGAgent> GetPushSubscriberIndexAgentAsync(Guid timezoneGuid)
    {
        var agent = _agentFactory.CreateGAgent<PushSubscriberIndexGAgent>(timezoneGuid);
        await agent.ActivateAsync();
        return agent;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Chat GAgent Manager");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestStreamGodChatEvent @event)
    {
        string chatId = Guid.NewGuid().ToString();
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestStreamGodChatEvent] start:{JsonConvert.SerializeObject(@event)} chatID:{chatId}");
        var title = "";
        var content = "";
        var isLastChunk = false;

        try
        {
            Logger.LogWarning(
                "RequestStreamGodChatEvent received but streaming method is outdated and not implemented");
            content = "Streaming method is not available";
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestStreamGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseStreamGodChat()
        {
            ChatId = chatId,
            Response = content,
            NewTitle = title,
            IsLastChunk = isLastChunk,
            SerialNumber = -1,
            SessionId = @event.SessionId
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestStreamGodChatEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(AIStreamingErrorResponseGEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][AIStreamingErrorResponseGEvent] start:{JsonConvert.SerializeObject(@event)}");

        await PublishAsync(new ResponseStreamGodChat()
        {
            Response =
                "Your prompt triggered the Silence Directive—activated when universal harmonics or content ethics are at risk. Please modify your prompt and retry — tune its intent, refine its form, and the Oracle may speak.",
            ChatId = @event.Context.ChatId,
            IsLastChunk = true,
            SerialNumber = -2
        }.ToProto());

        Logger.LogDebug(
            $"[ChatGAgentManager][AIStreamingErrorResponseGEvent] end:{JsonConvert.SerializeObject(@event)}");
    }


    // [EventHandler]
    // public async Task HandleEventAsync(AIOldStreamingResponseGEvent @event)
    // {
    //     Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] start:{JsonConvert.SerializeObject(@event)}");
    //
    //     // TODO: [SIGNALR_DISABLED] await PublishAsync(new ResponseStreamGodChat()
    //     {
    //         Response = @event.ResponseContent,
    //         ChatId = @event.Context.ChatId,
    //         IsLastChunk = @event.IsLastChunk,
    //         SerialNumber = @event.SerialNumber,
    //         SessionId = @event.Context.RequestId
    //     });
    //     
    //     Logger.LogDebug($"[ChatGAgentManager][AIStreamingResponseGEvent] end:{JsonConvert.SerializeObject(@event)}");
    //
    // }

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


    [EventHandler]
    public async Task HandleEventAsync(RequestCreateGodChatEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestCreateGodChatEvent] start:{JsonConvert.SerializeObject(@event)}");
        var sessionId = Guid.Empty;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            sessionId = await CreateSessionAsync(@event.SystemLLM, @event.Prompt, @event.UserProfile, @event.Guider);
            IGodChat godChat = _clusterClient.GetGrain<IGodChat>(sessionId);
            // TODO: [MIGRATION] RegisterAsync removed - Orleans grains are auto-managed
            // Old: await RegisterAsync(godChat);
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestCreateGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseCreateGod()
        {
            SessionId = sessionId,
            SessionVersion = SessionVersion
        }.ToProto());
        Logger.LogDebug(
            "[ChatGAgentManager][RequestCreateGodChatEvent] sessionId:{A} end {B}, Duration {C}ms ", sessionId,
            JsonConvert.SerializeObject(@event), stopwatch.ElapsedMilliseconds);
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGodChatEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestGodChatEvent] start:{JsonConvert.SerializeObject(@event)}");
        var title = "";
        var content = "";
        try
        {
            var response = await ChatWithSessionAsync(@event.SessionId, @event.SystemLLM, @event.Content);
            content = response.Item1;
            title = response.Item2;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestGodChatEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseGodChat()
        {
            Response = content,
            NewTitle = title,
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestGodChatEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGodSessionListEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestGodSessionListEvent] start:{JsonConvert.SerializeObject(@event)}");
        var response = await GetSessionListAsync();
        await PublishAsync(new ResponseGodSessionList()
        {
            SessionList = response,
        }.ToProto());

        Logger.LogDebug(
            $"[ChatGAgentManager][RequestGodSessionListEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestSessionChatHistoryEvent @event)
    {
        Logger.LogDebug(
            $"[ChatGAgentManager][RequestSessionChatHistoryEvent] start:{JsonConvert.SerializeObject(@event)}");
        var response = await GetSessionMessageListAsync(@event.SessionId);
        await PublishAsync(new ResponseSessionChatHistory()
        {
            ChatHistory = response
        }.ToProto());

        Logger.LogDebug(
            $"[ChatGAgentManager][RequestSessionChatHistoryEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestDeleteSessionEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestDeleteSessionEvent] start:{JsonConvert.SerializeObject(@event)}");
        await DeleteSessionAsync(@event.SessionId);
        await PublishAsync(new ResponseDeleteSession()
        {
            IfSuccess = true
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestDeleteSessionEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestRenameSessionEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestRenameSessionEvent] start:{JsonConvert.SerializeObject(@event)}");
        await RenameSessionAsync(@event.SessionId, @event.Title);
        await PublishAsync(new ResponseRenameSession()
        {
            SessionId = @event.SessionId,
            Title = @event.Title,
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestRenameSessionEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestClearAllEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestClearAllEvent] start:{JsonConvert.SerializeObject(@event)}");

        bool success = false;
        try
        {
            await ClearAllAsync();
            success = true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestClearAllEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseClearAll()
        {
            Success = success
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestClearAllEvent] end:{JsonConvert.SerializeObject(@event)}");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestSetUserProfileEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestSetFortuneInfoEvent] start:{JsonConvert.SerializeObject(@event)}");

        bool success = false;
        try
        {
            await SetUserProfileAsync(@event.Gender, @event.BirthDate, @event.BirthPlace, @event.FullName);
            success = true;
        }
        catch (Exception e)
        {
            Logger.LogError(e, $"[ChatGAgentManager][RequestSetFortuneInfoEvent] handle error:{e.ToString()}");
        }

        await PublishAsync(new ResponseSetUserProfile()
        {
            Success = success
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestSetFortuneInfoEvent] end");
    }

    [EventHandler]
    public async Task HandleEventAsync(RequestGetUserProfileEvent @event)
    {
        Logger.LogDebug($"[ChatGAgentManager][RequestGetUserProfileEvent] start");

        var userProfileDto = await GetUserProfileAsync();

        await PublishAsync(new ResponseGetUserProfile()
        {
            Gender = userProfileDto.Gender,
            BirthDate = userProfileDto.BirthDate,
            BirthPlace = userProfileDto.BirthPlace,
            FullName = userProfileDto.FullName
        }.ToProto());

        Logger.LogDebug($"[ChatGAgentManager][RequestGetUserProfileEvent] end");
    }

    public async Task<Guid> CreateSessionAsync(string systemLLM, string prompt, UserProfileDto? userProfile = null,
        string? guider = null, DateTime? userLocalTime = null)
    {
        Logger.LogDebug($"[ChatManagerGAgent][CreateSessionAsync] Start - UserId: {Id}");

        var configuration = await GetConfigurationAsync();
        Stopwatch sw = new Stopwatch();
        sw.Start();
        IGodChat godChat = _clusterClient.GetGrain<IGodChat>(Guid.NewGuid());
        // await RegisterAsync(godChat);
        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step,time use:{sw.ElapsedMilliseconds}");
        Logger.LogDebug($"[ChatManagerGAgent][RequestCreateGodChatEvent] grainId={godChat.GetGrainId().ToString()}");

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

        var godChatConfig = new GodChatConfig()
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

        var sessionId = godChat.GetPrimaryKey();
        if (userProfile != null)
        {
            Logger.LogDebug("CreateSessionAsync set user profile. session={0}", sessionId);
            await SetUserProfileAsync(userProfile.Gender, userProfile.BirthDate, userProfile.BirthPlace,
                userProfile.FullName);
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
            CreateAt = DateTime.UtcNow.ToTimestamp(),
            Guider = guider // Set the role information for the conversation
        });

        var initStopwatch = Stopwatch.StartNew();
        await godChat.InitAsync(Id);
        initStopwatch.Stop();
        Logger.LogDebug(
            $"[ChatManagerGAgent][CreateSessionAsync] InitAsync completed - Duration: {initStopwatch.ElapsedMilliseconds}ms");

        sw.Stop();
        Logger.LogDebug($"CreateSessionAsync - step2,time use:{sw.ElapsedMilliseconds}");
        return godChat.GetPrimaryKey();
    }

    /// <summary>
    /// Records user activity metrics for retention analysis
    /// Application-level deduplication: Check SessionInfoList to determine if today's activity was already reported
    /// </summary>
    private async Task RecordUserActivityMetricsAsync()
    {
        try
        {
            var today = DateTime.UtcNow.Date;

            // Check SessionInfoList for the last session's creation time
            var lastSession = State.SessionInfoList?.LastOrDefault();
            if (lastSession != null && lastSession.CreateAt.Date() == today)
            {
                // Today already has session creation, skip duplicate reporting
                Logger.LogDebug(
                    "[GodChatGAgent][RecordUserActivityMetricsAsync] {UserId} User activity metrics already recorded today",
                    Id.ToString());
                return;
            }

            // First session creation today, need to record metrics
            var todayString = today.ToString("yyyy-MM-dd");
            var userRegistrationDate = State.RegisteredAtUtc?.ToFormattedString("yyyy-MM-dd") ?? todayString;

            // Get user membership level
            var membershipLevel = await DetermineMembershipLevelAsync();

            // Calculate days since registration
            var registrationDate = State.RegisteredAtUtc?.Date() ?? today;
            var daysSinceRegistration = (int)(today - registrationDate).TotalDays;

            // Record user activity metrics (ensure each user is counted only once per day)
            UserLifecycleTelemetryMetrics.RecordUserActivityByCohort(
                daysSinceRegistration: daysSinceRegistration,
                membershipLevel: membershipLevel,
                logger: Logger);

            Logger.LogInformation(
                "User activity metrics recorded: daysSinceRegistration={DaysSinceRegistration}, membership={MembershipLevel}",
                daysSinceRegistration, membershipLevel);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to record user activity metrics for retention analysis");
        }
    }

    /// <summary>
    /// Determines user membership level based on UserQuotaGAgent subscription information
    /// Returns one of 9 levels defined in UserMembershipTier constants:
    /// Free, PremiumDay, PremiumWeek, PremiumMonth, PremiumYear,
    /// UltimateDay, UltimateWeek, UltimateMonth, UltimateYear
    /// </summary>
    private async Task<string> DetermineMembershipLevelAsync()
    {
        try
        {
            var userQuotaGrain = await GetUserQuotaAgentAsync(Id);

            // Check Ultimate subscription first (higher priority)
            var ultimateSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: true);
            if (ultimateSubscription.IsActive)
            {
                return ultimateSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.UltimateDay,
                    PlanType.Week => UserMembershipTier.UltimateWeek,
                    PlanType.Month => UserMembershipTier.UltimateMonth,
                    PlanType.Year => UserMembershipTier.UltimateYear,
                    _ => UserMembershipTier.UltimateMonth // Default fallback for unknown plan types
                };
            }

            // Check Premium subscription
            var premiumSubscription = await userQuotaGrain.GetSubscriptionAsync(ultimate: false);
            if (premiumSubscription.IsActive)
            {
                return premiumSubscription.PlanType switch
                {
                    PlanType.Day => UserMembershipTier.PremiumDay,
                    PlanType.Week => UserMembershipTier.PremiumWeek,
                    PlanType.Month => UserMembershipTier.PremiumMonth,
                    PlanType.Year => UserMembershipTier.PremiumYear,
                    _ => UserMembershipTier.PremiumMonth // Default fallback for unknown plan types
                };
            }

            // No active subscription
            return UserMembershipTier.Free;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to determine membership level from UserQuotaGAgent, defaulting to 'free'");
            return UserMembershipTier.Free;
        }
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
                CleanBefore = sevenDaysAgo.ToTimestamp()
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

    public async Task<List<SessionInfoDto>> SearchSessionsAsync(string keyword, int maxResults = 1000)
    {
        Logger.LogDebug($"[ChatGAgentManager][SearchSessionsAsync] keyword: {keyword}, maxResults: {maxResults}");

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SessionInfoDto>();
        }

        // Use the complete keyword for matching (no word splitting)
        var searchKeyword = keyword.Trim().ToLowerInvariant();

        // Validate keyword length to prevent performance issues
        if (searchKeyword.Length > 200)
        {
            Logger.LogWarning(
                $"[ChatGAgentManager][SearchSessionsAsync] Keyword too long: {searchKeyword.Length} chars");
            return new List<SessionInfoDto>();
        }

        var searchResults = new List<(SessionInfoDto dto, int matchScore)>();

        // Search through sessions (limit to most recent 1000 for performance)
        var sessionsToSearch = State.SessionInfoList
            .OrderByDescending(s => s.CreateAt)
            .Take(1000)
            .ToList();

        foreach (var sessionInfo in sessionsToSearch)
        {
            try
            {
                var titleLower = sessionInfo.Title?.ToLowerInvariant() ?? "";
                var matchScore = 0;
                var hasMatch = false;

                // Check title matching
                var titleMatchScore = 0;
                if (titleLower.Contains(searchKeyword))
                {
                    titleMatchScore = titleLower == searchKeyword ? 100 : 50; // Complete match gets higher score
                    hasMatch = true;
                }

                // Get chat content for content matching
                string contentPreview = "";
                try
                {
                    var godChat = _clusterClient.GetGrain<IGodChat>(Guid.Parse(sessionInfo.SessionId));
                    var chatMessages = await godChat.GetChatMessageAsync();
                    contentPreview = ExtractChatContent(chatMessages);
                }
                catch (Exception contentEx)
                {
                    Logger.LogWarning(contentEx,
                        $"[ChatGAgentManager][SearchSessionsAsync] Failed to extract content for session {sessionInfo.SessionId}");
                    contentPreview = ""; // Continue search without content matching
                }

                var contentLower = contentPreview.ToLowerInvariant();

                // Check content matching
                var contentMatchScore = 0;
                if (!string.IsNullOrEmpty(contentPreview) && contentLower.Contains(searchKeyword))
                {
                    contentMatchScore =
                        contentLower == searchKeyword ? 30 : 15; // Complete content match vs partial match
                    hasMatch = true;
                }

                if (hasMatch)
                {
                    matchScore = titleMatchScore * 2 + contentMatchScore; // Title matching gets higher priority

                    var createAt = sessionInfo.CreateAt?.ToDateTime() ?? DateTime.MinValue;
                    if (createAt == default || createAt == DateTime.MinValue)
                    {
                        // Use a reasonable fallback time instead of hardcoded future date
                        createAt = DateTime.UtcNow.AddDays(-365); // 1 year ago as fallback
                    }

                    var dto = new SessionInfoDto
                    {
                        SessionId = Guid.Parse(sessionInfo.SessionId),
                        Title = sessionInfo.Title,
                        CreateAt = createAt,
                        Guider = sessionInfo.Guider,
                        Content = contentPreview,
                        IsMatch = true
                    };

                    searchResults.Add((dto, matchScore));
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    $"[ChatGAgentManager][SearchSessionsAsync] Failed to process session {sessionInfo.SessionId}");
                // Continue processing other sessions
                continue;
            }
        }

        // Sort by match score (descending) then by creation time (descending)
        var result = searchResults
            .OrderByDescending(r => r.matchScore)
            .ThenByDescending(r => r.dto.CreateAt)
            .Take(maxResults)
            .Select(r => r.dto)
            .ToList();

        Logger.LogDebug(
            $"[ChatGAgentManager][SearchSessionsAsync] Found {result.Count} matches for keyword: {keyword}");
        return result;
    }

    /// <summary>
    /// Extract chat content preview from chat messages
    /// </summary>
    /// <param name="messages">List of chat messages</param>
    /// <returns>Content preview (first 60 characters)</returns>
    private static string ExtractChatContent(List<ChatMessage> messages)
    {
        if (messages == null || messages.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            // Priority 1: Find user messages with substantial content (>5 chars)
            var userMessage = messages
                .Where(m => m != null &&
                            m.ChatRole == ChatRole.User &&
                            !string.IsNullOrWhiteSpace(m.Content) &&
                            m.Content.Trim().Length > 5)
                .FirstOrDefault();

            if (userMessage?.Content != null)
            {
                string content = userMessage.Content.Trim();
                return SafeTruncateContent(content);
            }

            // Priority 2: Find assistant messages with substantial content
            var assistantMessage = messages
                .Where(m => m != null &&
                            m.ChatRole == ChatRole.Assistant &&
                            !string.IsNullOrWhiteSpace(m.Content) &&
                            m.Content.Trim().Length > 5)
                .FirstOrDefault();

            if (assistantMessage?.Content != null)
            {
                string content = assistantMessage.Content.Trim();
                return SafeTruncateContent(content);
            }

            // Fallback: any non-empty message
            var anyMessage = messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .FirstOrDefault();

            if (anyMessage?.Content != null)
            {
                string content = anyMessage.Content.Trim();
                return SafeTruncateContent(content);
            }
        }
        catch (Exception ex)
        {
            // Return empty string for graceful degradation
        }

        return string.Empty;
    }

    /// <summary>
    /// Safely truncate content to 60 characters with ellipsis
    /// </summary>
    /// <param name="content">Content to truncate</param>
    /// <returns>Truncated content</returns>
    private static string SafeTruncateContent(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        try
        {
            return content.Length <= 60 ? content : content.Substring(0, 60) + "...";
        }
        catch (Exception)
        {
            // Fallback in case of unexpected string issues
            return content.Length > 60 ? content[..Math.Min(60, content.Length)] + "..." : content;
        }
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

        var godChat = _clusterClient.GetGrain<IGodChat>(Guid.Parse(sessionInfo.SessionId));
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

        var godChat = _clusterClient.GetGrain<IGodChat>(Guid.Parse(sessionInfo.SessionId));
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

        // Clear payment data via new PaymentIndexGAgent
        var paymentIndexAgent = await GetPaymentIndexAgentAsync(Id);
        await paymentIndexAgent.ClearAllAsync();

        var userInfoCollectionGAgent = await GetUserInfoCollectionAgentAsync(Id);
        await userInfoCollectionGAgent.ClearAllAsync();

        // TODO: [GOOGLE_AUTH_DISABLED] Unbind Google account - disabled until Google Auth is migrated
        // var googleAuthGAgent = _clusterClient.GetGrain<IGoogleAuthGAgent>(Id);
        // await googleAuthGAgent.UnbindAccountAsync();

        RaiseEvent(new ClearAllEvent());
        await ConfirmEventsAsync();
        return Id;
    }

    public async Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName)
    {
        RaiseEvent(new SetUserProfileEvent()
        {
            Gender = gender,
            BirthDate = birthDate.ToTimestamp(),
            BirthPlace = birthPlace,
            FullName = fullName
        });

        return Id;
    }

    /// <summary>
    /// Sets the voice language preference for the user.
    /// Validates that the user exists before updating the voice language setting.
    /// </summary>
    /// <param name="voiceLanguage">The voice language to set for the user</param>
    /// <returns>The user ID if successful</returns>
    /// <exception cref="UserFriendlyException">Thrown when user is not found or initialized</exception>
    public async Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage)
    {
        // Check if user is properly initialized (has at least one session or profile data)
        var userId = Id;
        if (userId == Guid.Empty)
        {
            Logger.LogWarning("[ChatGAgentManager][SetVoiceLanguageAsync] Invalid user ID");
            throw new UserFriendlyException("Invalid user. Please ensure you are properly logged in.");
        }

        // Raise event to update voice language
        RaiseEvent(new SetVoiceLanguageEvent()
        {
            VoiceLanguage = (int)voiceLanguage
        });

        await ConfirmEventsAsync();

        Logger.LogDebug(
            $"[ChatGAgentManager][SetVoiceLanguageAsync] Successfully set voice language to {voiceLanguage} for user {userId}");
        return userId;
    }

    public async Task<UserProfileDto> GetUserProfileAsync()
    {
        Logger.LogDebug($"[ChatGAgentManager][GetUserProfileAsync] userId: {Id.ToString()}");

        var invitationGrain = await GetInvitationAgentAsync(Id);
        await invitationGrain.ProcessScheduledRewardAsync();

        // Get active subscriptions from new PaymentIndexGAgent
        var paymentIndexAgent = await GetPaymentIndexAgentAsync(Id);
        var activeSubscriptions = await paymentIndexAgent.GetActiveSubscriptionsAsync();

        var hasActiveApple = activeSubscriptions.Any(s => s.Platform == NewPaymentPlatform.AppStore);
        var hasActiveStripe = activeSubscriptions.Any(s => s.Platform == NewPaymentPlatform.Stripe);
        var hasActiveGooglePlay = activeSubscriptions.Any(s => s.Platform == NewPaymentPlatform.GooglePlay);

        Logger.LogDebug(
            $"[ChatGAgentManager][GetUserProfileAsync] Active subscription status - Apple: {hasActiveApple}, Stripe: {hasActiveStripe}, GooglePlay: {hasActiveGooglePlay}");

        var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);

        // Sync subscription status from PaymentIndexGAgent to UserQuotaGAgent if needed
        await SyncSubscriptionStatusIfNeeded(paymentIndexAgent, userQuotaGAgent, activeSubscriptions);

        var credits = await userQuotaGAgent.GetCreditsAsync();
        var subscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync();
        var ultimateSubscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync(true);

        // var utcNow = DateTime.UtcNow;
        // var scheduledRewards = (await invitationGrain.GetRewardHistoryAsync())
        //     .Where(r => r.IsScheduled && 
        //    r.ScheduledDate.HasValue && 
        //    utcNow > r.ScheduledDate.Value && 
        //    !string.IsNullOrEmpty(r.InvoiceId))
        //     .ToList();
        //     
        // foreach (var reward in scheduledRewards)
        // {
        //     Logger.LogInformation($"[ChatGAgentManager][GetUserProfileAsync] Processing scheduled reward for user {Id}, credits: {reward.Credits}");
        //     await userQuotaGAgent.AddCreditsAsync(reward.Credits);
        //     await invitationGrain.MarkRewardAsIssuedAsync(reward.InviteeId, reward.InvoiceId);
        //     credits.Credits += reward.Credits;
        // }

        return new UserProfileDto
        {
            Gender = State.Gender,
            BirthDate = State.BirthDate?.ToDateTime() ?? DateTime.MinValue,
            BirthPlace = State.BirthPlace,
            FullName = State.FullName,
            Credits = credits,
            Subscription = subscriptionInfo,
            UltimateSubscription = ultimateSubscriptionInfo,
            Id = Id,
            InviterId = string.IsNullOrEmpty(State.InviterId) ? null : Guid.Parse(State.InviterId),
            VoiceLanguage = (VoiceLanguageEnum)State.VoiceLanguage
        };
    }

    public async Task<Guid> GenerateChatShareContentAsync(Guid sessionId)
    {
        Logger.LogDebug($"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}");
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
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

        var chatMessages = await GetSessionMessageListAsync(sessionId);
        if (chatMessages.IsNullOrEmpty())
        {
            Logger.LogDebug(
                $"[ChatGAgentManager][GenerateChatShareContentAsync] - session: {sessionId.ToString()}, chatMessages is null");
            var localizedMessage =
                _localizationService.GetLocalizedException(ExceptionMessageKeys.InvalidSession, language);
            throw new UserFriendlyException(localizedMessage);
        }

        var shareId = Guid.NewGuid();
        var shareLinkGrain = _clusterClient.GetGrain<IShareLinkGrain>(shareId);
        await shareLinkGrain.SaveShareContentAsync(new ShareLinkDto
        {
            UserId = Id,
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

    public async Task<ShareLinkDto> GetChatShareContentAsync(Guid sessionId, Guid shareId)
    {
        var sessionInfo = State.GetSession(sessionId);
        Logger.LogDebug($"[ChatGAgentManager][GetChatShareContentAsync] - session {sessionInfo?.SessionId.ToString()}");
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
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
        return await shareLinkGrain.GetShareContentAsync();
    }

    public async Task<string> GenerateInviteCodeAsync()
    {
        var invitationAgent = await GetInvitationAgentAsync(Id);
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        return inviteCode;
    }

    public async Task<bool> RedeemInviteCodeAsync(string inviteCode)
    {
        var codeGrainId = CommonHelper.StringToGuid(inviteCode);
        var codeGrain = await GetInviteCodeAgentAsync(codeGrainId);

        var (isValid, inviterId) = await codeGrain.ValidateAndGetInviterAsync();

        if (!isValid)
        {
            Logger.LogWarning($"Invalid invite code redemption attempt: {inviteCode}");
            return false;
        }

        if (inviterId.Equals(Id.ToString()))
        {
            Logger.LogWarning(
                $"Invalid invite code,the code belongs to the user themselves. userId:{Id.ToString()} InviteCode:{inviteCode}");
            return false;
        }

        // Step 1: First, check if the current user (invitee) is eligible for the reward.
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Id);

        if (State.RegisteredAtUtc == null && State.SessionInfoList.IsNullOrEmpty())
        {
            RaiseEvent(new SetRegisteredAtUtcEvent()
            {
                RegisteredAtUtc = DateTime.UtcNow.ToTimestamp()
            });

            await ConfirmEventsAsync();
        }

        bool redeemResult = false;

        var registeredAtUtc = State.RegisteredAtUtc;
        Logger.LogWarning(
            $"State.RegisteredAtUtc {Id.ToString()} {registeredAtUtc?.ToString() ?? "null"}");

        if (registeredAtUtc == null)
        {
            Logger.LogWarning($"State.RegisteredAtUtc == null userId:{Id.ToString()}");
            redeemResult = false;
        }
        else
        {
            //show time
            var now = DateTime.UtcNow;
            var minutes = (now - registeredAtUtc.Value()).TotalMinutes;
            Logger.LogWarning(
                $"State.RegisteredAtUtc userId:{Id.ToString()} RegisteredAtUtc={registeredAtUtc.Value()} now={now} minutes={minutes}");
            //

            redeemResult =
                await userQuotaGAgent.RedeemInitialRewardAsync(Id.ToString(), registeredAtUtc.Value());
        }

        if (!redeemResult)
        {
            Logger.LogWarning(
                $"Failed to redeem initial reward for user {Id.ToString()} with code {inviteCode}. Eligibility check failed");
            return false;
        }

        // Step 2: If eligible, record the invitee in the inviter's grain.
        var inviterGuid = Guid.Parse(inviterId);
        var inviterGrain = await GetInvitationAgentAsync(inviterGuid);
        await inviterGrain.ProcessInviteeRegistrationAsync(Id.ToString());

        await SetInviterAsync(inviterGuid);

        return true;
    }

    public async Task<UserProfileDto> GetLastSessionUserProfileAsync()
    {
        var sessionInfo = State.SessionInfoList.LastOrDefault();
        if (sessionInfo == null || string.IsNullOrEmpty(sessionInfo.SessionId))
        {
            return new UserProfileDto();
        }

        var godChat = _clusterClient.GetGrain<IGodChat>(Guid.Parse(sessionInfo.SessionId));
        var userProfileDto = await godChat.GetUserProfileAsync();
        return userProfileDto ?? new UserProfileDto();
    }

    public async Task<Guid> SetInviterAsync(Guid inviterId)
    {
        RaiseEvent(new SetInviterEvent()
        {
            InviterId = inviterId.ToString()
        });

        await ConfirmEventsAsync();
        return Id;
    }

    public Task<Guid?> GetInviterAsync()
    {
        return Task.FromResult(State.InviterId.ToGuidNullable());
    }

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
                    state.RegisteredAtUtc = DateTime.UtcNow.ToTimestamp();
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
                state.Gender = string.Empty;
                state.BirthDate = null;
                state.BirthPlace = string.Empty;
                state.FullName = string.Empty;
                state.CurrentShareCount = 0;
                state.InviterId = "";
                state.VoiceLanguage = (int)VoiceLanguageEnum.Unset;
                break;
            case SetUserProfileEvent @setFortuneInfoEventLog:
                state.Gender = @setFortuneInfoEventLog.Gender;
                state.BirthDate = @setFortuneInfoEventLog.BirthDate;
                state.BirthPlace = @setFortuneInfoEventLog.BirthPlace;
                state.FullName = @setFortuneInfoEventLog.FullName;
                break;
            case SetVoiceLanguageEvent @setVoiceLanguageEventLog:
                // Update the voice language preference for the user
                state.VoiceLanguage = @setVoiceLanguageEventLog.VoiceLanguage;
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
            case SetInviterEvent setInviterEventLog:
                state.InviterId = setInviterEventLog.InviterId;
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
            case RegisterOrUpdateDeviceEvent registerDeviceEvent:
                // V1 compatibility: Keep V1 state but don't use in logic
                // Remove old token mapping if token changed
                if (!string.IsNullOrEmpty(registerDeviceEvent.OldPushToken))
                {
                    state.TokenToDeviceMap.Remove(registerDeviceEvent.OldPushToken);
                }

                // Update device info and token mapping
                state.UserDevices[registerDeviceEvent.DeviceId] = registerDeviceEvent.DeviceInfo;
                if (!string.IsNullOrEmpty(registerDeviceEvent.DeviceInfo.PushToken))
                {
                    state.TokenToDeviceMap[registerDeviceEvent.DeviceInfo.PushToken] = registerDeviceEvent.DeviceId;
                }

                break;
            case MarkDailyPushReadEvent markReadEvent:
                state.DailyPushReadStatus[markReadEvent.DateKey] = true;
                break;
            case CleanExpiredDevicesEvent cleanDevicesEvent:
                // Remove devices specified in the cleanup event
                foreach (var deviceIdToRemove in cleanDevicesEvent.DeviceIdsToRemove)
                {
                    if (state.UserDevices.ContainsKey(deviceIdToRemove))
                    {
                        var deviceToRemove = state.UserDevices[deviceIdToRemove];

                        // Remove token mapping if exists
                        if (!string.IsNullOrEmpty(deviceToRemove.PushToken))
                        {
                            state.TokenToDeviceMap.Remove(deviceToRemove.PushToken);
                        }

                        // Remove device from user devices
                        state.UserDevices.Remove(deviceIdToRemove);
                    }
                }

                Logger.LogDebug("🧹 Device cleanup completed: removed {RemovedCount} devices, reason: {CleanupReason}",
                    cleanDevicesEvent.RemovedCount, cleanDevicesEvent.CleanupReason);
                break;

            // === V2 Device Management Events ===
            case RegisterOrUpdateDeviceV2Event registerDeviceV2Event:
                // Update V2 device info and token mapping
                state.UserDevicesV2[registerDeviceV2Event.DeviceId] = registerDeviceV2Event.DeviceInfo;
                if (!string.IsNullOrEmpty(registerDeviceV2Event.DeviceInfo.PushToken))
                {
                    state.TokenToDeviceMapV2[registerDeviceV2Event.DeviceInfo.PushToken] =
                        registerDeviceV2Event.DeviceId;
                }

                // ✅ V2-only: No migration tracking needed
                break;

            // ✅ V2-only: Migration events removed - V1 and V2 are completely separate

            case CleanupDevicesV2Event cleanDevicesV2Event:
                // Remove V2 devices specified in the cleanup event
                foreach (var deviceIdToRemove in cleanDevicesV2Event.DeviceIdsToRemove)
                {
                    if (state.UserDevicesV2.ContainsKey(deviceIdToRemove))
                    {
                        var deviceToRemove = state.UserDevicesV2[deviceIdToRemove];

                        // Remove token mapping if exists
                        if (!string.IsNullOrEmpty(deviceToRemove.PushToken))
                        {
                            state.TokenToDeviceMapV2.Remove(deviceToRemove.PushToken);
                        }

                        // Remove device from V2 user devices
                        state.UserDevicesV2.Remove(deviceIdToRemove);

                        // ✅ V2-only: No migration tracking needed
                    }
                }

                Logger.LogDebug(
                    "🧹 V2 Device cleanup completed: removed {RemovedCount} devices, reason: {CleanupReason}",
                    cleanDevicesV2Event.RemovedCount, cleanDevicesV2Event.CleanupReason);
                break;
        }
    }

    /// <summary>
    /// Check and initialize first access status based on version history.
    /// Uses IsFirstConversation field to mark whether this is the first access to ChatManagerGAgent.
    /// If the field has a value, it means this is not the first access.
    /// </summary>
    private async Task<bool> CheckAndInitializeFirstAccessStatus()
    {
        // If IsFirstConversation is already set, no need to set it again
        if (State.IsFirstConversation != null)
        {
            return false;
        }

        // Use Version property to determine if this is a historical user or new user
        // Version > 0 means there are existing events, so it's a historical user
        // Version == 0 means no events yet, so it's a new user
        var isFirstAccess = GetCurrentVersion() == 0;
        var userId = Id;

        if (isFirstAccess)
        {
            // For new users: initialize all fields in one combined event
            RaiseEvent(new InitializeNewUserStatusEvent
            {
                IsFirstConversation = true,
                UserId = userId.ToString(),
                RegisteredAtUtc = DateTime.UtcNow.ToTimestamp(),
                MaxShareCount = 10000
            });
            await ConfirmEventsAsync();
            return true;
        }
        else
        {
            // For historical users: use separate events to maintain backward compatibility
            // Don't set RegisteredAtUtc and MaxShareCount for historical users here
            // as they should be handled by existing logic if needed
            RaiseEvent(new InitializeNewUserStatusEvent
            {
                IsFirstConversation = false,
                UserId = userId.ToString(),
                RegisteredAtUtc = null,
                MaxShareCount = 10000
            });
            await ConfirmEventsAsync();
            return false;
        }
    }

    protected override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Check and initialize first access status if needed
        var firstAccess = await CheckAndInitializeFirstAccessStatus();
        if (firstAccess)
        {
            // Record signup success event via OpenTelemetry
            var userId = Id.ToString();
            UserLifecycleTelemetryMetrics.RecordSignupSuccess(userId: userId, logger: Logger);
        }

        if (State.MaxShareCount == 0)
        {
            RaiseEvent(new SetMaxShareCountEvent
            {
                MaxShareCount = 10000
            });
        }
    }

    private async Task<ConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgent == null)
        {
            _configurationAgent = _agentFactory.CreateGAgent<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId());
            await _configurationAgent.ActivateAsync();
        }
        return _configurationAgent;
    }

    private async Task<InviteCodeGAgent> GetInviteCodeAgentAsync(Guid codeGrainId)
    {
        var agent = _agentFactory.CreateGAgent<InviteCodeGAgent>(codeGrainId);
        await agent.ActivateAsync();
        return agent;
    }

    private async Task<InvitationGAgent> GetInvitationAgentAsync(Guid userId)
    {
        var agent = _agentFactory.CreateGAgent<InvitationGAgent>(userId);
        await agent.ActivateAsync();
        return agent;
    }

    private async Task<PaymentIndexGAgent> GetPaymentIndexAgentAsync(Guid userId)
    {
        var agent = _agentFactory.CreateGAgent<PaymentIndexGAgent>(userId);
        await agent.ActivateAsync();
        return agent;
    }

    /// <summary>
    /// Get role-specific prompt from configuration based on role name
    /// </summary>
    /// <param name="roleName">The name of the role (e.g., "Doctor", "Teacher")</param>
    /// <returns>Role-specific prompt text or empty string if not found</returns>
    private string GetRolePrompt(string roleName)
    {
        try
        {
            var roleOptions =
                (_serviceProvider.GetService(typeof(IOptionsMonitor<RolePromptOptions>)) as
                    IOptionsMonitor<RolePromptOptions>)?.CurrentValue;
            var rolePrompt = roleOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;

            if (!string.IsNullOrEmpty(rolePrompt))
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] Found role prompt for: {roleName}");
            }
            else
            {
                Logger.LogDebug($"[ChatGAgentManager][GetRolePrompt] No role prompt found for: {roleName}");
            }

            return rolePrompt;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[ChatGAgentManager][GetRolePrompt] Failed to get role prompt for role: {RoleName}",
                roleName);
            return string.Empty;
        }
    }

    /// <summary>
    /// Sync subscription status from PaymentIndexGAgent to UserQuotaGAgent if needed
    /// This ensures all platform subscriptions are properly reflected in user quota
    /// </summary>
    private async Task SyncSubscriptionStatusIfNeeded(PaymentIndexGAgent paymentIndexAgent,
        IUserQuotaGAgent userQuotaGAgent, List<ActiveSubscription> activeSubscriptions)
    {
        try
        {
            if (!activeSubscriptions.Any())
            {
                Logger.LogDebug("[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] No active subscriptions to sync");
                return;
            }

            // Get current quota subscription status
            var quotaSubscription = await userQuotaGAgent.GetSubscriptionAsync(false);
            var quotaUltimateSubscription = await userQuotaGAgent.GetSubscriptionAsync(true);

            Logger.LogDebug(
                $"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Current quota subscription status - Premium: {quotaSubscription.IsActive}, Ultimate: {quotaUltimateSubscription.IsActive}");

            // Check if there's any mismatch between payment index and quota status
            bool needsSync = activeSubscriptions.Any() && !quotaSubscription.IsActive && !quotaUltimateSubscription.IsActive;

            if (needsSync)
            {
                Logger.LogInformation(
                    "[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Subscription status mismatch detected. Syncing...");

                // Sync the first active subscription (most recent by CreatedAt)
                var subscription = activeSubscriptions.OrderByDescending(s => s.CreatedAt).First();

                Logger.LogInformation(
                    $"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Found active subscription {subscription.PaymentId}, syncing to UserQuotaGAgent");

                // Default to non-ultimate subscription (business logic will determine this via events)
                // Note: In the new architecture, quota updates should happen via events from PaymentCompletedEvent handlers
                // This sync is a fallback for edge cases
                bool isUltimate = false;

                // Create subscription info to sync (use Month as default, actual value set by event handlers)
                var subscriptionToSync = new SubscriptionInfoDto
                {
                    IsActive = true,
                    PlanType = PlanType.Month, // Default - should be set by business event handlers
                    Status = Aevatar.Application.Grains.Common.Constants.PaymentStatus.Completed,
                    StartDate = subscription.CreatedAt,
                    EndDate = subscription.PeriodEnd,
                    SubscriptionIds = new List<string> { subscription.PaymentId },
                    InvoiceIds = new List<string>()
                };

                await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionToSync, isUltimate);
                Logger.LogInformation(
                    $"[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Successfully synced subscription to UserQuotaGAgent");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[ChatGAgentManager][SyncSubscriptionStatusIfNeeded] Error syncing subscription status");
            // Don't throw - this is a best-effort sync operation
        }
    }

    // === Daily Push Notification Methods ===

    // ❌ V1 INTERFACE REMOVED: Use RegisterOrUpdateDeviceV2Async instead

    public async Task MarkPushAsReadAsync(string deviceId)
    {
        // Check device registration switch
        var options =
            _serviceProvider.GetService(typeof(IOptionsMonitor<DailyPushOptions>)) as IOptionsMonitor<DailyPushOptions>;
        if (options != null && !options.CurrentValue.DeviceRegistrationEnabled)
        {
            Logger.LogInformation("Mark read disabled - returning mock success for device {DeviceId}", deviceId);
            return; // Return success but skip actual read status update
        }

        // ✅ V2 ONLY: Check if device exists in V2 structure
        if (State.UserDevicesV2.ContainsKey(deviceId))
        {
            var dateKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var date = DateOnly.FromDateTime(DateTime.UtcNow);

            // 1. Record user-level read status (existing logic)
            RaiseEvent(new MarkDailyPushReadEvent
            {
                DateKey = dateKey,
                ReadTime = DateTime.UtcNow.ToTimestamp()
            });

            await ConfirmEventsAsync();

            // 2. Record device-level read status in Redis (new logic)
            var deduplicationService = _serviceProvider.GetRequiredService<IPushDeduplicationService>();
            var deviceReadSuccess = await deduplicationService.MarkDeviceAsReadAsync(deviceId, date);

            if (deviceReadSuccess)
            {
                Logger.LogInformation(
                    $"Marked daily push as read for V2 device: {deviceId} on {dateKey} (user-level + device-level)");
            }
            else
            {
                Logger.LogWarning($"Failed to mark device-level read status for device: {deviceId} on {dateKey}");
            }
        }
        else
        {
            Logger.LogWarning($"V2 Device not found for provided deviceId: {deviceId}");
        }
    }

    public async Task<UserDeviceInfo?> GetDeviceStatusAsync(string deviceId)
    {
        // ✅ V2 ONLY: Convert V2 to V1 format for compatibility
        if (State.UserDevicesV2.TryGetValue(deviceId, out var deviceV2))
        {
            return new UserDeviceInfo
            {
                DeviceId = deviceV2.DeviceId,
                PushToken = deviceV2.PushToken,
                TimeZoneId = deviceV2.TimeZoneId,
                PushLanguage = deviceV2.PushLanguage,
                PushEnabled = deviceV2.PushEnabled,
                RegisteredAt = deviceV2.RegisteredAt?.ToDateTime() ?? DateTime.MinValue,
                LastTokenUpdate = deviceV2.LastTokenUpdate?.ToDateTime() ?? DateTime.MinValue
            };
        }

        return new UserDeviceInfo { PushEnabled = true }; // Default for non-existent devices
    }

    public async Task<bool> HasEnabledDeviceInTimezoneAsync(string timeZoneId)
    {
        // 🔄 Use unified interface to check all devices (V2 + V1)
        var allDevices = await GetUnifiedDevicesAsync();
        var enabledDevicesInTimezone = allDevices.Where(d => d.PushEnabled && d.TimeZoneId == timeZoneId).ToList();

        Logger.LogDebug(
            "HasEnabledDeviceInTimezoneAsync: TimeZone={TimeZone}, TotalDevices={Total}, EnabledInTimezone={Enabled}, DeviceIds=[{DeviceIds}]",
            timeZoneId, allDevices.Count, enabledDevicesInTimezone.Count,
            string.Join(", ", enabledDevicesInTimezone.Select(d => d.DeviceId)));

        return enabledDevicesInTimezone.Any();
    }

    /// <summary>
    /// Clean up expired, disabled, and duplicate devices to maintain data hygiene
    /// Similar to the session cleanup mechanism but for device data
    /// </summary>
    private async Task CleanupExpiredDevicesAsync()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);

        // 1. Find devices to remove based on age and status
        var devicesToRemove = new List<string>();

        // 🔥 V2-ONLY: Collect expired devices (30+ days old) from V2 structure
        var expiredDevices = State.UserDevicesV2.Values
            .Where(d => d.LastTokenUpdate.LessOrEqualThan(thirtyDaysAgo))
            .Select(d => d.DeviceId)
            .ToList();

        // 🔥 V2-ONLY: Collect long-disabled devices (7+ days disabled) from V2 structure
        var disabledDevices = State.UserDevicesV2.Values
            .Where(d => !d.PushEnabled && d.LastTokenUpdate.LessOrEqualThan(sevenDaysAgo))
            .Select(d => d.DeviceId)
            .ToList();

        // 🔥 V2-ONLY: Handle duplicates in V2 structure: same deviceId, keep the most recent one
        var duplicateGroups = State.UserDevicesV2.Values
            .GroupBy(d => d.DeviceId)
            .Where(g => g.Count() > 1)
            .ToList();

        var duplicatesToRemove = new List<string>();
        foreach (var group in duplicateGroups)
        {
            // Keep the most recent device (by LastTokenUpdate), remove others
            var devicesInGroup = group.OrderByDescending(d => d.LastTokenUpdate).ToList();
            var toRemove = devicesInGroup.Skip(1).Select(d => d.DeviceId).ToList();
            duplicatesToRemove.AddRange(toRemove);
        }

        // 3. Combine all devices to remove (ensure uniqueness)
        devicesToRemove.AddRange(expiredDevices);
        devicesToRemove.AddRange(disabledDevices);
        devicesToRemove.AddRange(duplicatesToRemove);
        devicesToRemove = devicesToRemove.Distinct().ToList();

        // 4. Trigger cleanup if there are devices to remove
        if (devicesToRemove.Count > 0)
        {
            var cleanupReasons = new List<string>();
            if (expiredDevices.Count > 0) cleanupReasons.Add($"{expiredDevices.Count} expired");
            if (disabledDevices.Count > 0) cleanupReasons.Add($"{disabledDevices.Count} disabled");
            if (duplicatesToRemove.Count > 0) cleanupReasons.Add($"{duplicatesToRemove.Count} duplicates");

            var reasonText = string.Join(", ", cleanupReasons);

            Logger.LogInformation("🧹 Cleaning up {Count} devices for user {UserId}: {Reasons}",
                devicesToRemove.Count, State.UserId, reasonText);

            var cleanEvent = new CleanExpiredDevicesEvent
            {
                CleanupTime = now.ToTimestamp(),
                CleanupReason = reasonText,
                RemovedCount = devicesToRemove.Count
            };
            cleanEvent.DeviceIdsToRemove.AddRange(devicesToRemove);
            RaiseEvent(cleanEvent);

            await ConfirmEventsAsync();
        }
    }

    /// <summary>
    /// Clean up expired daily push read status to prevent memory accumulation
    /// Keeps only current day and previous day's read status
    /// </summary>
    private async Task CleanupExpiredReadStatusAsync(DateTime currentDate)
    {
        try
        {
            var currentDateKey = currentDate.ToString("yyyy-MM-dd");
            var yesterdayDateKey = currentDate.AddDays(-1).ToString("yyyy-MM-dd");

            var keysToRemove = State.DailyPushReadStatus.Keys
                .Where(key => key != currentDateKey && key != yesterdayDateKey)
                .ToList();

            if (keysToRemove.Count > 0)
            {
                foreach (var key in keysToRemove)
                {
                    State.DailyPushReadStatus.Remove(key);
                }

                Logger.LogInformation(
                    "🧹 Cleaned up {Count} expired read status entries for user {UserId} (kept: {Current}, {Yesterday})",
                    keysToRemove.Count, State.UserId, currentDateKey, yesterdayDateKey);
            }

            await ConfirmEventsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup expired read status for user {UserId}", State.UserId);
        }
    }

    // Concurrent protection for daily push processing per user
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _pushSemaphores = new();

    public async Task ProcessDailyPushAsync(DateTime targetDate, List<DailyNotificationContent> contents,
        string timeZoneId, bool bypassReadStatusCheck = false, bool isRetryPush = false, bool isTestPush = false)
    {
        // 🔒 Acquire user-level semaphore to prevent concurrent push processing
        var userSemaphore = _pushSemaphores.GetOrAdd(State.UserId.ToGuid(), _ => new SemaphoreSlim(1, 1));

        await userSemaphore.WaitAsync();
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");

            Logger.LogDebug("🔒 Acquired push semaphore for user {UserId}, timezone {TimeZone}, pushType: {PushType}",
                State.UserId, timeZoneId, isRetryPush ? "retry" : "morning");

            // 🧹 Clean up expired read status (keep only today and yesterday)
            await CleanupExpiredReadStatusAsync(targetDate);


            // Check if any message has been read for today - if so, skip all pushes
            // Exception: bypass this check when explicitly requested (e.g., test mode main push)
            if (!bypassReadStatusCheck)
            {
                var hasAnyReadToday = State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                if (hasAnyReadToday)
                {
                    // 🔥 V2-ONLY: Get device info for logging from V2 structure
                    var devicesInTimezone = State.UserDevicesV2.Values
                        .Where(d => d.TimeZoneId == timeZoneId)
                        .Select(d => new
                        {
                            DeviceId = d.DeviceId,
                            PushToken = !string.IsNullOrEmpty(d.PushToken)
                                ? d.PushToken.Substring(0, Math.Min(8, d.PushToken.Length)) + "..."
                                : "EMPTY",
                            PushEnabled = d.PushEnabled
                        })
                        .ToList();

                    var deviceInfo = devicesInTimezone.Any()
                        ? string.Join(", ",
                            devicesInTimezone.Select(d =>
                                $"DeviceId:{d.DeviceId}|Token:{d.PushToken}|Enabled:{d.PushEnabled}"))
                        : "No devices in timezone";

                    Logger.LogInformation(
                        "At least one daily push already read for {DateKey}, skipping all pushes - UserId: {UserId}, TimeZone: {TimeZone}, Devices: [{DeviceInfo}]",
                        dateKey, State.UserId, timeZoneId, deviceInfo);
                    return;
                }
            }
            else
            {
                Logger.LogInformation("Bypassing read status check for daily push on {DateKey}", dateKey);
            }

            // Only process devices in the specified timezone with pushToken deduplication
            // 🔄 Use unified interface to get all devices (V2 prioritized, V1 fallback)
            var enabledDevicesRaw = await GetUnifiedDevicesAsync();
            var enabledDevicesFiltered = enabledDevicesRaw
                .Where(d => d.PushEnabled && d.TimeZoneId == timeZoneId && !string.IsNullOrEmpty(d.PushToken))
                .ToList();

            // 🎯 CRITICAL FIX: Deduplicate by deviceId first, then by pushToken
            // This prevents the same physical device from being processed multiple times
            // even if it has different pushTokens (e.g., after user switching)
            var enabledDevices = enabledDevicesFiltered
                .GroupBy(d => d.DeviceId) // 🔧 Primary deduplication by deviceId
                .Select(deviceGroup =>
                    deviceGroup.OrderByDescending(d => d.LastTokenUpdate).First()) // Keep latest record for the device
                .ToList();

            var duplicateCount = enabledDevicesFiltered.Count - enabledDevices.Count;
            if (duplicateCount > 0)
            {
                Logger.LogDebug(
                    "Device deduplication: removed {DuplicateCount} duplicate devices (by deviceId + pushToken)",
                    duplicateCount);
            }

            Logger.LogInformation("Found {DeviceCount} enabled devices in timezone {TimeZone} for user {UserId}",
                enabledDevices.Count, timeZoneId, State.UserId);

            if (enabledDevices.Count == 0)
            {
                Logger.LogWarning(
                    "No enabled devices for daily push - User {UserId}, TimeZone {TimeZone}, Total V2 devices: {TotalDevices}",
                    State.UserId, timeZoneId, State.UserDevicesV2.Count);
                return;
            }

            // Get Global JWT Provider (new architecture - single instance for entire system)
            var globalJwtProvider = await GetGlobalJwtProviderAgentAsync();

            // Get Firebase project configuration via FirebaseService
            var firebaseService = _serviceProvider.GetService(typeof(FirebaseService)) as FirebaseService;
            if (firebaseService == null)
            {
                Logger.LogError("FirebaseService not available for push notifications");
                return;
            }

            var projectId = firebaseService.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                Logger.LogError("Firebase ProjectId not configured in FirebaseService for push notifications");
                return;
            }

            var successCount = 0;
            var failureCount = 0;

            // 🎯 Redis-based deduplication for push notifications
            var eligibleDevices = new List<UserDeviceInfo>();
            var deduplicationService =
                _serviceProvider.GetService(typeof(IPushDeduplicationService)) as IPushDeduplicationService;
            var date = DateOnly.FromDateTime(targetDate);

            if (deduplicationService == null)
            {
                Logger.LogWarning("IPushDeduplicationService not available, proceeding without deduplication");
                eligibleDevices = enabledDevices; // Fallback: no deduplication
            }
            else
            {
                foreach (var device in enabledDevices)
                {
                    bool canSend;

                    if (isTestPush)
                    {
                        canSend = true; // Test pushes skip deduplication
                        Logger.LogInformation("Device ELIGIBLE (test): {DeviceId} in {TimeZone}", device.DeviceId,
                            timeZoneId);
                    }
                    else if (isRetryPush)
                    {
                        // Check both user-level and device-level read status
                        var userRead = !bypassReadStatusCheck &&
                                       State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                        var deviceRead = false;

                        if (!bypassReadStatusCheck && !userRead)
                        {
                            // Only check device-level read status if user-level is not read
                            deviceRead = await deduplicationService.IsDeviceReadAsync(device.DeviceId, date);
                        }

                        if (userRead || deviceRead)
                        {
                            canSend = false;
                            var readType = userRead ? "user-level" : "device-level";
                            Logger.LogInformation(
                                "Device BLOCKED (retry, already read - {ReadType}): {DeviceId} in {TimeZone}, read on {DateKey}",
                                readType, device.DeviceId, timeZoneId, dateKey);
                        }
                        else
                        {
                            canSend = await deduplicationService.TryClaimRetryPushAsync(device.DeviceId, date,
                                timeZoneId);
                            Logger.LogInformation("Device {Status} (retry): {DeviceId} in {TimeZone}",
                                canSend ? "ELIGIBLE" : "BLOCKED", device.DeviceId, timeZoneId);
                        }
                    }
                    else
                    {
                        canSend = await deduplicationService.TryClaimMorningPushAsync(device.DeviceId, date,
                            timeZoneId);
                        Logger.LogInformation("Device {Status} (morning): {DeviceId} in {TimeZone}",
                            canSend ? "ELIGIBLE" : "BLOCKED", device.DeviceId, timeZoneId);
                    }

                    if (canSend)
                    {
                        eligibleDevices.Add(device);
                    }
                }
            }

            if (eligibleDevices.Count == 0)
            {
                Logger.LogInformation(
                    "No eligible devices for daily push after Redis deduplication - User {UserId}, TimeZone {TimeZone}, " +
                    "Original devices: {OriginalCount}, PushType: {PushType}",
                    State.UserId, timeZoneId, enabledDevices.Count, isRetryPush ? "retry" : "morning");
                return;
            }

            Logger.LogInformation("Proceeding with {EligibleCount} eligible devices for user {UserId}",
                eligibleDevices.Count, State.UserId);

            // Create device-level push tasks with rollback support
            var deviceTasks = eligibleDevices.Select(async (device, deviceIndex) =>
            {
                var deviceSuccess = false;

                try
                {
                    // 🎯 Add device-level delay to reduce concurrent JWT requests
                    var deviceDelay =
                        Random.Shared.Next(50, 300) + (deviceIndex * 150); // Random + staggered per device
                    if (deviceDelay > 0)
                    {
                        await Task.Delay(deviceDelay);
                    }

                    // Send only one content based on push type: first for morning, second for afternoon retry
                    var contentIndex = isRetryPush ? 1 : 0;
                    var content = contents.ElementAtOrDefault(contentIndex);

                    if (content == null)
                    {
                        Logger.LogWarning("No content available for {PushType} push - Device: {DeviceId}",
                            isRetryPush ? "afternoon retry" : "morning", device.DeviceId);
                        deviceSuccess = false;
                    }
                    else
                    {
                        try
                        {
                            var availableLanguages = string.Join(", ", content.LocalizedContents.Keys);
                            Logger.LogInformation(
                                "Language selection: DeviceId={DeviceId}, RequestedLanguage='{PushLanguage}', AvailableLanguages=[{AvailableLanguages}]",
                                device.DeviceId, device.PushLanguage, availableLanguages);

                            var localizedContent = content.GetLocalizedContent(device.PushLanguage);

                            Logger.LogInformation(
                                "📬 PUSH ATTEMPT: User {UserId}, DeviceId {DeviceId}, Content {ContentIndex}/{Total}, Title '{Title}', ContentId {ContentId}, PushToken {TokenPrefix}...",
                                State.UserId, device.DeviceId, contentIndex + 1, contents.Count, localizedContent.Title,
                                content.Id,
                                device.PushToken.Substring(0, Math.Min(8, device.PushToken.Length)));

                            // Create unique data payload for this content
                            var messageId = Guid.NewGuid();
                            var pushData = new Dictionary<string, object>
                            {
                                ["message_id"] = messageId.ToString(),
                                ["type"] = (int)DailyPushConstants.PushType.DailyPush,
                                ["date"] = dateKey,
                                ["content_id"] = content.Id,
                                ["content_index"] = contentIndex + 1,
                                ["device_id"] = device.DeviceId,
                                ["total_contents"] = contents.Count,
                                ["timezone"] = timeZoneId,
                                ["is_retry"] = isRetryPush,
                                ["is_test_push"] = isTestPush
                            };

                            // Use new global JWT architecture with direct HTTP push
                            // Skip deduplication since device already passed UTC-based pre-check
                            var success = await SendDirectPushNotificationAsync(
                                globalJwtProvider,
                                projectId,
                                device.PushToken,
                                localizedContent.Title,
                                localizedContent.Content,
                                pushData,
                                timeZoneId,
                                isRetryPush,
                                contentIndex == 0, // isFirstContent
                                false, // isTestPush
                                true); // skipDeduplicationCheck

                            if (success)
                            {
                                Logger.LogInformation(
                                    "Daily push sent successfully: DeviceId={DeviceId}, MessageId={MessageId}, ContentIndex={ContentIndex}/{TotalContents}, Date={Date}",
                                    device.DeviceId, messageId, contentIndex + 1, contents.Count, dateKey);
                                deviceSuccess = true;
                            }
                            else
                            {
                                Logger.LogWarning(
                                    "Failed to send daily push: DeviceId={DeviceId}, MessageId={MessageId}, ContentIndex={ContentIndex}/{TotalContents}, Date={Date}",
                                    device.DeviceId, messageId, contentIndex + 1, contents.Count, dateKey);
                                deviceSuccess = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex,
                                $"Error sending daily push content {contentIndex + 1} to device {device.DeviceId}");
                            deviceSuccess = false;
                        }
                    }

                    Logger.LogInformation("Device push summary: DeviceId={DeviceId}, Success={DeviceSuccess}",
                        device.DeviceId, deviceSuccess);

                    return new[] { deviceSuccess };
                }
                finally
                {
                    // 🔄 Rollback Redis claim if ALL pushes for this device failed
                    if (!deviceSuccess && deduplicationService != null)
                    {
                        try
                        {
                            await deduplicationService.ReleasePushClaimAsync(device.DeviceId, date, timeZoneId,
                                isRetryPush);
                            Logger.LogInformation(
                                "🔄 Released Redis claim for failed device: {DeviceId} (all {ContentCount} pushes failed)",
                                device.DeviceId, contents.Count);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Failed to release Redis claim for device {DeviceId}", device.DeviceId);
                        }
                    }
                }
            });

            // Flatten device results to individual push results for compatibility
            var allDeviceResults = await Task.WhenAll(deviceTasks);
            var results = allDeviceResults.SelectMany(results => results).ToArray();

            var totalPushTasks = results.Length;
            Logger.LogInformation(
                "ProcessDailyPushAsync: Executing {TotalPushTasks} push tasks for {DeviceCount} devices × {ContentCount} contents",
                totalPushTasks, enabledDevices.Count, contents.Count);

            successCount = results.Count(r => r);
            failureCount = results.Count(r => !r);

            Logger.LogInformation(
                "ProcessDailyPushAsync Summary - User {UserId}: {SuccessCount} success, {FailureCount} failures for {DeviceCount} devices. Individual pushes: {TotalPushes}",
                State.UserId, successCount, failureCount, enabledDevices.Count, results.Length);
        }
        finally
        {
            userSemaphore.Release();
            Logger.LogDebug("🔓 Released push semaphore for user {UserId}, timezone {TimeZone}",
                State.UserId, timeZoneId);
        }
    }

    public async Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate)
    {
        var dateKey = targetDate.ToString("yyyy-MM-dd");
        var isRead = State.DailyPushReadStatus.TryGetValue(dateKey, out var readStatus) && readStatus;

        // 🔄 Use unified interface for consistency with HasEnabledDeviceInTimezoneAsync
        var allDevices = await GetUnifiedDevicesAsync();
        var hasEnabledDevices = allDevices.Any(d => d.PushEnabled);
        var shouldSend = !isRead && hasEnabledDevices;

        Logger.LogDebug(
            "ShouldSendAfternoonRetryAsync: DateKey={DateKey}, IsRead={IsRead}, HasEnabledDevices={HasEnabledDevices}, ShouldSend={ShouldSend}, ReadStatusEntries={ReadEntries}, TotalDevices={TotalDevices}",
            dateKey, isRead, hasEnabledDevices, shouldSend, State.DailyPushReadStatus.Count, allDevices.Count);

        return shouldSend;
    }

    public async Task<object> GetPushDebugInfoAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var deduplicationService =
            _serviceProvider.GetService(typeof(IPushDeduplicationService)) as IPushDeduplicationService;
        var readKey = date.ToString("yyyy-MM-dd");

        // Get Redis deduplication status
        PushDeduplicationStatus? redisStatus = null;
        if (deduplicationService != null)
        {
            try
            {
                redisStatus = await deduplicationService.GetStatusAsync(deviceId, date, timeZoneId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to get Redis deduplication status for device {DeviceId}", deviceId);
            }
        }

        // Get State information
        var stateInfo = new
        {
            IsRead = State.DailyPushReadStatus.TryGetValue(readKey, out var isRead) && isRead,
            ReadKey = readKey,
            HasDevice = State.UserDevicesV2.ContainsKey(deviceId),
            DeviceInfo = State.UserDevicesV2.TryGetValue(deviceId, out var deviceV2)
                ? new
                {
                    DeviceId = deviceV2.DeviceId,
                    PushToken = !string.IsNullOrEmpty(deviceV2.PushToken)
                        ? deviceV2.PushToken.Substring(0, Math.Min(12, deviceV2.PushToken.Length)) + "..."
                        : "EMPTY",
                    PushEnabled = deviceV2.PushEnabled,
                    TimeZoneId = deviceV2.TimeZoneId,
                    Language = deviceV2.PushLanguage,
                    RegisteredAt = deviceV2.RegisteredAt.ToFormattedString("yyyy-MM-dd HH:mm:ss"),
                    LastTokenUpdate = deviceV2.LastTokenUpdate.ToFormattedString("yyyy-MM-dd HH:mm:ss"),
                    Platform = deviceV2.Platform,
                    Status = deviceV2.Status
                }
                : null
        };

        return new
        {
            UserId = State.UserId,
            DeviceId = deviceId,
            Date = date.ToString("yyyy-MM-dd"),
            TimeZoneId = timeZoneId,
            Redis = redisStatus != null
                ? (object)new
                {
                    MorningSent = redisStatus.MorningSent,
                    RetrySent = redisStatus.RetrySent,
                    MorningKey = redisStatus.MorningKey,
                    RetryKey = redisStatus.RetryKey,
                    MorningSentTime = redisStatus.MorningSentTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    RetrySentTime = redisStatus.RetrySentTime?.ToString("yyyy-MM-dd HH:mm:ss")
                }
                : new { Error = "Redis service not available" },
            State = stateInfo,
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    // ❌ V1 METHOD REMOVED: Use GetAllDevicesV2Async or GetUnifiedDevicesAsync instead


    public async Task UpdateTimezoneIndexAsync(string? oldTimeZone, string newTimeZone)
    {
        try
        {
            // Remove user from old timezone index
            if (!string.IsNullOrEmpty(oldTimeZone))
            {
                var oldIndexGAgent =
                    await GetPushSubscriberIndexAgentAsync(DailyPushConstants.TimezoneToGuid(oldTimeZone));
                await oldIndexGAgent.InitializeAsync(oldTimeZone);
                await oldIndexGAgent.RemoveUserFromTimezoneAsync(State.UserId.ToGuid());
                Logger.LogDebug($"Removed user {State.UserId} from timezone index: {oldTimeZone}");
            }

            // Add user to new timezone index
            if (!string.IsNullOrEmpty(newTimeZone))
            {
                var newIndexGAgent =
                    await GetPushSubscriberIndexAgentAsync(DailyPushConstants.TimezoneToGuid(newTimeZone));
                await newIndexGAgent.InitializeAsync(newTimeZone);
                await newIndexGAgent.AddUserToTimezoneAsync(State.UserId.ToGuid());
                Logger.LogDebug($"Added user {State.UserId} to timezone index: {newTimeZone}");

                // CRITICAL: Complete timezone ecosystem initialization
                // This ensures ALL timezone-related agents are ready for immediate daily push operation
                await InitializeTimezoneEcosystemAsync(newTimeZone);
            }

            Logger.LogInformation($"Updated timezone index for user {State.UserId}: {oldTimeZone} -> {newTimeZone}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                $"Failed to update timezone index for user {State.UserId}: {oldTimeZone} -> {newTimeZone}");
        }
    }

    /// <summary>
    /// Complete timezone ecosystem initialization - ensures ALL timezone-related agents are ready
    /// This method guarantees immediate push system availability when user switches to new timezone
    /// </summary>
    private async Task InitializeTimezoneEcosystemAsync(string newTimeZone)
    {
        // 🛡️ SAFETY: Validate timezone before any Grain operations
        if (string.IsNullOrWhiteSpace(newTimeZone))
        {
            Logger.LogWarning(
                "Empty or invalid timezone ID '{TimeZone}' - skipping timezone ecosystem initialization to prevent orphaned Grains",
                newTimeZone ?? "null");
            return;
        }

        try
        {
            // Validate timezone format before creating any Grains
            TimeZoneInfo.FindSystemTimeZoneById(newTimeZone);
            Logger.LogInformation("Starting complete timezone ecosystem initialization for {TimeZone}", newTimeZone);

            // Step 1: Initialize DailyContentGAgent timezone mapping (global content service)
            var contentGAgent = await GetDailyContentAgentAsync();
            await contentGAgent.RegisterTimezoneGuidMappingAsync(DailyPushConstants.TimezoneToGuid(newTimeZone),
                newTimeZone);
            Logger.LogDebug("DailyContentGAgent timezone mapping registered for {TimeZone}", newTimeZone);

            // Step 2: Initialize and fully activate DailyPushCoordinatorGAgent
            var coordinatorGAgent =
                _clusterClient.GetGrain<IDailyPushCoordinatorGAgent>(DailyPushConstants.TimezoneToGuid(newTimeZone));

            // Force complete initialization and activation
            await coordinatorGAgent.InitializeAsync(newTimeZone);

            // 🔥 CRITICAL: Force grain to stay active by calling multiple methods
            var status = await coordinatorGAgent.GetStatusAsync();

            Logger.LogInformation(
                "DailyPushCoordinatorGAgent fully initialized for {TimeZone}. Status: {Status}, ReminderTargetId: {TargetId}",
                newTimeZone, status.Status, status.ReminderTargetId);

            // Step 3: Validate reminders are registered (if authorized)
            if (status.ReminderTargetId != Guid.Empty)
            {
                Logger.LogInformation("Daily push reminders are ready for {TimeZone} with authorized ReminderTargetId",
                    newTimeZone);
            }
            else
            {
                Logger.LogWarning(
                    "ReminderTargetId is empty for {TimeZone} - daily pushes may not work until properly configured",
                    newTimeZone);
            }

            // Step 4: Pre-warm timezone calculations to ensure immediate readiness
            var timezoneInfo = TimeZoneInfo.FindSystemTimeZoneById(newTimeZone);
            var currentUtc = DateTime.UtcNow;
            var currentLocal = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timezoneInfo);

            Logger.LogInformation(
                "Timezone ecosystem fully initialized for {TimeZone}. Current local time: {LocalTime} (UTC: {UtcTime})",
                newTimeZone, currentLocal.ToString("yyyy-MM-dd HH:mm:ss"), currentUtc.ToString("yyyy-MM-dd HH:mm:ss"));

            // Step 5: Final verification - ensure all components are responsive
            var verificationTasks = new Task[]
            {
                contentGAgent.GetTimezoneFromGuidAsync(DailyPushConstants.TimezoneToGuid(newTimeZone)),
                coordinatorGAgent.GetStatusAsync()
            };

            await Task.WhenAll(verificationTasks);
            Logger.LogInformation("Timezone ecosystem verification completed for {TimeZone} - all agents responsive",
                newTimeZone);
        }
        catch (TimeZoneNotFoundException ex)
        {
            Logger.LogError(ex, "Invalid timezone ID '{TimeZone}' - timezone ecosystem initialization failed",
                newTimeZone);
            throw new ArgumentException($"Invalid timezone ID: {newTimeZone}", nameof(newTimeZone), ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Timezone ecosystem initialization failed for {TimeZone} - daily pushes may not work properly",
                newTimeZone);
            // Don't rethrow - allow timezone index update to succeed even if ecosystem init partially fails
        }
    }

    /// <summary>
    /// Send push notification directly to Firebase FCM API using Global JWT Provider
    /// This replaces the FirebaseService architecture with a more efficient global JWT approach
    /// </summary>
    private async Task<bool> SendDirectPushNotificationAsync(
        GlobalJwtProviderGAgent globalJwtProvider,
        string projectId,
        string pushToken,
        string title,
        string content,
        Dictionary<string, object>? data = null,
        string timeZoneId = "UTC",
        bool isRetryPush = false,
        bool isFirstContent = true,
        bool isTestPush = false,
        bool skipDeduplicationCheck = false)
    {
        try
        {
            if (string.IsNullOrEmpty(pushToken))
            {
                Logger.LogWarning("Push token is empty");
                return false;
            }

            // Deduplication removed - all pushes are allowed

            // Get JWT access token from global provider
            var accessToken = await globalJwtProvider.GetFirebaseAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                Logger.LogError("Failed to obtain access token from Global JWT Provider");
                return false;
            }

            // Create FCM v1 payload
            var dataPayload = CreateDataPayload(data);
            var message = new
            {
                message = new
                {
                    token = pushToken,
                    notification = new
                    {
                        title = title,
                        body = content
                    },
                    data = dataPayload,
                    android = new
                    {
                        priority = "high",
                        notification = new
                        {
                            sound = "default",
                            channel_id = "daily_push_channel",
                            notification_count = dataPayload.TryGetValue("content_index", out var contentIndex)
                                ? int.Parse(contentIndex.ToString() ?? "1")
                                : 1
                        }
                    },
                    apns = new
                    {
                        headers = new
                        {
                            apns_push_type = "alert"
                        },
                        payload = new
                        {
                            aps = new
                            {
                                sound = "default"
                            }
                        }
                    }
                }
            };

            // Send HTTP request to Firebase FCM API
            var httpClient = _serviceProvider.GetService(typeof(HttpClient)) as HttpClient;
            if (httpClient == null)
            {
                Logger.LogError("HttpClient not available for push notification");
                return false;
            }

            var fcmEndpoint = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";
            var jsonPayload = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, MediaTypeNames.Application.Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, fcmEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = httpContent;

            Logger.LogDebug("Sending direct FCM push to token: {TokenPrefix}...",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            // 📱 Log FCM response for debugging (Information level for visibility)
            Logger.LogInformation("📱 FCM Response: {StatusCode} - Body: {ResponseContent}",
                response.StatusCode, responseContent);

            if (response.IsSuccessStatusCode)
            {
                // ✅ CRITICAL FIX: Parse FCM response to check for actual success/failure
                // HTTP 200 doesn't guarantee push success - FCM may return errors in response body
                try
                {
                    using var jsonDocument = JsonDocument.Parse(responseContent);
                    var root = jsonDocument.RootElement;

                    // Check if FCM returned an error despite 200 status
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errorCode = errorElement.TryGetProperty("code", out var codeElement)
                            ? codeElement.GetString()
                            : "UNKNOWN";
                        var errorMessage = errorElement.TryGetProperty("message", out var msgElement)
                            ? msgElement.GetString()
                            : "Unknown error";

                        Logger.LogError(
                            "🚨 FCM returned error despite 200 status - Code: {ErrorCode}, Message: {ErrorMessage}, Token: {TokenPrefix}..., Title: '{Title}'",
                            errorCode, errorMessage,
                            pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);

                        // Handle specific FCM errors for token cleanup
                        if (errorCode == "UNREGISTERED" || errorCode == "INVALID_ARGUMENT")
                        {
                            Logger.LogWarning(
                                "❌ Push token is invalid - initiating automatic device cleanup. Token: {TokenPrefix}..., ErrorCode: {ErrorCode}",
                                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, errorCode);

                            // 🎯 自动清理失效设备
                            await MarkDeviceTokenAsInvalidAsync(pushToken, errorCode);
                        }

                        return false; // ❌ Actual failure despite 200 status
                    }

                    // Check for success indicator (message name in response)
                    if (root.TryGetProperty("name", out var nameElement))
                    {
                        var messageName = nameElement.GetString();
                        Logger.LogInformation(
                            "✅ Push notification sent successfully - Message: {MessageName}, Token: {TokenPrefix}..., Title: '{Title}'",
                            messageName,
                            pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                        return true; // ✅ Confirmed success
                    }

                    // Unexpected response format with 200 status
                    Logger.LogWarning(
                        "⚠️ FCM returned 200 but unexpected response format: {ResponseContent}, Token: {TokenPrefix}..., Title: '{Title}'",
                        responseContent,
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return false; // Treat unexpected format as failure
                }
                catch (JsonException ex)
                {
                    Logger.LogError(ex, "Failed to parse FCM response JSON: {ResponseContent}", responseContent);
                    // For unparseable responses with 200 status, assume success for backward compatibility
                    Logger.LogInformation(
                        "⚠️ Push assumed successful due to 200 status (unparseable response) - Token: {TokenPrefix}..., Title: '{Title}'",
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return true;
                }
            }
            else
            {
                Logger.LogError(
                    "❌ FCM request failed with status {StatusCode}: {ResponseContent}, Token: {TokenPrefix}..., Title: '{Title}'",
                    response.StatusCode, responseContent,
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);

                // Handle specific FCM v1 errors for token cleanup
                if (responseContent.Contains("UNREGISTERED") || responseContent.Contains("INVALID_ARGUMENT"))
                {
                    Logger.LogWarning(
                        "❌ Token is invalid - initiating automatic device cleanup. Token: {TokenPrefix}..., Status: {StatusCode}",
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, response.StatusCode);

                    // 🎯 自动清理失效设备
                    var errorCode = responseContent.Contains("UNREGISTERED") ? "UNREGISTERED" : "INVALID_ARGUMENT";
                    await MarkDeviceTokenAsInvalidAsync(pushToken, errorCode);
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in direct push notification: {ErrorMessage}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Create data payload for FCM message, converting all values to strings
    /// </summary>
    private static Dictionary<string, string> CreateDataPayload(Dictionary<string, object>? data)
    {
        var dataPayload = new Dictionary<string, string>();
        if (data != null)
        {
            foreach (var kvp in data)
            {
                dataPayload[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }

        return dataPayload;
    }

    // === Enhanced Device Management V2 Methods ===

    /// <summary>
    /// Register or update device using V2 structure (enhanced version)
    /// All new device registrations should use this method
    /// </summary>
    public async Task<bool> RegisterOrUpdateDeviceV2Async(string deviceId, string pushToken, string timeZoneId,
        bool? pushEnabled, string pushLanguage, string? platform = null, string? appVersion = null)
    {
        // Check device registration switch
        var options =
            _serviceProvider.GetService(typeof(IOptionsMonitor<DailyPushOptions>)) as IOptionsMonitor<DailyPushOptions>;
        if (options != null && !options.CurrentValue.DeviceRegistrationEnabled)
        {
            Logger.LogInformation("Device registration disabled - returning mock success for device {DeviceId}",
                deviceId);
            return true; // Return success but skip actual registration
        }

        var isNewDevice = !State.UserDevicesV2.ContainsKey(deviceId);
        var now = DateTime.UtcNow;

        // 🔄 V2 ONLY: No migration from V1, V2 uses completely new data
        var deviceInfo = isNewDevice
            ? new UserDeviceInfoV2()
            : State.UserDevicesV2[deviceId].FromProto();

        // Store old values for cleanup and change detection
        var oldPushToken = deviceInfo.PushToken;
        var oldTimeZone = isNewDevice ? null : deviceInfo.TimeZoneId;
        var tokenChanged = oldPushToken != pushToken;
        var timeZoneChanged = oldTimeZone != timeZoneId;

        // Update device information
        deviceInfo.DeviceId = deviceId;
        deviceInfo.UserId = State.UserId.ToGuid();
        deviceInfo.PushToken = pushToken;
        deviceInfo.TimeZoneId = timeZoneId;
        deviceInfo.PushLanguage = pushLanguage;
        deviceInfo.LastActiveAt = now;
        deviceInfo.Status = DeviceStatus.Active;
        deviceInfo.ConsecutiveFailures = 0; // Reset on successful registration

        if (pushEnabled.HasValue)
        {
            deviceInfo.PushEnabled = pushEnabled.Value;
        }

        if (!string.IsNullOrEmpty(platform))
        {
            deviceInfo.Platform = platform;
        }

        if (!string.IsNullOrEmpty(appVersion))
        {
            deviceInfo.AppVersion = appVersion;
        }

        // Set timestamps
        if (isNewDevice)
        {
            deviceInfo.RegisteredAt = now;
            deviceInfo.LastTokenUpdate = now;
        }
        else if (tokenChanged)
        {
            deviceInfo.LastTokenUpdate = now;

            // Add old token to history
            if (!string.IsNullOrEmpty(oldPushToken))
            {
                deviceInfo.PushTokenHistory.Add(new HistoricalPushToken
                {
                    Token = oldPushToken,
                    UsedFrom = deviceInfo.LastTokenUpdate,
                    UsedUntil = now,
                    ReplacementReason = "token_refresh"
                });

                // Keep only recent history (last 5 tokens)
                if (deviceInfo.PushTokenHistory.Count > 5)
                {
                    deviceInfo.PushTokenHistory = deviceInfo.PushTokenHistory
                        .OrderByDescending(h => h.UsedUntil ?? DateTime.MaxValue)
                        .Take(5)
                        .ToList();
                }
            }
        }

        // Raise V2 event
        RaiseEvent(new RegisterOrUpdateDeviceV2Event
        {
            DeviceId = deviceId,
            DeviceInfo = deviceInfo.ToProto(),
            IsNewDevice = isNewDevice,
            OldPushToken = tokenChanged ? oldPushToken : null,
            IsMigration = false
        });

        await ConfirmEventsAsync();

        // 🎯 CRITICAL: Update timezone index for V2 devices  
        // This ensures the user appears in PushSubscriberIndexGAgent for coordinated push
        if (isNewDevice || timeZoneChanged)
        {
            await UpdateTimezoneIndexAsync(oldTimeZone, timeZoneId);
            Logger.LogInformation("🌍 V2 Device timezone index updated: User {UserId} from {OldTZ} to {NewTZ}",
                State.UserId, oldTimeZone ?? "none", timeZoneId);
        }

        Logger.LogInformation("📱 V2 Device {Action}: DeviceId={DeviceId}, PushToken={TokenPrefix}..., " +
                              "TimeZone={TimeZone}, Language={Language}, Enabled={Enabled}, Platform={Platform}",
            isNewDevice ? "registered" : "updated", deviceId,
            pushToken.Substring(0, Math.Min(8, pushToken.Length)),
            timeZoneId, pushLanguage, deviceInfo.PushEnabled, platform ?? "unknown");

        return true;
    }


    /// <summary>
    /// Get unified device list (V2 + migrated V1 devices)
    /// This method provides a unified view for push processing
    /// </summary>
    public async Task<List<UserDeviceInfoV2>> GetAllDevicesV2Async()
    {
        // ✅ SIMPLIFIED: V2 devices only - no migration logic
        // V1 and V2 are completely separate data sources
        return State.UserDevicesV2.Values.Select(p => p.FromProto()).ToList();
    }


    // === Coordinated Push Methods ===

    /// <summary>
    /// Get devices for coordinated push (called by DailyPushCoordinatorGAgent)
    /// Returns device information without executing push
    /// </summary>
    public async Task<List<UserDeviceInfo>> GetDevicesForCoordinatedPushAsync(string timeZoneId, DateTime targetDate)
    {
        try
        {
            // Use unified device source (V2 + V1 fallback)
            var allDevices = await GetUnifiedDevicesAsync();

            // Filter devices for the specified timezone and enabled status
            var targetDevices = allDevices
                .Where(d => d.TimeZoneId == timeZoneId && d.PushEnabled)
                .ToList();

            Logger.LogDebug("📋 Collected {DeviceCount} devices for coordinated push in {TimeZone} for user {UserId}",
                targetDevices.Count, timeZoneId, State.UserId);

            return targetDevices;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get devices for coordinated push - User: {UserId}, TimeZone: {TimeZone}",
                State.UserId, timeZoneId);
            return new List<UserDeviceInfo>();
        }
    }

    /// <summary>
    /// Execute coordinated push for a specific device (called by coordinator after device selection)
    /// Uses existing Redis deduplication logic
    /// </summary>
    public async Task<bool> ExecuteCoordinatedPushAsync(UserDeviceInfo device, DateTime targetDate,
        List<DailyNotificationContent> contents, bool isRetryPush = false, bool isTestPush = false)
    {
        try
        {
            Logger.LogInformation(
                "🎯 Executing coordinated push for device {DeviceId}, User: {UserId}, Retry: {IsRetry}, Test: {IsTest}",
                device.DeviceId, State.UserId, isRetryPush, isTestPush);

            // Use existing Redis deduplication logic
            var deduplicationService =
                _serviceProvider.GetService(typeof(IPushDeduplicationService)) as IPushDeduplicationService;
            var date = DateOnly.FromDateTime(targetDate);

            bool canSend = true; // Default for test pushes

            if (!isTestPush && deduplicationService != null)
            {
                if (isRetryPush)
                {
                    // Check both user-level and device-level read status
                    var dateKey = date.ToString("yyyy-MM-dd");
                    var userRead = State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                    var deviceRead = false;

                    if (!userRead)
                    {
                        // Only check device-level read status if user-level is not read
                        deviceRead = await deduplicationService.IsDeviceReadAsync(device.DeviceId, date);
                    }

                    if (userRead || deviceRead)
                    {
                        var readType = userRead ? "user-level" : "device-level";
                        Logger.LogInformation("📖 Device {DeviceId} already read ({ReadType}) - skipping retry push",
                            device.DeviceId, readType);
                        return false;
                    }

                    canSend = await deduplicationService.TryClaimRetryPushAsync(device.DeviceId, date,
                        device.TimeZoneId);
                }
                else
                {
                    canSend = await deduplicationService.TryClaimMorningPushAsync(device.DeviceId, date,
                        device.TimeZoneId);
                }

                Logger.LogInformation("🔑 Redis deduplication result for {DeviceId}: {CanSend}", device.DeviceId,
                    canSend);
            }

            if (!canSend)
            {
                Logger.LogInformation("❌ Coordinated push blocked by deduplication for device {DeviceId}",
                    device.DeviceId);
                return false;
            }

            // Execute the actual push using existing logic
            var success =
                await SendCoordinatedPushNotificationAsync(device, contents, targetDate, isRetryPush, isTestPush);

            if (success)
            {
                Logger.LogInformation("✅ Coordinated push completed successfully for device {DeviceId}",
                    device.DeviceId);
            }
            else
            {
                Logger.LogWarning("❌ Coordinated push failed for device {DeviceId}", device.DeviceId);

                // Release Redis claim if push failed
                if (!isTestPush && deduplicationService != null && canSend)
                {
                    await deduplicationService.ReleasePushClaimAsync(device.DeviceId, date, device.TimeZoneId,
                        isRetryPush);
                    Logger.LogInformation("🔄 Released Redis claim for failed push - Device: {DeviceId}",
                        device.DeviceId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in coordinated push execution - Device: {DeviceId}, User: {UserId}",
                device.DeviceId, State.UserId);
            return false;
        }
    }

    /// <summary>
    /// Send coordinated push notification using existing Firebase logic
    /// </summary>
    private async Task<bool> SendCoordinatedPushNotificationAsync(UserDeviceInfo device,
        List<DailyNotificationContent> contents, DateTime targetDate, bool isRetryPush, bool isTestPush)
    {
        try
        {
            // Get Global JWT Provider and Firebase project configuration
            var globalJwtProvider = await GetGlobalJwtProviderAgentAsync();
            var firebaseService = _serviceProvider.GetService(typeof(FirebaseService)) as FirebaseService;

            if (firebaseService == null)
            {
                Logger.LogError("FirebaseService not available for coordinated push");
                return false;
            }

            var projectId = firebaseService.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                Logger.LogError("Firebase ProjectId not configured for coordinated push");
                return false;
            }

            var successCount = 0;
            var totalContents = contents.Count;

            // Send only the first content for morning push, second content for afternoon retry
            var contentToSend = isRetryPush ? contents.Skip(1).FirstOrDefault() : contents.FirstOrDefault();

            if (contentToSend == null)
            {
                Logger.LogWarning("No content available for {PushType} push - Device: {DeviceId}",
                    isRetryPush ? "afternoon retry" : "morning", device.DeviceId);
                return false;
            }

            // Send the selected content
            var contentIndex = isRetryPush ? 1 : 0;

            try
            {
                // Get localized content based on device language preference
                var localizedContent = contentToSend.GetLocalizedContent(device.PushLanguage);

                var pushData = new Dictionary<string, object>
                {
                    ["type"] = (int)DailyPushConstants.PushType.DailyPush,
                    ["contentId"] = contentToSend.Id,
                    ["contentIndex"] = contentIndex,
                    ["totalContents"] = totalContents,
                    ["isTestPush"] = isTestPush,
                    ["targetDate"] = targetDate.ToString("yyyy-MM-dd"),
                    ["deviceId"] = device.DeviceId
                };

                var success = await SendDirectPushNotificationAsync(
                    globalJwtProvider,
                    projectId,
                    device.PushToken,
                    localizedContent.Title,
                    localizedContent.Content,
                    pushData,
                    device.TimeZoneId,
                    isRetryPush,
                    isTestPush,
                    skipDeduplicationCheck: true // Already handled by coordinator
                );

                if (success)
                {
                    successCount++;
                    Logger.LogDebug(
                        "✅ Coordinated push content {ContentIndex}/{TotalContents} sent successfully - Device: {DeviceId}",
                        contentIndex + 1, totalContents, device.DeviceId);
                }
                else
                {
                    Logger.LogWarning(
                        "❌ Coordinated push content {ContentIndex}/{TotalContents} failed - Device: {DeviceId}",
                        contentIndex + 1, totalContents, device.DeviceId);

                    // Check if this was a token failure and mark device for cleanup
                    await CheckAndMarkDeviceForCleanupAsync(device.PushToken, "PUSH_FAILED");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Exception sending coordinated push content {ContentIndex}/{TotalContents} - Device: {DeviceId}",
                    contentIndex + 1, totalContents, device.DeviceId);
            }

            var allSuccessful = successCount == 1; // Only one content sent
            Logger.LogInformation(
                "📊 Coordinated push summary - Device: {DeviceId}, Success: {SuccessCount}/1, AllSuccessful: {AllSuccessful}",
                device.DeviceId, successCount, allSuccessful);

            return allSuccessful;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in SendCoordinatedPushNotificationAsync - Device: {DeviceId}",
                device.DeviceId);
            return false;
        }
    }

    /// <summary>
    /// Check if device should be marked for cleanup based on push failures
    /// </summary>
    private async Task CheckAndMarkDeviceForCleanupAsync(string pushToken, string failureReason)
    {
        try
        {
            // For coordinated push, call the automatic cleanup
            await MarkDeviceTokenAsInvalidAsync(pushToken, failureReason);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in CheckAndMarkDeviceForCleanupAsync - Token: {TokenPrefix}...",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);
        }
    }

    /// <summary>
    /// Mark device token as invalid and remove the device automatically
    /// Called when FCM returns UNREGISTERED or INVALID_ARGUMENT errors
    /// </summary>
    private async Task MarkDeviceTokenAsInvalidAsync(string pushToken, string errorCode)
    {
        try
        {
            if (string.IsNullOrEmpty(pushToken))
            {
                Logger.LogWarning("Cannot mark device for cleanup: pushToken is null or empty");
                return;
            }

            var devicesToRemove = new List<string>();
            var cleanupDetails = new Dictionary<string, string>
            {
                ["error_code"] = errorCode,
                ["token_prefix"] = pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken,
                ["cleanup_trigger"] = "fcm_token_invalid",
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };

            // 🔥 V1 devices no longer used in logic - skip V1 token cleanup

            // Check V2 devices
            var v2DevicesToRemove = State.UserDevicesV2.Values
                .Where(d => d.PushToken == pushToken)
                .Select(d => d.DeviceId)
                .ToList();

            if (v2DevicesToRemove.Any())
            {
                devicesToRemove.AddRange(v2DevicesToRemove);
                cleanupDetails["v2_devices"] = string.Join(",", v2DevicesToRemove);

                Logger.LogInformation(
                    "🧹 Marking {Count} V2 devices for cleanup due to invalid token - Devices: {DeviceIds}, Error: {ErrorCode}",
                    v2DevicesToRemove.Count, string.Join(",", v2DevicesToRemove), errorCode);

                // Remove V2 devices  
                var cleanupEvent = new CleanupDevicesV2Event
                {
                    RemovedCount = v2DevicesToRemove.Count,
                    CleanupReason = $"fcm_token_invalid_{errorCode}"
                };
                cleanupEvent.DeviceIdsToRemove.AddRange(v2DevicesToRemove);
                foreach (var kvp in cleanupDetails)
                    cleanupEvent.CleanupDetails[kvp.Key] = kvp.Value;
                RaiseEvent(cleanupEvent);
            }

            if (devicesToRemove.Any())
            {
                await ConfirmEventsAsync();
                Logger.LogWarning(
                    "🗑️ Automatically removed {Count} devices with invalid pushToken - Error: {ErrorCode}, Token: {TokenPrefix}..., DeviceIds: {DeviceIds}",
                    devicesToRemove.Count, errorCode,
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken,
                    string.Join(",", devicesToRemove));
            }
            else
            {
                Logger.LogDebug("🔍 No devices found with invalid token {TokenPrefix}... for cleanup",
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Exception in MarkDeviceTokenAsInvalidAsync - Token: {TokenPrefix}..., Error: {ErrorCode}",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, errorCode);
        }
    }

    /// <summary>
    /// Get V2 devices in V1 format for compatibility - V2 ONLY
    /// ❌ V1 FALLBACK REMOVED: Pure V2 architecture
    /// </summary>
    public async Task<List<UserDeviceInfo>> GetUnifiedDevicesAsync()
    {
        // 🔥 V2 ONLY: Convert V2 devices to V1 format for compatibility
        var v2Devices = State.UserDevicesV2.Values.Select(v2 => new UserDeviceInfo
        {
            DeviceId = v2.DeviceId,
            PushToken = v2.PushToken,
            TimeZoneId = v2.TimeZoneId,
            PushLanguage = v2.PushLanguage,
            PushEnabled = v2.PushEnabled,
            RegisteredAt = v2.RegisteredAt?.ToDateTime() ?? DateTime.MinValue,
            LastTokenUpdate = v2.LastTokenUpdate?.ToDateTime() ?? DateTime.MinValue
        }).ToList();

        Logger.LogDebug("GetUnifiedDevicesAsync: V2-only devices. Count: {DeviceCount}", v2Devices.Count);

        return v2Devices;
    }

    /// <summary>
    /// Enhanced cleanup for V2 devices with smart criteria
    /// </summary>
    public async Task CleanupDevicesV2Async()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var sevenDaysAgo = now.AddDays(-7);
        var threeDaysAgo = now.AddDays(-3);

        var devicesToRemove = new List<string>();
        var cleanupDetails = new Dictionary<string, string>();

        // 1. Remove devices with consecutive push failures (token likely invalid)
        var failedDevices = State.UserDevicesV2.Values
            .Where(d => d.ConsecutiveFailures >= 5 && d.LastActiveAt.LessOrEqualThan(threeDaysAgo))
            .Select(d => d.DeviceId)
            .ToList();
        devicesToRemove.AddRange(failedDevices);
        if (failedDevices.Any())
        {
            cleanupDetails["consecutive_failures"] = string.Join(",", failedDevices);
        }

        // 2. Remove very old devices (30+ days inactive)
        var expiredDevices = State.UserDevicesV2.Values
            .Where(d => d.LastActiveAt.LessOrEqualThan(thirtyDaysAgo))
            .Select(d => d.DeviceId)
            .ToList();
        devicesToRemove.AddRange(expiredDevices);
        if (expiredDevices.Any())
        {
            cleanupDetails["expired"] = string.Join(",", expiredDevices);
        }

        // 3. Remove devices marked for cleanup
        var pendingCleanupDevices = State.UserDevicesV2.Values
            .Where(d => d.Status == (int)DeviceStatus.PendingCleanup)
            .Select(d => d.DeviceId)
            .ToList();
        devicesToRemove.AddRange(pendingCleanupDevices);
        if (pendingCleanupDevices.Any())
        {
            cleanupDetails["pending_cleanup"] = string.Join(",", pendingCleanupDevices);
        }

        // 4. Handle duplicates: same deviceId, keep the most recent one
        var duplicateGroups = State.UserDevicesV2.Values
            .GroupBy(d => d.DeviceId)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var devicesToKeep = group.OrderByDescending(d => d.LastActiveAt).Skip(1);
            var duplicateIds = devicesToKeep.Select(d => d.DeviceId).ToList();
            devicesToRemove.AddRange(duplicateIds);
            if (duplicateIds.Any())
            {
                cleanupDetails["duplicates"] = string.Join(",", duplicateIds);
            }
        }

        // Remove duplicates from the removal list
        devicesToRemove = devicesToRemove.Distinct().ToList();

        if (devicesToRemove.Any())
        {
            var cleanupEvent = new CleanupDevicesV2Event
            {
                RemovedCount = devicesToRemove.Count,
                CleanupReason = "enhanced_criteria",
                CleanupTime = now.ToTimestamp()
            };
            cleanupEvent.DeviceIdsToRemove.AddRange(devicesToRemove);
            foreach (var kvp in cleanupDetails)
                cleanupEvent.CleanupDetails[kvp.Key] = kvp.Value;
            RaiseEvent(cleanupEvent);

            await ConfirmEventsAsync();

            Logger.LogInformation("🧹 V2 Enhanced device cleanup: removed {Count} devices. Details: {Details}",
                devicesToRemove.Count, JsonSerializer.Serialize(cleanupDetails));
        }
    }
}