using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.GodGPT.Dtos;
using Aevatar.Dtos;

namespace Aevatar.App.Application.Services;

/// <summary>
/// Invitation service - manages invitation codes and rewards
/// Uses InvitationGAgent and InviteCodeGAgent architecture
/// </summary>
public interface IInvitationService
{
    /// <summary>
    /// Get invitation information for a user
    /// </summary>
    Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid userId);
    
    /// <summary>
    /// Redeem an invite code
    /// </summary>
    Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid userId, RedeemInviteCodeRequest request);
    
    /// <summary>
    /// Get invitation code type
    /// </summary>
    Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid userId, GetInvitationCodeTypeRequest request);
    
    /// <summary>
    /// Generate free trial code
    /// </summary>
    Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(Guid userId, GenerateFreeTrialCodeRequest request);
    
    /// <summary>
    /// Get batch info for free trial code
    /// </summary>
    Task<BatchInfoDto> GetBatchInfoAsync(string batchId);
    
    /// <summary>
    /// Get credits history with pagination
    /// </summary>
    Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid userId, GetCreditsHistoryInput input);
}

