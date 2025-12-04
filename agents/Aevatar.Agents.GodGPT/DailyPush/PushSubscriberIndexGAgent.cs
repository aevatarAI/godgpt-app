using Aevatar.Agents.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// Protobuf types aliases
using IndexStateProto = Aevatar.Agents.GodGPT.Protos.DailyPush.PushSubscriberIndexStateProto;
using InitializeTimezoneIndexEvent = Aevatar.Agents.GodGPT.Protos.DailyPush.InitializeTimezoneIndexEvent;
using AddUserToTimezoneEvent = Aevatar.Agents.GodGPT.Protos.DailyPush.AddUserToTimezoneEvent;
using RemoveUserFromTimezoneEvent = Aevatar.Agents.GodGPT.Protos.DailyPush.RemoveUserFromTimezoneEvent;
using BatchUpdateUsersEvent = Aevatar.Agents.GodGPT.Protos.DailyPush.BatchUpdateUsersEvent;
using TimezoneUpdateRequestProto = Aevatar.Agents.GodGPT.Protos.DailyPush.TimezoneUpdateRequestProto;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Timezone user index GAgent implementation
/// </summary>
public class PushSubscriberIndexGAgent : GAgentBase<IndexStateProto>, IPushSubscriberIndexGAgent
{
    private readonly ILogger<PushSubscriberIndexGAgent> _logger;

    public PushSubscriberIndexGAgent(ILogger<PushSubscriberIndexGAgent> logger)
    {
        _logger = logger;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Timezone user index management");
    }

    protected override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await base.OnActivateAsync(cancellationToken);
        _logger.LogInformation("PushSubscriberIndexGAgent activated");
    }

    protected override void TransitionState(IndexStateProto state, IMessage @event)
    {
        switch (@event)
        {
            case InitializeTimezoneIndexEvent initEvent:
                state.TimeZoneId = initEvent.TimeZoneId;
                state.LastUpdated = initEvent.InitTime;
                break;

            case AddUserToTimezoneEvent addEvent:
                if (!state.ActiveUsers.Contains(addEvent.UserId))
                {
                    state.ActiveUsers.Add(addEvent.UserId);
                }
                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = addEvent.UpdateTime;
                break;

            case RemoveUserFromTimezoneEvent removeEvent:
                state.ActiveUsers.Remove(removeEvent.UserId);
                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = removeEvent.UpdateTime;
                break;

            case BatchUpdateUsersEvent batchEvent:
                foreach (var update in batchEvent.Updates)
                {
                    if (update.IsAdd)
                    {
                        if (!state.ActiveUsers.Contains(update.UserId))
                        {
                            state.ActiveUsers.Add(update.UserId);
                        }
                    }
                    else
                    {
                        state.ActiveUsers.Remove(update.UserId);
                    }
                }

                state.ActiveUserCount = state.ActiveUsers.Count;
                state.LastUpdated = batchEvent.UpdateTime;
                break;

            default:
                _logger.LogDebug("Unhandled event type: {EventType}", @event.GetType().Name);
                break;
        }
    }

    public async Task InitializeAsync(string timeZoneId)
    {
        RaiseEvent(new InitializeTimezoneIndexEvent
        {
            TimeZoneId = timeZoneId,
            InitTime = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        _logger.LogInformation("Initialized timezone index for: {TimeZoneId}", timeZoneId);
    }

    public async Task AddUserToTimezoneAsync(Guid userId)
    {
        RaiseEvent(new AddUserToTimezoneEvent
        {
            UserId = userId.ToString(),
            TimeZoneId = State.TimeZoneId,
            UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        _logger.LogInformation("Added user {UserId} to timezone {TimeZone}", userId, State.TimeZoneId);
    }

    public async Task RemoveUserFromTimezoneAsync(Guid userId)
    {
        RaiseEvent(new RemoveUserFromTimezoneEvent
        {
            UserId = userId.ToString(),
            TimeZoneId = State.TimeZoneId,
            UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        _logger.LogInformation("Removed user {UserId} from timezone {TimeZone}", userId, State.TimeZoneId);
    }

    public async Task<List<Guid>> GetActiveUsersAsync()
    {
        return State.ActiveUsers.Select(s => Guid.Parse(s)).ToList();
    }

    public async Task<List<Guid>> GetActiveUsersInTimezoneAsync(int skip, int take)
    {
        return State.ActiveUsers.Skip(skip).Take(take).Select(s => Guid.Parse(s)).ToList();
    }

    public async Task<int> GetActiveUserCountAsync()
    {
        return State.ActiveUsers.Count;
    }

    public async Task<int> GetUserCountAsync()
    {
        return State.ActiveUsers.Count;
    }

    public async Task<string> GetTimezoneIdAsync()
    {
        return State.TimeZoneId;
    }

    public async Task RefreshUserIndexAsync()
    {
        // 🧹 Lightweight cleanup: Only log current status for monitoring
        var currentCount = State.ActiveUsers.Count;
        
        // TODO: Future enhancement could involve:
        // 1. Querying all ChatManagerGAgents to find users with devices in this timezone
        // 2. Updating the ActiveUsers set based on current device states
        // 3. Removing users who no longer have enabled devices in this timezone
        // 
        // Current approach: Passive cleanup via natural user activity
        // - Users are added when they register/update devices
        // - Cleanup happens naturally when users update their timezone or device status
        // - repeated field prevents duplicates checked in TransitionState

        RaiseEvent(new InitializeTimezoneIndexEvent
        {
            TimeZoneId = State.TimeZoneId,
            InitTime = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();
        _logger.LogInformation("📊 Timezone index status: {TimeZone} has {UserCount} active users", 
            State.TimeZoneId, currentCount);
    }

    public async Task<bool> HasActiveDeviceInTimezoneAsync(Guid userId)
    {
        return State.ActiveUsers.Contains(userId.ToString());
    }

    public async Task BatchUpdateUsersAsync(List<TimezoneUpdateRequest> updates)
    {
        var validUpdates = updates.Where(u => !string.IsNullOrEmpty(u.TargetTimezone)).ToList();
        if (validUpdates.Count > 0)
        {
            var batchEvent = new BatchUpdateUsersEvent
            {
                UpdatedCount = validUpdates.Count,
                UpdateTime = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            foreach (var update in validUpdates)
            {
                batchEvent.Updates.Add(new TimezoneUpdateRequestProto
                {
                    UserId = update.UserId.ToString(),
                    SourceTimezone = update.SourceTimezone,
                    TargetTimezone = update.TargetTimezone,
                    IsAdd = update.IsAdd
                });
            }
            
            RaiseEvent(batchEvent);
            await ConfirmEventsAsync();
        }

        _logger.LogInformation("Batch updated {Count} users in timezone {TimeZone}", validUpdates.Count, State.TimeZoneId);
    }
}