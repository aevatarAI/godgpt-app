using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Services.Admin;
using Aevatar.App.Application.Services.Invitation;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

[RemoteService]
[ControllerName("Invitation")]
[Route("api/godgpt/invitation")]
[Authorize]
public class GodGPTInvitationController : AevatarController
{
    private readonly ILogger<GodGPTInvitationController> _logger;
    private readonly IGodGPTInvitationService _invitationService;
    private readonly IGodGPTAdminService _adminService;

    public GodGPTInvitationController(
        ILogger<GodGPTInvitationController> logger, 
        IGodGPTInvitationService invitationService,
        IGodGPTAdminService adminService)
    {
        _logger = logger;
        _invitationService = invitationService;
        _adminService = adminService;
    }
    
    [HttpPost("generate-trial-code")]
    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(GenerateFreeTrialCodeRequest input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var generateCodesResultDto = await _adminService.GenerateFreeTrialCodeAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTInvitationController][GenerateFreeTrialCodeAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return generateCodesResultDto;
    }
    
    [HttpGet("info")]
    public async Task<GetInvitationInfoResponse> GetInvitationInfoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var invitationInfo = await _invitationService.GetInvitationInfoAsync(currentUserId);
        _logger.LogDebug("[GodGPTInvitationController][GetInvitationInfoAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return invitationInfo;
    }
    
    [HttpGet("code-type")]
    public async Task<GetInvitationCodeTypeResponse> GetInvitationCodeTypeAsync(GetInvitationCodeTypeRequest input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var invitationInfo = await _invitationService.GetInvitationCodeTypeAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTInvitationController][GetInvitationCodeTypeAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return invitationInfo;
    }

    [HttpPost("redeem")]
    public async Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(RedeemInviteCodeRequest input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _invitationService.RedeemInviteCodeAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTInvitationController][RedeemInviteCodeAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return response;
    }

    [HttpGet("credits/history")]
    public async Task<PagedResultDto<RewardHistoryDto>> GetCreditsHistoryAsync(GetCreditsHistoryInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _invitationService.GetCreditsHistoryAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTInvitationController][GetCreditsHistoryAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    // NOTE: Twitter endpoints removed - feature deprecated
}