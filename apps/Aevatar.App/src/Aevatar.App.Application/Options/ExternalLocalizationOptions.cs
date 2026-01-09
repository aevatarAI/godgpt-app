namespace Aevatar.App.Application.Options;

/// <summary>
/// Configuration options for external localization file mounting
/// </summary>
public class ExternalLocalizationOptions
{
    /// <summary>
    /// Configuration section name
    /// </summary>
    public const string SectionName = "ExternalLocalization";
    
    /// <summary>
    /// Base directory path for localization files
    /// Default: "/app/localization"
    /// </summary>
    public string BasePath { get; set; } = "/app/localization";
    
    /// <summary>
    /// GodGPT localization subdirectory
    /// Default: "godgpt"
    /// </summary>
    public string GodGPTSubPath { get; set; } = "godgpt";
    
    /// <summary>
    /// Lumen localization subdirectory
    /// Default: "lumen"
    /// </summary>
    public string LumenSubPath { get; set; } = "lumen";
}

