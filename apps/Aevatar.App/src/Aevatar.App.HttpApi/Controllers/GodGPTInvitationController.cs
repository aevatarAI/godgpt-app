using Aevatar.App.Application.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.Application.Contracts.Services;
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
    private readonly ITwitterService _twitterService;

    public GodGPTInvitationController(
        ILogger<GodGPTInvitationController> logger, 
        IInvitationService invitationService,
        ITwitterService twitterService)
    {
        _logger = logger;
        _invitationService = invitationService;
        _twitterService = twitterService;
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
    
    // ==========================================
    // Twitter OAuth2 Endpoints
    // ==========================================
    
    /// <summary>
    /// Get Twitter OAuth2 PKCE authentication parameters
    /// </summary>
    [HttpGet("twitter/params")]
    public async Task<TwitterAuthParamsDto> GetTwitterAuthParamsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.GetAuthParamsAsync(currentUserId);
        _logger.LogDebug("[GodGPTInvitationController][GetTwitterAuthParamsAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Verify Twitter OAuth2 authorization code and bind account
    /// </summary>
    [HttpPost("twitter/verify")]
    public async Task<TwitterAuthResultDto> TwitterAuthVerifyAsync(TwitterAuthVerifyInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.VerifyAuthCodeAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTInvitationController][TwitterAuthVerifyAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Get current Twitter account bind status
    /// </summary>
    [HttpGet("twitter/bind-status")]
    public async Task<TwitterBindStatusDto> GetTwitterBindStatusAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.GetBindStatusAsync(currentUserId);
        _logger.LogDebug("[GodGPTInvitationController][GetTwitterBindStatusAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Unbind Twitter account from user
    /// </summary>
    [HttpPost("twitter/unbind")]
    public async Task<TwitterOperationResultDto> UnbindTwitterAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.UnbindTwitterAsync(currentUserId);
        _logger.LogDebug("[GodGPTInvitationController][UnbindTwitterAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
}