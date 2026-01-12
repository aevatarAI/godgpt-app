using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.GodGPT.Dtos;
using Aevatar.Service;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for Twitter system management operations (monitoring and rewards)
/// These are system-level operations that require administrative privileges
/// </summary>
[RemoteService]
[ControllerName("TwitterManagement")]
[Route("api/godgpt/twitter-management")]
[Authorize]
public class GodGPTTwitterManagementController : AevatarController
{
    private readonly ILogger<GodGPTTwitterManagementController> _logger;
    private readonly ITwitterService _twitterService;
    private readonly IGodGPTService _godGptService;

    public GodGPTTwitterManagementController(
        ILogger<GodGPTTwitterManagementController> logger,
        ITwitterService twitterService,
        IGodGPTService godGptService)
    {
        _logger = logger;
        _twitterService = twitterService;
        _godGptService = godGptService;
    }

    // ==========================================
    // Twitter Monitor Management Endpoints
    // ==========================================

    /// <summary>
    /// Manually trigger tweets fetching with default configuration
    /// </summary>
    [HttpPost("monitor/fetch-manually")]
    public async Task<TwitterOperationResultDto> FetchTweetsManuallyAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][FetchTweetsManuallyAsync] Starting manual tweet fetch");
        
        var result = await _twitterService.FetchTweetsManuallyAsync();
        
        _logger.LogDebug("[TwitterManagement][FetchTweetsManuallyAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Refetch tweets by specified time range
    /// </summary>
    [HttpPost("monitor/refetch-by-time-range")]
    public async Task<TwitterOperationResultDto> RefetchTweetsByTimeRangeAsync(
        [FromQuery] long startTimeUtcSecond, 
        [FromQuery] long endTimeUtcSecond)
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][RefetchTweetsByTimeRangeAsync] Refetch from {Start} to {End}",
            startTimeUtcSecond, endTimeUtcSecond);
        
        var result = await _twitterService.RefetchTweetsByTimeRangeAsync(startTimeUtcSecond, endTimeUtcSecond);
        
        _logger.LogDebug("[TwitterManagement][RefetchTweetsByTimeRangeAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Start automatic tweet monitoring task
    /// </summary>
    [HttpPost("monitor/start")]
    public async Task<TwitterOperationResultDto> StartTweetMonitoringAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][StartTweetMonitoringAsync] Starting tweet monitoring task");
        
        var result = await _twitterService.StartTweetMonitoringAsync();
        
        _logger.LogDebug("[TwitterManagement][StartTweetMonitoringAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Stop automatic tweet monitoring task
    /// </summary>
    [HttpPost("monitor/stop")]
    public async Task<TwitterOperationResultDto> StopTweetMonitoringAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][StopTweetMonitoringAsync] Stopping tweet monitoring task");
        
        var result = await _twitterService.StopTweetMonitoringAsync();
        
        _logger.LogDebug("[TwitterManagement][StopTweetMonitoringAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Get current status of tweet monitoring task
    /// </summary>
    [HttpGet("monitor/status")]
    public async Task<TwitterMonitorStatusDto> GetTweetMonitoringStatusAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("[TwitterManagement][GetTweetMonitoringStatusAsync] Getting tweet monitoring status");
        
        var result = await _twitterService.GetTweetMonitoringStatusAsync();
        
        _logger.LogDebug("[TwitterManagement][GetTweetMonitoringStatusAsync] completed, duration: {Duration}ms",
            stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    // ==========================================
    // Twitter Reward Management Endpoints
    // ==========================================

    /// <summary>
    /// Manually trigger reward calculation for specific date
    /// </summary>
    [HttpPost("rewards/trigger-calculation")]
    public async Task<TwitterOperationResultDto> TriggerRewardCalculationAsync([FromQuery] long targetDateUtcSeconds)
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][TriggerRewardCalculationAsync] Trigger for date: {Date}",
            targetDateUtcSeconds);
        
        var result = await _twitterService.TriggerRewardCalculationAsync(targetDateUtcSeconds);
        
        _logger.LogDebug("[TwitterManagement][TriggerRewardCalculationAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Clear reward records for specific date (for testing purposes)
    /// </summary>
    [HttpDelete("rewards/clear-by-day")]
    public async Task<TwitterOperationResultDto> ClearRewardByDayAsync([FromQuery] long targetDateUtcSeconds)
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][ClearRewardByDayAsync] Clear for date: {Date}",
            targetDateUtcSeconds);
        
        var result = await _twitterService.ClearRewardByDayAsync(targetDateUtcSeconds);
        
        _logger.LogDebug("[TwitterManagement][ClearRewardByDayAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Start automatic reward calculation task
    /// </summary>
    [HttpPost("rewards/start")]
    public async Task<TwitterOperationResultDto> StartRewardCalculationAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][StartRewardCalculationAsync] Starting reward calculation task");
        
        var result = await _twitterService.StartRewardCalculationAsync();
        
        _logger.LogDebug("[TwitterManagement][StartRewardCalculationAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Stop automatic reward calculation task
    /// </summary>
    [HttpPost("rewards/stop")]
    public async Task<TwitterOperationResultDto> StopRewardCalculationAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][StopRewardCalculationAsync] Stopping reward calculation task");
        
        var result = await _twitterService.StopRewardCalculationAsync();
        
        _logger.LogDebug("[TwitterManagement][StopRewardCalculationAsync] completed with success: {Success}, duration: {Duration}ms",
            result.IsSuccess, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Get reward calculation status
    /// </summary>
    [HttpGet("rewards/status")]
    public async Task<TwitterRewardStatusDto> GetRewardCalculationStatusAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogDebug("[TwitterManagement][GetRewardCalculationStatusAsync] Getting reward status");
        
        var result = await _twitterService.GetRewardCalculationStatusAsync();
        
        _logger.LogDebug("[TwitterManagement][GetRewardCalculationStatusAsync] completed, duration: {Duration}ms",
            stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Get user rewards by user ID
    /// </summary>
    [HttpGet("rewards/user/{userId}")]
    public async Task<Dictionary<string, List<ManagerUserRewardRecordDto>>> GetUserRewardsByUserIdAsync([FromRoute] string userId)
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][GetUserRewardsByUserIdAsync] Getting rewards for user: {UserId}", userId);
        
        var result = await _twitterService.GetUserRewardsByUserIdAsync(userId);
        
        _logger.LogDebug("[TwitterManagement][GetUserRewardsByUserIdAsync] completed, duration: {Duration}ms",
            stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    /// <summary>
    /// Get full calculation history list
    /// </summary>
    [HttpGet("rewards/calculation-history")]
    public async Task<List<ManagerRewardCalculationHistoryDto>> GetCalculationHistoryListAsync()
    {
        await BeforeCheckUserIsManager();
        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation("[TwitterManagement][GetCalculationHistoryListAsync] Getting calculation history");
        
        var result = await _twitterService.GetCalculationHistoryListAsync();
        
        _logger.LogDebug("[TwitterManagement][GetCalculationHistoryListAsync] completed with {Count} records, duration: {Duration}ms",
            result?.Count ?? 0, stopwatch.ElapsedMilliseconds);
        
        return result;
    }

    // ==========================================
    // Helper Methods
    // ==========================================

    private async Task BeforeCheckUserIsManager()
    {
        var currentUserId = (Guid)CurrentUser.Id!;
        if (currentUserId == Guid.Empty)
        {
            throw new SecurityException($"currentUserId is null {currentUserId}");
        }
      
        if (!await _godGptService.CheckIsManager(currentUserId))
        {
            _logger.LogInformation("currentUserId is not manager {UserId}", currentUserId);
            throw new SecurityException($"currentUserId is not manager {currentUserId}");
        }
    }
}
