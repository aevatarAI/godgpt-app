using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.InviteCode;

namespace Aevatar.Application.Grains.Agents.Invitation;

/// <summary>
/// Invite Code Agent interface - manages invitation codes and free trial codes.
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IInviteCodeGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Initialize a new invite code with inviter ID
    /// </summary>
    Task<bool> InitializeAsync(string inviterId, string inviteCode);

    /// <summary>
    /// Validate invite code and return inviter ID if valid (uses Protobuf wrapper for RPC)
    /// </summary>
    Task<ValidateInviteCodeResponse> ValidateAndGetInviterAsync();

    /// <summary>
    /// Checks if the invite code has been initialized with an inviter.
    /// </summary>
    Task<bool> IsInitialized();
    
    /// <summary>
    /// Deactivate the invite code
    /// </summary>
    Task DeactivateCodeAsync();
    
    /// <summary>
    /// Initialize free trial code (uses Protobuf type for RPC)
    /// </summary>
    Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitProto initDto);
    
    /// <summary>
    /// Validate and get free trial code info (uses Protobuf type for RPC)
    /// </summary>
    Task<ValidateCodeResultProto> ValidateAndGetFreeTrialCodeInfoAsync(string userId);
    
    /// <summary>
    /// Mark code as used
    /// </summary>
    Task<bool> MarkCodeAsUsedAsync();

    /// <summary>
    /// Get free trial code information (returns empty message if not found, nullable not supported by RPC)
    /// </summary>
    Task<FreeTrialCodeInfoProto> GetCodeInfoAsync();
}
