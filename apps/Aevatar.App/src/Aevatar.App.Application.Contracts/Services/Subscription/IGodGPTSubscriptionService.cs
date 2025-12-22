using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.GodGPT.Dtos;

namespace Aevatar.App.Application.Contracts.Services.Subscription;

/// <summary>
/// Service interface for managing subscription status and user quotas.
/// </summary>
public interface IGodGPTSubscriptionService
{
    /// <summary>
    /// Checks if user has an active Apple subscription.
    /// </summary>
    Task<bool> HasActiveAppleSubscriptionAsync(Guid currentUserId);

    /// <summary>
    /// Gets the active subscription status for a user.
    /// </summary>
    Task<ActiveSubscriptionStatusDto> HasActiveSubscriptionAsync(Guid currentUserId);

    /// <summary>
    /// Updates user credits.
    /// </summary>
    Task<GrainResultDto<int>> UpdateUserCreditsAsync(Guid currentUserId, UpdateUserCreditsInput input);

    /// <summary>
    /// Updates user subscription information.
    /// </summary>
    Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(Guid currentUserId, UpdateUserSubscriptionsInput input);
}
