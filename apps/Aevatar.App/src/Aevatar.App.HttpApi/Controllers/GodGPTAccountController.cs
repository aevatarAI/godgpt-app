using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Services.Subscription;
using Aevatar.App.Application.Services.User;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Dtos;
using Aevatar.GAgents.AI.Common;
using GodGPT.GAgents.SpeechChat;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Aevatar.GodGPT.Dtos;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT user account management.
/// Handles profile, credits, subscription, and preferences.
/// </summary>
[RemoteService]
[ControllerName("GodGPTAccount")]
[Route("api")]
[Authorize]
public class GodGPTAccountController : AevatarController
{
    private readonly IGodGPTUserService _userService;
    private readonly IGodGPTSubscriptionService _subscriptionService;
    private readonly ILogger<GodGPTAccountController> _logger;

    public GodGPTAccountController(
        IGodGPTUserService userService,
        IGodGPTSubscriptionService subscriptionService,
        ILogger<GodGPTAccountController> logger)
    {
        _userService = userService;
        _subscriptionService = subscriptionService;
        _logger = logger;
    }

    /// <summary>
    /// Get current user's profile
    /// </summary>
    [HttpGet("godgpt/account")]
    public async Task<UserProfileDto> GetUserProfileAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var userProfileDto = await _userService.GetUserProfileAsync(currentUserId);
        _logger.LogDebug("[GodGPTAccountController][GetUserProfileAsync] userId: {0}, duration: {1}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return userProfileDto;
    }

    /// <summary>
    /// Update current user's profile
    /// </summary>
    [HttpPut("godgpt/account")]
    public async Task<Guid> SetUserProfileAsync(SetUserProfileInput userProfile)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var updateUserId = await _userService.SetUserProfileAsync(currentUserId, userProfile);
        _logger.LogDebug("[GodGPTAccountController][SetUserProfileAsync] userId: {0}, duration: {1}ms",
            updateUserId, stopwatch.ElapsedMilliseconds);
        return updateUserId;
    }

    /// <summary>
    /// Delete current user's account
    /// </summary>
    [HttpDelete("godgpt/account")]
    public async Task<Guid> DeleteAccountAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var deleteUserId = await _userService.DeleteAccountAsync(currentUserId);
        _logger.LogDebug("[GodGPTAccountController][DeleteAccountAsync] userId: {0}, duration: {1}ms",
            deleteUserId, stopwatch.ElapsedMilliseconds);
        return deleteUserId;
    }

    /// <summary>
    /// Update show toast status for current user
    /// </summary>
    [HttpPost("godgpt/account/show-toast")]
    public async Task<Guid> UpdateShowToastAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        await _userService.UpdateShowToastAsync(currentUserId);
        _logger.LogDebug("[GodGPTAccountController][UpdateShowToastAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return currentUserId;
    }

    /// <summary>
    /// Update user credits
    /// </summary>
    [HttpPost("godgpt/account/credits")]
    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(UpdateUserCreditsInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var resultDto = await _subscriptionService.UpdateUserCreditsAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTAccountController][UpdateUserCreditsAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return resultDto;
    }

    /// <summary>
    /// Update user subscription
    /// </summary>
    [HttpPost("godgpt/account/subscription")]
    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(UpdateUserSubscriptionsInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var resultDto = await _subscriptionService.UpdateUserSubscriptionAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTAccountController][UpdateUserSubscriptionAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return resultDto;
    }

    /// <summary>
    /// Set voice language preference
    /// </summary>
    [HttpPost("godgpt/voice/set")]
    public async Task<UserProfileDto> SetVoiceLanguageAsync([FromBody] SetVoiceLanguageRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var userProfileDto = new UserProfileDto();
        try
        {
            userProfileDto = await _userService.SetVoiceLanguageAsync(currentUserId, request.VoiceLanguage);
        }
        catch (Exception ex)
        {
            userProfileDto = new UserProfileDto();
            userProfileDto.VoiceLanguage = VoiceLanguageEnum.Unset;
            _logger.LogError($"[GodGPTAccountController][SetVoiceLanguageAsync] exception userId: {currentUserId},voiceLanguage:{request.VoiceLanguage} duration: {stopwatch.ElapsedMilliseconds}ms error:{ex.Message}");
            return userProfileDto;
        }
        _logger.LogDebug($"[GodGPTAccountController][SetVoiceLanguageAsync] userId: {currentUserId},voiceLanguage:{request.VoiceLanguage} duration: {stopwatch.ElapsedMilliseconds}ms");
        return userProfileDto;
    }
}
