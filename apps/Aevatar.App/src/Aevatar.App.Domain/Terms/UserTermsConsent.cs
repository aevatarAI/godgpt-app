using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Domain.Entities.Auditing;

namespace Aevatar.App.Terms;

/// <summary>
/// Records user consent to Terms of Service for legal compliance.
/// Each consent is a new record (append-only for audit trail).
/// </summary>
public class UserTermsConsent : CreationAuditedAggregateRoot<Guid>
{
    /// <summary>
    /// The user who gave consent.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Version of ToS that was consented to.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// Content hash at the time of consent for verification.
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ContentHash { get; set; } = string.Empty;

    /// <summary>
    /// Timestamp when consent was given.
    /// </summary>
    public DateTime ConsentedAt { get; set; }

    /// <summary>
    /// IP address of the user (captured server-side).
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; set; }

    /// <summary>
    /// Platform: iOS or Android.
    /// </summary>
    [Required]
    [MaxLength(20)]
    public string Platform { get; set; } = string.Empty;

    /// <summary>
    /// Operating system version.
    /// </summary>
    [MaxLength(20)]
    public string? OsVersion { get; set; }

    /// <summary>
    /// Application version.
    /// </summary>
    [MaxLength(20)]
    public string? AppVersion { get; set; }

    protected UserTermsConsent()
    {
    }

    public UserTermsConsent(
        Guid id,
        Guid userId,
        string version,
        string contentHash,
        DateTime consentedAt,
        string platform,
        string? ipAddress = null,
        string? osVersion = null,
        string? appVersion = null)
        : base(id)
    {
        UserId = userId;
        Version = version;
        ContentHash = contentHash;
        ConsentedAt = consentedAt;
        Platform = platform;
        IpAddress = ipAddress;
        OsVersion = osVersion;
        AppVersion = appVersion;
    }
}
