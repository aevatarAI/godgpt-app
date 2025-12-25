using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core.Context;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserProfile;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.Awakening;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
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
    private readonly IAgentContextAccessor _agentContextAccessor;

    public GodGPTUserService(
        IGAgentActorFactory actorFactory,
        ILogger<GodGPTUserService> logger,
        IAgentContextAccessor agentContextAccessor)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _agentContextAccessor = agentContextAccessor;
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> GetUserProfileAsync(Guid currentUserId)
    {
        var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(currentUserId.ToString());
        var userProfileGAgent = userProfileActor.As<IUserProfileGAgent>();
        var profileProto = await userProfileGAgent.GetUserProfileAsync();
        
        // Convert Protobuf to DTO
        var creditsDto = new CreditsInfoDto
        {
            IsInitialized = profileProto.Credits?.IsInitialized ?? false,
            Credits = profileProto.Credits?.Credits ?? 0,
            ShouldShowToast = profileProto.Credits?.ShouldShowToast ?? false
        };
        
        return new UserProfileDto
        {
            Gender = profileProto.Gender,
            BirthDate = profileProto.BirthDate?.ToDateTime() ?? DateTime.MinValue,
            BirthPlace = profileProto.BirthPlace,
            FullName = profileProto.FullName,
            Credits = creditsDto,
            InviterId = string.IsNullOrEmpty(profileProto.InviterId) ? null : Guid.Parse(profileProto.InviterId),
            VoiceLanguage = (VoiceLanguageEnum)profileProto.VoiceLanguage
        };
    }

    /// <inheritdoc />
    public async Task<Guid> SetUserProfileAsync(Guid currentUserId, SetUserProfileInput userProfileDto)
    {
        var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(currentUserId.ToString());
        var userProfileGAgent = userProfileActor.As<IUserProfileGAgent>();
        return await userProfileGAgent.SetUserProfileAsync(userProfileDto.Gender, userProfileDto.BirthDate,
            userProfileDto.BirthPlace, userProfileDto.FullName);
    }

    /// <inheritdoc />
    public async Task<Guid> DeleteAccountAsync(Guid currentUserId)
    {
        try
        {
            var awakeningActor = await _actorFactory.CreateGAgentActorAsync<AwakeningGAgent>(currentUserId.ToString());
            var awakeningAgent = awakeningActor.As<IAwakeningGAgent>();
            await awakeningAgent.ResetTodayContentAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "[GodGPTUserService][DeleteAccountAsync] AwakeningGAgent ResetTodayContentAsync error currentUserId: {UserId}", currentUserId);
        }
        
        var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId.ToString());
        var manager = managerActor.As<IChatManagerGAgent>();
        return await manager.ClearAllAsync();
    }

    /// <inheritdoc />
    public async Task<UserProfileDto> SetVoiceLanguageAsync(Guid currentUserId, VoiceLanguageEnum voiceLanguage)
    {
        var userProfileActor = await _actorFactory.CreateGAgentActorAsync<UserProfileGAgent>(currentUserId.ToString());
        var userProfileGAgent = userProfileActor.As<IUserProfileGAgent>();
        await userProfileGAgent.SetVoiceLanguageAsync(voiceLanguage);
        
        var profileProto = await userProfileGAgent.GetUserProfileAsync();
        
        // Convert Protobuf to DTO
        var creditsDto = new CreditsInfoDto
        {
            IsInitialized = profileProto.Credits?.IsInitialized ?? false,
            Credits = profileProto.Credits?.Credits ?? 0,
            ShouldShowToast = profileProto.Credits?.ShouldShowToast ?? false
        };
        
        return new UserProfileDto
        {
            Gender = profileProto.Gender,
            BirthDate = profileProto.BirthDate?.ToDateTime() ?? DateTime.MinValue,
            BirthPlace = profileProto.BirthPlace,
            FullName = profileProto.FullName,
            Credits = creditsDto,
            InviterId = string.IsNullOrEmpty(profileProto.InviterId) ? null : Guid.Parse(profileProto.InviterId),
            VoiceLanguage = (VoiceLanguageEnum)profileProto.VoiceLanguage
        };
    }

    /// <inheritdoc />
    public async Task UpdateShowToastAsync(Guid currentUserId)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(currentUserId.ToString());
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();
        var request = new SetShownCreditsToastRequestProto
        {
            UserId = currentUserId.ToString(),
            HasShownInitialCreditsToast = true
        };
        await userQuotaGAgent.SetShownCreditsToastAsync(request);
    }

    /// <inheritdoc />
    public async Task<ExecuteActionResultDto> CanUploadImageAsync(Guid currentUserId, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(currentUserId.ToString());
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();
        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
        var protoResult = await userQuotaGAgent.CanUploadImageAsync();
        
        // Convert Protobuf to DTO
        return new ExecuteActionResultDto
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Code = protoResult.CanUpload ? 0 : 1  // 0 = success, 1 = rate limit exceeded
        };
    }
}
