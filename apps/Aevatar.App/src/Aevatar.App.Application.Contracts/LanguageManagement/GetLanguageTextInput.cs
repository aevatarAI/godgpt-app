using System.ComponentModel.DataAnnotations;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Input DTO for getting a language text by key.
/// </summary>
public class GetLanguageTextInput
{
    /// <summary>
    /// The resource name (e.g., "Aevatar", "AbpIdentity").
    /// </summary>
    [Required]
    [MaxLength(128)]
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// The text key name.
    /// </summary>
    [Required]
    [MaxLength(512)]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The target culture name (e.g., "en", "zh-Hans").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// The base culture name for comparison (e.g., "en").
    /// </summary>
    [Required]
    [MaxLength(10)]
    public string BaseCultureName { get; set; } = "en";
}

