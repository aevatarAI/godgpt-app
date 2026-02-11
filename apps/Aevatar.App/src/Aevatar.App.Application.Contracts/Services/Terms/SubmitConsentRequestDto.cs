using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Request DTO for submitting user consent.
/// </summary>
public class SubmitConsentRequestDto
{
    /// <summary>
    /// Version of ToS being consented to.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Content hash for verification.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Platform: iOS or Android.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Operating system version.
    /// </summary>
    [MaxLength(60)]
    public string? OsVersion { get; set; }

    /// <summary>
    /// Application version.
    /// </summary>
    [MaxLength(20)]
    public string? AppVersion { get; set; }
}
