using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Data transfer object for creating a new Language.
/// </summary>
public class CreateLanguageDto
{
    /// <summary>
    /// Display name of the language (e.g., "English", "简体中文").
    /// </summary>
    [MaxLength(32)]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [MaxLength(10)]
    public string? CultureName { get; set; }

    /// <summary>
    /// UI culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [MaxLength(10)]
    public string? UiCultureName { get; set; }

    /// <summary>
    /// Flag icon identifier.
    /// </summary>
    [MaxLength(48)]
    public string? FlagIcon { get; set; }

    /// <summary>
    /// Indicates whether this language is enabled.
    /// </summary>
    public bool IsEnabled { get; set; }
}

