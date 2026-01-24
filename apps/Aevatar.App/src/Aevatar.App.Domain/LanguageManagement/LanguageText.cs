using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Domain.Entities.Auditing;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Represents a language text entity for storing localized strings.
/// </summary>
public class LanguageText : FullAuditedAggregateRoot<Guid>
{
    /// <summary>
    /// Resource name (e.g., "Aevatar", "AbpIdentity").
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// The key/name of the localized string.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The localized value/text.
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;

    protected LanguageText()
    {
    }

    public LanguageText(
        Guid id,
        string resourceName,
        string cultureName,
        string name,
        string value)
        : base(id)
    {
        ResourceName = resourceName;
        CultureName = cultureName;
        Name = name;
        Value = value;
    }
}

