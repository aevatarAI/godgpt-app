using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services.User;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.ChatManager.Dtos;
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
/// Handles profile and preferences.
/// Note: Credits and subscription endpoints are handled by GodGPTUserQuotaController.
/// </summary>
[RemoteService]
[ControllerName("GodGPTAccount")]
[Route("api")]
[Authorize]
public class GodGPTAccountController : AevatarController
{
    private readonly IGodGPTUserService _userService;
    private readonly ILogger<GodGPTAccountController> _logger;

    public GodGPTAccountController(
        IGodGPTUserService userService,
        ILogger<GodGPTAccountController> logger)
    {
        _userService = userService;
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
