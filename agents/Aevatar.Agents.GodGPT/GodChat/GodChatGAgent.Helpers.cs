using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GAgents.AIGAgent.Dtos;
using Aevatar.GAgents.ChatAgent.Dtos;
using GodGPT.GAgents.Common.Constants;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Aevatar.Agents.Abstractions.Extensions;

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Helper methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    #region Agent Factory Methods
    
    private async Task<IUserInfoCollectionGAgent> GetUserInfoCollectionAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId);
        return actor.As<IUserInfoCollectionGAgent>();
    }
    
    private async Task<IUserQuotaGAgent> GetUserQuotaAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
        return actor.As<IUserQuotaGAgent>();
    }

    private async Task<IConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgentInterface == null)
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId().ToString());
            _configurationAgentInterface = actor.As<IConfigurationGAgent>();
        }
        return _configurationAgentInterface;
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(string userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId);
        return actor.As<IInvitationGAgent>();
    }
    
    #endregion

    #region Session Management
    
    private async Task SetSessionTitleAsync(Guid sessionId, string content)
    {
        var totalStopwatch = Stopwatch.StartNew();
        if (State.Title.IsNullOrEmpty())
        {
            // Take first 4 words and limit total length to 100 characters
            var title = string.Join(" ", content.Split(" ").Take(4));
            if (title.Length > 100)
            {
                title = title.Substring(0, 100);
            }
            RaiseEvent(new Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent()
            {
                Title = title
            });
            
            // Fire-and-forget to avoid deadlock with ChatGAgentManager
            // ChatGAgentManager may call back to GodChatGAgent.GetChatMessageAsync during Share operations
            Logger.LogInformation("[PERF][GodChatGAgent] SetSessionTitleAsync scheduling ChatGAgentManager.RenameChatTitleAsync (fire-and-forget) - SessionId: {SessionId}, ChatManagerGuid: {ChatManagerGuid}", sessionId, State.ChatManagerGuid);
            
            _ = Task.Run(async () =>
            {
                try
                {
                    var chatManagerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(State.ChatManagerGuid);
                    var chatManagerGAgent = chatManagerActor.As<IChatManagerGAgent>();
                    await chatManagerGAgent.RenameChatTitleAsync(new Aevatar.Agents.GodGPT.Protos.GodChat.RenameChatTitleEvent()
                    {
                        SessionId = sessionId.ToString(),
                        Title = title
                    });
                    Logger.LogInformation("[PERF][GodChatGAgent] SetSessionTitleAsync background task COMPLETED - SessionId: {SessionId}", sessionId);
                }
                catch (Exception ex)
                {
                    Logger.LogWarning(ex, "[GodChatGAgent] SetSessionTitleAsync background task failed - SessionId: {SessionId}", sessionId);
                }
            });
            
            totalStopwatch.Stop();
            Logger.LogInformation("[PERF][GodChatGAgent] SetSessionTitleAsync scheduled (not waiting) - Duration: {Duration}ms, SessionId: {SessionId}", totalStopwatch.ElapsedMilliseconds, sessionId);
        }
        else
        {
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][SetSessionTitleAsync] SKIP (title exists) - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
        }
    }
    
    #endregion

    #region Context Creation
    
    private AIChatContextDto CreateAIChatContext(Guid sessionId, string llm, bool streamingModeEnabled,
        string message, string chatId, ExecutionPromptSettings? promptSettings = null, bool isHttpRequest = false,
        string? region = null, List<string>? images = null, UserTimeContext? userTimeContext = null)
    {
        var aiChatContextDto = new AIChatContextDto()
        {
            ChatId = chatId,
            RequestId = sessionId
        };
        if (isHttpRequest)
        {
            aiChatContextDto.MessageId = JsonConvert.SerializeObject(new Dictionary<string, object>()
            {
                { "IsHttpRequest", true }, { "LLM", llm }, { "StreamingModeEnabled", streamingModeEnabled },
                { "Message", message }, { "Region", region }, { "Images", images },
                { "UserLocalTime", userTimeContext?.UserLocalTime }, { "UserTimeZoneId", userTimeContext?.UserTimeZoneId }
            });
        }

        return aiChatContextDto;
    }
    
    #endregion

    #region Daily Recommendations

    private async Task<GenerateUserInfoPromptResponseProto> GetUserInfoPromptResponseAsync(UserTimeContext? userTimeContext)
    {
        Logger.LogInformation(
            "[GodChatGAgent][GetUserInfoPromptResponseAsync] UserInfoCollection actor lookup - SessionId={SessionId}, ActorId={ActorId}, RequestUserId={RequestUserId}",
            Id, State.ChatManagerGuid, State.ChatManagerGuid);
        var userInfoCollectionGAgent = await GetUserInfoCollectionAgentAsync(State.ChatManagerGuid);
        var request = new GenerateUserInfoPromptRequestProto
        {
            UserId = State.ChatManagerGuid,
            UserLocalTime = userTimeContext?.UserLocalTime.HasValue == true
                ? Timestamp.FromDateTime(DateTime.SpecifyKind(userTimeContext.UserLocalTime.Value, DateTimeKind.Utc))
                : null
        };

        Logger.LogInformation(
            "[GodChatGAgent][GetUserInfoPromptResponseAsync] UserInfoCollection request prepared - SessionId={SessionId}, ActorId={ActorId}, ProtoRequestUserId={ProtoRequestUserId}",
            Id, State.ChatManagerGuid, request.UserId);
        var response = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync(request);
        Logger.LogDebug(
            "[GodChatGAgent][GetUserInfoPromptResponseAsync] SessionId={SessionId}, UserId={UserId}, HasPrompt={HasPrompt}, PromptLength={PromptLength}, FullName={FullName}, Prompt={Prompt}",
            Id, State.ChatManagerGuid, !string.IsNullOrWhiteSpace(response?.Prompt), response?.Prompt?.Length ?? 0,
            response?.FullName ?? string.Empty, response?.Prompt ?? string.Empty);
        return response;
    }

    private async Task<string> GetSharedUserInfoPromptAsync(UserTimeContext? userTimeContext)
    {
        var response = await GetUserInfoPromptResponseAsync(userTimeContext);
        if (string.IsNullOrWhiteSpace(response?.Prompt))
        {
            return string.Empty;
        }

        var mergedPrompt = $"Use the following user background when it is relevant to the reply:\n{response.Prompt}";
        Logger.LogDebug(
            "[GodChatGAgent][GetSharedUserInfoPromptAsync] SessionId={SessionId}, UserId={UserId}, SharedPromptLength={PromptLength}, SharedPrompt={SharedPrompt}",
            Id, State.ChatManagerGuid, mergedPrompt.Length, mergedPrompt);
        return mergedPrompt;
    }

    private static string MergeUserInfoPrompt(string userInfoPrompt, string message)
    {
        if (string.IsNullOrWhiteSpace(userInfoPrompt))
        {
            return message;
        }

        return $"{userInfoPrompt}\n\nCurrent user message:\n{message}";
    }
    
    private async Task<string> GenerateDailyRecommendationsAsync(GodGPTLanguage language,
        UserTimeContext? userTimeContext)
    {
        // TODO: [GOOGLE_CALENDAR_DISABLED] Google Calendar integration temporarily disabled
        // This method previously fetched calendar events and generated personalized recommendations
        // Re-enable when Google Calendar integration is migrated to new framework
        Logger.LogDebug($"[GodChatGAgent][GenerateDailyRecommendationsAsync] {Id} Google Calendar disabled - returning empty prompt");
        
        var userQuotaGAgent = await GetUserQuotaAgentAsync(State.ChatManagerGuid);
        var response = await GetUserInfoPromptResponseAsync(userTimeContext);
        var prompt = response.Prompt;
        var isSubscribed = await userQuotaGAgent.IsSubscribedAsync(true) || await userQuotaGAgent.IsSubscribedAsync(false);
        
        var languageEnglishName = GodGPTLanguageHelper.GetLanguageEnglishName(language);
        prompt =
            $"Use {languageEnglishName} to respond including titles like DO, DON'T.\n" +
            "Generate a personalized \"Today's Dos and Don'ts\" for the user based on their information and cosmological theories.\n" +
            $"{prompt}";
        
        // Return prompt without calendar events
        return prompt;
    }

    private string GeneratePaidAndBoundUserWithoutEventsRecommendations(string language)
    {
        var prompt = $@"Use the following format for the output:
Hi, {{user_name}}, based on your name, gender, age, location, and local time, here are your exclusive Dos and Don'ts for today (which may cover health, work, relationships, life, etc.), as follows:
DO
- Xxxx
- xxxxx
DON'T
- Xxxxx
- Xxxxx

We noticed your calendar is empty for today. This is a perfect opportunity to take some time for yourself, engage in deep thought, or simply enjoy the freedom! When you have new plans, we'll be here to provide you with personalized guidance.

xxxxx (A brief one-sentence summary, under 20 words)";

        return prompt;
    }   

    /// <summary>
    /// Generate recommendations for users without events
    /// </summary>
    private string GenerateUserWithoutEventsRecommendations(string language)
    {
        var prompt = $@"Use the following format for the output:
Based on your name, gender, age, location, and local time, here are your exclusive Dos and Don'ts for today (which may cover health, work, relationships, life, etc.), as follows:
DO
- Xxxx
- xxxxx
DON'T
- Xxxxx
- Xxxxx
xxxxx (A brief one-sentence summary, under 20 words)";

        return prompt;
    }
    
    #endregion

    #region Google Calendar Integration (Disabled)
    // TODO: [GOOGLE_CALENDAR_DISABLED] The following methods are disabled until Google Calendar integration is migrated
    /*
    /// <summary>
    /// Generate daily recommendations based on subscription status and calendar events
    /// </summary>
    private string GenerateDailyRecommendationsAsync(string prompt, bool isSubscribed, GoogleCalendarListDto calendarEvents, 
        string language)
    {
        // ... original implementation ...
    }

    /// <summary>
    /// Format calendar events for display
    /// </summary>
    private List<string> FormatCalendarEvents(List<GoogleCalendarEventDto> events)
    {
        // ... original implementation ...
    }

    private string GenerateNonSubscribedUserWithEventsRecommendations(List<string> formattedEvents, List<GoogleCalendarEventDto> events, string language)
    {
        // ... original implementation ...
    }

    private string GenerateSubscribedUserWithEventsRecommendations(List<string> formattedEvents, List<GoogleCalendarEventDto> events, string language)
    {
        // ... original implementation ...
    }
    */
    #endregion
}
