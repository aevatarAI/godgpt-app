using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserProfile;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.Application.Grains.UserQuota;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Aevatar.Agents.Abstractions.Extensions;

namespace Aevatar.Application.Grains.UserProfile;

/// <summary>
/// User Profile GAgent implementation
/// Manages user basic information and voice preference settings
/// </summary>
public class UserProfileGAgent : GAgentBase<UserProfileState>, IUserProfileGAgent
{
    // Injected by OrleansGAgentGrain via reflection
    public IGAgentActorFactory? ActorFactory { get; set; }

    public UserProfileGAgent()
    {
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
        
        return Guid.Parse(AgentId.ExtractRawId(Id));
    }

    /// <summary>
    /// Gets user basic profile information (includes credits and subscription)
    /// </summary>
    public async Task<UserProfileDtoProto> GetUserProfileAsync()
    {
        var rawId = AgentId.ExtractRawId(Id);
        Logger.LogDebug($"[UserProfileGAgent][GetUserProfileAsync] userId: {rawId}");

        var invitationActor = await ActorFactory!.CreateGAgentActorAsync<InvitationGAgent>(rawId);
        var invitationGrain = invitationActor.As<IInvitationGAgent>();
        await invitationGrain.ProcessScheduledRewardAsync();

        // TODO: [USER_BILLING_DISABLED] UserBillingGAgent not implemented
        // Sync latest subscription status from UserBillingGAgent before getting user profile
        // This ensures Google Pay and other platform subscriptions are up-to-date
        // var userBillingActor = await _actorFactory.CreateGAgentActorAsync<UserBillingGAgent>(Id);
        // var userBillingGAgent = (IUserBillingGAgent)userBillingActor.GetAgent();
        // var activeSubscriptionStatus = await userBillingGAgent.GetActiveSubscriptionStatusAsync();

        // Logger.LogDebug(
        //     $"[ChatGAgentManager][GetUserProfileAsync] Active subscription status - Apple: {activeSubscriptionStatus.HasActiveAppleSubscription}, Stripe: {activeSubscriptionStatus.HasActiveStripeSubscription}, GooglePlay: {activeSubscriptionStatus.HasActiveGooglePlaySubscription}");

        var userQuotaActor = await ActorFactory!.CreateGAgentActorAsync<UserQuotaGAgent>(rawId);
        var userQuotaGAgent = userQuotaActor.As<IUserQuotaGAgent>();

        // TODO: [USER_BILLING_DISABLED] SyncSubscriptionStatusIfNeeded disabled until UserBillingGAgent is implemented
        // Check if we need to sync subscription status between UserBillingGAgent and UserQuotaGAgent
        // This is particularly important for Google Pay subscriptions that might not be reflected in UserQuotaGAgent yet
        // await SyncSubscriptionStatusIfNeeded(userBillingGAgent, userQuotaGAgent, activeSubscriptionStatus);

        // Use Proto methods for RPC compatibility
        var creditsProto = await userQuotaGAgent.GetCreditsAsync();
        var subscriptionProto = await userQuotaGAgent.GetAndSetSubscriptionProtoAsync();
        var ultimateSubscriptionProto = await userQuotaGAgent.GetAndSetSubscriptionProtoAsync(true);

        // Get inviter ID from UserInvitationGAgent
        var userInvitationActor = await ActorFactory!.CreateGAgentActorAsync<UserInvitationGAgent>(rawId);
        var userInvitationGAgent = userInvitationActor.As<IUserInvitationGAgent>();
        var inviterId = await userInvitationGAgent.GetInviterAsync();

        return new UserProfileDtoProto
        {
            Id = Id.ToString(),
            Gender = State.Gender ?? string.Empty,
            BirthDate = State.BirthDate,
            BirthPlace = State.BirthPlace ?? string.Empty,
            FullName = State.FullName ?? string.Empty,
            VoiceLanguage = State.VoiceLanguage,
            Credits = creditsProto,
            Subscription = subscriptionProto,
            UltimateSubscription = ultimateSubscriptionProto,
            InviterId = inviterId?.ToString() ?? string.Empty
        };
    }

    private static SubscriptionInfoProto MapToProtoSubscriptionFromDto(SubscriptionInfoDto sub)
    {
        if (sub == null) return new SubscriptionInfoProto();
        var result = new SubscriptionInfoProto
        {
            IsActive = sub.IsActive,
            PlanType = (QuotaPlanType)(int)sub.PlanType,
            Status = (QuotaPaymentStatus)(int)sub.Status,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(sub.StartDate, DateTimeKind.Utc)),
            EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(sub.EndDate, DateTimeKind.Utc))
        };
        if (sub.SubscriptionIds != null) result.SubscriptionIds.AddRange(sub.SubscriptionIds);
        if (sub.InvoiceIds != null) result.InvoiceIds.AddRange(sub.InvoiceIds);
        return result;
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
        if (string.IsNullOrEmpty(Id))
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

        var rawId = AgentId.ExtractRawId(Id);
        Logger.LogDebug(
            "[UserProfileGAgent][SetVoiceLanguageAsync] Successfully set voice language to {VoiceLanguage} for user {UserId}",
            voiceLanguage, rawId);
        
        return Guid.Parse(rawId);
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

    // TODO: [USER_BILLING_DISABLED] SyncSubscriptionStatusIfNeeded method disabled until UserBillingGAgent is implemented
    // /// <summary>
    // /// Sync subscription status between UserBillingGAgent and UserQuotaGAgent if needed
    // /// This ensures Google Pay and other platform subscriptions are properly reflected in user quota
    // /// </summary>
    // private async Task SyncSubscriptionStatusIfNeeded(IUserBillingGAgent userBillingGAgent,
    //     IUserQuotaGAgent userQuotaGAgent, ActiveSubscriptionStatusDto activeSubscriptionStatus)
    // {
    //     try
    //     {
    //         // Get current quota subscription status
    //         var quotaSubscription = await userQuotaGAgent.GetSubscriptionAsync(false);
    //         var quotaUltimateSubscription = await userQuotaGAgent.GetSubscriptionAsync(true);
    //
    //         Logger.LogDebug(
    //             $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Current quota subscription status - Premium: {quotaSubscription.IsActive}, Ultimate: {quotaUltimateSubscription.IsActive}");
    //
    //         // Check if there's any mismatch between billing and quota status
    //         bool needsSync = false;
    //
    //         // If billing shows active subscriptions but quota doesn't, we need to sync
    //         if (activeSubscriptionStatus.HasActiveSubscription && !quotaSubscription.IsActive &&
    //             !quotaUltimateSubscription.IsActive)
    //         {
    //             needsSync = true;
    //             Logger.LogInformation(
    //                 $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Subscription status mismatch detected. Billing shows active subscription but quota shows inactive. Syncing...");
    //         }
    //
    //         // Additional check: If Google Play specifically shows active but neither quota subscription is active
    //         if (activeSubscriptionStatus.HasActiveGooglePlaySubscription && !quotaSubscription.IsActive &&
    //             !quotaUltimateSubscription.IsActive)
    //         {
    //             needsSync = true;
    //             Logger.LogInformation(
    //                 $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Google Play subscription detected but not reflected in quota. Syncing...");
    //         }
    //
    //         if (needsSync)
    //         {
    //             // Get latest payment history to sync subscription status
    //             var paymentHistory = await userBillingGAgent.GetPaymentHistoryAsync(1, 10); // Get recent payments
    //
    //             foreach (var payment in paymentHistory.Where(p =>
    //                          p.Status == PaymentStatus.Completed && p.Platform == PaymentPlatform.GooglePlay))
    //             {
    //                 Logger.LogInformation(
    //                     $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Found Google Play payment {payment.PaymentGrainId} with PlanType {payment.PlanType}, syncing to UserQuotaGAgent");
    //
    //                 // Determine if this is ultimate subscription based on membership level
    //                 bool isUltimate = payment.MembershipLevel == MembershipLevel.Membership_Level_Ultimate;
    //
    //                 // Create subscription info to sync using Proto type
    //                 var subscriptionToSync = new SubscriptionInfoProto
    //                 {
    //                     IsActive = true,
    //                     PlanType = (QuotaPlanType)payment.PlanType,
    //                     Status = (QuotaPaymentStatus)payment.Status,
    //                     StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(payment.SubscriptionStartDate, DateTimeKind.Utc)),
    //                     EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(payment.SubscriptionEndDate, DateTimeKind.Utc)),
    //                     SubscriptionIds = { payment.SubscriptionId }
    //                 };
    //
    //                 await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionToSync, isUltimate);
    //                 Logger.LogInformation(
    //                     $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Successfully synced Google Play subscription to UserQuotaGAgent - Ultimate: {isUltimate}");
    //                 break; // Only sync the most recent active subscription
    //             }
    //         }
    //     }
    //     catch (Exception ex)
    //     {
    //         Logger.LogError(ex,
    //             $"[UserProfileGAgent][SyncSubscriptionStatusIfNeeded] Error syncing subscription status");
    //         // Don't throw - this is a best-effort sync operation
    //     }
    // }
}

