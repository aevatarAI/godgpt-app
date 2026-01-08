using System;

namespace Aevatar.App.HttpApi.Host.BackgroundJobs;

/// <summary>
/// Configuration options for Lumen daily reminder background job
/// </summary>
public class LumenReminderOptions
{
    /// <summary>
    /// Section name in appsettings.json
    /// </summary>
    public const string SectionName = "LumenReminder";
    
    /// <summary>
    /// Whether the daily reminder job is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// Cron expression for reminder check interval
    /// Default: every 15 minutes ("*/15 * * * *")
    /// </summary>
    public string CronExpression { get; set; } = "*/15 * * * *";
    
    /// <summary>
    /// Number of days of inactivity before stopping reminders for a user
    /// Default: 3 days
    /// </summary>
    public int InactivityThresholdDays { get; set; } = 3;
    
    /// <summary>
    /// Minutes after midnight to check for new day in a timezone
    /// Default: 15 minutes (check if within 15 minutes after 00:00)
    /// </summary>
    public int NewDayWindowMinutes { get; set; } = 15;
    
    /// <summary>
    /// Maximum number of users to process per batch
    /// Default: 100
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// Delay between processing each user (milliseconds)
    /// Used to prevent overwhelming the system
    /// Default: 100ms
    /// </summary>
    public int UserProcessingDelayMs { get; set; } = 100;
    
    /// <summary>
    /// Reminder target ID for version control
    /// When changed, existing reminders will be invalidated
    /// </summary>
    public string ReminderTargetId { get; set; } = "v2024-01-01";
    
    /// <summary>
    /// Whether to enable daily auto-generation of predictions
    /// </summary>
    public bool EnableDailyAutoGeneration { get; set; } = true;
    
    /// <summary>
    /// Bypass timezone check for testing purposes
    /// When true, all users will be processed regardless of their local time
    /// WARNING: Only enable for testing!
    /// </summary>
    public bool BypassTimezoneCheck { get; set; } = false;
}

