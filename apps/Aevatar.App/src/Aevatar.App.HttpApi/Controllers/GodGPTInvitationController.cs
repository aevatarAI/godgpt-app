using Aevatar.App.Application.Services;
using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserBilling;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using Aevatar.Service;
using Aevatar.App.HttpApi.Extensions;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using OpenIddict.Abstractions;
using Orleans;
using Stripe;
using Volo.Abp;
using Volo.Abp.Security.Claims;

namespace Aevatar.Controllers;

/// <summary>
/// GodGPT Invitation Controller (Compatibility Layer)
/// Note: For new implementations, use InvitationController (/api/invitation)
/// </summary>
[RemoteService]
[ControllerName("Invitation")]
[Route("api/godgpt/invitation")]
[Authorize]
public class GodGPTInvitationController : AevatarController
{
    private readonly ILogger<GodGPTInvitationController> _logger;
    private readonly IInvitationService _invitationService;

    public GodGPTInvitationController(
        ILogger<GodGPTInvitationController> logger, 
        IInvitationService invitationService)
    {
        _logger = logger;
        _invitationService = invitationService;
    }
    
    [HttpPost("generate-trial-code")]
    public async Task<GenerateCodesResultDto> GenerateFreeTrialCodeAsync(GenerateFreeTrialCodeRequest input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var generateCodesResultDto = await _invitationService.GenerateFreeTrialCodeAsync(currentUserId, input);
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
    
    /// <summary>
    /// Test endpoint: Get invitation info by fixed test ID (temporary, no auth required)
    /// </summary>
    [HttpGet("test")]
    [AllowAnonymous]
    public async Task<GetInvitationInfoResponse> GetInvitationInfoTestAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        // Fixed test ID: 9bcf411b-f21e-f1d9-10cc-3a1aa2a71eb7
        var testUserId = Guid.Parse("9bcf411b-f21e-f1d9-10cc-3a1aa2a71eb7");
        var invitationInfo = await _invitationService.GetInvitationInfoAsync(testUserId);
        _logger.LogDebug("[GodGPTInvitationController][GetInvitationInfoTestAsync] userId: {0}, duration: {1}ms",
            testUserId.ToString(), stopwatch.ElapsedMilliseconds);
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
    
    // NOTE: Twitter endpoints moved to GodGPTTwitterController to avoid PaymentService DI chain
}