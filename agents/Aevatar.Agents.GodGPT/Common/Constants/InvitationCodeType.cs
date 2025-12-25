namespace Aevatar.Application.Grains.Common.Constants;

/// <summary>
/// Invitation code type enum
/// Maps to Proto: Aevatar.Agents.GodGPT.Protos.InviteCode.InvitationCodeType
/// </summary>
public enum InvitationCodeType
{
    FriendInvitation = 0,
    FreeTrialReward = 1
}

/// <summary>
/// Alias for InvitationCodeType for backward compatibility
/// </summary>
public enum InviteCodeType
{
    FriendInvitation = 0,
    FreeTrialReward = 1
}
 