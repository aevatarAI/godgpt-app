using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Core.Abstractions;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.UserStatistics;

public interface IUserStatisticsGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<AppRatingRecordDto> RecordAppRatingAsync(Guid userId, string platform, string deviceId);
    Task<UserStatisticsDto> GetUserStatisticsAsync();
    Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(string? deviceId = null);
    Task<bool> CanUserRateAppAsync(string deviceId);
}

/// <summary>
/// User Statistics GAgent - manages user behavior statistics including app ratings
/// </summary>
[GAgent(nameof(UserStatisticsGAgent))]
public class UserStatisticsGAgent : GAgentBase<UserStatisticsState>, IUserStatisticsGAgent
{
    private readonly IOptionsMonitor<UserStatisticsOptions> _userStatisticsOptions;

    public UserStatisticsGAgent(Guid id, IOptionsMonitor<UserStatisticsOptions> userStatisticsOptions) : base(id)
    {
        _userStatisticsOptions = userStatisticsOptions;
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("manages user behavior statistics including app ratings");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        await InitializeUserStatisticsAsync();
    }

    public async Task<AppRatingRecordDto> RecordAppRatingAsync(Guid userId, string platform, string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Logger.LogWarning("[UserStatisticsGAgent][RecordAppRatingAsync] DeviceId cannot be empty for user: {UserId}", Id);
            return new AppRatingRecordDto();
        }

        try
        {
            var isFirstRating = !State.AppRatings.ContainsKey(deviceId);
            var ratingTime = DateTime.UtcNow;

            Logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] Recording app rating for user: {UserId}, platform: {Platform}, device: {DeviceID}, isFirstRating: {IsFirstRating}", 
                Id, platform, deviceId, isFirstRating);

            RaiseEvent(new RecordAppRatingEvent
            {
                Platform = platform,
                DeviceId = deviceId,
                RatingTime = Timestamp.FromDateTime(ratingTime),
                RatingCount = isFirstRating ? 1 : State.AppRatings[deviceId].RatingCount + 1,
                IsRealUser = false,
                RealUserId = userId.ToString()
            });

            await ConfirmEventsAsync();

            Logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] App rating recorded successfully for user: {UserId}, platform: {Platform}, device: {DeviceID}", 
                Id, platform, deviceId);

            return new AppRatingRecordDto
            {
                Platform = platform,
                DeviceId = deviceId,
                FirstRatingTime = isFirstRating ? ratingTime : State.AppRatings[deviceId].FirstRatingTime.ToDateTime(),
                LastRatingTime = ratingTime,
                RatingCount = isFirstRating ? 1 : State.AppRatings[deviceId].RatingCount + 1
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[UserStatisticsGAgent][RecordAppRatingAsync] Failed to record app rating for user: {UserId}, platform: {Platform}", 
                Id, platform);
            throw;
        }
    }

    /// <summary>
    /// Get user statistics information
    /// </summary>
    public Task<UserStatisticsDto> GetUserStatisticsAsync()
    {
        var result = new UserStatisticsDto
        {
            UserId = Guid.TryParse(State.UserId, out var uid) ? uid : Guid.Empty,
            AppRatings = State.AppRatings.Values.Select(rating => new AppRatingRecordDto
            {
                Platform = rating.Platform,
                DeviceId = rating.DeviceId,
                FirstRatingTime = rating.FirstRatingTime?.ToDateTime() ?? DateTime.MinValue,
                LastRatingTime = rating.LastRatingTime?.ToDateTime() ?? DateTime.MinValue,
                RatingCount = rating.RatingCount
            }).ToList(),
        };
        return Task.FromResult(result);
    }
    
    /// <summary>
    /// Get app rating records for specific platform or all platforms
    /// </summary>
    public Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(string? deviceId = null)
    {
        var query = State.AppRatings.Values.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            query = query.Where(r => r.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
        }

        var result = query.Select(rating => new AppRatingRecordDto
        {
            Platform = rating.Platform,
            DeviceId = rating.DeviceId,
            FirstRatingTime = rating.FirstRatingTime?.ToDateTime() ?? DateTime.MinValue,
            LastRatingTime = rating.LastRatingTime?.ToDateTime() ?? DateTime.MinValue,
            RatingCount = rating.RatingCount
        }).ToList();

        return Task.FromResult(result);
    }
    
    public Task<bool> CanUserRateAppAsync(string deviceId)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] DeviceId is null or empty for user: {UserId}", Id);
            return Task.FromResult(false);
        }

        if (!State.AppRatings.TryGetValue(deviceId, out var ratingInfo))
        {
            Logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] No rating record for device, user can rate for user: {UserId}, device: {DeviceId}", Id, deviceId);
            return Task.FromResult(true);
        }

        var ratingIntervalMinutes = _userStatisticsOptions.CurrentValue.RatingIntervalMinutes;
        var lastRatingTime = ratingInfo.LastRatingTime?.ToDateTime() ?? DateTime.MinValue;
        var minutesSinceLastRating = (DateTime.UtcNow - lastRatingTime).TotalMinutes;
        var canRate = minutesSinceLastRating >= ratingIntervalMinutes;
        
        Logger.LogDebug("[UserStatisticsGAgent][CanUserRateAppAsync] User: {UserId}, device: {DeviceId}, minutes since last rating: {MinutesSinceLastRating}, interval required: {IntervalMinutes}, can rate: {CanRate}", 
            Id, deviceId, minutesSinceLastRating, ratingIntervalMinutes, canRate);

        return Task.FromResult(canRate);
    }

    private async Task InitializeUserStatisticsAsync()
    {
        if (State.IsInitialized)
        {
            Logger.LogDebug("[UserStatisticsGAgent][InitializeUserStatisticsAsync] User statistics already initialized for user: {UserId}", Id);
            return;
        }

        try
        {
            RaiseEvent(new InitializeUserStatsEvent
            {
                UserId = Id.ToString()
            });

            await ConfirmEventsAsync();

            Logger.LogDebug("[UserStatisticsGAgent][InitializeUserStatisticsAsync] User statistics initialized for user: {UserId}", Id);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[UserStatisticsGAgent][InitializeUserStatisticsAsync] Failed to initialize user statistics for user: {UserId}", Id);
        }
    }

    #region EventHandlers

    [EventHandler]
    public void HandleInitializeUserStatsEvent(InitializeUserStatsEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleRecordAppRatingEvent(RecordAppRatingEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    /// <summary>
    /// Handle state transitions for events
    /// </summary>
    protected override void TransitionState(UserStatisticsState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeUserStatsEvent initEvent:
                state.UserId = initEvent.UserId;
                state.IsInitialized = true;
                break;

            case RecordAppRatingEvent ratingEvent:
                var key = ratingEvent.DeviceId;
                if (ratingEvent.HasRealUserId)
                {
                    state.UserId = ratingEvent.RealUserId;
                }

                if (ratingEvent.HasIsRealUser)
                {
                    state.IsRealUser = ratingEvent.IsRealUser;
                }
                
                if (state.AppRatings.ContainsKey(key))
                {
                    var existing = state.AppRatings[key];
                    existing.LastRatingTime = ratingEvent.RatingTime;
                    existing.RatingCount = ratingEvent.RatingCount;
                    existing.DeviceId = ratingEvent.DeviceId;
                }
                else
                {
                    state.AppRatings[key] = new AppRatingInfo
                    {
                        Platform = ratingEvent.Platform,
                        DeviceId = ratingEvent.DeviceId,
                        FirstRatingTime = ratingEvent.RatingTime,
                        LastRatingTime = ratingEvent.RatingTime,
                        RatingCount = ratingEvent.RatingCount
                    };
                }
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}

