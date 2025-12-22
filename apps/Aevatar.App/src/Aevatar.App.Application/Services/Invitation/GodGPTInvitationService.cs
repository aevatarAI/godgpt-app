using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserBilling;
using Aevatar.GodGPT.Dtos;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace Aevatar.App.Application.Services.Invitation;

/// <summary>
/// Service implementation for managing invitation and referral system.
/// Handles invite codes, redemption, and reward history through InvitationGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTInvitationService : ApplicationService, IGodGPTInvitationService
{
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<GodGPTInvitationService> _logger;

    public GodGPTInvitationService(
        IGAgentFactory agentFactory,
        ILogger<GodGPTInvitationService> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<GetInvitationInfoResponse> GetInvitationInfoAsync(Guid currentUserId)
    {
        var invitationAgent = _agentFactory.CreateGAgent<InvitationGAgent>(currentUserId);
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        var invitationStatsDto = await invitationAgent.GetInvitationStatsAsync();
        var rewardTierDtos = await invitationAgent.GetRewardTiersAsync();
        
        return new GetInvitationInfoResponse
        {
            InviteCode = inviteCode,
            TotalInvites = invitationStatsDto.TotalInvites,
            ValidInvites = invitationStatsDto.ValidInvites,
            TotalCreditsEarned = invitationStatsDto.TotalCreditsEarned,
            RewardTiers = rewardTierDtos,
            TotalCreditsFromX = invitationStatsDto.TotalCreditsFromX,
            IsBound = invitationStatsDto.IsBound
        };
    }

    /// <inheritdoc />
    public async Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(Guid currentUserId, RedeemInviteCodeRequest input)
    {
        var codeType = InvitationCodeHelper.GetCodeType(input.InviteCode) ?? InvitationCodeType.FriendInvitation;
        
        if (codeType == InvitationCodeType.FriendInvitation)
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(currentUserId);
            var result = await manager.RedeemInviteCodeAsync(input.InviteCode);
            return new RedeemInviteCodeResponse
            {
                IsValid = result,
                CodeType = codeType
            };
        }
        else if (codeType == InvitationCodeType.FreeTrialReward)
        {
            if (!input.IsWeb)
            {
                _logger.LogWarning("FreeTrialCode web only, IsWeb: {IsWeb}", input.IsWeb);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = InvitationCodeType.FreeTrialReward,
                    URL = null
                };
            }
            
            try
            {
                var userBillingGAgent = _agentFactory.CreateGAgent<UserBillingGAgent>(currentUserId);
                var url = await userBillingGAgent.CreateCheckoutSessionAsync(new CreateCheckoutSessionDto
                {
                    UserId = currentUserId.ToString(),
                    PriceId = null,
                    Quantity = 1,
                    TrialCode = input.InviteCode
                });
                return new RedeemInviteCodeResponse
                {
                    IsValid = true,
                    CodeType = codeType,
                    URL = url
                };
            }
            catch (Exception e)
            {
                _logger.LogError(e, "[GodGPTInvitationService][RedeemInviteCodeAsync] {UserId} invalid InviteCode {Code}",
                    currentUserId.ToString(), input.InviteCode);
                return new RedeemInviteCodeResponse
                {
                    IsValid = false,
                    CodeType = codeType
                };
            }
        }
        else
        {
            _logger.LogError("[GodGPTInvitationService][RedeemInviteCodeAsync] {UserId} Unknown code type. {Code}",
                currentUserId.ToString(), input.InviteCode);
            return new RedeemInviteCodeResponse
            {
                IsValid = false
            };
        }
    }

    /// <inheritdoc />
    public Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(Guid currentUserId, GetInvitationCodeTypeRequest input)
    {
        var codeType = InvitationCodeHelper.GetCodeType(input.InviteCode);
        return Task.FromResult(new GetInvitationCodeTypeResponse
        {
            CodeType = codeType ?? InvitationCodeType.FriendInvitation
        });
    }

    /// <inheritdoc />
    public async Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(Guid currentUserId, GetCreditsHistoryInput input)
    {
        var invitationAgent = _agentFactory.CreateGAgent<InvitationGAgent>(currentUserId);
        var rewardHistoryDtos = await invitationAgent.GetRewardHistoryAsync(new GetRewardHistoryRequestDto
        {
            PageNo = input.Page,
            PageSize = input.PageSize
        });
        return rewardHistoryDtos;
    }
}
