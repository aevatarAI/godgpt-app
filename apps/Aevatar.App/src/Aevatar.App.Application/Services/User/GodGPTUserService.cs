using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.Awakening;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.App.Domain.Shared;
using Aevatar.App.Application.Contracts.Services.User;

namespace Aevatar.App.Application.Services.User;

/// <summary>
/// Service implementation for managing user profile and account operations.
/// Handles profile retrieval, updates, deletion, and user preferences.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTUserService : ApplicationService, IGodGPTUserService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<GodGPTUserService> _logger;

    public GodGPTUserService(
        IGAgentActorFactory actorFactory,
        ILogger<GodGPTUserService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> GetUserProfileAsync(Guid currentUserId)
    {
        var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId);
        var manager = (IChatManagerGAgent)managerActor.GetAgent();
        return await manager.GetUserProfileAsync();
    }

    /// <inheritdoc />
    public async Task<Guid> SetUserProfileAsync(Guid currentUserId, SetUserProfileInput userProfileDto)
    {
        var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId);
        var manager = (IChatManagerGAgent)managerActor.GetAgent();
        return await manager.SetUserProfileAsync(userProfileDto.Gender, userProfileDto.BirthDate,
            userProfileDto.BirthPlace, userProfileDto.FullName);
    }

    /// <inheritdoc />
    public async Task<Guid> DeleteAccountAsync(Guid currentUserId)
    {
        try
        {
            var awakeningActor = await _actorFactory.CreateGAgentActorAsync<AwakeningGAgent>(currentUserId);
            var awakeningAgent = (IAwakeningGAgent)awakeningActor.GetAgent();
            await awakeningAgent.ResetTodayContentAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[GodGPTUserService][DeleteAccountAsync] AwakeningGAgent ResetTodayContentAsync error currentUserId: {UserId}", currentUserId);
        }
        
        var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId);
        var manager = (IChatManagerGAgent)managerActor.GetAgent();
        return await manager.ClearAllAsync();
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> SetVoiceLanguageAsync(Guid currentUserId, VoiceLanguageEnum voiceLanguage)
    {
        var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId);
        var manager = (IChatManagerGAgent)managerActor.GetAgent();
        await manager.SetVoiceLanguageAsync(voiceLanguage);
        return await manager.GetUserProfileAsync();
    }

    /// <inheritdoc />
    public async Task UpdateShowToastAsync(Guid currentUserId)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(currentUserId);
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();
        // No need to save immediately, can be executed in one step
        userQuotaGAgent.SetShownCreditsToastAsync(true);
    }

    /// <inheritdoc />
    public async Task<ExecuteActionResultDto> CanUploadImageAsync(Guid currentUserId, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(currentUserId);
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();
        RequestContext.Set("GodGPTLanguage", language.ToString());
        return await userQuotaGAgent.CanUploadImageAsync();
    }
}
