using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Google.Protobuf.WellKnownTypes;
using MigrationTool.Models;

namespace MigrationTool.Converters.UserStatistics;

/// <summary>
/// Converter for UserStatistics state migration
/// Converts between old C# state and new Protobuf state
/// </summary>
public class UserStatisticsConverter : IStateConverter<OldUserStatisticsState, UserStatisticsState>
{
    /// <summary>
    /// Convert old C# state to new Protobuf state
    /// </summary>
    public UserStatisticsState Convert(OldUserStatisticsState old)
    {
        var newState = new UserStatisticsState
        {
            UserId = old.UserId.ToString(),
            IsInitialized = old.IsInitialized,
            IsRealUser = old.IsRealUser
        };

        // Convert Dictionary<string, OldAppRatingInfo> to map<string, AppRatingInfo>
        if (old.AppRatings != null)
        {
            foreach (var kvp in old.AppRatings)
            {
                newState.AppRatings[kvp.Key] = ConvertAppRatingInfo(kvp.Value);
            }
        }

        return newState;
    }

    /// <summary>
    /// Convert AppRatingInfo from old to new format
    /// </summary>
    private static AppRatingInfo ConvertAppRatingInfo(OldAppRatingInfo old)
    {
        return new AppRatingInfo
        {
            Platform = old.Platform ?? string.Empty,
            DeviceId = old.DeviceId ?? string.Empty,
            FirstRatingTime = ToTimestamp(old.FirstRatingTime),
            LastRatingTime = ToTimestamp(old.LastRatingTime),
            RatingCount = old.RatingCount
        };
    }

    /// <summary>
    /// Convert DateTime to Protobuf Timestamp (ensuring UTC)
    /// </summary>
    private static Timestamp ToTimestamp(DateTime dateTime)
    {
        // Ensure UTC for Protobuf Timestamp
        var utc = dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        
        // Handle edge cases
        if (utc < DateTime.UnixEpoch)
        {
            return Timestamp.FromDateTime(DateTime.UnixEpoch);
        }
        
        return Timestamp.FromDateTime(utc);
    }

    /// <summary>
    /// Convert new Protobuf state back to old C# state (for verification)
    /// </summary>
    public OldUserStatisticsState ConvertBack(UserStatisticsState newState)
    {
        var old = new OldUserStatisticsState
        {
            UserId = string.IsNullOrEmpty(newState.UserId) 
                ? Guid.Empty 
                : Guid.Parse(newState.UserId),
            IsInitialized = newState.IsInitialized,
            IsRealUser = newState.IsRealUser,
            AppRatings = new Dictionary<string, OldAppRatingInfo>()
        };

        foreach (var kvp in newState.AppRatings)
        {
            old.AppRatings[kvp.Key] = new OldAppRatingInfo
            {
                Platform = kvp.Value.Platform,
                DeviceId = kvp.Value.DeviceId,
                FirstRatingTime = kvp.Value.FirstRatingTime?.ToDateTime() ?? DateTime.MinValue,
                LastRatingTime = kvp.Value.LastRatingTime?.ToDateTime() ?? DateTime.MinValue,
                RatingCount = kvp.Value.RatingCount
            };
        }

        return old;
    }
}
