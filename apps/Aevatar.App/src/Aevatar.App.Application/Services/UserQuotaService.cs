using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services;

/// <summary>
/// User Quota service implementation - manages user credits, subscriptions, and quota limits
/// Uses UserQuotaGAgent architecture
/// </summary>
public class UserQuotaService : IUserQuotaService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UserQuotaService> _logger;

    public UserQuotaService(
        IGAgentActorFactory actorFactory,
        ILogger<UserQuotaService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    private async Task<IUserQuotaGAgent> GetAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(userId);
        return actor.As<IUserQuotaGAgent>();
    }

    public async Task SetShownCreditsToastAsync(Guid userId, bool hasShownInitialCreditsToast)
    {
        _logger.LogDebug("[UserQuotaService] Setting shown credits toast for user {UserId} to {Value}", 
            userId, hasShownInitialCreditsToast);
        
        var agent = await GetAgentAsync(userId);
        var request = new SetShownCreditsToastRequestProto
        {
            UserId = userId.ToString(),
            HasShownInitialCreditsToast = hasShownInitialCreditsToast
        };
        
        await agent.SetShownCreditsToastAsync(request);
    }

    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid userId, UpdateUserCreditsInput input)
    {
        _logger.LogInformation("[UserQuotaService] Updating credits for user {UserId}, change: {CreditsChange}", 
            userId, input.Credits);
        
        var agent = await GetAgentAsync(input.UserId);
        
        var request = new UpdateCreditsRequestProto
        {
            UserId = input.UserId.ToString(),
            OperatorUserId = userId.ToString(),
            CreditsChange = input.Credits
        };
        
        var protoResult = await agent.UpdateCreditsAsync(request);
        
        // Convert Protobuf to DTO
        return new GrainResultDto<int>
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Data = protoResult.Data
        };
    }

    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid userId, UpdateUserSubscriptionsInput input)
    {
        _logger.LogInformation("[UserQuotaService] Updating subscription for user {UserId}, planType: {PlanType}, isUltimate: {IsUltimate}", 
            userId, input.PlanType, input.IsUltimate);
        
        var agent = await GetAgentAsync(input.UserId);
        
        var request = new UpdateSubscriptionRequestProto
        {
            UserId = input.UserId.ToString(),
            OperatorUserId = userId.ToString(),
            PlanType = (int)input.PlanType,
            IsUltimate = input.IsUltimate
        };
        
        var protoResult = await agent.UpdateSubscriptionAsync(request);
        
        // Convert Protobuf to DTO
        var subscriptionList = protoResult.Data.Select(s => new SubscriptionInfoDto
        {
            IsActive = s.IsActive,
            PlanType = (PlanType)(int)s.PlanType,
            Status = (PaymentStatus)(int)s.Status,
            StartDate = s.StartDate?.ToDateTime() ?? DateTime.MinValue,
            EndDate = s.EndDate?.ToDateTime() ?? DateTime.MinValue,
            SubscriptionIds = s.SubscriptionIds.ToList(),
            InvoiceIds = s.InvoiceIds.ToList()
        }).ToList();
        
        return new GrainResultDto<List<SubscriptionInfoDto>>
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Data = subscriptionList
        };
    }

    public async Task<ExecuteActionResultDto> CanUploadImageAsync(Guid userId)
    {
        _logger.LogDebug("[UserQuotaService] Checking if user {UserId} can upload image", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.CanUploadImageAsync();
        
        // Convert Protobuf to DTO
        return new ExecuteActionResultDto
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            Code = protoResult.CanUpload ? 0 : 1  // 0 = success, 1 = rate limit exceeded
        };
    }
}

