using Aevatar.Agents.Abstractions;
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
[GAgent(nameof(UserInvitationGAgent))]
public class UserInvitationGAgent : GAgentBase<UserInvitationState>, IUserInvitationGAgent
{
    private readonly IGAgentActorFactory _actorFactory;

    public UserInvitationGAgent(Guid id, IGAgentActorFactory actorFactory) : base(id)
    {
        _actorFactory = actorFactory;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Invitation Management GAgent");
    }

    /// <summary>
    /// Generates invite code for the current user
    /// </summary>
    public async Task<string> GenerateInviteCodeAsync()
    {
        var invitationActor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(this.GetPrimaryKey());
        var invitationAgent = (IInvitationGAgent)invitationActor.GetAgent();
        var inviteCode = await invitationAgent.GenerateInviteCodeAsync();
        return inviteCode;
    }

    /// <summary>
    /// Redeems an invite code
    /// </summary>
    public async Task<bool> RedeemInviteCodeAsync(string inviteCode)
    {
        var codeGrainId = CommonHelper.StringToGuid(inviteCode);
        var codeActor = await _actorFactory.CreateGAgentActorAsync<InviteCodeGAgent>(codeGrainId);
        var codeGrain = (IInviteCodeGAgent)codeActor.GetAgent();

        var (isValid, inviterId) = await codeGrain.ValidateAndGetInviterAsync();

        if (!isValid)
        {
            Logger.LogWarning($"Invalid invite code redemption attempt: {inviteCode}");
            return false;
        }


        if (inviterId.Equals(Id.ToString()))
        {
            Logger.LogWarning(
                $"Invalid invite code,the code belongs to the user themselves. userId:{Id.ToString()} InviteCode:{inviteCode}");
            return false;
        }

        // Step 1: First, check if the current user (invitee) is eligible for the reward.
        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(this.GetPrimaryKey());
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();

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
            Logger.LogWarning($"State.RegisteredAtUtc == null userId:{Id.ToString()}");
            redeemResult = false;
        }

        Logger.LogDebug(
            $"[UserInvitationGAgent][RedeemInviteCodeAsync] User {Id} RegisteredAtUtc={registeredAtUtc.ToDateTime()}");

        // Attempt to redeem initial reward
        redeemResult = await userQuotaGAgent.RedeemInitialRewardAsync(Id.ToString(), registeredAtUtc.ToDateTime());

        if (!redeemResult)
        {
            Logger.LogWarning(
                $"[UserInvitationGAgent][RedeemInviteCodeAsync] Failed to redeem initial reward for user {Id}");
            return false;
        }

        // Record invitee in inviter's InvitationGAgent
        var inviterGuid = Guid.Parse(inviterId);
        var inviterActor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(inviterGuid);
        var inviterGrain = (IInvitationGAgent)inviterActor.GetAgent();
        await inviterGrain.ProcessInviteeRegistrationAsync(Id.ToString());

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
            $"[UserInvitationGAgent][RedeemInviteCodeAsync] User {Id} successfully redeemed invite code from {inviterId}");
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

        return Task.FromResult<Guid?>(Guid.Parse(State.InviterId));
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

