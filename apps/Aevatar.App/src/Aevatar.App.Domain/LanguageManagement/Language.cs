using System;
using System.ComponentModel.DataAnnotations;
using Volo.Abp.Domain.Entities.Auditing;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Represents a language entity for multi-language support.
/// </summary>
public class Language : FullAuditedAggregateRoot<Guid>
{
    /// <summary>
    /// Culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// UI culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string UiCultureName { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the language (e.g., "English", "简体中文").
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Flag icon identifier.
    /// </summary>
    [MaxLength(48)]
    public string? FlagIcon { get; set; }

    /// <summary>
    /// Indicates whether this language is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }

    protected Language()
    {
    }

    public Language(
        Guid id,
        string cultureName,
        string uiCultureName,
        string displayName,
        string? flagIcon = null,
        bool isEnabled = true)
        : base(id)
    {
        CultureName = cultureName;
        UiCultureName = uiCultureName;
        DisplayName = displayName;
        FlagIcon = flagIcon;
        IsEnabled = isEnabled;
    }
}

