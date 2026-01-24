using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Data transfer object for updating a Language.
/// </summary>
public class UpdateLanguageDto
{
    /// <summary>
    /// Display name of the language (e.g., "English", "简体中文").
    /// </summary>
    [MaxLength(32)]
    public string? DisplayName { get; set; }

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

