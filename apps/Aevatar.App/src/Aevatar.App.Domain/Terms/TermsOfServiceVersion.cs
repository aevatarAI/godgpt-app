using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Domain.Entities.Auditing;

namespace Aevatar.App.Terms;

/// <summary>
/// Represents a version of Terms of Service for legal compliance and audit trail.
/// </summary>
public class TermsOfServiceVersion : FullAuditedAggregateRoot<Guid>
{
    /// <summary>
    /// Version number (e.g., "1.2.0").
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the content for integrity verification.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// S3 URL or CloudFront URL or CDN URL for the content.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string ContentUrl { get; set; } = string.Empty;

    /// <summary>
    /// Storage version ID for legal audit trail (e.g., S3 VersionId, Azure Blob ETag).
    /// </summary>
    [MaxLength(128)]
    public string? StorageVersionId { get; set; }

    /// <summary>
    /// Full content backup for archival purposes.
    /// </summary>
    public string? ContentBackup { get; set; }

    /// <summary>
    /// Date when this version becomes effective.
    /// </summary>
    public DateTime EffectiveDate { get; set; }

    /// <summary>
    /// Indicates whether this is the currently active version.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Language code (e.g., "en", "zh").
    /// </summary>
    [MaxLength(10)]
    public string? Language { get; set; }

    protected TermsOfServiceVersion()
    {
    }

    public TermsOfServiceVersion(
        Guid id,
        string version,
        string contentHash,
        string contentUrl,
        DateTime effectiveDate,
        bool isActive = false,
        string? storageVersionId = null,
        string? contentBackup = null,
        string? language = null)
        : base(id)
    {
        Version = version;
        ContentHash = contentHash;
        ContentUrl = contentUrl;
        EffectiveDate = effectiveDate;
        IsActive = isActive;
        StorageVersionId = storageVersionId;
        ContentBackup = contentBackup;
        Language = language;
    }
}
