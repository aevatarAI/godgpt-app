namespace Aevatar.AuthServer.Grants.Providers;

/// <summary>
/// Constants for Apple Sign In API endpoints
/// </summary>
public static class AppleConstants
{
    private const string AuthorityUrl = "https://appleid.apple.com";
    
    /// <summary>
    /// Apple token exchange endpoint
    /// </summary>
    public const string TokenEndpoint = AuthorityUrl + "/auth/token";
    
    /// <summary>
    /// Apple JWKS endpoint for public keys
    /// </summary>
    public const string JwksEndpoint = AuthorityUrl + "/auth/keys";
    
    /// <summary>
    /// Valid issuer for Apple ID tokens
    /// </summary>
    public const string ValidIssuer = AuthorityUrl;

    public static class Claims
    {
        public const string Kid = "kid";
    }
}

