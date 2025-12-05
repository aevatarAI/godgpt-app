namespace Aevatar.Options;

/// <summary>
/// Aevatar application options
/// </summary>
public class AevatarOptions
{
    /// <summary>
    /// API base URL
    /// </summary>
    public string ApiBaseUrl { get; set; } = string.Empty;
    
    /// <summary>
    /// Enable debug mode
    /// </summary>
    public bool DebugMode { get; set; }
    
    /// <summary>
    /// Stream namespace for Orleans streaming (default: AINamespace)
    /// </summary>
    public string StreamNamespace { get; set; } = "AINamespace";
}

