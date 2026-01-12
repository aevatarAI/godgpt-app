using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.Twitter;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Auth Agent Interface - OAuth2 authentication agent
/// </summary>
public interface ITwitterAuthGAgent : IGAgent
{
    /// <summary>
    /// Generate PKCE code verifier and challenge (plain method)
    /// </summary>
    Task<PkceResultProto> GeneratePkcePlainAsync();
    
    /// <summary>
    /// Generate PKCE code verifier and challenge (S256 method)
    /// </summary>
    Task<PkceResultProto> GeneratePkceAsync();
    
    /// <summary>
    /// Verify OAuth2 authorization code and bind Twitter account
    /// </summary>
    Task<TwitterAuthResultProto> VerifyAuthCodeAsync(string platform, string code, string redirectUri);
    
    /// <summary>
    /// Get Twitter binding status
    /// </summary>
    Task<TwitterBindStatusProto> GetBindStatusAsync();
    
    /// <summary>
    /// Get OAuth2 authorization parameters
    /// </summary>
    Task<TwitterAuthParamsProto> GetAuthParamsAsync();
}
