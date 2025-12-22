using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GodGPT.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.App.Application.Contracts.Services.Subscription;

namespace Aevatar.App.Application.Services.Subscription;

/// <summary>
/// Service implementation for managing subscription status and user quotas.
/// Handles subscription checks and quota updates through UserBillingGAgent and UserQuotaGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTSubscriptionService : ApplicationService, IGodGPTSubscriptionService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<GodGPTSubscriptionService> _logger;

    public GodGPTSubscriptionService(
        IGAgentActorFactory actorFactory,
        ILogger<GodGPTSubscriptionService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HasActiveAppleSubscriptionAsync(Guid currentUserId)
    {
        var userBillingActor = await _actorFactory.CreateGAgentActorAsync<UserBillingGAgent>(currentUserId);
        var userBillingGAgent = (IUserBillingGAgent)userBillingActor.GetAgent();
        return await userBillingGAgent.HasActiveAppleSubscriptionAsync();
    }

    /// <inheritdoc />
    public async Task<ActiveSubscriptionStatusDto> HasActiveSubscriptionAsync(Guid currentUserId)
    {
        var userBillingActor = await _actorFactory.CreateGAgentActorAsync<UserBillingGAgent>(currentUserId);
        var userBillingGAgent = (IUserBillingGAgent)userBillingActor.GetAgent();
        return await userBillingGAgent.GetActiveSubscriptionStatusAsync();
    }

    /// <inheritdoc />
    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid currentUserId, UpdateUserCreditsInput input)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(input.UserId);
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();
        return await userQuotaGAgent.UpdateCreditsAsync(currentUserId.ToString(), input.Credits);
    }

    /// <inheritdoc />
    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid currentUserId, UpdateUserSubscriptionsInput input)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(input.UserId);
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();
        return await userQuotaGAgent.UpdateSubscriptionAsync(currentUserId.ToString(), input.PlanType, input.IsUltimate);
    }
}
