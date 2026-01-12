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
using Volo.Abp.Identity;
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
    private readonly IdentityUserManager _userManager;
    private readonly ILogger<GodGPTAccountController> _logger;

    public GodGPTAccountController(
        IGodGPTUserService userService,
        IdentityUserManager userManager,
        ILogger<GodGPTAccountController> logger)
    {
        _userService = userService;
        _userManager = userManager;
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

    /// <summary>
    /// Get basic user information (ProfileController endpoint).
    /// Returns uid, email, name, avatar for legacy API compatibility.
    /// </summary>
    [HttpGet("profile/user-info")]
    public async Task<BasicUserInfoDto> GetUserInfoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = (Guid)CurrentUser.Id!;
        _logger.LogDebug("[GodGPTAccountController][GetUserInfoAsync] UserId: {UserId}", userId);
        
        var user = await _userManager.FindByIdAsync(userId.ToString());
        
        var result = new BasicUserInfoDto
        {
            Uid = userId,
            Email = user?.Email,
            Name = user?.UserName,
            Avatar = null // Avatar not stored in Identity, kept for API compatibility
        };
        
        _logger.LogDebug("[GodGPTAccountController][GetUserInfoAsync] UserId: {UserId}, duration: {Duration}ms",
            userId, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Get current user ID (QueryController endpoint)
    /// </summary>
    [HttpGet("query/user-id")]
    public Task<Guid> GetUserId()
    {
        return Task.FromResult((Guid)CurrentUser.Id!);
    }
}

/// <summary>
/// Basic user information DTO for legacy API compatibility.
/// </summary>
public class BasicUserInfoDto
{
    /// <summary>
    /// User ID
    /// </summary>
    public Guid Uid { get; set; }
    
    /// <summary>
    /// User email address
    /// </summary>
    public string? Email { get; set; }
    
    /// <summary>
    /// User display name
    /// </summary>
    public string? Name { get; set; }
    
    /// <summary>
    /// User avatar URL (reserved for future use)
    /// </summary>
    public string? Avatar { get; set; }
}
