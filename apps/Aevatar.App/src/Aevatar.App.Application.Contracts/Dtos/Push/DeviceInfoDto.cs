using System;

namespace Aevatar.Dtos.Push;

/// <summary>
/// Device information DTO for API responses.
/// </summary>
public class DeviceInfoDto
{
    /// <summary>
    /// Unique device identifier.
    /// </summary>
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// FCM push token.
    /// </summary>
    public string PushToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Timezone ID (e.g., "Asia/Shanghai").
    /// </summary>
    public string TimeZoneId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether push notifications are enabled.
    /// </summary>
    public bool PushEnabled { get; set; }
    
    /// <summary>
    /// Platform: "ios" / "android".
    /// </summary>
    public string? Platform { get; set; }
    
    /// <summary>
    /// App version.
    /// </summary>
    public string? AppVersion { get; set; }
    
    /// <summary>
    /// Device language.
    /// </summary>
    public string? Language { get; set; }
    
    /// <summary>
    /// Token update timestamp.
    /// </summary>
    public DateTime? TokenUpdatedAt { get; set; }
    
    /// <summary>
    /// Last active timestamp.
    /// </summary>
    public DateTime? LastActiveAt { get; set; }
    
    /// <summary>
    /// Whether the token is marked as invalid.
    /// </summary>
    public bool TokenInvalid { get; set; }
}
