using Aevatar.Agents.GodGPT.Protos.UserDevice;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Json.Schema.Generation;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.UserDevice;

/// <summary>
/// User device GAgent - stores the most recently used device for push notifications.
/// One agent per user (userId as agent ID), only one device is stored.
/// </summary>
[Description("Store user device for push notifications")]
[Reentrant]
public class UserDeviceGAgent : Aevatar.Agents.Core.GAgentBase<UserDeviceState>,
    IUserDeviceGAgent
{
    public override Task<string> GetDescriptionAsync()
    {
        var hasDevice = !string.IsNullOrEmpty(State.DeviceId);
        return Task.FromResult($"User Device - {(hasDevice ? "1 device" : "no device")}");
    }

    protected override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await base.OnActivateAsync(cancellationToken);
        
        // Initialize user_id if not set
        if (string.IsNullOrEmpty(State.UserId))
        {
            State.UserId = Id.ToString();
        }
    }

    // =========================================================================
    // State Transition
    // =========================================================================

    protected override void TransitionState(UserDeviceState state, IMessage @event)
    {
        switch (@event)
        {
            case RegisterOrUpdateDeviceEvent e:
                // Replace any existing device with the new one
                state.DeviceId = e.DeviceId;
                state.PushToken = e.PushToken;
                state.TimeZoneId = e.TimeZoneId;
                state.PushEnabled = e.PushEnabled;
                state.Platform = e.Platform;
                state.AppVersion = e.AppVersion;
                state.Language = e.Language;
                state.TokenUpdatedAt = e.Timestamp;
                state.LastActiveAt = e.Timestamp;
                state.TokenInvalid = false;
                break;
            
            case UpdateDeviceTokenEvent e:
                if (!string.IsNullOrEmpty(state.DeviceId))
                {
                    state.PushToken = e.PushToken;
                    state.TokenUpdatedAt = e.Timestamp;
                    state.TokenInvalid = false; // Reset invalid flag after token update
                }
                break;
            
            case ClearDeviceEvent:
                state.DeviceId = string.Empty;
                state.PushToken = string.Empty;
                state.TimeZoneId = string.Empty;
                state.PushEnabled = false;
                state.Platform = string.Empty;
                state.AppVersion = string.Empty;
                state.Language = string.Empty;
                state.TokenUpdatedAt = null;
                state.LastActiveAt = null;
                state.TokenInvalid = false;
                break;
            
            case MarkDeviceTokenInvalidEvent:
                if (!string.IsNullOrEmpty(state.DeviceId))
                {
                    state.TokenInvalid = true;
                }
                break;
        }
    }

    // =========================================================================
    // Interface Implementation
    // =========================================================================

    /// <summary>
    /// Register or update device information.
    /// Replaces any existing device - only the most recent device is stored.
    /// </summary>
    /// <returns>True if this is a new device, false if updating an existing device.</returns>
    public async Task<bool> RegisterOrUpdateDeviceAsync(
        string deviceId,
        string pushToken,
        string timeZoneId,
        bool pushEnabled,
        string platform,
        string appVersion,
        string language)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
        {
            Logger.LogWarning("[UserDeviceGAgent][RegisterOrUpdateDevice] deviceId is empty");
            return false;
        }
        
        // Check if this is a new device before updating state
        // New device if: no existing device OR different device ID
        var isNewDevice = string.IsNullOrEmpty(State.DeviceId) || State.DeviceId != deviceId;
        
        Logger.LogInformation(
            "[UserDeviceGAgent][RegisterOrUpdateDevice] UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TimeZone: {TimeZone}, IsNewDevice: {IsNewDevice}",
            Id, deviceId, platform, timeZoneId, isNewDevice);
        
        RaiseEvent(new RegisterOrUpdateDeviceEvent
        {
            DeviceId = deviceId,
            PushToken = pushToken ?? string.Empty,
            TimeZoneId = timeZoneId ?? string.Empty,
            PushEnabled = pushEnabled,
            Platform = platform ?? string.Empty,
            AppVersion = appVersion ?? string.Empty,
            Language = language ?? string.Empty,
            Timestamp = DateTime.UtcNow.ToProtoTimestamp()
        });
        
        await ConfirmEventsAsync();
        
        return isNewDevice;
    }
    
    /// <summary>
    /// Update push token only.
    /// </summary>
    public async Task UpdatePushTokenAsync(string pushToken)
    {
        if (string.IsNullOrEmpty(State.DeviceId))
        {
            Logger.LogWarning("[UserDeviceGAgent][UpdatePushToken] No device registered");
            return;
        }
        
        Logger.LogInformation(
            "[UserDeviceGAgent][UpdatePushToken] UserId: {UserId}, DeviceId: {DeviceId}", 
            Id, State.DeviceId);
        
        RaiseEvent(new UpdateDeviceTokenEvent
        {
            PushToken = pushToken ?? string.Empty,
            Timestamp = DateTime.UtcNow.ToProtoTimestamp()
        });
        
        await ConfirmEventsAsync();
    }
    
    /// <summary>
    /// Get the current device information.
    /// </summary>
    public Task<DeviceInfo?> GetDeviceAsync()
    {
        if (string.IsNullOrEmpty(State.DeviceId))
        {
            return Task.FromResult<DeviceInfo?>(null);
        }
        
        var deviceInfo = new DeviceInfo
        {
            DeviceId = State.DeviceId,
            PushToken = State.PushToken,
            TimeZoneId = State.TimeZoneId,
            PushEnabled = State.PushEnabled,
            TokenUpdatedAt = State.TokenUpdatedAt,
            LastActiveAt = State.LastActiveAt,
            Platform = State.Platform,
            AppVersion = State.AppVersion,
            TokenInvalid = State.TokenInvalid,
            Language = State.Language
        };
        
        return Task.FromResult<DeviceInfo?>(deviceInfo);
    }
    
    /// <summary>
    /// Get push target if device is pushable.
    /// </summary>
    public Task<PushTarget?> GetPushTargetAsync()
    {
        if (string.IsNullOrEmpty(State.DeviceId) || 
            !State.PushEnabled || 
            State.TokenInvalid || 
            string.IsNullOrEmpty(State.PushToken))
        {
            return Task.FromResult<PushTarget?>(null);
        }
        
        var target = new PushTarget
        {
            UserId = State.UserId,
            DeviceId = State.DeviceId,
            PushToken = State.PushToken,
            Platform = State.Platform,
            Language = State.Language,
            TimeZoneId = State.TimeZoneId
        };
        
        return Task.FromResult<PushTarget?>(target);
    }
    
    /// <summary>
    /// Clear device.
    /// </summary>
    public async Task ClearDeviceAsync()
    {
        if (string.IsNullOrEmpty(State.DeviceId))
        {
            Logger.LogWarning("[UserDeviceGAgent][ClearDevice] No device to clear");
            return;
        }
        
        Logger.LogInformation("[UserDeviceGAgent][ClearDevice] UserId: {UserId}, DeviceId: {DeviceId}", 
            Id, State.DeviceId);
        
        RaiseEvent(new ClearDeviceEvent());
        
        await ConfirmEventsAsync();
    }
    
    /// <summary>
    /// Mark token as invalid.
    /// </summary>
    public async Task MarkTokenInvalidAsync()
    {
        if (string.IsNullOrEmpty(State.DeviceId))
        {
            Logger.LogWarning("[UserDeviceGAgent][MarkTokenInvalid] No device registered");
            return;
        }
        
        Logger.LogInformation("[UserDeviceGAgent][MarkTokenInvalid] UserId: {UserId}, DeviceId: {DeviceId}", 
            Id, State.DeviceId);
        
        RaiseEvent(new MarkDeviceTokenInvalidEvent());
        
        await ConfirmEventsAsync();
    }
}
