using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.UserStatistics;

/// <summary>
/// User Statistics Agent interface - manages user behavior statistics including app ratings.
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IUserStatisticsGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Record app rating for a user (uses Protobuf type for RPC)
    /// </summary>
    Task<AppRatingRecordProto> RecordAppRatingAsync(RecordAppRatingRequestProto request);
    
    /// <summary>
    /// Get user statistics (uses Protobuf type for RPC)
    /// </summary>
    Task<UserStatisticsProto> GetUserStatisticsAsync();
    
    /// <summary>
    /// Get app rating records (uses Protobuf type for RPC)
    /// </summary>
    Task<GetAppRatingRecordsResponseProto> GetAppRatingRecordsAsync(GetAppRatingRecordsRequestProto request);
    
    /// <summary>
    /// Check if user can rate the app
    /// </summary>
    Task<bool> CanUserRateAppAsync(string deviceId);
}

/// <summary>
/// User Statistics GAgent - manages user behavior statistics including app ratings
/// </summary>
public class UserStatisticsGAgent : GAgentBase<UserStatisticsState>, IUserStatisticsGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public IOptionsMonitor<UserStatisticsOptions>? UserStatisticsOptions { get; set; }

    // Parameterless constructor required for Orleans activation
    public UserStatisticsGAgent() : base()
    {
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

    public async Task<AppRatingRecordProto> RecordAppRatingAsync(RecordAppRatingRequestProto request)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            Logger.LogWarning("[UserStatisticsGAgent][RecordAppRatingAsync] DeviceId cannot be empty for user: {UserId}", Id);
            return new AppRatingRecordProto();
        }

        try
        {
            var isFirstRating = !State.AppRatings.ContainsKey(request.DeviceId);
            var ratingTime = DateTime.UtcNow;

            Logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] Recording app rating for user: {UserId}, platform: {Platform}, device: {DeviceID}, isFirstRating: {IsFirstRating}", 
                Id, request.Platform, request.DeviceId, isFirstRating);

            RaiseEvent(new RecordAppRatingEvent
            {
                Platform = request.Platform,
                DeviceId = request.DeviceId,
                RatingTime = Timestamp.FromDateTime(ratingTime),
                RatingCount = isFirstRating ? 1 : State.AppRatings[request.DeviceId].RatingCount + 1,
                IsRealUser = false,
                RealUserId = request.UserId
            });

            await ConfirmEventsAsync();

            Logger.LogDebug("[UserStatisticsGAgent][RecordAppRatingAsync] App rating recorded successfully for user: {UserId}, platform: {Platform}, device: {DeviceID}", 
                Id, request.Platform, request.DeviceId);

            var ratingInfo = State.AppRatings[request.DeviceId];
            return new AppRatingRecordProto
            {
                Platform = request.Platform,
                DeviceId = request.DeviceId,
                FirstRatingTime = ratingInfo.FirstRatingTime,
                LastRatingTime = ratingInfo.LastRatingTime,
                RatingCount = ratingInfo.RatingCount
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[UserStatisticsGAgent][RecordAppRatingAsync] Failed to record app rating for user: {UserId}, platform: {Platform}", 
                Id, request.Platform);
            throw;
        }
    }

    /// <summary>
    /// Get user statistics information (uses Protobuf type for RPC)
    /// </summary>
    public Task<UserStatisticsProto> GetUserStatisticsAsync()
    {
        var result = new UserStatisticsProto
        {
            UserId = State.UserId
        };
        
        foreach (var rating in State.AppRatings.Values)
        {
            result.AppRatings.Add(new AppRatingRecordProto
            {
                Platform = rating.Platform,
                DeviceId = rating.DeviceId,
                FirstRatingTime = rating.FirstRatingTime ?? Timestamp.FromDateTime(DateTime.MinValue),
                LastRatingTime = rating.LastRatingTime ?? Timestamp.FromDateTime(DateTime.MinValue),
                RatingCount = rating.RatingCount
            });
        }
        
        return Task.FromResult(result);
    }
    
    /// <summary>
    /// Get app rating records (uses Protobuf type for RPC)
    /// </summary>
    public Task<GetAppRatingRecordsResponseProto> GetAppRatingRecordsAsync(GetAppRatingRecordsRequestProto request)
    {
        var query = State.AppRatings.Values.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(request.DeviceId))
        {
            query = query.Where(r => r.DeviceId.Equals(request.DeviceId, StringComparison.OrdinalIgnoreCase));
        }

        var response = new GetAppRatingRecordsResponseProto();
        foreach (var rating in query)
        {
            response.Records.Add(new AppRatingRecordProto
            {
                Platform = rating.Platform,
                DeviceId = rating.DeviceId,
                FirstRatingTime = rating.FirstRatingTime ?? Timestamp.FromDateTime(DateTime.MinValue),
                LastRatingTime = rating.LastRatingTime ?? Timestamp.FromDateTime(DateTime.MinValue),
                RatingCount = rating.RatingCount
            });
        }

        return Task.FromResult(response);
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

        var ratingIntervalMinutes = UserStatisticsOptions?.CurrentValue.RatingIntervalMinutes ?? 60;
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

