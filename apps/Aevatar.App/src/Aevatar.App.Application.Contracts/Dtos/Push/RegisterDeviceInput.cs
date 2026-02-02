using System.ComponentModel.DataAnnotations;

namespace Aevatar.Dtos.Push;

/// <summary>
/// Input for registering or updating user device for push notifications.
/// </summary>
public class RegisterDeviceInput
{
    /// <summary>
    /// Unique device identifier (client-generated).
    /// </summary>
    [Required]
    public string DeviceId { get; set; } = string.Empty;
    
    /// <summary>
    /// FCM push token.
    /// </summary>
    [Required]
    public string PushToken { get; set; } = string.Empty;
    
    /// <summary>
    /// Timezone ID (e.g., "Asia/Shanghai", "America/New_York").
    /// </summary>
    [Required]
    public string TimeZoneId { get; set; } = string.Empty;
    
    /// <summary>
    /// Whether push notifications are enabled by user.
    /// </summary>
    public bool PushEnabled { get; set; } = true;
    
    /// <summary>
    /// Platform: "ios" / "android" (optional).
    /// </summary>
    public string? Platform { get; set; }
    
    /// <summary>
    /// App version (optional).
    /// </summary>
    public string? AppVersion { get; set; }
}
