using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Analytics;
using Aevatar.App.Application.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT analytics event tracking.
/// Handles Google Analytics (gtag) and Firebase Analytics event tracking.
/// Note: User statistics endpoints are handled by GodGPTUserStatisticsController.
/// </summary>
[RemoteService]
[ControllerName("GodGPTAnalytics")]
[Route("api")]
[Authorize]
public class GodGPTAnalyticsController : AevatarController
{
    private readonly ILogger<GodGPTAnalyticsController> _logger;
    private readonly IGoogleAnalyticsService _googleAnalyticsService;

    public GodGPTAnalyticsController(
        ILogger<GodGPTAnalyticsController> logger,
        IGoogleAnalyticsService googleAnalyticsService)
    {
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
}
