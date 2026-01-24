using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Input DTO for creating a new language text.
/// </summary>
public class CreateLanguageTextInput
{
    /// <summary>
    /// The resource name (e.g., "Aevatar", "AbpIdentity").
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// The culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// The text key name.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The localized value/text.
    /// </summary>
    [Required]
    public string Value { get; set; } = string.Empty;
}

