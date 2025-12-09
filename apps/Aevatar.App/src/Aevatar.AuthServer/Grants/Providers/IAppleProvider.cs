using System.Security.Claims;
using System.Threading.Tasks;

namespace Aevatar.AuthServer.Grants.Providers;

/// <summary>
/// Provider interface for Apple Sign In token validation
/// </summary>
public interface IAppleProvider
{
    /// <summary>
    /// Exchange authorization code for ID token
    /// </summary>
    Task<string> ExchangeCodeForTokenAsync(string code, string? source, string? platform, AppleAppOptions appOptions);

    /// <summary>
    /// Validate Apple ID token and return claims principal
    /// </summary>
    Task<(bool IsValid, ClaimsPrincipal? Principal)> ValidateAppleTokenAsync(
        string idToken, string? source, AppleAppOptions appOptions);
}

/// <summary>
/// User info extracted from Apple Sign In
/// </summary>
public class AppleUserInfo
{
    public string? SubjectId { get; set; }
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

