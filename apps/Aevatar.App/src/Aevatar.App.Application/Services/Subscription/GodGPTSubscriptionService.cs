using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.GodGPT.Dtos;
using Aevatar.Payment.Agents;
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
        // Use PaymentIndexGAgent to check Apple subscription status
        var paymentIndexActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(currentUserId.ToString());
        var paymentIndexGAgent = paymentIndexActor.As<IPaymentIndexGAgent>();
        var subscriptions = await paymentIndexGAgent.GetActiveSubscriptionsByBusinessAsync("godgpt");
        
        // Check if any active subscription is from Apple AppStore
        foreach (var sub in subscriptions.Subscriptions)
        {
            if (sub.Platform == (int)PaymentPlatform.AppStore)
            {
                return true;
            }
        }
        return false;
    }

    /// <inheritdoc />
    public async Task<ActiveSubscriptionStatusDto> HasActiveSubscriptionAsync(Guid currentUserId)
    {
        // Use PaymentIndexGAgent to check subscription status
        var paymentIndexActor = await _actorFactory.CreateGAgentActorAsync<PaymentIndexGAgent>(currentUserId.ToString());
        var paymentIndexGAgent = paymentIndexActor.As<IPaymentIndexGAgent>();
        var subscriptions = await paymentIndexGAgent.GetActiveSubscriptionsByBusinessAsync("godgpt");
        
        // Filter out expired subscriptions (align with old code: SubscriptionEndDate > now)
        var now = DateTime.UtcNow;
        var validSubscriptions = subscriptions.Subscriptions
            .Where(s => s.PeriodEnd != null && s.PeriodEnd.ToDateTime() > now)
            .ToList();
        
        var result = new ActiveSubscriptionStatusDto
        {
            HasActiveSubscription = validSubscriptions.Count > 0
        };
        
        foreach (var sub in validSubscriptions)
        {
            if (sub.Platform == (int)PaymentPlatform.AppStore)
            {
                result.HasActiveAppleSubscription = true;
            }
            else if (sub.Platform == (int)PaymentPlatform.Stripe)
            {
                result.HasActiveStripeSubscription = true;
            }
            else if (sub.Platform == (int)PaymentPlatform.GooglePlay)
            {
                result.HasActiveGooglePlaySubscription = true;
            }
        }
        
        return result;
    }

    /// <inheritdoc />
    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid currentUserId, UpdateUserCreditsInput input)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(input.UserId.ToString());
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();
        var request = new UpdateCreditsRequestProto
        {
            UserId = input.UserId.ToString(),
            OperatorUserId = currentUserId.ToString(),
            CreditsChange = input.Credits
        };
        var response = await userQuotaGAgent.UpdateCreditsAsync(request);
        return new GrainResultDto<int>
        {
            Success = response.Success,
            Message = response.Message,
            Data = response.Data
        };
    }

    /// <inheritdoc />
    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid currentUserId, UpdateUserSubscriptionsInput input)
    {
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(input.UserId.ToString());
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();
        
        var request = new UpdateSubscriptionRequestProto
        {
            UserId = input.UserId.ToString(),
            OperatorUserId = currentUserId.ToString(),
            PlanType = (int)input.PlanType,
            IsUltimate = input.IsUltimate
        };
        
        var response = await userQuotaGAgent.UpdateSubscriptionAsync(request);
        
        // Convert Protobuf response to DTO
        var subscriptionDtos = new List<SubscriptionInfoDto>();
        if (response.Success && response.Data != null)
        {
            foreach (var subscriptionProto in response.Data)
            {
                subscriptionDtos.Add(new SubscriptionInfoDto
                {
                    IsActive = subscriptionProto.IsActive,
                    PlanType = subscriptionProto.PlanType.ToPlanType(),
                    Status = subscriptionProto.Status.ToPaymentStatus(),
                    StartDate = subscriptionProto.StartDate.ToDateTime(),
                    EndDate = subscriptionProto.EndDate.ToDateTime(),
                    SubscriptionIds = subscriptionProto.SubscriptionIds.ToList(),
                    InvoiceIds = subscriptionProto.InvoiceIds.ToList()
                });
            }
        }
        
        return new GrainResultDto<List<SubscriptionInfoDto>>
        {
            Success = response.Success,
            Message = response.Message,
            Data = subscriptionDtos
        };
    }
}
