using System;

namespace Aevatar.Options;

/// <summary>
/// Weekly user feedback report scheduled task configuration options
/// </summary>
public class WeeklyFeedbackReportOptions
{
    /// <summary>
    /// Whether the scheduled task is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Recipient email addresses (separated by semicolon)
    /// </summary>
    public string RecipientEmails { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for querying user feedback data API
    /// </summary>
    public string FeedbackApiBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Number of items to query per page
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Day of week to send weekly feedback report
    /// Default: Sunday
    /// </summary>
    public DayOfWeek ExecutionDayOfWeek { get; set; } = DayOfWeek.Sunday;

    /// <summary>
    /// Hour of day to send weekly feedback report (24-hour format)
    /// Default: 12 (12:00 PM)
    /// </summary>
    public int ExecutionHour { get; set; } = 12;

    /// <summary>
    /// HTTP request timeout in seconds
    /// </summary>
    public int HttpTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Email subject
    /// </summary>
    public string EmailSubject { get; set; } = "Weekly Feedback Report";

    /// <summary>
    /// Email body template
    /// </summary>
    public string EmailBodyTemplate { get; set; } = @"
Dear Team,

Please find attached the weekly user feedback report for the period from {StartDate} to {EndDate}.

Total feedback count: {FeedbackCount}

Best regards,
GodGPT System
";

    /// <summary>
    /// Distributed lock expiration time in hours
    /// This controls how long the lock is held to prevent duplicate executions
    /// Recommended values: 2-24 hours depending on your needs
    /// Default: 4 hours (sufficient to prevent same-day duplicates)
    /// </summary>
    public int DistributedLockExpirationHours { get; set; } = 4;
}
