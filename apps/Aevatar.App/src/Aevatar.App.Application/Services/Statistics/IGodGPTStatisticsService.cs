using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Dtos;

namespace Aevatar.App.Application.Services.Statistics;

/// <summary>
/// Service interface for managing user statistics and app rating operations.
/// </summary>
public interface IGodGPTStatisticsService
{
    /// <summary>
    /// Records an app rating from a user.
    /// </summary>
    /// <param name="currentUserId">The ID of the user submitting the rating</param>
    /// <param name="input">The rating input containing device and platform information</param>
    /// <returns>The recorded app rating information</returns>
    Task<AppRatingRecordDto> RecordAppRatingAsync(Guid currentUserId, RecordAppRatingInput input);

    /// <summary>
    /// Checks if a user is eligible to rate the app.
    /// </summary>
    /// <param name="currentUserId">The ID of the user</param>
    /// <param name="input">Input containing device information</param>
    /// <returns>True if the user can rate the app, false otherwise</returns>
    Task<bool> CanUserRateAppAsync(Guid currentUserId, CanUserRateAppInput input);
}
