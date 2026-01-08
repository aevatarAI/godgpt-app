using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions.CQRS;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs;

/// <summary>
/// Hangfire background job for Lumen daily prediction reminders
/// Replaces Orleans IRemindable with external scheduling
/// Uses CQRS/Elasticsearch to query active users, triggers via LumenService
/// </summary>
public class LumenDailyReminderJob
{
    private readonly ILumenService _lumenService;
    private readonly IStateIndexService _stateIndexService;
    private readonly ILogger<LumenDailyReminderJob> _logger;
    private readonly LumenReminderOptions _options;

    // Agent type name for CQRS query (must use full namespace)
    private const string LumenUserProfileAgentType = "Aevatar.Agents.Lumen.UserProfile.LumenUserProfileGAgent";

    public LumenDailyReminderJob(
        ILumenService lumenService,
        IStateIndexService stateIndexService,
        ILogger<LumenDailyReminderJob> logger,
        IOptions<LumenReminderOptions> options)
    {
        _lumenService = lumenService;
        _stateIndexService = stateIndexService;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>
    /// Main execution method called by Hangfire scheduler
    /// </summary>
    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.IsEnabled || !_options.EnableDailyAutoGeneration)
        {
            _logger.LogDebug("[LumenReminder] Job is disabled, skipping execution");
            return;
        }

        _logger.LogInformation("[LumenReminder] Starting daily reminder check at {Time}", DateTime.UtcNow);

        try
        {
            // Get all active users from CQRS/Elasticsearch
            var cutoffDate = DateTime.UtcNow.AddDays(-_options.InactivityThresholdDays);
            var activeUsers = await GetActiveUsersFromCqrsAsync(cutoffDate, cancellationToken);

            if (!activeUsers.Any())
            {
                _logger.LogInformation("[LumenReminder] No active users found");
                return;
            }

            _logger.LogInformation("[LumenReminder] Found {Count} active users", activeUsers.Count);

            // Group users by timezone
            var usersByTimezone = activeUsers
                .GroupBy(u => u.TimeZone ?? "UTC")
                .ToList();

            var processedCount = 0;
            var skippedCount = 0;
            var errorCount = 0;

            foreach (var timezoneGroup in usersByTimezone)
            {
                var timeZoneId = timezoneGroup.Key;
                
                // Check if this timezone just entered a new day
                if (!IsNewDayInTimezone(timeZoneId))
                {
                    skippedCount += timezoneGroup.Count();
                    continue;
                }

                _logger.LogInformation(
                    "[LumenReminder] Processing {Count} users in timezone {TimeZone}",
                    timezoneGroup.Count(), timeZoneId);

                // Process users in this timezone
                foreach (var user in timezoneGroup)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        await ProcessUserReminderAsync(user, timeZoneId, cancellationToken);
                        processedCount++;
                        
                        // Add small delay to prevent overwhelming the system
                        if (_options.UserProcessingDelayMs > 0)
                        {
                            await Task.Delay(_options.UserProcessingDelayMs, cancellationToken);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorCount++;
                        _logger.LogError(ex, 
                            "[LumenReminder] Error processing user {UserId}", user.UserId);
                    }
                }
            }

            _logger.LogInformation(
                "[LumenReminder] Completed: Processed={Processed}, Skipped={Skipped}, Errors={Errors}",
                processedCount, skippedCount, errorCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenReminder] Fatal error in daily reminder job");
            throw; // Re-throw to let Hangfire handle retry
        }
    }

    /// <summary>
    /// Get active users using CQRS/Elasticsearch query
    /// Supports both new users (with isDailyReminderEnabled) and legacy users (without the field)
    /// </summary>
    private async Task<List<LumenReminderUserInfo>> GetActiveUsersFromCqrsAsync(
        DateTime cutoffDate,
        CancellationToken cancellationToken)
    {
        try
        {
            // Query all LumenUserProfile users, filter in code for backwards compatibility
            // Legacy users don't have isDailyReminderEnabled field, treat them as enabled
            var cutoffDateStr = cutoffDate.ToString("yyyy-MM-ddTHH:mm:ssZ");
            
            // Build query: get users active within threshold (using updatedAt as fallback)
            // OR all users if they don't have lastActiveDate field yet
            var queryString = $"updatedAt:[{cutoffDateStr} TO *]";
            
            _logger.LogDebug("[LumenReminder] CQRS query: {Query}", queryString);

            var query = new StateQuery
            {
                AgentType = LumenUserProfileAgentType,
                QueryString = queryString,
                PageIndex = 0,
                PageSize = _options.BatchSize,
                SortFields = new List<string> { "updatedAt:desc" }
            };

            var result = await _stateIndexService.QueryAsync(query, cancellationToken);
            
            _logger.LogInformation("[LumenReminder] CQRS returned {Count} users (total: {Total})", 
                result.Items.Count(), result.TotalCount);

            var users = new List<LumenReminderUserInfo>();
            
            foreach (var item in result.Items)
            {
                try
                {
                    var data = item.Data;
                    if (data == null) continue;

                    // Check if reminder is explicitly disabled (default to true for legacy users)
                    var isDailyReminderEnabled = GetBoolValue(data, "isDailyReminderEnabled") ?? true;
                    if (!isDailyReminderEnabled)
                    {
                        _logger.LogDebug("[LumenReminder] User {UserId} has daily reminder disabled, skipping", 
                            GetStringValue(data, "userId"));
                        continue;
                    }

                    // Use lastActiveDate if available, otherwise use updatedAt
                    var lastActive = GetDateTimeValue(data, "lastActiveDate") 
                        ?? GetDateTimeValue(data, "updatedAt") 
                        ?? DateTime.UtcNow;
                    
                    // Skip users who haven't been active within threshold
                    if (lastActive < cutoffDate)
                    {
                        continue;
                    }

                    users.Add(new LumenReminderUserInfo
                    {
                        UserId = GetStringValue(data, "userId") ?? item.AgentId,
                        TimeZone = GetStringValue(data, "currentTimeZone") ?? "UTC",
                        Language = GetStringValue(data, "currentLanguage") ?? "en",
                        LastActiveDate = lastActive
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LumenReminder] Error parsing user data for {AgentId}", item.AgentId);
                }
            }

            return users;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenReminder] Error querying active users from CQRS");
            return new List<LumenReminderUserInfo>();
        }
    }
    
    /// <summary>
    /// Helper to get bool value from dictionary
    /// </summary>
    private static bool? GetBoolValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is bool b) return b;
            if (value is JsonElement je)
            {
                if (je.ValueKind == JsonValueKind.True) return true;
                if (je.ValueKind == JsonValueKind.False) return false;
            }
            if (value is string str && bool.TryParse(str, out var parsed))
                return parsed;
        }
        return null;
    }

    /// <summary>
    /// Helper to get string value from dictionary
    /// </summary>
    private static string? GetStringValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            return value?.ToString();
        }
        return null;
    }

    /// <summary>
    /// Helper to get DateTime value from dictionary
    /// </summary>
    private static DateTime? GetDateTimeValue(Dictionary<string, object> data, string key)
    {
        if (data.TryGetValue(key, out var value))
        {
            if (value is DateTime dt)
                return dt;
            if (value is JsonElement je && je.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(je.GetString(), out var parsed))
                    return parsed;
            }
            if (value is string str && DateTime.TryParse(str, out var parsed2))
                return parsed2;
        }
        return null;
    }

    /// <summary>
    /// Check if a timezone has just entered a new day (within configured window)
    /// </summary>
    private bool IsNewDayInTimezone(string timeZoneId)
    {
        // TODO: Remove this bypass after testing
        if (_options.BypassTimezoneCheck)
        {
            _logger.LogWarning("[LumenReminder] Timezone check bypassed for testing!");
            return true;
        }
        
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

            // Check if we're within N minutes after midnight
            return localNow.Hour == 0 && localNow.Minute < _options.NewDayWindowMinutes;
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("[LumenReminder] Invalid timezone: {TimeZoneId}", timeZoneId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[LumenReminder] Error checking timezone: {TimeZoneId}", timeZoneId);
            return false;
        }
    }

    /// <summary>
    /// Process reminder for a single user via LumenService
    /// </summary>
    private async Task ProcessUserReminderAsync(
        LumenReminderUserInfo user,
        string timeZoneId,
        CancellationToken cancellationToken)
    {
        // Calculate today in user's local timezone
        DateOnly userToday;
        try
        {
            var timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);
            userToday = DateOnly.FromDateTime(userNow);
        }
        catch
        {
            userToday = DateOnly.FromDateTime(DateTime.UtcNow);
        }

        _logger.LogInformation(
            "[LumenReminder] Triggering daily prediction for user {UserId} (Date: {Date}, TZ: {TimeZone}, Lang: {Lang})",
            user.UserId, userToday, timeZoneId, user.Language);

        // Trigger prediction generation via LumenService
        await _lumenService.TriggerPredictionGenerationAsync(
            user.UserId, 
            new List<PredictionType> { PredictionType.PredictionDaily });
    }
}

/// <summary>
/// User information needed for reminder processing
/// </summary>
public class LumenReminderUserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string? TimeZone { get; set; }
    public string? Language { get; set; }
    public DateTime LastActiveDate { get; set; }
}
