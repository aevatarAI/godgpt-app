using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Dtos;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// User Statistics service - manages user behavior statistics including app ratings
/// </summary>
public interface IUserStatisticsService
{
    /// <summary>
    /// Record app rating for a user
    /// </summary>
    Task<AppRatingRecordDto> RecordAppRatingAsync(Guid userId, RecordAppRatingInput input);
    
    /// <summary>
    /// Check if user can rate the app
    /// </summary>
    Task<bool> CanUserRateAppAsync(Guid userId, CanUserRateAppInput input);
    
    /// <summary>
    /// Get user statistics
    /// </summary>
    Task<UserStatisticsDto> GetUserStatisticsAsync(Guid userId);
    
    /// <summary>
    /// Get app rating records
    /// </summary>
    Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(Guid userId, string? deviceId = null);
}

