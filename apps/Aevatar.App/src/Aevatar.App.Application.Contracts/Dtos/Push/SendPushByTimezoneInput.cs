using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.Dtos.Push;

/// <summary>
/// Input for sending push notifications to users by timezone.
/// </summary>
public class SendPushByTimezoneInput
{
    /// <summary>
    /// Timezone ID to target (e.g., "Asia/Shanghai").
    /// </summary>
    [Required]
    public string TimeZoneId { get; set; } = string.Empty;
    
    /// <summary>
    /// Notification title.
    /// </summary>
    [Required]
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Notification body.
    /// </summary>
    [Required]
    public string Body { get; set; } = string.Empty;
}

/// <summary>
/// Result of push notification sending.
/// </summary>
public class PushResult
{
    /// <summary>
    /// Number of successfully sent notifications.
    /// </summary>
    public int SuccessCount { get; set; }
    
    /// <summary>
    /// Number of failed notifications.
    /// </summary>
    public int FailureCount { get; set; }
    
    /// <summary>
    /// Total number of targeted users.
    /// </summary>
    public int TotalTargeted { get; set; }
    
    /// <summary>
    /// Error message if any.
    /// </summary>
    public string? Error { get; set; }
}
