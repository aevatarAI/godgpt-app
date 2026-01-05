using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen;
using Aevatar.App.Lumen.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers.Lumen;

/// <summary>
/// Lumen API Controller - Daily Lumen Prediction
/// </summary>
[RemoteService]
[Route("api/lumen")]
[Authorize]
public partial class LumenController : AppController
{
    private readonly ILumenService _lumenService;
    private readonly ILogger<LumenController> _logger;

    public LumenController(
        ILumenService lumenService,
        ILogger<LumenController> logger)
    {
        _lumenService = lumenService;
        _logger = logger;
    }

    /// <summary>
    /// Get current authenticated user ID
    /// </summary>
    private string GetCurrentUserId()
    {
        if (CurrentUser?.Id == null)
        {
            throw new UserFriendlyException("User not authenticated");
        }
        return CurrentUser.Id.ToString()!;
    }

    /// <summary>
    /// Get language code from header
    /// </summary>
    private string GetLanguageCode()
    {
        var acceptLanguage = HttpContext.Request.Headers["Accept-Language"].FirstOrDefault();
        return acceptLanguage?.ToLower() switch
        {
            var lang when lang?.Contains("zh-tw") == true => "zh-tw",
            var lang when lang?.Contains("zh") == true => "zh",
            var lang when lang?.Contains("es") == true => "es",
            _ => "en"
        };
    }

    #region User Management

    /// <summary>
    /// Register/update Lumen user profile
    /// </summary>
    [HttpPost("user/profile")]
    public virtual async Task<UpdateUserProfileResult> UpdateUserProfileAsync([FromBody] RegisterUserProfileApiRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        var userLanguage = GetLanguageCode();

        try
        {
            _logger.LogDebug("[LumenController][UpdateUserProfileAsync] Updating: {UserId}, Language: {Language}", 
                userId, userLanguage);

            // Convert API request to Protobuf request
            var updateRequest = new UpdateUserProfileRequest
            {
                UserId = userId,
                FullName = request.FullName,
                Gender = request.Gender,
                BirthDate = new DateValue 
                { 
                    Year = request.BirthDate.Year, 
                    Month = request.BirthDate.Month, 
                    Day = request.BirthDate.Day 
                },
                BirthTime = new TimeValue 
                { 
                    Hour = request.BirthTime.Hour, 
                    Minute = request.BirthTime.Minute, 
                    Second = request.BirthTime.Second 
                },
                BirthCity = request.BirthCity,
                LatLong = request.LatLong ?? "",
                CurrentResidence = request.CurrentResidence ?? "",
                Email = request.Email ?? "",
                Occupation = request.Occupation ?? "",
                Icon = request.Icon ?? "",
                CurrentTimeZone = request.CurrentTimeZone ?? "",
                Interests = request.Interests ?? ""
            };

            if (request.MbtiType.HasValue)
                updateRequest.MbtiType = request.MbtiType.Value;
            if (request.RelationshipStatus.HasValue)
                updateRequest.RelationshipStatus = request.RelationshipStatus.Value;
            if (request.CalendarType.HasValue)
                updateRequest.CalendarType = request.CalendarType.Value;
            if (request.InterestsList != null)
                updateRequest.InterestsList.AddRange(request.InterestsList);

            var result = await _lumenService.UpdateUserProfileAsync(updateRequest, userLanguage);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to update profile");
            }

            _logger.LogDebug("[LumenController][UpdateUserProfileAsync] userId: {UserId}, duration: {Duration}ms",
                userId, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][UpdateUserProfileAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to update profile");
        }
    }

    /// <summary>
    /// Get current user profile
    /// </summary>
    [HttpGet("user/profile")]
    public virtual async Task<LumenUserProfileDto> GetUserProfileAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetUserProfileAsync] Getting: {UserId}", userId);

            var result = await _lumenService.GetUserProfileAsync(userId, "en");

            if (!result.Success || result.UserProfile == null)
            {
                throw new UserFriendlyException(result.Message ?? "User profile not found");
            }

            _logger.LogInformation("[LumenController][GetUserProfileAsync] SUCCESS - UserId: {UserId}, Duration: {Duration}ms",
                userId, stopwatch.ElapsedMilliseconds);

            return result.UserProfile;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetUserProfileAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get user profile");
        }
    }

    /// <summary>
    /// Get remaining profile update count
    /// </summary>
    [HttpGet("user/profile/remaining-updates")]
    public virtual async Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync()
    {
        var userId = GetCurrentUserId();
        try
        {
            var result = await _lumenService.GetRemainingUpdatesAsync(userId);
            _logger.LogInformation("[LumenController][GetRemainingUpdatesAsync] UserId: {UserId}, Remaining: {Remaining}/{Max}",
                userId, result.RemainingCount, result.MaxCount);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetRemainingUpdatesAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get remaining updates");
        }
    }

    /// <summary>
    /// Update user's timezone
    /// </summary>
    [HttpPut("user/timezone")]
    public virtual async Task<UpdateTimeZoneResult> UpdateTimeZoneAsync([FromBody] UpdateTimeZoneApiRequest request)
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][UpdateTimeZoneAsync] Updating: {UserId}, TimeZone: {TimeZoneId}", 
                userId, request.TimeZoneId);

            var result = await _lumenService.UpdateTimeZoneAsync(userId, request.TimeZoneId);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to update timezone");
            }

            _logger.LogInformation("[LumenController][UpdateTimeZoneAsync] SUCCESS - UserId: {UserId}, TimeZone: {TimeZoneId}",
                userId, request.TimeZoneId);

            return result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][UpdateTimeZoneAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to update timezone");
        }
    }

    /// <summary>
    /// Clear current user data (for testing)
    /// </summary>
    [HttpDelete("user")]
    public virtual async Task<ClearUserResult> ClearUserAsync()
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][ClearUserAsync] Clearing: {UserId}", userId);
            var result = await _lumenService.ClearUserAsync(userId);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to clear user");
            }

            return result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][ClearUserAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to clear user");
        }
    }

    /// <summary>
    /// Set user language
    /// </summary>
    [HttpPost("user/language")]
    public virtual async Task<SetLanguageResult> SetLanguageAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            var languageCode = GetLanguageCode();
            _logger.LogInformation("[LumenController][SetLanguageAsync] Setting: {UserId}, Language: {Language}", 
                userId, languageCode);

            var result = await _lumenService.SetLanguageAsync(userId, languageCode);

            _logger.LogInformation("[LumenController][SetLanguageAsync] Completed - UserId: {UserId}, Success: {Success}, Duration: {Duration}ms",
                userId, result.Success, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][SetLanguageAsync] Error: {UserId}", userId);
            return new SetLanguageResult
            {
                Success = false,
                Message = "Failed to set language"
            };
        }
    }

    /// <summary>
    /// Get user's language information
    /// </summary>
    [HttpGet("user/language")]
    public virtual async Task<GetLanguageInfoResult> GetLanguageInfoAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            var result = await _lumenService.GetLanguageInfoAsync(userId);
            _logger.LogInformation("[LumenController][GetLanguageInfoAsync] Completed - UserId: {UserId}, Duration: {Duration}ms",
                userId, stopwatch.ElapsedMilliseconds);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetLanguageInfoAsync] Error: {UserId}", userId);
            return new GetLanguageInfoResult
            {
                Success = false,
                Message = "Failed to get language info"
            };
        }
    }

    #endregion
}
