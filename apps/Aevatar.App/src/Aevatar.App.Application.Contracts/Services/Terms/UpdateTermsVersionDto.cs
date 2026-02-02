using System;
using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// DTO for updating a Terms of Service version.
/// </summary>
public class UpdateTermsVersionDto
{
    /// <summary>
    /// Updated content hash.
    /// </summary>
    [MaxLength(128)]
    public string? ContentHash { get; set; }

    /// <summary>
    /// Updated content URL.
    /// </summary>
    [MaxLength(512)]
    public string? ContentUrl { get; set; }

    /// <summary>
    /// Updated storage version ID.
    /// </summary>
    [MaxLength(128)]
    public string? StorageVersionId { get; set; }

    /// <summary>
    /// Updated effective date.
    /// </summary>
    public DateTime? EffectiveDate { get; set; }

    /// <summary>
    /// Updated language code.
    /// </summary>
    [MaxLength(10)]
    public string? Language { get; set; }

    /// <summary>
    /// Updated content backup.
    /// </summary>
    public string? ContentBackup { get; set; }
}
