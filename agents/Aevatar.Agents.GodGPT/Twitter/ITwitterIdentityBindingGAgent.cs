using Aevatar.Agents.GodGPT.Protos.Twitter;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Identity Binding Agent Interface - Maps Twitter ID to system user ID
/// Note: Does NOT inherit IGAgent - RPC proxy pattern requires plain interface
/// </summary>
public interface ITwitterIdentityBindingGAgent
{
    /// <summary>
    /// Create or update binding between Twitter ID and system user ID
    /// </summary>
    Task<TwitterAuthResultProto> CreateOrUpdateBindingAsync(
        string twitterUserId, 
        Guid userId, 
        string twitterUsername, 
        string profileImageUrl);

    /// <summary>
    /// Get the system user ID bound to a Twitter ID
    /// </summary>
    Task<Guid?> GetUserIdAsync();

    /// <summary>
    /// Get binding status
    /// </summary>
    Task<TwitterBindStatusProto> GetBindStatusAsync();
}
