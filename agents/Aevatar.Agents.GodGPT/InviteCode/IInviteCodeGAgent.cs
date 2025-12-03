using Aevatar.Application.Grains.FreeTrialCode.Dtos;

namespace Aevatar.Application.Grains.Agents.Invitation;

public interface IInviteCodeGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Initialize a new invite code with inviter ID
    /// </summary>
    Task<bool> InitializeAsync(string inviterId, string inviteCode);

    /// <summary>
    /// Validate invite code and return inviter ID if valid
    /// </summary>
    Task<(bool isValid, string inviterId)> ValidateAndGetInviterAsync();

    /// <summary>
    /// Checks if the invite code has been initialized with an inviter.
    /// </summary>
    Task<bool> IsInitialized();
    
    /// <summary>
    /// Deactivate the invite code
    /// </summary>
    Task DeactivateCodeAsync();
    
    /// <summary>
    /// Initialize free trial code
    /// </summary>
    Task<bool> InitializeFreeTrialCodeAsync(FreeTrialCodeInitDto initDto);
    
    Task<ValidateCodeResultDto> ValidateAndGetFreeTrialCodeInfoAsync(string userId);
    Task<bool> MarkCodeAsUsedAsync();

    /// <summary>
    /// Get free trial code information
    /// </summary>
    Task<FreeTrialCodeInfoDto?> GetCodeInfoAsync();
}
