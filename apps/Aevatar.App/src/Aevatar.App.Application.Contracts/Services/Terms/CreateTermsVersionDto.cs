using System;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// DTO for creating a new Terms of Service version.
/// </summary>
public class CreateTermsVersionDto
{
    /// <summary>
    /// Version number (e.g., "1.2.0").
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the content for verification.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// URL to the content (S3 or CloudFront).
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string ContentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Storage version ID for audit trail (e.g., S3 VersionId, Azure Blob ETag).
    /// </summary>
    [MaxLength(128)]
    public string? StorageVersionId { get; set; }

    /// <summary>
    /// Date when this version becomes effective.
    /// </summary>
    [Required]
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Whether to set this as the active version.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "zh").
    /// </summary>
    [MaxLength(10)]
    public string? Language { get; set; }

    /// <summary>
    /// Full content backup for archival.
    /// </summary>
    public string? ContentBackup { get; set; }
}
