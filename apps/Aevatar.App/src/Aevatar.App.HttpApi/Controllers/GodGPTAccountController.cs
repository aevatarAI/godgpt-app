using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services.User;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
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
    public async Task<UserProfileApiResponse> GetUserProfileAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var userProfileDto = await _userService.GetUserProfileAsync(currentUserId);
        _logger.LogDebug("[GodGPTAccountController][GetUserProfileAsync] userId: {0}, duration: {1}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return ToApiResponse(userProfileDto);
    }
    
    private static UserProfileApiResponse ToApiResponse(UserProfileDto dto)
    {
        return new UserProfileApiResponse
        {
            Gender = dto.Gender,
            BirthDate = dto.BirthDate,
            BirthPlace = dto.BirthPlace,
            FullName = dto.FullName,
            Credits = dto.Credits,
            Subscription = ToSubscriptionApiResponse(dto.Subscription),
            UltimateSubscription = ToSubscriptionApiResponse(dto.UltimateSubscription),
            Id = dto.Id,
            InviterId = dto.InviterId,
            VoiceLanguage = dto.VoiceLanguage,
            IsFirstConversation = dto.IsFirstConversation
        };
    }
    
    private static SubscriptionApiResponse? ToSubscriptionApiResponse(Aevatar.Agents.GodGPT.Protos.UserQuota.SubscriptionInfoProto? proto)
    {
        if (proto == null) return null;
        return new SubscriptionApiResponse
        {
            IsActive = proto.IsActive,
            PlanType = (int)proto.PlanType,
            Status = (int)proto.Status,
            StartDate = proto.StartDate?.ToDateTime(),
            EndDate = proto.EndDate?.ToDateTime(),
            SubscriptionIds = proto.SubscriptionIds?.ToList() ?? new List<string>(),
            InvoiceIds = proto.InvoiceIds?.ToList() ?? new List<string>()
        };
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
    /// Handles Apple Private Relay and third-party login (Apple/Google) users.
    /// </summary>
    [HttpGet("profile/user-info")]
    public async Task<BasicUserInfoDto> GetUserInfoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = (Guid)CurrentUser.Id!;
        _logger.LogDebug("[GodGPTAccountController][GetUserInfoAsync] UserId: {UserId}", userId);
        
        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            _logger.LogWarning("[GodGPTAccountController][GetUserInfoAsync] User not found: {UserId}", userId);
            return new BasicUserInfoDto { Uid = userId };
        }
        
        // Try to get Apple provided name from ExtraProperties
        string? fullName = null;
        if (user.ExtraProperties.TryGetValue("AppleFullName", out var appleFullName))
        {
            fullName = appleFullName?.ToString();
        }
        if (string.IsNullOrWhiteSpace(fullName))
        {
            var firstName = user.ExtraProperties.TryGetValue("AppleFirstName", out var fn) ? fn?.ToString() : "";
            var lastName = user.ExtraProperties.TryGetValue("AppleLastName", out var ln) ? ln?.ToString() : "";
            fullName = $"{firstName} {lastName}".Trim();
            if (string.IsNullOrWhiteSpace(fullName)) fullName = null;
        }
        
        // Check for Apple Private Relay (privacy protection)
        if (IsApplePrivateRelay(user))
        {
            _logger.LogDebug("[GodGPTAccountController][GetUserInfoAsync] Apple Private Relay user: {UserId}", userId);
            return new BasicUserInfoDto
            {
                Uid = userId,
                Email = null, // Privacy protection - no email for private relay
                Avatar = null,
                Name = string.IsNullOrWhiteSpace(fullName) ? "Profile" : fullName
            };
        }
        
        // Extract real email based on login type
        var email = ExtractRealEmail(user.UserName, user.Email);
        
        // Extract display name
        var displayName = ExtractDisplayName(user.UserName, email, fullName);
        
        var result = new BasicUserInfoDto
        {
            Uid = userId,
            Email = email,
            Avatar = null,
            Name = displayName
        };
        
        _logger.LogDebug("[GodGPTAccountController][GetUserInfoAsync] UserId: {UserId}, duration: {Duration}ms",
            userId, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
    
    #region User Info Helpers
    
    /// <summary>
    /// Check if the user is using Apple's private relay
    /// </summary>
    private static bool IsApplePrivateRelay(IdentityUser user)
    {
        if (user.UserName?.EndsWith("@apple.privaterelay.com@apple", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (user.Email?.Contains("@apple.privaterelay.com", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        if (user.ExtraProperties.ContainsKey("AppleFirstName") || 
            user.ExtraProperties.ContainsKey("AppleLastName") ||
            user.ExtraProperties.ContainsKey("AppleFullName"))
            return true;
        return false;
    }
    
    /// <summary>
    /// Extract real email address based on login type (handles @apple/@google suffix)
    /// </summary>
    private static string? ExtractRealEmail(string? userName, string? systemEmail)
    {
        if (string.IsNullOrEmpty(userName)) return systemEmail;
        
        // Check if it's third-party login (Apple/Google)
        if (userName.EndsWith("@apple", StringComparison.OrdinalIgnoreCase))
            return userName[..^"@apple".Length];
        if (userName.EndsWith("@google", StringComparison.OrdinalIgnoreCase))
            return userName[..^"@google".Length];
        
        return systemEmail;
    }
    
    /// <summary>
    /// Extract display name based on available information
    /// </summary>
    private static string ExtractDisplayName(string? userName, string? email, string? fullName)
    {
        // Priority 1: Use fullName if available
        if (!string.IsNullOrWhiteSpace(fullName))
            return fullName;
        
        // Priority 2: For third-party login, extract from email
        if (userName?.EndsWith("@apple", StringComparison.OrdinalIgnoreCase) == true ||
            userName?.EndsWith("@google", StringComparison.OrdinalIgnoreCase) == true)
        {
            return GetDisplayNameFromEmail(email);
        }
        
        // Priority 3: For regular login with custom username (not GUID), use username
        if (!string.IsNullOrEmpty(userName) && !Guid.TryParse(userName, out _))
            return userName;
        
        // Priority 4: Extract from email as fallback
        return GetDisplayNameFromEmail(email);
    }
    
    /// <summary>
    /// Extract display name from email address (part before @)
    /// </summary>
    private static string GetDisplayNameFromEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return email ?? "User";
        return email[..email.IndexOf('@')];
    }
    
    #endregion

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

/// <summary>
/// User profile API response with ISO8601 dates
/// </summary>
public class UserProfileApiResponse
{
    public string? Gender { get; set; }
    public DateTime BirthDate { get; set; }
    public string? BirthPlace { get; set; }
    public string? FullName { get; set; }
    public CreditsInfoDto? Credits { get; set; }
    public SubscriptionApiResponse? Subscription { get; set; }
    public SubscriptionApiResponse? UltimateSubscription { get; set; }
    public Guid Id { get; set; }
    public Guid? InviterId { get; set; }
    public VoiceLanguageEnum VoiceLanguage { get; set; }
    public bool? IsFirstConversation { get; set; }
}

/// <summary>
/// Subscription info with DateTime (serializes as ISO8601)
/// </summary>
public class SubscriptionApiResponse
{
    public bool IsActive { get; set; }
    public int PlanType { get; set; }
    public int Status { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public List<string> SubscriptionIds { get; set; } = new();
    public List<string> InvoiceIds { get; set; } = new();
}
