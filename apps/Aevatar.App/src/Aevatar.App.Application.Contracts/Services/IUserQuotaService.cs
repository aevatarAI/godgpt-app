using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.UserQuota;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User Quota service - manages user credits, subscriptions, and quota limits
/// </summary>
public interface IUserQuotaService
{
    /// <summary>
    /// Set shown credits toast flag
    /// </summary>
    Task SetShownCreditsToastAsync(Guid userId, bool hasShownInitialCreditsToast);
    
    /// <summary>
    /// Update user credits (admin operation)
    /// </summary>
    Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid userId, UpdateUserCreditsInput input);
    
    /// <summary>
    /// Update user subscription (admin operation)
    /// </summary>
    Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid userId, UpdateUserSubscriptionsInput input);
    
    /// <summary>
    /// Check if user can upload image
    /// </summary>
    Task<ExecuteActionResultDto> CanUploadImageAsync(Guid userId);
}

