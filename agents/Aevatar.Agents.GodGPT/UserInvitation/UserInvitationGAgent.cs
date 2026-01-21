using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.ChatManager;
using Aevatar.Agents.GodGPT.Protos.UserInvitation;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserQuota;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.UserInvitation;

/// <summary>
/// User Invitation GAgent implementation
/// Manages the invitation information for the current user
/// </summary>
public class UserInvitationGAgent : GAgentBase<UserInvitationState>, IUserInvitationGAgent
{
    // Use property injection for Orleans compatibility (parameterless constructor required)
    public IGAgentActorFactory ActorFactory { get; set; } = null!;

    // Parameterless constructor required for Orleans activation
    public UserInvitationGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Invitation Management GAgent");
    }

    /// <summary>
    /// Extract raw Guid from Agent Id (format: "AgentType:Guid" or just "Guid")
    /// </summary>
    private string ExtractRawGuidFromAgentId(string agentId)
    {
        var colonIndex = agentId.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < agentId.Length - 1)
        {
            // Format: "AgentType:Guid" - extract Guid part
            return agentId[(colonIndex + 1)..];
        }
        // Format: "Guid" - return as is
        return agentId;
    }

    /// <summary>
    /// Generates invite code for the current user
    /// </summary>
    public async Task<string> GenerateInviteCodeAsync()
    {
        // Extract raw Guid from Agent Id for CreateGAgentActorAsync (expects simple Guid string)
        var rawUserId = ExtractRawGuidFromAgentId(Id);
        if (ActorFactory == null)
        {
            throw new InvalidOperationException("ActorFactory is not injected. Cannot create InvitationGAgent.");
        }
        var invitationActor = await ActorFactory.CreateGAgentActorAsync<InvitationGAgent>(rawUserId);
        var invitationAgent = invitationActor.As<IInvitationGAgent>();
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        return inviteCode;
    }

    /// <summary>
    /// Redeems an invite code
    /// </summary>
    public async Task<bool> RedeemInviteCodeAsync(string inviteCode)
    {
        if (ActorFactory == null)
        {
            throw new InvalidOperationException("ActorFactory is not injected. Cannot create InviteCodeGAgent.");
        }
        var codeGrainId = CommonHelper.StringToGuid(inviteCode);
        var codeActor = await ActorFactory.CreateGAgentActorAsync<InviteCodeGAgent>(codeGrainId.ToString());
        var codeGrain = codeActor.As<IInviteCodeGAgent>();

        var response = await codeGrain.ValidateAndGetInviterAsync();
        var isValid = response.IsValid;
        var inviterId = response.InviterId;

        if (!isValid)
        {
            Logger.LogInformation(
                "[UserInvitationGAgent][RedeemInviteCodeAsync] Invite code invalid. Code: {Code}, InviterId: {InviterId}",
                inviteCode, string.IsNullOrEmpty(inviterId) ? "<empty>" : inviterId);
            Logger.LogWarning($"Invalid invite code redemption attempt: {inviteCode}");
            return false;
        }


        // Extract raw Guid from Agent Id for comparison and CreateGAgentActorAsync
        var rawUserId = ExtractRawGuidFromAgentId(Id);
        
        if (inviterId.Equals(rawUserId))
        {
            Logger.LogWarning(
                $"Invalid invite code,the code belongs to the user themselves. userId:{rawUserId} InviteCode:{inviteCode}");
            return false;
        }

        // Step 1: First, check if the current user (invitee) is eligible for the reward.
        var userQuotaActor = await ActorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(rawUserId);
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();

        if (State.RegisteredAtUtc == null) //&& State.SessionInfoList.IsNullOrEmpty())
        {
            RaiseEvent(new SetRegisteredAtUtcEvent()
            {
                RegisteredAtUtc = DateTime.UtcNow.ToProtoTimestamp()
            });

            await ConfirmEventsAsync();
        }
        bool redeemResult = false;

        var registeredAtUtc = State.RegisteredAtUtc;

        if (registeredAtUtc == null)
        {
            Logger.LogWarning($"State.RegisteredAtUtc == null userId:{rawUserId}");
            return false;
        }

        Logger.LogInformation(
            "[UserInvitationGAgent][RedeemInviteCodeAsync] User {UserId} RegisteredAtUtc={RegisteredAtUtc}",
            rawUserId, registeredAtUtc.ToDateTime());

        // Attempt to redeem initial reward
        redeemResult = await userQuotaGAgent.RedeemInitialRewardAsync(rawUserId, registeredAtUtc.ToDateTime());

        if (!redeemResult)
        {
            Logger.LogWarning(
                $"[UserInvitationGAgent][RedeemInviteCodeAsync] Failed to redeem initial reward for user {Id}");
            return false;
        }

        // Record invitee in inviter's InvitationGAgent
        // Extract raw Guid from inviterId (may be full Agent Id format: "AgentType:Guid")
        var rawInviterId = ExtractRawGuidFromAgentId(inviterId);
        var inviterActor = await ActorFactory.CreateGAgentActorAsync<InvitationGAgent>(rawInviterId);
        var inviterGrain = inviterActor.As<IInvitationGAgent>();
        await inviterGrain.ProcessInviteeRegistrationAsync(rawUserId);

        // Record redemption event
        RaiseEvent(new RedeemInviteCodeEvent
        {
            InviterId = inviterId
        });

        // Set inviter
        RaiseEvent(new SetInviterEvent
        {
            InviterId = inviterId
        });

        await ConfirmEventsAsync();

        Logger.LogInformation(
            $"[UserInvitationGAgent][RedeemInviteCodeAsync] User {rawUserId} successfully redeemed invite code from {rawInviterId}");
        return true;
    }

    /// <summary>
    /// Gets the inviter ID
    /// </summary>
    public Task<Guid?> GetInviterAsync()
    {
        if (string.IsNullOrEmpty(State.InviterId))
        {
            return Task.FromResult<Guid?>(null);
        }

        // State.InviterId should be raw Guid (stored from inviterId parameter which is already raw Guid)
        // But handle both formats for safety
        var inviterIdStr = State.InviterId;
        var colonIndex = inviterIdStr.IndexOf(':');
        if (colonIndex >= 0 && colonIndex < inviterIdStr.Length - 1)
        {
            inviterIdStr = inviterIdStr[(colonIndex + 1)..];
        }
        
        return Guid.TryParse(inviterIdStr, out var guid) 
            ? Task.FromResult<Guid?>(guid) 
            : Task.FromResult<Guid?>(null);
    }

    // #region Event Handlers

    // [EventHandler]
    // public async Task HandleEventAsync(SetInviterEvent @event)
    // {
        
    // }

    // [EventHandler]
    // public void HandleEventAsync(RedeemInviteCodeEvent @event)
    // {
    //     TransitionState(State, @event);
    // }

    // #endregion

    protected override void TransitionState(UserInvitationState state, IMessage evt)
    {
        switch (evt)
        {
            case SetRegisteredAtUtcEvent setRegisteredAtUtcEvent:
                state.RegisteredAtUtc = setRegisteredAtUtcEvent.RegisteredAtUtc;
                break;

            case SetInviterEvent setInviterEvent:
                state.InviterId = setInviterEvent.InviterId;
                break;

            case RedeemInviteCodeEvent redeemEvent:
                state.InviterId = redeemEvent.InviterId;
                break;

            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}

