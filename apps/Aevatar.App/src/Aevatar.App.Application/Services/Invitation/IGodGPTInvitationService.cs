using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Invitation;
using Aevatar.GodGPT.Dtos;


namespace Aevatar.App.Application.Services.Invitation;

/// <summary>
/// Service interface for managing invitation and referral system.
/// </summary>
public interface IGodGPTInvitationService
{
    /// <summary>
    /// Gets invitation information for a user including invite code, stats, and reward tiers.
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <returns>Invitation info response</returns>
    Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid currentUserId);

    /// <summary>
    /// Redeems an invite code for a user.
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="input">Redeem request with invite code</param>
    /// <returns>Redeem result response</returns>
    Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid currentUserId, RedeemInviteCodeRequest input);

    /// <summary>
    /// Gets the type of an invitation code.
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="input">Request with invite code</param>
    /// <returns>Code type response</returns>
    Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid currentUserId, GetInvitationCodeTypeRequest input);

    /// <summary>
    /// Gets the credits/reward history for a user.
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="input">Pagination input</param>
    /// <returns>Paged list of reward history</returns>
    Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid currentUserId, GetCreditsHistoryInput input);
}
