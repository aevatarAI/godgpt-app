namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Data transfer object for culture information.
/// </summary>
public class CultureInfoDto
{
    /// <summary>
    /// Display name of the culture (e.g., "Chinese (Simplified, Singapore)").
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Culture name (e.g., "zh-Hans-SG").
    /// </summary>
    public string Name { get; set; } = string.Empty;
}

