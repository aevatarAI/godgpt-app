using System.Collections.Generic;

namespace Aevatar.AuthServer.Grants;

/// <summary>
/// Configuration options for Apple Sign In
/// </summary>
public class AppleOptions
{
    /// <summary>
    /// App-specific configurations for multi-app support
    /// </summary>
    public Dictionary<string, AppleAppOptions> APPs { get; set; } = new();
}

/// <summary>
/// Per-app Apple Sign In configuration
/// </summary>
public class AppleAppOptions
{
    /// <summary>
    /// Native iOS app bundle ID (e.g., com.yourapp.ios)
    /// </summary>
    public string NativeClientId { get; set; } = string.Empty;

    /// <summary>
    /// Web service ID for Sign in with Apple on web
    /// </summary>
    public string WebClientId { get; set; } = string.Empty;

    /// <summary>
    /// Key ID from Apple Developer Console
    /// </summary>
    public string KeyId { get; set; } = string.Empty;

    /// <summary>
    /// Team ID from Apple Developer Console
    /// </summary>
    public string TeamId { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI for web Sign in with Apple
    /// </summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Redirect URI for mobile Sign in with Apple
    /// </summary>
    public string MobileRedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Private key (PKCS#8 format, base64 encoded) for generating client secret
    /// </summary>
    public string Pk { get; set; } = string.Empty;
}

