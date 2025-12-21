using System.Diagnostics;
using Aevatar.Agents.GodGPT.Protos.GodChat;
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

namespace Aevatar.Application.Grains.Agents.ChatManager.Chat;

/// <summary>
/// Helper methods for GodChatGAgent.
/// </summary>
public partial class GodChatGAgent
{
    #region Agent Factory Methods
    
    private async Task<IUserInfoCollectionGAgent> GetUserInfoCollectionAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId);
        return (IUserInfoCollectionGAgent)actor.GetAgent();
    }
    
    private async Task<IUserQuotaGAgent> GetUserQuotaAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
        return (IUserQuotaGAgent)actor.GetAgent();
    }

    private async Task<ConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgent == null)
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId());
            _configurationAgent = (ConfigurationGAgent)actor.GetAgent();
        }
        return _configurationAgent;
    }

    private async Task<IInvitationGAgent> GetInvitationAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(userId);
        return (IInvitationGAgent)actor.GetAgent();
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
            var chatManagerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(Guid.Parse(State.ChatManagerGuid));
            var chatManagerGAgent = (IChatManagerGAgent)chatManagerActor.GetAgent();
            await chatManagerGAgent.RenameChatTitleAsync(new RenameChatTitleEvent()
            {
                SessionId = sessionId,
                Title = title
            });
            
            totalStopwatch.Stop();
            Logger.LogDebug($"[GodChatGAgent][SetSessionTitleAsync] TOTAL_Time - Duration: {totalStopwatch.ElapsedMilliseconds}ms, SessionId: {sessionId}");
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
        string? region = null, List<string>? images = null)
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
                { "Message", message }, { "Region", region }, { "Images", images }
            });
        }

        return aiChatContextDto;
    }
    
    #endregion

    #region Daily Recommendations
    
    private async Task<string> GenerateDailyRecommendationsAsync(GodGPTLanguage language,
        DateTime? userLocalTime, string? userTimeZoneId)
    {
        // TODO: [GOOGLE_CALENDAR_DISABLED] Google Calendar integration temporarily disabled
        // This method previously fetched calendar events and generated personalized recommendations
        // Re-enable when Google Calendar integration is migrated to new framework
        Logger.LogDebug($"[GodChatGAgent][GenerateDailyRecommendationsAsync] {Id} Google Calendar disabled - returning empty prompt");
        
        var userQuotaGAgent = await GetUserQuotaAgentAsync(Guid.Parse(State.ChatManagerGuid));
        var userInfoCollectionGAgent = await GetUserInfoCollectionAgentAsync(Guid.Parse(State.ChatManagerGuid));
        (string fullName, string prompt) = await userInfoCollectionGAgent.GenerateUserInfoPromptAsync(userLocalTime);
        var isSubscribed = await userQuotaGAgent.IsSubscribedAsync(true) || await userQuotaGAgent.IsSubscribedAsync(false);
        
        var languageEnglishName = GodGPTLanguageHelper.GetLanguageEnglishName(language);
        prompt = $"Use {languageEnglishName} to respond including titles like DO, DON'T \n {prompt}";
        
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

