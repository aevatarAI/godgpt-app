using System.Collections.Generic;

namespace Aevatar.AuthServer.Grants;

/// <summary>
/// Configuration options for Google Sign In
/// </summary>
public class GoogleOptions
{
    /// <summary>
    /// Web client ID for Google OAuth
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// iOS client ID for Google OAuth
    /// </summary>
    public string IOSClientId { get; set; } = string.Empty;

    /// <summary>
    /// Android client ID for Google OAuth
    /// </summary>
    public string AndroidClientId { get; set; } = string.Empty;

    /// <summary>
    /// App-specific configurations for multi-app support
    /// </summary>
    public Dictionary<string, AppClientConfig> AppConfigs { get; set; } = new();
}

/// <summary>
/// Per-app Google OAuth client configuration
/// </summary>
public class AppClientConfig
{
    public string ClientId { get; set; } = string.Empty;
    public string IOSClientId { get; set; } = string.Empty;
    public string AndroidClientId { get; set; } = string.Empty;
}

