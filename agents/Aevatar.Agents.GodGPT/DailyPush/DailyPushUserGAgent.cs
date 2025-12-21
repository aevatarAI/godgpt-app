using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.DailyPushUser;
using Aevatar.Application.Grains.Agents.ChatManager;
using GodGPT.GAgents.DailyPush.Options;
using Google.Protobuf;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Concurrency;

namespace GodGPT.GAgents.DailyPush;

/// <summary>
/// Manages daily push notifications for a single user.
/// Handles device registration, push status, and notification delivery.
/// Extracted from ChatManagerGAgent to follow single responsibility principle.
/// </summary>
[GAgent(nameof(DailyPushUserGAgent))]
[Reentrant]
public class DailyPushUserGAgent : Aevatar.Agents.Core.GAgentBase<DailyPushUserStateProto>, IDailyPushUserGAgent
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IClusterClient _clusterClient;  // Keep for traditional Orleans Grains (e.g., IDailyPushCoordinatorGAgent)
    
    // Concurrent protection for daily push processing
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> PushSemaphores = new();

    public DailyPushUserGAgent(IServiceProvider serviceProvider, IGAgentActorFactory actorFactory, IClusterClient clusterClient)
    {
        _serviceProvider = serviceProvider;
        _actorFactory = actorFactory;
        _clusterClient = clusterClient;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Daily Push User GAgent - UserId: {State.UserId}");
    }

    public Task InitializeAsync(Guid userId)
    {
        if (string.IsNullOrEmpty(State.UserId))
        {
            RaiseEvent(new InitializeDailyPushUserEvent
            {
                UserId = userId.ToString(),
                InitializedAt = DateTime.UtcNow.ToProtoTimestamp()
            });
        }
        return Task.CompletedTask;
    }

    // === Device Registration Methods ===

    public async Task<bool> RegisterOrUpdateDeviceV2Async(
        string deviceId, string pushToken, string timeZoneId,
        bool? pushEnabled, string pushLanguage, string? platform = null, string? appVersion = null)
    {
        var options = _serviceProvider.GetService<IOptionsMonitor<DailyPushOptions>>();
        if (options != null && !options.CurrentValue.DeviceRegistrationEnabled)
        {
            Logger.LogInformation("Device registration disabled - returning mock success for device {DeviceId}", deviceId);
            return true;
        }

        var isNewDevice = !State.UserDevicesV2.ContainsKey(deviceId);
        var now = DateTime.UtcNow.ToProtoTimestamp();

        var deviceInfo = isNewDevice
            ? new UserDeviceInfoV2Proto()
            : State.UserDevicesV2[deviceId];

        var oldPushToken = deviceInfo.PushToken;
        var oldTimeZone = isNewDevice ? null : deviceInfo.TimeZoneId;
        var tokenChanged = oldPushToken != pushToken;
        var timeZoneChanged = oldTimeZone != timeZoneId;

        // Update device information
        deviceInfo.DeviceId = deviceId;
        deviceInfo.UserId = State.UserId;
        deviceInfo.PushToken = pushToken;
        deviceInfo.TimeZoneId = timeZoneId;
        deviceInfo.PushLanguage = pushLanguage;
        deviceInfo.LastActiveAt = now;
        deviceInfo.Status = (int)DeviceStatus.Active;
        deviceInfo.ConsecutiveFailures = 0;

        if (pushEnabled.HasValue) deviceInfo.PushEnabled = pushEnabled.Value;
        if (!string.IsNullOrEmpty(platform)) deviceInfo.Platform = platform;
        if (!string.IsNullOrEmpty(appVersion)) deviceInfo.AppVersion = appVersion;

        if (isNewDevice)
        {
            deviceInfo.RegisteredAt = now;
            deviceInfo.LastTokenUpdate = now;
        }
        else if (tokenChanged)
        {
            deviceInfo.LastTokenUpdate = now;
            if (!string.IsNullOrEmpty(oldPushToken))
            {
                deviceInfo.PushTokenHistory.Add(new HistoricalPushTokenProto
                {
                    Token = oldPushToken,
                    UsedFrom = deviceInfo.LastTokenUpdate,
                    UsedUntil = now,
                    ReplacementReason = "token_refresh"
                });
                if (deviceInfo.PushTokenHistory.Count > 5)
                {
                    var history = deviceInfo.PushTokenHistory
                        .OrderByDescending(h => h.UsedUntil ?? DateTime.MaxValue.ToProtoTimestamp())
                        .Take(5).ToList();
                    deviceInfo.PushTokenHistory.Clear();
                    deviceInfo.PushTokenHistory.AddRange(history);
                }
            }
        }

        RaiseEvent(new RegisterOrUpdateDeviceV2Event
        {
            DeviceId = deviceId,
            DeviceInfo = deviceInfo,
            IsNewDevice = isNewDevice,
            OldPushToken = tokenChanged ? oldPushToken : null,
        });

        await ConfirmEventsAsync();

        if (isNewDevice || timeZoneChanged)
        {
            await UpdateTimezoneIndexAsync(oldTimeZone, timeZoneId);
            Logger.LogInformation("🌍 V2 Device timezone index updated: User {UserId} from {OldTZ} to {NewTZ}",
                State.UserId, oldTimeZone ?? "none", timeZoneId);
        }

        Logger.LogInformation("📱 V2 Device {Action}: DeviceId={DeviceId}, PushToken={TokenPrefix}..., " +
                              "TimeZone={TimeZone}, Language={Language}, Enabled={Enabled}, Platform={Platform}",
            isNewDevice ? "registered" : "updated", deviceId,
            pushToken.Substring(0, Math.Min(8, pushToken.Length)),
            timeZoneId, pushLanguage, deviceInfo.PushEnabled, platform ?? "unknown");

        return true;
    }

    public Task<List<UserDeviceInfoV2Proto>> GetAllDevicesV2Async()
    {
        var devices = State.UserDevicesV2.Values.ToList();
        return Task.FromResult(devices);
    }

    public Task<List<UserDeviceInfoV2Proto>> GetUnifiedDevicesAsync()
    {
        var devices = State.UserDevicesV2.Values.ToList();
        Logger.LogDebug("GetUnifiedDevicesAsync: Devices count: {DeviceCount}", devices.Count);

        return Task.FromResult(devices);
    }

    // === Push Status Methods ===

    public async Task MarkPushAsReadAsync(string deviceId)
    {
        var options = _serviceProvider.GetService<IOptionsMonitor<DailyPushOptions>>();
        if (options != null && !options.CurrentValue.DeviceRegistrationEnabled)
        {
            Logger.LogInformation("Mark read disabled - returning mock success for device {DeviceId}", deviceId);
            return; // Return success but skip actual read status update
        }

        if (State.UserDevicesV2.ContainsKey(deviceId))
        {
            var dateKey = DateTime.UtcNow.ToString("yyyy-MM-dd");
            var date = DateOnly.FromDateTime(DateTime.UtcNow);

            RaiseEvent(new MarkDailyPushReadEvent
            {
                DateKey = dateKey,
                ReadTime = DateTime.UtcNow.ToProtoTimestamp()
            });

            await ConfirmEventsAsync();

            var deduplicationService = _serviceProvider.GetRequiredService<IPushDeduplicationService>();
            var deviceReadSuccess = await deduplicationService.MarkDeviceAsReadAsync(deviceId, date);

            if (deviceReadSuccess)
            {
                Logger.LogInformation(
                    $"Marked daily push as read for V2 device: {deviceId} on {dateKey} (user-level + device-level)");
            }
            else
            {
                Logger.LogWarning($"Failed to mark device-level read status for device: {deviceId} on {dateKey}");
            }
        }
        else
        {
            Logger.LogWarning("V2 Device not found for provided deviceId: {DeviceId}", deviceId);
        }
    }

    public async Task<bool> ShouldSendAfternoonRetryAsync(DateTime targetDate)
    {
        var dateKey = targetDate.ToString("yyyy-MM-dd");
        var isRead = State.DailyPushReadStatus.TryGetValue(dateKey, out var readStatus) && readStatus;

        // 🔄 Use unified interface for consistency with HasEnabledDeviceInTimezoneAsync
        var allDevices = await GetUnifiedDevicesAsync();
        var hasEnabledDevices = allDevices.Any(d => d.PushEnabled);
        var shouldSend = !isRead && hasEnabledDevices;

        Logger.LogDebug(
            "ShouldSendAfternoonRetryAsync: DateKey={DateKey}, IsRead={IsRead}, HasEnabledDevices={HasEnabledDevices}, ShouldSend={ShouldSend}, ReadStatusEntries={ReadEntries}, TotalDevices={TotalDevices}",
            dateKey, isRead, hasEnabledDevices, shouldSend, State.DailyPushReadStatus.Count, allDevices.Count);

        return shouldSend;
    }

    // === Push Processing Methods ===

    public async Task ProcessDailyPushAsync(DateTime targetDate, List<DailyNotificationContent> contents,
        string timeZoneId, bool bypassReadStatusCheck = false, bool isRetryPush = false, bool isTestPush = false)
    {
        var userId = State.UserId;
        var userSemaphore = PushSemaphores.GetOrAdd(userId, _ => new SemaphoreSlim(1, 1));

        await userSemaphore.WaitAsync();
        try
        {
            var dateKey = targetDate.ToString("yyyy-MM-dd");
            Logger.LogDebug("🔒 Acquired push semaphore for user {UserId}, timezone {TimeZone}, pushType: {PushType}",
                State.UserId, timeZoneId, isRetryPush ? "retry" : "morning");

            // Clean up expired read status (keep only today and yesterday)
            await CleanupExpiredReadStatusAsync(targetDate);

            // Check if any message has been read for today - if so, skip all pushes
            // Exception: bypass this check when explicitly requested (e.g., test mode main push)
            if (!bypassReadStatusCheck)
            {
                var hasAnyReadToday = State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                if (hasAnyReadToday)
                {
                    // 🔥 V2-ONLY: Get device info for logging from V2 structure
                    var devicesInTimezone = State.UserDevicesV2.Values
                        .Where(d => d.TimeZoneId == timeZoneId)
                        .Select(d => new
                        {
                            DeviceId = d.DeviceId,
                            PushToken = !string.IsNullOrEmpty(d.PushToken)
                                ? d.PushToken.Substring(0, Math.Min(8, d.PushToken.Length)) + "..."
                                : "EMPTY",
                            PushEnabled = d.PushEnabled
                        })
                        .ToList();

                    var deviceInfo = devicesInTimezone.Any()
                        ? string.Join(", ",
                            devicesInTimezone.Select(d =>
                                $"DeviceId:{d.DeviceId}|Token:{d.PushToken}|Enabled:{d.PushEnabled}"))
                        : "No devices in timezone";

                    Logger.LogInformation(
                        "At least one daily push already read for {DateKey}, skipping all pushes - UserId: {UserId}, TimeZone: {TimeZone}, Devices: [{DeviceInfo}]",
                        dateKey, State.UserId, timeZoneId, deviceInfo);
                    return;
                }
            }
            else
            {
                Logger.LogInformation("Bypassing read status check for daily push on {DateKey}", dateKey);
            }

            // Only process devices in the specified timezone with pushToken deduplication
            // 🔄 Use unified interface to get all devices (V2 prioritized, V1 fallback)
            var enabledDevicesRaw = await GetUnifiedDevicesAsync();
            var enabledDevicesFiltered = enabledDevicesRaw
                .Where(d => d.PushEnabled && d.TimeZoneId == timeZoneId && !string.IsNullOrEmpty(d.PushToken))
                .ToList();

            // 🎯 CRITICAL FIX: Deduplicate by deviceId first, then by pushToken
            // This prevents the same physical device from being processed multiple times
            // even if it has different pushTokens (e.g., after user switching)
            var enabledDevices = enabledDevicesFiltered
                .GroupBy(d => d.DeviceId) // 🔧 Primary deduplication by deviceId
                .Select(deviceGroup =>
                    deviceGroup.OrderByDescending(d => d.LastTokenUpdate).First()) // Keep latest record for the device
                .ToList();

            var duplicateCount = enabledDevicesFiltered.Count - enabledDevices.Count;
            if (duplicateCount > 0)
            {
                Logger.LogDebug(
                    "Device deduplication: removed {DuplicateCount} duplicate devices (by deviceId + pushToken)",
                    duplicateCount);
            }

            Logger.LogInformation("Found {DeviceCount} enabled devices in timezone {TimeZone} for user {UserId}",
                enabledDevices.Count, timeZoneId, State.UserId);

            if (enabledDevices.Count == 0)
            {
                Logger.LogWarning(
                    "No enabled devices for daily push - User {UserId}, TimeZone {TimeZone}, Total V2 devices: {TotalDevices}",
                    State.UserId, timeZoneId, State.UserDevicesV2.Count);
                return;
            }

            // Get Global JWT Provider (new architecture - single instance for entire system)
            var globalJwtProvider = await GetGlobalJwtProviderAgentAsync();

            // Get Firebase project configuration via FirebaseService
            var firebaseService = _serviceProvider.GetService(typeof(FirebaseService)) as FirebaseService;
            if (firebaseService == null)
            {
                Logger.LogError("FirebaseService not available for push notifications");
                return;
            }

            var projectId = firebaseService.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                Logger.LogError("Firebase ProjectId not configured in FirebaseService for push notifications");
                return;
            }

            var successCount = 0;
            var failureCount = 0;

            // 🎯 Redis-based deduplication for push notifications
            var eligibleDevices = new List<UserDeviceInfoV2Proto>();
            var deduplicationService =
                _serviceProvider.GetService(typeof(IPushDeduplicationService)) as IPushDeduplicationService;
            var date = DateOnly.FromDateTime(targetDate);

            if (deduplicationService == null)
            {
                Logger.LogWarning("IPushDeduplicationService not available, proceeding without deduplication");
                eligibleDevices.AddRange(enabledDevices); // Fallback: no deduplication
            }
            else
            {
                foreach (var device in enabledDevices)
                {
                    bool canSend;

                    if (isTestPush)
                    {
                        canSend = true; // Test pushes skip deduplication
                        Logger.LogInformation("Device ELIGIBLE (test): {DeviceId} in {TimeZone}", device.DeviceId,
                            timeZoneId);
                    }
                    else if (isRetryPush)
                    {
                        // Check both user-level and device-level read status
                        var userRead = !bypassReadStatusCheck &&
                                       State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                        var deviceRead = false;

                        if (!bypassReadStatusCheck && !userRead)
                        {
                            // Only check device-level read status if user-level is not read
                            deviceRead = await deduplicationService.IsDeviceReadAsync(device.DeviceId, date);
                        }

                        if (userRead || deviceRead)
                        {
                            canSend = false;
                            var readType = userRead ? "user-level" : "device-level";
                            Logger.LogInformation(
                                "Device BLOCKED (retry, already read - {ReadType}): {DeviceId} in {TimeZone}, read on {DateKey}",
                                readType, device.DeviceId, timeZoneId, dateKey);
                        }
                        else
                        {
                            canSend = await deduplicationService.TryClaimRetryPushAsync(device.DeviceId, date,
                                timeZoneId);
                            Logger.LogInformation("Device {Status} (retry): {DeviceId} in {TimeZone}",
                                canSend ? "ELIGIBLE" : "BLOCKED", device.DeviceId, timeZoneId);
                        }
                    }
                    else
                    {
                        canSend = await deduplicationService.TryClaimMorningPushAsync(device.DeviceId, date,
                            timeZoneId);
                        Logger.LogInformation("Device {Status} (morning): {DeviceId} in {TimeZone}",
                            canSend ? "ELIGIBLE" : "BLOCKED", device.DeviceId, timeZoneId);
                    }

                    if (canSend)
                    {
                        eligibleDevices.Add(device);
                    }
                }
            }

            if (eligibleDevices.Count == 0)
            {
                Logger.LogInformation(
                    "No eligible devices for daily push after Redis deduplication - User {UserId}, TimeZone {TimeZone}, " +
                    "Original devices: {OriginalCount}, PushType: {PushType}",
                    State.UserId, timeZoneId, enabledDevices.Count, isRetryPush ? "retry" : "morning");
                return;
            }

            Logger.LogInformation("Proceeding with {EligibleCount} eligible devices for user {UserId}",
                eligibleDevices.Count, State.UserId);

            // Create device-level push tasks with rollback support
            var deviceTasks = eligibleDevices.Select(async (device, deviceIndex) =>
            {
                var deviceSuccess = false;

                try
                {
                    // 🎯 Add device-level delay to reduce concurrent JWT requests
                    var deviceDelay =
                        Random.Shared.Next(50, 300) + (deviceIndex * 150); // Random + staggered per device
                    if (deviceDelay > 0)
                    {
                        await Task.Delay(deviceDelay);
                    }

                    // Send only one content based on push type: first for morning, second for afternoon retry
                    var contentIndex = isRetryPush ? 1 : 0;
                    var content = contents.ElementAtOrDefault(contentIndex);

                    if (content == null)
                    {
                        Logger.LogWarning("No content available for {PushType} push - Device: {DeviceId}",
                            isRetryPush ? "afternoon retry" : "morning", device.DeviceId);
                        deviceSuccess = false;
                    }
                    else
                    {
                        try
                        {
                            var availableLanguages = string.Join(", ", content.LocalizedContents.Keys);
                            Logger.LogInformation(
                                "Language selection: DeviceId={DeviceId}, RequestedLanguage='{PushLanguage}', AvailableLanguages=[{AvailableLanguages}]",
                                device.DeviceId, device.PushLanguage, availableLanguages);

                            var localizedContent = content.GetLocalizedContent(device.PushLanguage);

                            Logger.LogInformation(
                                "📬 PUSH ATTEMPT: User {UserId}, DeviceId {DeviceId}, Content {ContentIndex}/{Total}, Title '{Title}', ContentId {ContentId}, PushToken {TokenPrefix}...",
                                State.UserId, device.DeviceId, contentIndex + 1, contents.Count, localizedContent.Title,
                                content.Id,
                                device.PushToken.Substring(0, Math.Min(8, device.PushToken.Length)));

                            // Create unique data payload for this content
                            var messageId = Guid.NewGuid();
                            var pushData = new Dictionary<string, object>
                            {
                                ["message_id"] = messageId.ToString(),
                                ["type"] = (int)DailyPushConstants.PushType.DailyPush,
                                ["date"] = dateKey,
                                ["content_id"] = content.Id,
                                ["content_index"] = contentIndex + 1,
                                ["device_id"] = device.DeviceId,
                                ["total_contents"] = contents.Count,
                                ["timezone"] = timeZoneId,
                                ["is_retry"] = isRetryPush,
                                ["is_test_push"] = isTestPush
                            };

                            // Use new global JWT architecture with direct HTTP push
                            // Skip deduplication since device already passed UTC-based pre-check
                            var success = await SendDirectPushNotificationAsync(
                                globalJwtProvider,
                                projectId,
                                device.PushToken,
                                localizedContent.Title,
                                localizedContent.Content,
                                pushData,
                                timeZoneId,
                                isRetryPush,
                                contentIndex == 0, // isFirstContent
                                false, // isTestPush
                                true); // skipDeduplicationCheck

                            if (success)
                            {
                                Logger.LogInformation(
                                    "Daily push sent successfully: DeviceId={DeviceId}, MessageId={MessageId}, ContentIndex={ContentIndex}/{TotalContents}, Date={Date}",
                                    device.DeviceId, messageId, contentIndex + 1, contents.Count, dateKey);
                                deviceSuccess = true;
                            }
                            else
                            {
                                Logger.LogWarning(
                                    "Failed to send daily push: DeviceId={DeviceId}, MessageId={MessageId}, ContentIndex={ContentIndex}/{TotalContents}, Date={Date}",
                                    device.DeviceId, messageId, contentIndex + 1, contents.Count, dateKey);
                                deviceSuccess = false;
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex,
                                $"Error sending daily push content {contentIndex + 1} to device {device.DeviceId}");
                            deviceSuccess = false;
                        }
                    }

                    Logger.LogInformation("Device push summary: DeviceId={DeviceId}, Success={DeviceSuccess}",
                        device.DeviceId, deviceSuccess);

                    return new[] { deviceSuccess };
                }
                finally
                {
                    // 🔄 Rollback Redis claim if ALL pushes for this device failed
                    if (!deviceSuccess && deduplicationService != null)
                    {
                        try
                        {
                            await deduplicationService.ReleasePushClaimAsync(device.DeviceId, date, timeZoneId,
                                isRetryPush);
                            Logger.LogInformation(
                                "🔄 Released Redis claim for failed device: {DeviceId} (all {ContentCount} pushes failed)",
                                device.DeviceId, contents.Count);
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError(ex, "Failed to release Redis claim for device {DeviceId}", device.DeviceId);
                        }
                    }
                }
            });

            // Flatten device results to individual push results for compatibility
            var allDeviceResults = await Task.WhenAll(deviceTasks);
            var results = allDeviceResults.SelectMany(results => results).ToArray();

            var totalPushTasks = results.Length;
            Logger.LogInformation(
                "ProcessDailyPushAsync: Executing {TotalPushTasks} push tasks for {DeviceCount} devices × {ContentCount} contents",
                totalPushTasks, enabledDevices.Count, contents.Count);

            successCount = results.Count(r => r);
            failureCount = results.Count(r => !r);

            Logger.LogInformation(
                "ProcessDailyPushAsync Summary - User {UserId}: {SuccessCount} success, {FailureCount} failures for {DeviceCount} devices. Individual pushes: {TotalPushes}",
                State.UserId, successCount, failureCount, enabledDevices.Count, results.Length);
        }
        finally
        {
            userSemaphore.Release();
            Logger.LogDebug("🔓 Released push semaphore for user {UserId}, timezone {TimeZone}",
                State.UserId, timeZoneId);
        }
    }

    public async Task<List<UserDeviceInfoV2Proto>> GetDevicesForCoordinatedPushAsync(string timeZoneId, DateTime targetDate)
    {
        try
        {
            var allDevices = await GetUnifiedDevicesAsync();
            var targetDevices = allDevices.Where(d => d.TimeZoneId == timeZoneId && d.PushEnabled).ToList();

            Logger.LogDebug("📋 Collected {DeviceCount} devices for coordinated push in {TimeZone} for user {UserId}",
                targetDevices.Count, timeZoneId, State.UserId);

            return targetDevices;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to get devices for coordinated push - User: {UserId}, TimeZone: {TimeZone}",
                State.UserId, timeZoneId);
            return new List<UserDeviceInfoV2Proto>();
        }
    }

    public async Task<bool> ExecuteCoordinatedPushAsync(UserDeviceInfoV2Proto device, DateTime targetDate,
        List<DailyNotificationContent> contents, bool isRetryPush = false, bool isTestPush = false)
    {
        try
        {
            Logger.LogInformation(
                "🎯 Executing coordinated push for device {DeviceId}, User: {UserId}, Retry: {IsRetry}, Test: {IsTest}",
                device.DeviceId, State.UserId, isRetryPush, isTestPush);

            var deduplicationService = _serviceProvider.GetService<IPushDeduplicationService>();
            var date = DateOnly.FromDateTime(targetDate);
            bool canSend = true;

            if (!isTestPush && deduplicationService != null)
            {
                if (isRetryPush)
                {
                    // Check both user-level and device-level read status
                    var dateKey = date.ToString("yyyy-MM-dd");
                    var userRead = State.DailyPushReadStatus.TryGetValue(dateKey, out var isRead) && isRead;
                    var deviceRead = false;

                    if (!userRead)
                    {
                        // Only check device-level read status if user-level is not read
                        deviceRead = await deduplicationService.IsDeviceReadAsync(device.DeviceId, date);
                    }

                    if (userRead || deviceRead)
                    {
                        var readType = userRead ? "user-level" : "device-level";
                        Logger.LogInformation("📖 Device {DeviceId} already read ({ReadType}) - skipping retry push",
                            device.DeviceId, readType);
                        return false;
                    }

                    canSend = await deduplicationService.TryClaimRetryPushAsync(device.DeviceId, date,
                        device.TimeZoneId);
                }
                else
                {
                    canSend = await deduplicationService.TryClaimMorningPushAsync(device.DeviceId, date,
                        device.TimeZoneId);
                }

                Logger.LogInformation("🔑 Redis deduplication result for {DeviceId}: {CanSend}", device.DeviceId,
                    canSend);
            }

            if (!canSend)
            {
                Logger.LogInformation("❌ Coordinated push blocked by deduplication for device {DeviceId}",
                    device.DeviceId);
                return false;
            }

            // Execute the actual push using existing logic
            var success =
                await SendCoordinatedPushNotificationAsync(device, contents, targetDate, isRetryPush, isTestPush);

            if (success)
            {
                Logger.LogInformation("✅ Coordinated push completed successfully for device {DeviceId}",
                    device.DeviceId);
            }
            else
            {
                Logger.LogWarning("❌ Coordinated push failed for device {DeviceId}", device.DeviceId);

                // Release Redis claim if push failed
                if (!isTestPush && deduplicationService != null && canSend)
                {
                    await deduplicationService.ReleasePushClaimAsync(device.DeviceId, date, device.TimeZoneId,
                        isRetryPush);
                    Logger.LogInformation("🔄 Released Redis claim for failed push - Device: {DeviceId}",
                        device.DeviceId);
                }
            }

            return success;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in coordinated push execution - Device: {DeviceId}, User: {UserId}",
                device.DeviceId, State.UserId);
            return false;
        }
    }

    // === Device Management Methods ===

    public async Task CleanupDevicesV2Async()
    {
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);
        var threeDaysAgo = now.AddDays(-3);

        var devicesToRemove = new List<string>();
        var cleanupDetails = new Dictionary<string, string>();

        // Remove devices with consecutive failures
        var failedDevices = State.UserDevicesV2.Values
            .Where(d => d.ConsecutiveFailures >= 5 && d.LastActiveAt.LessOrEqualThan(threeDaysAgo))
            .Select(d => d.DeviceId).ToList();
        devicesToRemove.AddRange(failedDevices);
        if (failedDevices.Any())
        {
            cleanupDetails["consecutive_failures"] = string.Join(",", failedDevices);
        }

        // Remove expired devices
        var expiredDevices = State.UserDevicesV2.Values
            .Where(d => d.LastActiveAt.LessOrEqualThan(thirtyDaysAgo))
            .Select(d => d.DeviceId).ToList();
        devicesToRemove.AddRange(expiredDevices);
         if (expiredDevices.Any())
        {
            cleanupDetails["expired"] = string.Join(",", expiredDevices);
        }

        // Remove pending cleanup devices
        var pendingCleanup = State.UserDevicesV2.Values
            .Where(d => d.Status == (int)DeviceStatus.PendingCleanup)
            .Select(d => d.DeviceId).ToList();
        devicesToRemove.AddRange(pendingCleanup);
        if (pendingCleanup.Any())
        {
            cleanupDetails["pending_cleanup"] = string.Join(",", pendingCleanup);
        }

        // Handle duplicates: same deviceId, keep the most recent one
        var duplicateGroups = State.UserDevicesV2.Values
            .GroupBy(d => d.DeviceId)
            .Where(g => g.Count() > 1)
            .ToList();

        foreach (var group in duplicateGroups)
        {
            var devicesToKeep = group.OrderByDescending(d => d.LastActiveAt).Skip(1);
            var duplicateIds = devicesToKeep.Select(d => d.DeviceId).ToList();
            devicesToRemove.AddRange(duplicateIds);
            if (duplicateIds.Any())
            {
                cleanupDetails["duplicates"] = string.Join(",", duplicateIds);
            }
        }

        devicesToRemove = devicesToRemove.Distinct().ToList();

        if (devicesToRemove.Any())
        {
            var cleanupEvent = new CleanupDevicesV2Event
            {
                RemovedCount = devicesToRemove.Count,
                CleanupReason = "enhanced_criteria",
                CleanupTime = now.ToProtoTimestamp()
            };
            cleanupEvent.DeviceIdsToRemove.AddRange(devicesToRemove);
            foreach (var kvp in cleanupDetails)
                cleanupEvent.CleanupDetails[kvp.Key] = kvp.Value;
            RaiseEvent(cleanupEvent);

            await ConfirmEventsAsync();
            Logger.LogInformation("🧹 V2 Enhanced device cleanup: removed {Count} devices. Details: {Details}",
                devicesToRemove.Count, JsonSerializer.Serialize(cleanupDetails));
        }
    }

    public async Task UpdateTimezoneIndexAsync(string? oldTimeZone, string newTimeZone)
    {
        var userId = Guid.Parse(State.UserId);
        try
        {

            // Remove user from old timezone index
            if (!string.IsNullOrEmpty(oldTimeZone))
            {
                var oldIndexGAgent =
                    await GetPushSubscriberIndexAgentAsync(DailyPushConstants.TimezoneToGuid(oldTimeZone));
                await oldIndexGAgent.InitializeAsync(oldTimeZone);
                await oldIndexGAgent.RemoveUserFromTimezoneAsync(userId);
                Logger.LogDebug($"Removed user {State.UserId} from timezone index: {oldTimeZone}");
            }

            // Add user to new timezone index
            if (!string.IsNullOrEmpty(newTimeZone))
            {
                var newIndexGAgent =
                    await GetPushSubscriberIndexAgentAsync(DailyPushConstants.TimezoneToGuid(newTimeZone));
                await newIndexGAgent.InitializeAsync(newTimeZone);
                await newIndexGAgent.AddUserToTimezoneAsync(userId);
                Logger.LogDebug($"Added user {State.UserId} to timezone index: {newTimeZone}");

                // CRITICAL: Complete timezone ecosystem initialization
                // This ensures ALL timezone-related agents are ready for immediate daily push operation
                await InitializeTimezoneEcosystemAsync(newTimeZone);
            }

            Logger.LogInformation($"Updated timezone index for user {userId}: {oldTimeZone} -> {newTimeZone}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Failed to update timezone index for user {UserId}", userId);
        }
    }

    public async Task<object> GetPushDebugInfoAsync(string deviceId, DateOnly date, string timeZoneId)
    {
        var deduplicationService =
            _serviceProvider.GetService(typeof(IPushDeduplicationService)) as IPushDeduplicationService;
        var readKey = date.ToString("yyyy-MM-dd");

        // Get Redis deduplication status
        PushDeduplicationStatus? redisStatus = null;
        if (deduplicationService != null)
        {
            try
            {
                redisStatus = await deduplicationService.GetStatusAsync(deviceId, date, timeZoneId);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "Failed to get Redis deduplication status for device {DeviceId}", deviceId);
            }
        }

        // Get State information
        var stateInfo = new
        {
            IsRead = State.DailyPushReadStatus.TryGetValue(readKey, out var isRead) && isRead,
            ReadKey = readKey,
            HasDevice = State.UserDevicesV2.ContainsKey(deviceId),
            DeviceInfo = State.UserDevicesV2.TryGetValue(deviceId, out var deviceV2)
                ? new
                {
                    DeviceId = deviceV2.DeviceId,
                    PushToken = !string.IsNullOrEmpty(deviceV2.PushToken)
                        ? deviceV2.PushToken.Substring(0, Math.Min(12, deviceV2.PushToken.Length)) + "..."
                        : "EMPTY",
                    PushEnabled = deviceV2.PushEnabled,
                    TimeZoneId = deviceV2.TimeZoneId,
                    Language = deviceV2.PushLanguage,
                    RegisteredAt = deviceV2.RegisteredAt.ToFormattedString("yyyy-MM-dd HH:mm:ss"),
                    LastTokenUpdate = deviceV2.LastTokenUpdate.ToFormattedString("yyyy-MM-dd HH:mm:ss"),
                    Platform = deviceV2.Platform,
                    Status = deviceV2.Status
                }
                : null
        };

        return new
        {
            UserId = State.UserId,
            DeviceId = deviceId,
            Date = date.ToString("yyyy-MM-dd"),
            TimeZoneId = timeZoneId,
            Redis = redisStatus != null
                ? (object)new
                {
                    MorningSent = redisStatus.MorningSent,
                    RetrySent = redisStatus.RetrySent,
                    MorningKey = redisStatus.MorningKey,
                    RetryKey = redisStatus.RetryKey,
                    MorningSentTime = redisStatus.MorningSentTime?.ToString("yyyy-MM-dd HH:mm:ss"),
                    RetrySentTime = redisStatus.RetrySentTime?.ToString("yyyy-MM-dd HH:mm:ss")
                }
                : new { Error = "Redis service not available" },
            State = stateInfo,
            Timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    public Task<UserDeviceInfoV2Proto?> GetDeviceStatusAsync(string deviceId)
    {
        // ✅ V2 ONLY: Return V2 Proto format
        if (State.UserDevicesV2.TryGetValue(deviceId, out var deviceV2))
        {
            return Task.FromResult<UserDeviceInfoV2Proto?>(deviceV2);
        }

        return Task.FromResult<UserDeviceInfoV2Proto?>(new UserDeviceInfoV2Proto { PushEnabled = true });  // Default for non-existent devices
    }

    // === Private Helper Methods ===

    private async Task CleanupExpiredReadStatusAsync(DateTime currentDate)
    {
        try
        {
            var currentDateKey = currentDate.ToString("yyyy-MM-dd");
            var yesterdayDateKey = currentDate.AddDays(-1).ToString("yyyy-MM-dd");

            var keysToRemove = State.DailyPushReadStatus.Keys
                .Where(key => key != currentDateKey && key != yesterdayDateKey)
                .ToList();

            if (keysToRemove.Count > 0)
            {
                foreach (var key in keysToRemove)
                {
                    //TODO Handle through event log
                    State.DailyPushReadStatus.Remove(key);
                }

                Logger.LogInformation(
                    "🧹 Cleaned up {Count} expired read status entries for user {UserId} (kept: {Current}, {Yesterday})",
                    keysToRemove.Count, State.UserId, currentDateKey, yesterdayDateKey);

                await ConfirmEventsAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Failed to cleanup expired read status for user {UserId}", State.UserId);
        }
    }

    public async Task<bool> HasEnabledDeviceInTimezoneAsync(string timeZoneId)
    {
        // 🔄 Use unified interface to check all devices (V2 + V1)
        var allDevices = await GetUnifiedDevicesAsync();
        var enabledDevicesInTimezone = allDevices.Where(d => d.PushEnabled && d.TimeZoneId == timeZoneId).ToList();

        Logger.LogDebug(
            "HasEnabledDeviceInTimezoneAsync: TimeZone={TimeZone}, TotalDevices={Total}, EnabledInTimezone={Enabled}, DeviceIds=[{DeviceIds}]",
            timeZoneId, allDevices.Count, enabledDevicesInTimezone.Count,
            string.Join(", ", enabledDevicesInTimezone.Select(d => d.DeviceId)));

        return enabledDevicesInTimezone.Any();
    }

    // === State Transition ===

    protected override void TransitionState(DailyPushUserStateProto state, IMessage @event)
    {
        switch (@event)
        {
            case InitializeDailyPushUserEvent initEvent:
                state.UserId = initEvent.UserId;
                state.InitializedAt = initEvent.InitializedAt;
                state.StateVersion = 1;
                break;

            case RegisterOrUpdateDeviceV2Event registerEvent:
                state.UserDevicesV2[registerEvent.DeviceId] = registerEvent.DeviceInfo;
                if (!string.IsNullOrEmpty(registerEvent.DeviceInfo.PushToken))
                {
                    state.TokenToDeviceMapV2[registerEvent.DeviceInfo.PushToken] = registerEvent.DeviceId;
                }
                break;

            case MarkDailyPushReadEvent markEvent:
                state.DailyPushReadStatus[markEvent.DateKey] = true;
                break;

            case CleanupDevicesV2Event cleanupEvent:
                foreach (var deviceId in cleanupEvent.DeviceIdsToRemove)
                {
                    if (state.UserDevicesV2.TryGetValue(deviceId, out var device))
                    {
                        if (!string.IsNullOrEmpty(device.PushToken))
                        {
                            state.TokenToDeviceMapV2.Remove(device.PushToken);
                        }
                        state.UserDevicesV2.Remove(deviceId);
                    }
                }
                Logger.LogDebug(
                    "🧹 V2 Device cleanup completed: removed {RemovedCount} devices, reason: {CleanupReason}",
                    cleanupEvent.RemovedCount, cleanupEvent.CleanupReason);
                break;

            case CleanExpiredReadStatusEvent cleanReadEvent:
                foreach (var dateKey in cleanReadEvent.DateKeysRemoved)
                {
                    state.DailyPushReadStatus.Remove(dateKey);
                }
                break;
        }
    }

    /// <summary>
    /// Send push notification directly to Firebase FCM API using Global JWT Provider
    /// This replaces the FirebaseService architecture with a more efficient global JWT approach
    /// </summary>
    private async Task<bool> SendDirectPushNotificationAsync(
        GlobalJwtProviderGAgent globalJwtProvider,
        string projectId,
        string pushToken,
        string title,
        string content,
        Dictionary<string, object>? data = null,
        string timeZoneId = "UTC",
        bool isRetryPush = false,
        bool isFirstContent = true,
        bool isTestPush = false,
        bool skipDeduplicationCheck = false)
    {
        try
        {
            if (string.IsNullOrEmpty(pushToken))
            {
                Logger.LogWarning("Push token is empty");
                return false;
            }

            // Deduplication removed - all pushes are allowed

            // Get JWT access token from global provider
            var accessToken = await globalJwtProvider.GetFirebaseAccessTokenAsync();
            if (string.IsNullOrEmpty(accessToken))
            {
                Logger.LogError("Failed to obtain access token from Global JWT Provider");
                return false;
            }

            // Create FCM v1 payload
            var dataPayload = CreateDataPayload(data);
            var message = new
            {
                message = new
                {
                    token = pushToken,
                    notification = new
                    {
                        title = title,
                        body = content
                    },
                    data = dataPayload,
                    android = new
                    {
                        priority = "high",
                        notification = new
                        {
                            sound = "default",
                            channel_id = "daily_push_channel",
                            notification_count = dataPayload.TryGetValue("content_index", out var contentIndex)
                                ? int.Parse(contentIndex.ToString() ?? "1")
                                : 1
                        }
                    },
                    apns = new
                    {
                        headers = new
                        {
                            apns_push_type = "alert"
                        },
                        payload = new
                        {
                            aps = new
                            {
                                sound = "default"
                            }
                        }
                    }
                }
            };

            // Send HTTP request to Firebase FCM API
            var httpClient = _serviceProvider.GetService(typeof(HttpClient)) as HttpClient;
            if (httpClient == null)
            {
                Logger.LogError("HttpClient not available for push notification");
                return false;
            }

            var fcmEndpoint = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";
            var jsonPayload = JsonSerializer.Serialize(message, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            var httpContent = new StringContent(jsonPayload, Encoding.UTF8, MediaTypeNames.Application.Json);
            using var request = new HttpRequestMessage(HttpMethod.Post, fcmEndpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = httpContent;

            Logger.LogDebug("Sending direct FCM push to token: {TokenPrefix}...",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);

            using var response = await httpClient.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            // 📱 Log FCM response for debugging (Information level for visibility)
            Logger.LogInformation("📱 FCM Response: {StatusCode} - Body: {ResponseContent}",
                response.StatusCode, responseContent);

            if (response.IsSuccessStatusCode)
            {
                // ✅ CRITICAL FIX: Parse FCM response to check for actual success/failure
                // HTTP 200 doesn't guarantee push success - FCM may return errors in response body
                try
                {
                    using var jsonDocument = JsonDocument.Parse(responseContent);
                    var root = jsonDocument.RootElement;

                    // Check if FCM returned an error despite 200 status
                    if (root.TryGetProperty("error", out var errorElement))
                    {
                        var errorCode = errorElement.TryGetProperty("code", out var codeElement)
                            ? codeElement.GetString()
                            : "UNKNOWN";
                        var errorMessage = errorElement.TryGetProperty("message", out var msgElement)
                            ? msgElement.GetString()
                            : "Unknown error";

                        Logger.LogError(
                            "🚨 FCM returned error despite 200 status - Code: {ErrorCode}, Message: {ErrorMessage}, Token: {TokenPrefix}..., Title: '{Title}'",
                            errorCode, errorMessage,
                            pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);

                        // Handle specific FCM errors for token cleanup
                        if (errorCode == "UNREGISTERED" || errorCode == "INVALID_ARGUMENT")
                        {
                            Logger.LogWarning(
                                "❌ Push token is invalid - initiating automatic device cleanup. Token: {TokenPrefix}..., ErrorCode: {ErrorCode}",
                                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, errorCode);

                            // 🎯 Automatically cleanup invalid devices
                            await MarkDeviceTokenAsInvalidAsync(pushToken, errorCode);
                        }

                        return false; // ❌ Actual failure despite 200 status
                    }

                    // Check for success indicator (message name in response)
                    if (root.TryGetProperty("name", out var nameElement))
                    {
                        var messageName = nameElement.GetString();
                        Logger.LogInformation(
                            "✅ Push notification sent successfully - Message: {MessageName}, Token: {TokenPrefix}..., Title: '{Title}'",
                            messageName,
                            pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                        return true; // ✅ Confirmed success
                    }

                    // Unexpected response format with 200 status
                    Logger.LogWarning(
                        "⚠️ FCM returned 200 but unexpected response format: {ResponseContent}, Token: {TokenPrefix}..., Title: '{Title}'",
                        responseContent,
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return false; // Treat unexpected format as failure
                }
                catch (JsonException ex)
                {
                    Logger.LogError(ex, "Failed to parse FCM response JSON: {ResponseContent}", responseContent);
                    // For unparseable responses with 200 status, assume success for backward compatibility
                    Logger.LogInformation(
                        "⚠️ Push assumed successful due to 200 status (unparseable response) - Token: {TokenPrefix}..., Title: '{Title}'",
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);
                    return true;
                }
            }
            else
            {
                Logger.LogError(
                    "❌ FCM request failed with status {StatusCode}: {ResponseContent}, Token: {TokenPrefix}..., Title: '{Title}'",
                    response.StatusCode, responseContent,
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, title);

                // Handle specific FCM v1 errors for token cleanup
                if (responseContent.Contains("UNREGISTERED") || responseContent.Contains("INVALID_ARGUMENT"))
                {
                    Logger.LogWarning(
                        "❌ Token is invalid - initiating automatic device cleanup. Token: {TokenPrefix}..., Status: {StatusCode}",
                        pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, response.StatusCode);

                    // 🎯 Automatically cleanup invalid devices
                    var errorCode = responseContent.Contains("UNREGISTERED") ? "UNREGISTERED" : "INVALID_ARGUMENT";
                    await MarkDeviceTokenAsInvalidAsync(pushToken, errorCode);
                }

                return false;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in direct push notification: {ErrorMessage}", ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Mark device token as invalid and remove the device automatically
    /// Called when FCM returns UNREGISTERED or INVALID_ARGUMENT errors
    /// </summary>
    private async Task MarkDeviceTokenAsInvalidAsync(string pushToken, string errorCode)
    {
        try
        {
            if (string.IsNullOrEmpty(pushToken))
            {
                Logger.LogWarning("Cannot mark device for cleanup: pushToken is null or empty");
                return;
            }

            var devicesToRemove = new List<string>();
            var cleanupDetails = new Dictionary<string, string>
            {
                ["error_code"] = errorCode,
                ["token_prefix"] = pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken,
                ["cleanup_trigger"] = "fcm_token_invalid",
                ["timestamp"] = DateTime.UtcNow.ToString("O")
            };

            // 🔥 V1 devices no longer used in logic - skip V1 token cleanup

            // Check V2 devices
            var v2DevicesToRemove = State.UserDevicesV2.Values
                .Where(d => d.PushToken == pushToken)
                .Select(d => d.DeviceId)
                .ToList();

            if (v2DevicesToRemove.Any())
            {
                devicesToRemove.AddRange(v2DevicesToRemove);
                cleanupDetails["v2_devices"] = string.Join(",", v2DevicesToRemove);

                Logger.LogInformation(
                    "🧹 Marking {Count} V2 devices for cleanup due to invalid token - Devices: {DeviceIds}, Error: {ErrorCode}",
                    v2DevicesToRemove.Count, string.Join(",", v2DevicesToRemove), errorCode);

                // Remove V2 devices  
                var cleanupEvent = new CleanupDevicesV2Event
                {
                    RemovedCount = v2DevicesToRemove.Count,
                    CleanupReason = $"fcm_token_invalid_{errorCode}"
                };
                cleanupEvent.DeviceIdsToRemove.AddRange(v2DevicesToRemove);
                foreach (var kvp in cleanupDetails)
                    cleanupEvent.CleanupDetails[kvp.Key] = kvp.Value;
                RaiseEvent(cleanupEvent);
            }

            if (devicesToRemove.Any())
            {
                await ConfirmEventsAsync();
                Logger.LogWarning(
                    "🗑️ Automatically removed {Count} devices with invalid pushToken - Error: {ErrorCode}, Token: {TokenPrefix}..., DeviceIds: {DeviceIds}",
                    devicesToRemove.Count, errorCode,
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken,
                    string.Join(",", devicesToRemove));
            }
            else
            {
                Logger.LogDebug("🔍 No devices found with invalid token {TokenPrefix}... for cleanup",
                    pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Exception in MarkDeviceTokenAsInvalidAsync - Token: {TokenPrefix}..., Error: {ErrorCode}",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken, errorCode);
        }
    }

    /// <summary>
    /// Send coordinated push notification using existing Firebase logic
    /// </summary>
    private async Task<bool> SendCoordinatedPushNotificationAsync(UserDeviceInfoV2Proto device,
        List<DailyNotificationContent> contents, DateTime targetDate, bool isRetryPush, bool isTestPush)
    {
        try
        {
            // Get Global JWT Provider and Firebase project configuration
            var globalJwtProvider = await GetGlobalJwtProviderAgentAsync();
            var firebaseService = _serviceProvider.GetService(typeof(FirebaseService)) as FirebaseService;

            if (firebaseService == null)
            {
                Logger.LogError("FirebaseService not available for coordinated push");
                return false;
            }

            var projectId = firebaseService.ProjectId;
            if (string.IsNullOrEmpty(projectId))
            {
                Logger.LogError("Firebase ProjectId not configured for coordinated push");
                return false;
            }

            var successCount = 0;
            var totalContents = contents.Count;

            // Send only the first content for morning push, second content for afternoon retry
            var contentToSend = isRetryPush ? contents.Skip(1).FirstOrDefault() : contents.FirstOrDefault();

            if (contentToSend == null)
            {
                Logger.LogWarning("No content available for {PushType} push - Device: {DeviceId}",
                    isRetryPush ? "afternoon retry" : "morning", device.DeviceId);
                return false;
            }

            // Send the selected content
            var contentIndex = isRetryPush ? 1 : 0;

            try
            {
                // Get localized content based on device language preference
                var localizedContent = contentToSend.GetLocalizedContent(device.PushLanguage);

                var pushData = new Dictionary<string, object>
                {
                    ["type"] = (int)DailyPushConstants.PushType.DailyPush,
                    ["contentId"] = contentToSend.Id,
                    ["contentIndex"] = contentIndex,
                    ["totalContents"] = totalContents,
                    ["isTestPush"] = isTestPush,
                    ["targetDate"] = targetDate.ToString("yyyy-MM-dd"),
                    ["deviceId"] = device.DeviceId
                };

                var success = await SendDirectPushNotificationAsync(
                    globalJwtProvider,
                    projectId,
                    device.PushToken,
                    localizedContent.Title,
                    localizedContent.Content,
                    pushData,
                    device.TimeZoneId,
                    isRetryPush,
                    isTestPush,
                    skipDeduplicationCheck: true // Already handled by coordinator
                );

                if (success)
                {
                    successCount++;
                    Logger.LogDebug(
                        "✅ Coordinated push content {ContentIndex}/{TotalContents} sent successfully - Device: {DeviceId}",
                        contentIndex + 1, totalContents, device.DeviceId);
                }
                else
                {
                    Logger.LogWarning(
                        "❌ Coordinated push content {ContentIndex}/{TotalContents} failed - Device: {DeviceId}",
                        contentIndex + 1, totalContents, device.DeviceId);

                    // Check if this was a token failure and mark device for cleanup
                    await CheckAndMarkDeviceForCleanupAsync(device.PushToken, "PUSH_FAILED");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "Exception sending coordinated push content {ContentIndex}/{TotalContents} - Device: {DeviceId}",
                    contentIndex + 1, totalContents, device.DeviceId);
            }

            var allSuccessful = successCount == 1; // Only one content sent
            Logger.LogInformation(
                "📊 Coordinated push summary - Device: {DeviceId}, Success: {SuccessCount}/1, AllSuccessful: {AllSuccessful}",
                device.DeviceId, successCount, allSuccessful);

            return allSuccessful;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in SendCoordinatedPushNotificationAsync - Device: {DeviceId}",
                device.DeviceId);
            return false;
        }
    }

    /// <summary>
    /// Check if device should be marked for cleanup based on push failures
    /// </summary>
    private async Task CheckAndMarkDeviceForCleanupAsync(string pushToken, string failureReason)
    {
        try
        {
            // For coordinated push, call the automatic cleanup
            await MarkDeviceTokenAsInvalidAsync(pushToken, failureReason);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Exception in CheckAndMarkDeviceForCleanupAsync - Token: {TokenPrefix}...",
                pushToken.Length > 10 ? pushToken.Substring(0, 10) : pushToken);
        }
    }

    /// <summary>
    /// Complete timezone ecosystem initialization - ensures ALL timezone-related agents are ready
    /// This method guarantees immediate push system availability when user switches to new timezone
    /// </summary>
    private async Task InitializeTimezoneEcosystemAsync(string newTimeZone)
    {
        // 🛡️ SAFETY: Validate timezone before any Grain operations
        if (string.IsNullOrWhiteSpace(newTimeZone))
        {
            Logger.LogWarning(
                "Empty or invalid timezone ID '{TimeZone}' - skipping timezone ecosystem initialization to prevent orphaned Grains",
                newTimeZone ?? "null");
            return;
        }

        try
        {
            // Validate timezone format before creating any Grains
            TimeZoneInfo.FindSystemTimeZoneById(newTimeZone);
            Logger.LogInformation("Starting complete timezone ecosystem initialization for {TimeZone}", newTimeZone);

            // Step 1: Initialize DailyContentGAgent timezone mapping (global content service)
            var contentGAgent = await GetDailyContentAgentAsync();
            await contentGAgent.RegisterTimezoneGuidMappingAsync(DailyPushConstants.TimezoneToGuid(newTimeZone),
                newTimeZone);
            Logger.LogDebug("DailyContentGAgent timezone mapping registered for {TimeZone}", newTimeZone);

            // Step 2: Initialize and fully activate DailyPushCoordinatorGAgent
            // Note: DailyPushCoordinatorGAgent uses Aevatar.Core.GAgentBase (Orleans-based), not IGAgent, so use _clusterClient
            var coordinatorGAgent =
                _clusterClient.GetGrain<IDailyPushCoordinatorGAgent>(DailyPushConstants.TimezoneToGuid(newTimeZone));

            // Force complete initialization and activation
            await coordinatorGAgent.InitializeAsync(newTimeZone);

            // 🔥 CRITICAL: Force grain to stay active by calling multiple methods
            var status = await coordinatorGAgent.GetStatusAsync();

            Logger.LogInformation(
                "DailyPushCoordinatorGAgent fully initialized for {TimeZone}. Status: {Status}, ReminderTargetId: {TargetId}",
                newTimeZone, status.Status, status.ReminderTargetId);

            // Step 3: Validate reminders are registered (if authorized)
            if (status.ReminderTargetId != Guid.Empty)
            {
                Logger.LogInformation("Daily push reminders are ready for {TimeZone} with authorized ReminderTargetId",
                    newTimeZone);
            }
            else
            {
                Logger.LogWarning(
                    "ReminderTargetId is empty for {TimeZone} - daily pushes may not work until properly configured",
                    newTimeZone);
            }

            // Step 4: Pre-warm timezone calculations to ensure immediate readiness
            var timezoneInfo = TimeZoneInfo.FindSystemTimeZoneById(newTimeZone);
            var currentUtc = DateTime.UtcNow;
            var currentLocal = TimeZoneInfo.ConvertTimeFromUtc(currentUtc, timezoneInfo);

            Logger.LogInformation(
                "Timezone ecosystem fully initialized for {TimeZone}. Current local time: {LocalTime} (UTC: {UtcTime})",
                newTimeZone, currentLocal.ToString("yyyy-MM-dd HH:mm:ss"), currentUtc.ToString("yyyy-MM-dd HH:mm:ss"));

            // Step 5: Final verification - ensure all components are responsive
            var verificationTasks = new Task[]
            {
                contentGAgent.GetTimezoneFromGuidAsync(DailyPushConstants.TimezoneToGuid(newTimeZone)),
                coordinatorGAgent.GetStatusAsync()
            };

            await Task.WhenAll(verificationTasks);
            Logger.LogInformation("Timezone ecosystem verification completed for {TimeZone} - all agents responsive",
                newTimeZone);
        }
        catch (TimeZoneNotFoundException ex)
        {
            Logger.LogError(ex, "Invalid timezone ID '{TimeZone}' - timezone ecosystem initialization failed",
                newTimeZone);
            throw new ArgumentException($"Invalid timezone ID: {newTimeZone}", nameof(newTimeZone), ex);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "Timezone ecosystem initialization failed for {TimeZone} - daily pushes may not work properly",
                newTimeZone);
            // Don't rethrow - allow timezone index update to succeed even if ecosystem init partially fails
        }
    }

    

    /// <summary>
    /// Create data payload for FCM message, converting all values to strings
    /// </summary>
    private static Dictionary<string, string> CreateDataPayload(Dictionary<string, object>? data)
    {
        var dataPayload = new Dictionary<string, string>();
        if (data != null)
        {
            foreach (var kvp in data)
            {
                dataPayload[kvp.Key] = kvp.Value?.ToString() ?? "";
            }
        }

        return dataPayload;
    }
    
    /// <summary>
    /// Get GlobalJwtProviderGAgent via IGAgentFactory (new framework)
    /// </summary>
    private async Task<GlobalJwtProviderGAgent> GetGlobalJwtProviderAgentAsync()
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<GlobalJwtProviderGAgent>(DailyPushConstants.GLOBAL_JWT_PROVIDER_ID);
        return (GlobalJwtProviderGAgent)actor.GetAgent();
    }
    
    private async Task<IPushSubscriberIndexGAgent> GetPushSubscriberIndexAgentAsync(Guid timezoneGuid)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<PushSubscriberIndexGAgent>(timezoneGuid);
        return (IPushSubscriberIndexGAgent)actor.GetAgent();
    }
    
    /// <summary>
    /// Get DailyContentGAgent via IGAgentFactory (new framework)
    /// </summary>
    private async Task<IDailyContentGAgent> GetDailyContentAgentAsync()
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<DailyContentGAgent>(DailyPushConstants.CONTENT_GAGENT_ID);
        return (IDailyContentGAgent)actor.GetAgent();
    }
}

