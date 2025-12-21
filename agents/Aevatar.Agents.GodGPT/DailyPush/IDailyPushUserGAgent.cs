using Aevatar.Core.Abstractions;
using Aevatar.Agents.GodGPT.Protos.DailyPushUser;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Interface for user-level daily push notification management.
/// Handles device registration, push status, and notification delivery for a single user.
/// Extracted from ChatManagerGAgent to follow single responsibility principle.
/// </summary>
public interface IDailyPushUserGAgent : IGAgent
{
    /// <summary>
    /// Initialize the agent with user context
    /// </summary>
    Task InitializeAsync(Guid userId);

    // === Device Registration Methods ===

    /// <summary>
    /// Register or update a device using V2 structure (enhanced version)
    /// All new device registrations should use this method
    /// </summary>
    Task<bool> RegisterOrUpdateDeviceV2Async(
        string deviceId, 
        string pushToken, 
        string timeZoneId,
        bool? pushEnabled, 
        string pushLanguage, 
        string? platform = null, 
        string? appVersion = null);

    /// <summary>
    /// Get all V2 devices for this user
    /// </summary>
    Task<List<UserDeviceInfoV2Proto>> GetAllDevicesV2Async();

    /// <summary>
    /// Get unified device list (for push processing)
    /// </summary>
    Task<List<UserDeviceInfoV2Proto>> GetUnifiedDevicesAsync();

    // === Push Status Methods ===

    /// <summary>
    /// Mark daily push as read for today
    /// </summary>
    Task MarkPushAsReadAsync(string deviceId);

    /// <summary>
    /// Check if user should receive afternoon retry push
    /// </summary>
    Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate);


    // === Push Processing Methods ===

    /// <summary>
    /// Process daily push for this user (called by timezone scheduler)
    /// </summary>
    Task ProcessDailyPushAsync(
        DateTime targetDate, 
        List<DailyNotificationContent> contents, 
        string timeZoneId, 
        bool bypassReadStatusCheck = false, 
        bool isRetryPush = false, 
        bool isTestPush = false);

    /// <summary>
    /// Get devices for coordinated push (called by DailyPushCoordinatorGAgent)
    /// </summary>
    Task<List<UserDeviceInfoV2Proto>> GetDevicesForCoordinatedPushAsync(string timeZoneId, DateTime targetDate);

    /// <summary>
    /// Execute coordinated push for a specific device (called by coordinator after device selection)
    /// Uses existing Redis deduplication logic
    /// </summary>
    Task<bool> ExecuteCoordinatedPushAsync(
        UserDeviceInfoV2Proto device, 
        DateTime targetDate,
        List<DailyNotificationContent> contents, 
        bool isRetryPush = false, 
        bool isTestPush = false);

    // === Device Management Methods ===

    /// <summary>
    /// Cleanup expired or invalid devices
    /// </summary>
    Task CleanupDevicesV2Async();

    /// <summary>
    /// Update timezone index for this user
    /// </summary>
    Task UpdateTimezoneIndexAsync(string? oldTimeZone, string newTimeZone);

    // === Debug Methods ===

    /// <summary>
    /// Get push debug information for a specific device (for troubleshooting)
    /// </summary>
    Task<object> GetPushDebugInfoAsync(string deviceId, DateOnly date, string timeZoneId);

    /// <summary>
    /// Get device status for query API
    /// </summary>
    Task<UserDeviceInfoV2Proto?> GetDeviceStatusAsync(string deviceId);
}

