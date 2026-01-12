using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.GodGPT.Dtos;
using Volo.Abp.Application.Services;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// Service interface for Twitter-related operations
/// </summary>
public interface ITwitterService : IApplicationService
{
    // ==========================================
    // User-Level Twitter Operations (OAuth2)
    // ==========================================
    
    /// <summary>
    /// Get Twitter OAuth2 PKCE authentication parameters
    /// </summary>
    Task<TwitterAuthParamsDto> GetAuthParamsAsync(Guid userId);
    
    /// <summary>
    /// Verify Twitter OAuth2 authorization code and bind account
    /// </summary>
    Task<TwitterAuthResultDto> VerifyAuthCodeAsync(Guid userId, TwitterAuthVerifyInput input);
    
    /// <summary>
    /// Get current Twitter account bind status
    /// </summary>
    Task<TwitterBindStatusDto> GetBindStatusAsync(Guid userId);
    
    /// <summary>
    /// Unbind Twitter account from user
    /// </summary>
    Task<TwitterOperationResultDto> UnbindTwitterAsync(Guid userId);
    
    // ==========================================
    // Management-Level Twitter Operations
    // ==========================================
    
    // Monitor Management
    
    /// <summary>
    /// Manually trigger tweets fetching
    /// </summary>
    Task<TwitterOperationResultDto> FetchTweetsManuallyAsync();
    
    /// <summary>
    /// Refetch tweets by specified time range
    /// </summary>
    Task<TwitterOperationResultDto> RefetchTweetsByTimeRangeAsync(long startTimeUtcSecond, long endTimeUtcSecond);
    
    /// <summary>
    /// Start automatic tweet monitoring task
    /// </summary>
    Task<TwitterOperationResultDto> StartTweetMonitoringAsync();
    
    /// <summary>
    /// Stop automatic tweet monitoring task
    /// </summary>
    Task<TwitterOperationResultDto> StopTweetMonitoringAsync();
    
    /// <summary>
    /// Get current status of tweet monitoring task
    /// </summary>
    Task<TwitterMonitorStatusDto> GetTweetMonitoringStatusAsync();
    
    // Reward Management
    
    /// <summary>
    /// Manually trigger reward calculation for specific date
    /// </summary>
    Task<TwitterOperationResultDto> TriggerRewardCalculationAsync(long targetDateUtcSeconds);
    
    /// <summary>
    /// Clear reward records for specific date
    /// </summary>
    Task<TwitterOperationResultDto> ClearRewardByDayAsync(long targetDateUtcSeconds);
    
    /// <summary>
    /// Start automatic reward calculation task
    /// </summary>
    Task<TwitterOperationResultDto> StartRewardCalculationAsync();
    
    /// <summary>
    /// Stop automatic reward calculation task
    /// </summary>
    Task<TwitterOperationResultDto> StopRewardCalculationAsync();
    
    /// <summary>
    /// Get reward calculation status
    /// </summary>
    Task<TwitterRewardStatusDto> GetRewardCalculationStatusAsync();
    
    /// <summary>
    /// Get user rewards by user ID
    /// </summary>
    Task<Dictionary<string, List<ManagerUserRewardRecordDto>>> GetUserRewardsByUserIdAsync(string userId);
    
    /// <summary>
    /// Get full calculation history list
    /// </summary>
    Task<List<ManagerRewardCalculationHistoryDto>> GetCalculationHistoryListAsync();
}

// ==========================================
// DTOs for Twitter Operations
// ==========================================

/// <summary>
/// Twitter OAuth2 PKCE authentication parameters
/// </summary>
public class TwitterAuthParamsDto
{
    public string AuthorizationUrl { get; set; } = string.Empty;
    public string CodeVerifier { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

/// <summary>
/// Result of Twitter OAuth2 authentication verification
/// </summary>
public class TwitterAuthResultDto
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? TwitterUserId { get; set; }
    public string? TwitterUsername { get; set; }
}

/// <summary>
/// Twitter account bind status
/// </summary>
public class TwitterBindStatusDto
{
    public bool IsBound { get; set; }
    public string? TwitterUserId { get; set; }
    public string? TwitterUsername { get; set; }
    public string? ProfileImageUrl { get; set; }
}

/// <summary>
/// Twitter monitor status
/// </summary>
public class TwitterMonitorStatusDto
{
    public bool IsRunning { get; set; }
    public DateTime? LastFetchTime { get; set; }
    public int TotalTweetsFetched { get; set; }
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Twitter reward calculation status
/// </summary>
public class TwitterRewardStatusDto
{
    public bool IsRunning { get; set; }
    public DateTime? LastCalculationTime { get; set; }
    public int TotalCalculations { get; set; }
    public string? ErrorMessage { get; set; }
}
