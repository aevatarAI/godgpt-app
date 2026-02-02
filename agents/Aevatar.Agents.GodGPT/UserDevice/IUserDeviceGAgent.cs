using Aevatar.Agents.GodGPT.Protos.UserDevice;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.UserDevice;

/// <summary>
/// User device GAgent interface - stores the most recently used device for push notifications.
/// One agent per user (userId as agent ID), stores only one device.
/// </summary>
public interface IUserDeviceGAgent : IGAgent
{
    /// <summary>
    /// Register or update device information (called on app launch/login).
    /// Replaces any existing device - only the most recent device is stored.
    /// </summary>
    /// <returns>True if this is a new device, false if updating an existing device.</returns>
    Task<bool> RegisterOrUpdateDeviceAsync(
        string deviceId,
        string pushToken,
        string timeZoneId,
        bool pushEnabled,
        string platform,
        string appVersion,
        string language);
    
    /// <summary>
    /// Update push token only (called when system refreshes the token).
    /// </summary>
    Task UpdatePushTokenAsync(string pushToken);
    
    /// <summary>
    /// Get the current device information.
    /// Returns null if no device registered.
    /// </summary>
    [ReadOnly]
    Task<DeviceInfo?> GetDeviceAsync();
    
    /// <summary>
    /// Get push target if device is pushable (push_enabled=true and token_invalid=false).
    /// Returns null if no valid push target.
    /// </summary>
    [ReadOnly]
    Task<PushTarget?> GetPushTargetAsync();
    
    /// <summary>
    /// Clear device (called on user logout).
    /// </summary>
    Task ClearDeviceAsync();
    
    /// <summary>
    /// Mark token as invalid (called by push service when push fails).
    /// </summary>
    Task MarkTokenInvalidAsync();
}
