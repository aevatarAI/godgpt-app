using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Analytics;
using Aevatar.App.Application.Services.Statistics;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Dtos;
using Aevatar.App.Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT analytics and user statistics.
/// Handles event tracking (GA/Firebase) and app rating.
/// </summary>
[RemoteService]
[ControllerName("GodGPTAnalytics")]
[Route("api")]
[Authorize]
public class GodGPTAnalyticsController : AevatarController
{
    private readonly IGodGPTStatisticsService _statisticsService;
    private readonly ILogger<GodGPTAnalyticsController> _logger;
    private readonly IGoogleAnalyticsService _googleAnalyticsService;

    public GodGPTAnalyticsController(
        IGodGPTStatisticsService statisticsService,
        ILogger<GodGPTAnalyticsController> logger,
        IGoogleAnalyticsService googleAnalyticsService)
    {
        _statisticsService = statisticsService;
        _logger = logger;
        _googleAnalyticsService = googleAnalyticsService;
    }

    /// <summary>
    /// Track event to Google Analytics (gtag)
    /// </summary>
    /// <param name="request">GA event request</param>
    /// <returns>Tracking result</returns>
    [HttpPost("godgpt/analytics/track/gtag")]
    public async Task<IActionResult> TrackAnalyticsEventAsync(GoogleAnalyticsEventRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (CurrentUser?.Id != null)
            {
                request.UserId = CurrentUser.Id.ToString();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _googleAnalyticsService.TrackEventAsync(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GodGPTAnalyticsController][TrackAnalyticsEventAsync] Background GA tracking failed for event: {EventName}",
                        request.EventName);
                }
            });

            _logger.LogDebug("[GodGPTAnalyticsController][TrackAnalyticsEventAsync] Event queued: {EventName}, ClientId: {ClientId}, UserId: {UserId}, duration: {Duration}ms",
                request.EventName, request.ClientId, request.UserId, stopwatch.ElapsedMilliseconds);

            return Ok(new GoogleAnalyticsEventResponseDto
            {
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTAnalyticsController][TrackAnalyticsEventAsync] Error processing analytics event: {EventName}",
                request.EventName);

            return StatusCode(500, new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Track event to Firebase Analytics
    /// </summary>
    /// <param name="request">Firebase event request</param>
    /// <returns>Tracking result</returns>
    [HttpPost("godgpt/analytics/track")]
    public async Task<IActionResult> TrackFirebaseAnalyticsEventAsync(GoogleAnalyticsEventRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            if (CurrentUser?.Id != null)
            {
                request.UserId = CurrentUser.Id.ToString();
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await _googleAnalyticsService.TrackFirebaseEventAsync(request);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GodGPTAnalyticsController][TrackFirebaseAnalyticsEventAsync] Background Firebase tracking failed for event: {EventName}",
                        request.EventName);
                }
            });

            _logger.LogDebug("[GodGPTAnalyticsController][TrackFirebaseAnalyticsEventAsync] Firebase event queued: {EventName}, UserId: {UserId}, duration: {Duration}ms",
                request.EventName, request.UserId, stopwatch.ElapsedMilliseconds);

            return Ok(new GoogleAnalyticsEventResponseDto
            {
                Success = true
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTAnalyticsController][TrackFirebaseAnalyticsEventAsync] Error processing Firebase analytics event: {EventName}",
                request.EventName);

            return StatusCode(500, new GoogleAnalyticsEventResponseDto
            {
                Success = false,
                ErrorMessage = "Internal server error"
            });
        }
    }

    /// <summary>
    /// Record user's app rating
    /// </summary>
    [HttpPost("godgpt/user-statistics/app-rating")]
    public async Task<AppRatingRecordDto> RecordAppRatingAsync(RecordAppRatingInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _statisticsService.RecordAppRatingAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTAnalyticsController][RecordAppRatingAsync] userId: {0}, duration: {1}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }

    /// <summary>
    /// Check if user can rate the app
    /// </summary>
    [HttpGet("godgpt/user-statistics/can-rate")]
    public async Task<bool> CanUserRateAppAsync(CanUserRateAppInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _statisticsService.CanUserRateAppAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTAnalyticsController][CanUserRateAppAsync] userId: {0}, duration: {1}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
}
