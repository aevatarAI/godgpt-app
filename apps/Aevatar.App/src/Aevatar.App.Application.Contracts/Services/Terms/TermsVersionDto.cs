using System;
using Volo.Abp.Application.Dtos;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// DTO for Terms of Service version.
/// </summary>
public class TermsVersionDto : EntityDto<Guid>
{
    /// <summary>
    /// Version number (e.g., "1.2.0").
    /// </summary>
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the content.
    /// </summary>
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// URL to the content (S3 or CloudFront).
    /// </summary>
    public string ContentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Storage version ID for audit trail (e.g., S3 VersionId, Azure Blob ETag).
    /// </summary>
    public string? StorageVersionId { get; set; }

    /// <summary>
    /// Date when this version becomes effective.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Whether this is the currently active version.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "zh").
    /// </summary>
    public string? Language { get; set; }

    /// <summary>
    /// When this version was created.
    /// </summary>
    public DateTime CreationTime { get; set; }

    /// <summary>
    /// Who created this version.
    /// </summary>
    public Guid? CreatorId { get; set; }
}
