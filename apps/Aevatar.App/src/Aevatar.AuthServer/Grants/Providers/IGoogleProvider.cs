using System.Threading.Tasks;
using Google.Apis.Auth;

namespace Aevatar.AuthServer.Grants.Providers;

/// <summary>
/// Provider interface for Google OAuth token validation
/// </summary>
public interface IGoogleProvider
{
    /// <summary>
    /// Validate Google ID token and return payload if valid
    /// </summary>
    Task<GoogleJsonWebSignature.Payload?> ValidateGoogleTokenAsync(string idToken, string clientId);

    /// <summary>
    /// Get client ID based on source platform and optional app ID
    /// </summary>
    Task<string> GetClientIdAsync(string? source, string? appId = null);
}

