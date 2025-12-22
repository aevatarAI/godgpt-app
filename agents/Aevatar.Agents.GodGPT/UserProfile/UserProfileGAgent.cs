using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserProfile;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.Application.Grains.UserQuota;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Application.Grains.UserProfile;

/// <summary>
/// User Profile GAgent implementation
/// Manages user basic information and voice preference settings
/// </summary>
[GAgent(nameof(UserProfileGAgent))]
public class UserProfileGAgent : GAgentBase<UserProfileState>, IUserProfileGAgent
{
    private readonly IGAgentActorFactory _actorFactory;

    public UserProfileGAgent(Guid id, IGAgentActorFactory actorFactory) : base(id)
    {
        _actorFactory = actorFactory;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Profile Management GAgent");
    }

    /// <summary>
    /// Sets user profile information
    /// </summary>
    public async Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName)
    {
        RaiseEvent(new SetUserProfileEvent
        {
            Gender = gender,
            BirthDate = birthDate.ToProtoTimestamp(),
            BirthPlace = birthPlace,
            FullName = fullName
        });
        
        return Id;
    }

    /// <summary>
    /// Gets user basic profile information (includes credits and subscription)
    /// </summary>
    public async Task<UserProfileDtoProto> GetUserProfileAsync()
    {
        Logger.LogDebug($"[ChatGAgentManager][GetUserProfileAsync] userId: {this.GetPrimaryKey().ToString()}");

        var invitationActor = await _actorFactory.CreateGAgentActorAsync<InvitationGAgent>(this.GetPrimaryKey());
        var invitationGrain = (IInvitationGAgent)invitationActor.GetAgent();
        await invitationGrain.ProcessScheduledRewardAsync();

        // Sync latest subscription status from UserBillingGAgent before getting user profile
        // This ensures Google Pay and other platform subscriptions are up-to-date
        var userBillingActor = await _actorFactory.CreateGAgentActorAsync<UserBillingGAgent>(this.GetPrimaryKey());
        var userBillingGAgent = (IUserBillingGAgent)userBillingActor.GetAgent();
        var activeSubscriptionStatus = await userBillingGAgent.GetActiveSubscriptionStatusAsync();

        Logger.LogDebug(
            $"[ChatGAgentManager][GetUserProfileAsync] Active subscription status - Apple: {activeSubscriptionStatus.HasActiveAppleSubscription}, Stripe: {activeSubscriptionStatus.HasActiveStripeSubscription}, GooglePlay: {activeSubscriptionStatus.HasActiveGooglePlaySubscription}");

        var userQuotaActor = await _actorFactory.CreateGAgentActorAsync<UserQuotaGAgent>(this.GetPrimaryKey());
        var userQuotaGAgent = (IUserQuotaGAgent)userQuotaActor.GetAgent();

        // Check if we need to sync subscription status between UserBillingGAgent and UserQuotaGAgent
        // This is particularly important for Google Pay subscriptions that might not be reflected in UserQuotaGAgent yet
        await SyncSubscriptionStatusIfNeeded(userBillingGAgent, userQuotaGAgent, activeSubscriptionStatus);

        var credits = await userQuotaGAgent.GetCreditsAsync();
        var subscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync();
        var ultimateSubscriptionInfo = await userQuotaGAgent.GetAndSetSubscriptionAsync(true);

        // Get inviter ID from UserInvitationGAgent
        var userInvitationActor = await _actorFactory.CreateGAgentActorAsync<UserInvitationGAgent>(Id);
        var userInvitationGAgent = (IUserInvitationGAgent)userInvitationActor.GetAgent();
        var inviterId = await userInvitationGAgent.GetInviterAsync();

        return new UserProfileDtoProto
        {
            Id = Id.ToString(),
            Gender = State.Gender ?? string.Empty,
            BirthDate = State.BirthDate,
            BirthPlace = State.BirthPlace ?? string.Empty,
            FullName = State.FullName ?? string.Empty,
            VoiceLanguage = State.VoiceLanguage,
            Credits = credits,
            Subscription = subscriptionInfo,
            UltimateSubscription = ultimateSubscriptionInfo,
            InviterId = inviterId?.ToString() ?? string.Empty
        };
    }

    /// <summary>
    /// Sets the voice language preference for the user.
    /// Validates that the user exists before updating the voice language setting.
    /// </summary>
    /// <param name="voiceLanguage">The voice language to set for the user</param>
    /// <returns>The user ID if successful</returns>
    /// <exception cref="UserFriendlyException">Thrown when user is not found or initialized</exception>
    public async Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage)
    {
        if (Id == Guid.Empty)
        {
            Logger.LogWarning("[UserProfileGAgent][SetVoiceLanguageAsync] Invalid user ID");
            throw new UserFriendlyException("Invalid user. Please ensure you are properly logged in.");
        }

        // Raise event to update voice language
        RaiseEvent(new SetVoiceLanguageEvent
        {
            VoiceLanguage = (int)voiceLanguage
        });

        await ConfirmEventsAsync();

        Logger.LogDebug(
            "[UserProfileGAgent][SetVoiceLanguageAsync] Successfully set voice language to {VoiceLanguage} for user {UserId}",
            voiceLanguage, Id);
        
        return Id;
    }

    /// <summary>
    /// Clears user profile information
    /// </summary>
    public async Task ClearAsync()
    {
        RaiseEvent(new ClearUserProfileEvent());
        await ConfirmEventsAsync();
        
        Logger.LogDebug("[UserProfileGAgent][ClearAsync] Cleared profile for user {UserId}", Id);
    }

    protected override void TransitionState(UserProfileState state, IMessage evt)
    {
        switch (evt)
        {
            case SetUserProfileEvent setProfileEvent:
                state.Gender = setProfileEvent.Gender;
                state.BirthDate = setProfileEvent.BirthDate;
                state.BirthPlace = setProfileEvent.BirthPlace;
                state.FullName = setProfileEvent.FullName;
                break;

            case SetVoiceLanguageEvent setVoiceLanguageEvent:
                state.VoiceLanguage = setVoiceLanguageEvent.VoiceLanguage;
                break;

            case ClearUserProfileEvent:
                state.Gender = string.Empty;
                state.BirthDate = null;
                state.BirthPlace = string.Empty;
                state.FullName = string.Empty;
                state.VoiceLanguage = (int)VoiceLanguageEnum.Unset;
                break;

            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }

    /// <summary>
    /// Sync subscription status between UserBillingGAgent and UserQuotaGAgent if needed
    /// This ensures Google Pay and other platform subscriptions are properly reflected in user quota
    /// </summary>
    private async Task SyncSubscriptionStatusIfNeeded(IUserBillingGAgent userBillingGAgent,
        IUserQuotaGAgent userQuotaGAgent, ActiveSubscriptionStatusDto activeSubscriptionStatus)
    {
        try
        {
            // Get current quota subscription status
            var quotaSubscription = await userQuotaGAgent.GetSubscriptionAsync(false);
            var quotaUltimateSubscription = await userQuotaGAgent.GetSubscriptionAsync(true);

            Logger.LogDebug(
                $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Current quota subscription status - Premium: {quotaSubscription.IsActive}, Ultimate: {quotaUltimateSubscription.IsActive}");

            // Check if there's any mismatch between billing and quota status
            bool needsSync = false;

            // If billing shows active subscriptions but quota doesn't, we need to sync
            if (activeSubscriptionStatus.HasActiveSubscription && !quotaSubscription.IsActive &&
                !quotaUltimateSubscription.IsActive)
            {
                needsSync = true;
                Logger.LogInformation(
                    $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Subscription status mismatch detected. Billing shows active subscription but quota shows inactive. Syncing...");
            }

            // Additional check: If Google Play specifically shows active but neither quota subscription is active
            if (activeSubscriptionStatus.HasActiveGooglePlaySubscription && !quotaSubscription.IsActive &&
                !quotaUltimateSubscription.IsActive)
            {
                needsSync = true;
                Logger.LogInformation(
                    $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Google Play subscription detected but not reflected in quota. Syncing...");
            }

            if (needsSync)
            {
                // Get latest payment history to sync subscription status
                var paymentHistory = await userBillingGAgent.GetPaymentHistoryAsync(1, 10); // Get recent payments

                foreach (var payment in paymentHistory.Where(p =>
                             p.Status == PaymentStatus.Completed && p.Platform == PaymentPlatform.GooglePlay))
                {
                    Logger.LogInformation(
                        $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Found Google Play payment {payment.PaymentGrainId} with PlanType {payment.PlanType}, syncing to UserQuotaGAgent");

                    // Determine if this is ultimate subscription based on membership level
                    bool isUltimate = payment.MembershipLevel == MembershipLevel.Membership_Level_Ultimate;

                    // Create subscription info to sync using Proto type
                    var subscriptionToSync = new SubscriptionInfoProto
                    {
                        IsActive = true,
                        PlanType = (QuotaPlanType)payment.PlanType,
                        Status = (QuotaPaymentStatus)payment.Status,
                        StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(payment.SubscriptionStartDate, DateTimeKind.Utc)),
                        EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(payment.SubscriptionEndDate, DateTimeKind.Utc)),
                        SubscriptionIds = { payment.SubscriptionId }
                    };

                    await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionToSync, isUltimate);
                    Logger.LogInformation(
                        $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Successfully synced Google Play subscription to UserQuotaGAgent - Ultimate: {isUltimate}");
                    break; // Only sync the most recent active subscription
                }
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Error syncing subscription status");
            // Don't throw - this is a best-effort sync operation
        }
    }
}

