using Aevatar.Agents.Abstractions;

namespace Aevatar.Application.Grains.UserInvitation;

/// <summary>
/// User Invitation GAgent interface
/// Manages the invitation information for the current user, including invite code redemption, inviter setting, etc.
/// </summary>
public interface IUserInvitationGAgent : IGAgent
{
    /// <summary>
    /// Generates invite code for the current user
    /// Delegates to InvitationGAgent for processing
    /// </summary>
    /// <returns>Generated invite code</returns>
    Task<string> GenerateInviteCodeAsync();

    /// <summary>
    /// Redeems an invite code
    /// Validates invite code, claims initial reward, and establishes invitation relationship
    /// </summary>
    /// <param name="inviteCode">Invite code</param>
    /// <returns>Whether redemption was successful</returns>
    Task<bool> RedeemInviteCodeAsync(string inviteCode);

    /// <summary>
    /// Gets the inviter ID
    /// </summary>
    /// <returns>Inviter ID, or null if not available</returns>
    Task<Guid?> GetInviterAsync();
}

