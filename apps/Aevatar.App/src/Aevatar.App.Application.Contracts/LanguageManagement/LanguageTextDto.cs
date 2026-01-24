namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Data transfer object for language text.
/// </summary>
public class LanguageTextDto
{
    /// <summary>
    /// Resource name (e.g., "Aevatar").
    /// </summary>
    public string ResourceName { get; set; } = string.Empty;

    /// <summary>
    /// Target culture name (e.g., "zh-Hans").
    /// </summary>
    public string CultureName { get; set; } = string.Empty;

    /// <summary>
    /// Base culture name (e.g., "en").
    /// </summary>
    public string BaseCultureName { get; set; } = string.Empty;

    /// <summary>
    /// Base value in base culture.
    /// </summary>
    public string BaseValue { get; set; } = string.Empty;

    /// <summary>
    /// The key/name of the localized string.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The localized value in target culture.
    /// </summary>
    public string Value { get; set; } = string.Empty;
}

