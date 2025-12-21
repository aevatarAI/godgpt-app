using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Observability;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

// PlanType is now available via GlobalUsings (Proto version)

namespace Aevatar.Application.Grains.UserQuota;

public interface IUserQuotaGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<bool> InitializeCreditsAsync();
    Task<CreditsInfoProto> GetCreditsAsync();
    Task SetShownCreditsToastAsync(bool hasShownInitialCreditsToast);
    Task<bool> IsSubscribedAsync(bool ultimate = false);
    Task<SubscriptionInfoProto> GetSubscriptionAsync(bool ultimate = false);
    Task<SubscriptionInfoProto> GetAndSetSubscriptionAsync(bool ultimate = false);
    Task UpdateSubscriptionAsync(SubscriptionInfoProto subscriptionInfoProto, bool ultimate = false);
    Task CancelSubscriptionAsync();
    Task<ExecuteActionResultProto> ExecuteActionAsync(string sessionId, string chatManagerGuid, QuotaActionType actionType = QuotaActionType.Conversation);
    Task<ExecuteActionResultProto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid);
    Task<ExecuteActionResultProto> CanUploadImageAsync();
    Task ResetRateLimitsAsync(string actionType = "conversation");
    Task ClearAllAsync();
    Task UpdateQuotaAsync(string productId, DateTime expiresDate);
    Task ResetQuotaAsync();
    Task<GrainResultIntProto> UpdateCreditsAsync(string operatorUserId, int creditsChange);
    Task<GrainResultSubscriptionListProto> UpdateSubscriptionAsync(string operatorUserId, QuotaPlanType planType, bool ultimate = false);
    Task AddCreditsAsync(int credits);
    Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime);
    Task<UserQuotaState> GetUserQuotaStateAsync();
    Task<bool> ActivateFreeTrialAsync(int trialDays, QuotaPlanType planType, bool isUltimate);
    Task<FreeTrialInfoProto> GetFreeTrialInfoAsync();
}

[GAgent(nameof(UserQuotaGAgent))]
public class UserQuotaGAgent : GAgentBase<UserQuotaState>, IUserQuotaGAgent
{
    private readonly IOptionsMonitor<CreditsOptions> _creditsOptions;
    private readonly IOptionsMonitor<RateLimitOptions> _rateLimiterOptions;
    private readonly ILocalizationService _localizationService;

    public UserQuotaGAgent(
        Guid id,
        IOptionsMonitor<CreditsOptions> creditsOptions,
        IOptionsMonitor<RateLimitOptions> rateLimiterOptions,
        ILocalizationService localizationService) : base(id)
    {
        _creditsOptions = creditsOptions;
        _rateLimiterOptions = rateLimiterOptions;
        _localizationService = localizationService;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User Quota Management GAgent");
    }

    public async Task<bool> InitializeCreditsAsync()
    {
        if (State.HasInitialCredits)
        {
            return true;
        }

        var initialCredits = _creditsOptions.CurrentValue.InitialCreditsAmount;

        RaiseEvent(new InitializeCreditsEvent { InitialCredits = initialCredits });
        await ConfirmEventsAsync();

        Logger.LogDebug("[UserQuotaGAgent][InitializeCreditsAsync] User {UserId} received {Credits} initial credits.", Id, initialCredits);
        return true;
    }

    public async Task<CreditsInfoProto> GetCreditsAsync()
    {
        await InitializeCreditsAsync();
        var creditsInfo = new CreditsInfoProto
        {
            IsInitialized = State.HasInitialCredits,
            Credits = State.Credits,
            ShouldShowToast = State.HasShownInitialCreditsToast
        };
        if (State.HasInitialCredits && !State.HasShownInitialCreditsToast)
        {
            creditsInfo.ShouldShowToast = true;
        }
        else
        {
            creditsInfo.ShouldShowToast = false;
        }

        return creditsInfo;
    }

    public async Task SetShownCreditsToastAsync(bool hasShownInitialCreditsToast)
    {
        RaiseEvent(new SetShownCreditsToastEvent { HasShownInitialCreditsToast = hasShownInitialCreditsToast });
        await ConfirmEventsAsync();
    }

    public async Task<bool> IsSubscribedAsync(bool ultimate = false)
    {
        var subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;
        if (subscriptionInfo == null)
        {
            subscriptionInfo = new SubscriptionInfoProto();
        }

        var now = DateTime.UtcNow;
        var startDate = subscriptionInfo.StartDate?.ToDateTime() ?? DateTime.MinValue;
        var endDate = subscriptionInfo.EndDate?.ToDateTime() ?? DateTime.MinValue;
        var isSubscribed = subscriptionInfo.IsActive && startDate <= now && endDate > now;

        if (subscriptionInfo.IsActive && endDate <= now)
        {
            Logger.LogDebug("[UserQuotaGAgent][IsSubscribedAsync] Subscription for user {UserId} expired. Ultimate: {Ultimate}", Id, ultimate);

            var expiredSubscription = new SubscriptionInfoProto
            {
                IsActive = false,
                PlanType = subscriptionInfo.PlanType,
                Status = subscriptionInfo.Status,
                StartDate = subscriptionInfo.StartDate,
                EndDate = subscriptionInfo.EndDate
            };
            expiredSubscription.SubscriptionIds.AddRange(subscriptionInfo.SubscriptionIds);
            expiredSubscription.InvoiceIds.AddRange(subscriptionInfo.InvoiceIds);

            RaiseEvent(new UpdateSubscriptionEvent
            {
                SubscriptionInfo = expiredSubscription,
                IsUltimate = ultimate
            });

            if (State.RateLimits.ContainsKey("conversation"))
            {
                RaiseEvent(new ClearRateLimitEvent { ActionType = "conversation" });
            }
        }

        return isSubscribed;
    }

    public async Task ResetRateLimitsAsync(string actionType = "conversation")
    {
        RaiseEvent(new ClearRateLimitEvent { ActionType = actionType });
        await ConfirmEventsAsync();
    }

    public async Task ClearAllAsync()
    {
        Logger.LogInformation("[UserQuotaGAgent] ClearAllAsync before GrainId={A} CanReceiveInviteReward={B}", Id, State.CanReceiveInviteReward);
        RaiseEvent(new ClearAllQuotaEvent { CanReceiveInviteReward = State.CanReceiveInviteReward });
        await ConfirmEventsAsync();
    }

    public async Task<SubscriptionInfoProto> GetSubscriptionAsync(bool ultimate = false)
    {
        var subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;

        if (subscriptionInfo == null)
        {
            RaiseEvent(new UpdateSubscriptionEvent
            {
                SubscriptionInfo = new SubscriptionInfoProto(),
                IsUltimate = ultimate
            });
            await ConfirmEventsAsync();
            subscriptionInfo = ultimate ? State.UltimateSubscription : State.Subscription;
        }

        var result = new SubscriptionInfoProto
        {
            IsActive = subscriptionInfo?.IsActive ?? false,
            PlanType = subscriptionInfo?.PlanType ?? QuotaPlanType.None,
            Status = subscriptionInfo?.Status ?? QuotaPaymentStatus.None,
            StartDate = subscriptionInfo?.StartDate,
            EndDate = subscriptionInfo?.EndDate
        };
        if (subscriptionInfo?.SubscriptionIds != null) result.SubscriptionIds.AddRange(subscriptionInfo.SubscriptionIds);
        if (subscriptionInfo?.InvoiceIds != null) result.InvoiceIds.AddRange(subscriptionInfo.InvoiceIds);
        return result;
    }

    public async Task<SubscriptionInfoProto> GetAndSetSubscriptionAsync(bool ultimate = false)
    {
        await IsSubscribedAsync(ultimate);
        return await GetSubscriptionAsync(ultimate);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionInfoProto subscriptionInfoProto, bool ultimate = false)
    {
        Logger.LogInformation("[UserQuotaGAgent][UpdateSubscriptionAsync] Updated subscription for user {UserId}: PlanType={PlanType}", Id, subscriptionInfoProto.PlanType);

        RaiseEvent(new UpdateSubscriptionEvent
        {
            SubscriptionInfo = subscriptionInfoProto,
            IsUltimate = ultimate
        });
        await ConfirmEventsAsync();
    }

    public async Task CancelSubscriptionAsync()
    {
        var premiumSubscription = State.Subscription;
        if (premiumSubscription != null && premiumSubscription.IsActive)
        {
            Logger.LogInformation("[UserQuotaGAgent][CancelSubscriptionAsync] cancel premium subscription {0}", Id);
            RaiseEvent(new CancelSubscriptionEvent { IsUltimate = false });
            await ConfirmEventsAsync();
        }

        var ultimateSubscription = State.UltimateSubscription;
        if (ultimateSubscription != null && ultimateSubscription.IsActive)
        {
            Logger.LogInformation("[UserQuotaGAgent][CancelSubscriptionAsync] cancel ultimate subscription {0}", Id);
            RaiseEvent(new CancelSubscriptionEvent { IsUltimate = true });
            await ConfirmEventsAsync();
        }
    }

    public async Task<ExecuteActionResultProto> ExecuteActionAsync(string sessionId, string chatManagerGuid, QuotaActionType actionType = QuotaActionType.Conversation)
    {
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        Logger.LogDebug($"[ExecuteActionAsync] Language from sessionId:{sessionId} chatManagerGuid: {chatManagerGuid}, language:{language}");
        
        if (actionType == QuotaActionType.ImageConversation)
        {
            var today = DateTime.UtcNow.Date;
            var dailyInfo = State.DailyImageConversation ?? new DailyImageConversationInfoProto();
            var lastTime = dailyInfo.LastConversationTime?.ToDateTime() ?? DateTime.MinValue;
            
            if (lastTime.Date != today)
            {
                dailyInfo = new DailyImageConversationInfoProto
                {
                    LastConversationTime = Timestamp.FromDateTime(DateTime.UtcNow),
                    Count = 1
                };
            }
            else
            {
                dailyInfo = new DailyImageConversationInfoProto
                {
                    LastConversationTime = Timestamp.FromDateTime(DateTime.UtcNow),
                    Count = dailyInfo.Count + 1
                };
            }
            
            if (!await IsSubscribedAsync(true) && !await IsSubscribedAsync(false) && dailyInfo.Count > 1)
            {
                var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.DailyUpdateLimit, language);
                return new ExecuteActionResultProto
                {
                    Code = ExecuteActionStatus.RateLimitExceeded,
                    Message = localizedMessage
                };
            }

            RaiseEvent(new UpdateDailyImageConversationEvent { DailyImageConversation = dailyInfo });
            return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, QuotaActionType.Conversation);
        }
        
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, actionType);
    }

    public async Task<ExecuteActionResultProto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid)
    {
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, QuotaActionType.VoiceConversation);
    }

    public async Task<ExecuteActionResultProto> CanUploadImageAsync()
    {
        if (await IsSubscribedAsync(true) || await IsSubscribedAsync(false))
        {
            return new ExecuteActionResultProto { Success = true };
        }

        var today = DateTime.UtcNow.Date;
        var dailyInfo = State.DailyImageConversation;
        var lastTime = dailyInfo?.LastConversationTime?.ToDateTime() ?? DateTime.MinValue;

        if (lastTime.Date != today)
        {
            return new ExecuteActionResultProto { Success = true };
        }

        if ((dailyInfo?.Count ?? 0) >= 1)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.DailyUpdateLimit, language);
            return new ExecuteActionResultProto
            {
                Success = false,
                Code = ExecuteActionStatus.RateLimitExceeded,
                Message = localizedMessage
            };
        }

        return new ExecuteActionResultProto { Success = true };
    }

    private async Task<ExecuteActionResultProto> ExecuteStandardActionAsync(string sessionId, string chatManagerGuid, QuotaActionType actionTypeEnum)
    {
        var now = DateTime.UtcNow;
        var isVoiceMessage = actionTypeEnum == QuotaActionType.VoiceConversation;
        var actionType = actionTypeEnum.ToString().ToLowerInvariant();

        if (await IsSubscribedAsync(true))
        {
            return new ExecuteActionResultProto { Success = true };
        }

        var isSubscribed = await IsSubscribedAsync(false);
        var maxTokens = isSubscribed
            ? (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceSubscribedUserMaxRequests : _rateLimiterOptions.CurrentValue.SubscribedUserMaxRequests)
            : (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceUserMaxRequests : _rateLimiterOptions.CurrentValue.UserMaxRequests);
        var timeWindow = isSubscribed
            ? (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceSubscribedUserTimeWindowSeconds : _rateLimiterOptions.CurrentValue.SubscribedUserTimeWindowSeconds)
            : (isVoiceMessage ? _rateLimiterOptions.CurrentValue.VoiceUserTimeWindowSeconds : _rateLimiterOptions.CurrentValue.UserTimeWindowSeconds);

        if (!State.RateLimits.TryGetValue(actionType, out var rateLimitInfo))
        {
            rateLimitInfo = new RateLimitInfoProto { Count = maxTokens, LastTime = Timestamp.FromDateTime(now) };
            RaiseEvent(new UpdateRateLimitEvent { ActionType = actionType, RateLimitInfo = rateLimitInfo });
        }
        else
        {
            var lastTime = rateLimitInfo.LastTime?.ToDateTime() ?? now;
            var timeElapsed = now - lastTime;
            var elapsedSeconds = timeElapsed.TotalSeconds;
            var refillRate = (double)maxTokens / timeWindow;
            var tokensToAdd = (int)(elapsedSeconds * refillRate);

            if (tokensToAdd > 0)
            {
                rateLimitInfo = new RateLimitInfoProto
                {
                    Count = Math.Min(maxTokens, rateLimitInfo.Count + tokensToAdd),
                    LastTime = Timestamp.FromDateTime(now)
                };
                RaiseEvent(new UpdateRateLimitEvent { ActionType = actionType, RateLimitInfo = rateLimitInfo });
            }
        }

        if (!isSubscribed)
        {
            var requiredCredits = _creditsOptions.CurrentValue.CreditsPerConversation;
            var credits = (await GetCreditsAsync()).Credits;
            var isAllowed = credits >= requiredCredits;

            if (!isAllowed)
            {
                return new ExecuteActionResultProto
                {
                    Code = ExecuteActionStatus.InsufficientCredits,
                    Message = "You've run out of credits."
                };
            }
        }

        await ConfirmEventsAsync();
        
        try
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            var localizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.ChatRateLimit, language);
            var voiceLocalizedMessage = _localizationService.GetLocalizedException(ExceptionMessageKeys.VoiceChatRateLimit, language);

            var oldValue = State.RateLimits[actionType].Count;
            if (oldValue <= 0)
            {
                return new ExecuteActionResultProto
                {
                    Code = ExecuteActionStatus.RateLimitExceeded,
                    Message = isVoiceMessage ? voiceLocalizedMessage : localizedMessage
                };
            }
        }
        catch (Exception e)
        {
            Logger.LogWarning($"[UserQuotaGAgent][ExecuteStandardActionAsync] RateLimits check error {actionType} msg:{e.Message}");
        }

        if (!isSubscribed)
        {
            try
            {
                var newCredits = State.Credits - _creditsOptions.CurrentValue.CreditsPerConversation;
                RaiseEvent(new UpdateCreditsEvent { NewCredits = newCredits });

                if (newCredits == 0)
                {
                    await ReportCreditsExhaustedAsync();
                }
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[UserQuotaGAgent][ExecuteStandardActionAsync] ReportCreditsExhaustedAsync error msg:{e.Message}");
            }
        }

        var updatedRateLimitInfo = State.RateLimits[actionType];
        var newRateLimitInfo = new RateLimitInfoProto
        {
            Count = updatedRateLimitInfo.Count - 1,
            LastTime = updatedRateLimitInfo.LastTime
        };
        RaiseEvent(new UpdateRateLimitEvent { ActionType = actionType, RateLimitInfo = newRateLimitInfo });

        return new ExecuteActionResultProto { Success = true };
    }

    private Task ReportCreditsExhaustedAsync()
    {
        try
        {
            var createdAt = State.CreatedAt?.ToDateTime() ?? DateTime.UtcNow;
            var daysSinceSignup = (int)(DateTime.UtcNow - createdAt).TotalDays;
            UserLifecycleTelemetryMetrics.RecordCreditsExhausted(Id.ToString(), daysSinceSignup, Logger);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[UserQuotaGAgent] Failed to report credits exhausted event for user: {UserId}", Id);
        }
        return Task.CompletedTask;
    }

    public async Task UpdateQuotaAsync(string productId, DateTime expiresDate)
    {
        Logger.LogInformation("[UserQuotaGAgent][UpdateQuotaAsync] Updating quota for user {UserId} with product {ProductId}, expires on {ExpiresDate}", Id, productId, expiresDate);

        var planType = DeterminePlanTypeFromProductIdToProto(productId);

        var subscriptionProto = new SubscriptionInfoProto
        {
            PlanType = planType,
            IsActive = true,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Utc)),
            EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(expiresDate, DateTimeKind.Utc)),
            Status = QuotaPaymentStatus.Completed
        };
        if (State.Subscription?.SubscriptionIds != null) subscriptionProto.SubscriptionIds.AddRange(State.Subscription.SubscriptionIds);
        if (State.Subscription?.InvoiceIds != null) subscriptionProto.InvoiceIds.AddRange(State.Subscription.InvoiceIds);

        RaiseEvent(new UpdateSubscriptionEvent { SubscriptionInfo = subscriptionProto, IsUltimate = false });
        await ConfirmEventsAsync();
        await ResetRateLimitsAsync();
    }

    public async Task ResetQuotaAsync()
    {
        Logger.LogInformation("[UserQuotaGAgent][ResetQuotaAsync] Resetting quota for user {UserId}", Id);

        var subscriptionProto = new SubscriptionInfoProto
        {
            IsActive = false,
            PlanType = State.Subscription?.PlanType ?? QuotaPlanType.None,
            Status = QuotaPaymentStatus.None,
            StartDate = State.Subscription?.StartDate,
            EndDate = State.Subscription?.EndDate
        };
        if (State.Subscription?.SubscriptionIds != null) subscriptionProto.SubscriptionIds.AddRange(State.Subscription.SubscriptionIds);
        if (State.Subscription?.InvoiceIds != null) subscriptionProto.InvoiceIds.AddRange(State.Subscription.InvoiceIds);

        RaiseEvent(new UpdateSubscriptionEvent { SubscriptionInfo = subscriptionProto, IsUltimate = false });
        await ConfirmEventsAsync();
        await ResetRateLimitsAsync();
    }

    private QuotaPlanType DeterminePlanTypeFromProductIdToProto(string productId)
    {
        if (productId.Contains("monthly") || productId.Contains("month"))
            return QuotaPlanType.Month;
        if (productId.Contains("yearly") || productId.Contains("year"))
            return QuotaPlanType.Year;
        if (productId.Contains("daily") || productId.Contains("day"))
            return QuotaPlanType.Day;
        return QuotaPlanType.Month;
    }

    public async Task<GrainResultIntProto> UpdateCreditsAsync(string operatorUserId, int creditsChange)
    {
        if (!IsUserAuthorizedToUpdateCredits(operatorUserId))
        {
            return new GrainResultIntProto
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update credits",
                Data = State.Credits
            };
        }

        var newCredits = Math.Max(0, State.Credits + creditsChange);
        RaiseEvent(new UpdateCreditsEvent { NewCredits = newCredits });
        await ConfirmEventsAsync();

        return new GrainResultIntProto
        {
            Success = true,
            Message = $"Credits successfully updated by {creditsChange}",
            Data = State.Credits
        };
    }

    public async Task<GrainResultSubscriptionListProto> UpdateSubscriptionAsync(string operatorUserId, QuotaPlanType planType, bool ultimate = false)
    {
        if (!IsUserAuthorizedToUpdateCredits(operatorUserId))
        {
            return new GrainResultSubscriptionListProto
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update subscription"
            };
        }

        var oldSubscriptionInfo = await GetSubscriptionAsync(ultimate);
        if (await IsSubscribedAsync(ultimate))
        {
            var subscriptionInfo = await GetSubscriptionAsync(ultimate);
            if (SubscriptionHelper.IsUpgradeOrSameLevel(subscriptionInfo.PlanType, planType))
            {
                subscriptionInfo.PlanType = planType;
            }
            subscriptionInfo.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(SubscriptionHelper.GetSubscriptionEndDate(planType,  subscriptionInfo.EndDate!.ToDateTime() ), DateTimeKind.Utc));
            await UpdateSubscriptionAsync(subscriptionInfo, ultimate);
        }
        else
        {
            var startDate = DateTime.UtcNow;
            var subscriptionInfo = new SubscriptionInfoProto
            {
                IsActive = true,
                PlanType = planType,
                Status = QuotaPaymentStatus.Completed,
                StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(startDate, DateTimeKind.Utc)),
                EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(SubscriptionHelper.GetSubscriptionEndDate(planType, startDate), DateTimeKind.Utc))
            };
            await UpdateSubscriptionAsync(subscriptionInfo, ultimate);
        }

        if (ultimate && await IsSubscribedAsync(false))
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            var startDate = premiumSubscription.StartDate!.ToDateTime();
            var endDate = premiumSubscription.EndDate!.ToDateTime();
            premiumSubscription.StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(SubscriptionHelper.GetSubscriptionEndDate(planType, startDate), DateTimeKind.Utc));
            premiumSubscription.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(SubscriptionHelper.GetSubscriptionEndDate(planType, endDate), DateTimeKind.Utc));
            await UpdateSubscriptionAsync(premiumSubscription, false);
        }
        await ConfirmEventsAsync();

        var currentSubscriptionInfo = await GetSubscriptionAsync(ultimate);
        var result = new GrainResultSubscriptionListProto { Success = true };
        result.Data.Add(oldSubscriptionInfo);
        result.Data.Add(currentSubscriptionInfo);
        return result;
    }

    private bool IsUserAuthorizedToUpdateCredits(string operatorUserId)
    {
        var authorizedUsers = _creditsOptions.CurrentValue.OperatorUserId;
        return authorizedUsers.Contains(operatorUserId);
    }

    public async Task AddCreditsAsync(int credits)
    {
        await InitializeCreditsAsync();
        if (credits < 0)
        {
            Logger.LogWarning("[UserQuotaGAgent][AddCreditsAsync] Attempt to add negative credits: {Credits}", credits);
            return;
        }

        var oldCredits = State.Credits;
        RaiseEvent(new UpdateCreditsEvent { NewCredits = oldCredits + credits });
        await ConfirmEventsAsync();
    }

    public async Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime)
    {
        if (!State.CanReceiveInviteReward)
        {
            Logger.LogWarning($"User {userId} cannot receive invite reward, CanReceiveInviteReward is false");
            return false;
        }

        if ((DateTime.UtcNow - dateTime).TotalHours > 72)
        {
            Logger.LogWarning($"User {userId} invite reward redemption window expired.");
            RaiseEvent(new UpdateCanReceiveInviteRewardEvent { CanReceiveInviteReward = false });
            await ConfirmEventsAsync();
            return false;
        }

        if (await IsSubscribedAsync(false))
        {
            Logger.LogWarning($"User {userId} cannot receive invite,reward,IsSubscribedAsync is true.");
            RaiseEvent(new UpdateCanReceiveInviteRewardEvent { CanReceiveInviteReward = false });
            await ConfirmEventsAsync();
            return false;
        }

        var startDate = DateTime.UtcNow;
        var subscriptionProto = new SubscriptionInfoProto
        {
            IsActive = true,
            PlanType = QuotaPlanType.Week,
            Status = QuotaPaymentStatus.Completed,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(startDate, DateTimeKind.Utc)),
            EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(SubscriptionHelper.GetSubscriptionEndDate(QuotaPlanType.Week, startDate), DateTimeKind.Utc))
        };
        await UpdateSubscriptionAsync(subscriptionProto, false);

        RaiseEvent(new UpdateCanReceiveInviteRewardEvent { CanReceiveInviteReward = false });
        await ConfirmEventsAsync();
        return true;
    }

    public Task<UserQuotaState> GetUserQuotaStateAsync()
    {
        return Task.FromResult(State);
    }

    public async Task<bool> ActivateFreeTrialAsync(int trialDays, QuotaPlanType planType, bool isUltimate)
    {
        var startDate = DateTime.UtcNow;
        var endDate = startDate.AddDays(trialDays);

        RaiseEvent(new ActivateFreeTrialEvent
        {
            TrialDays = trialDays,
            PlanType = planType,
            IsUltimate = isUltimate,
            StartDate = Timestamp.FromDateTime(startDate),
            EndDate = Timestamp.FromDateTime(endDate)
        });

        await ConfirmEventsAsync();
        Logger.LogInformation("Free trial activated for user {UserId}. Days: {TrialDays}, PlanType: {PlanType}, IsUltimate: {IsUltimate}", Id, trialDays, planType, isUltimate);
        return true;
    }

    public Task<FreeTrialInfoProto> GetFreeTrialInfoAsync()
    {
        if (State.FreeTrialInfo == null)
        {
            return Task.FromResult(new FreeTrialInfoProto());
        }

        return Task.FromResult(new FreeTrialInfoProto
        {
            FreeTrialCode = State.FreeTrialInfo.FreeTrialCode,
            TrialDays = State.FreeTrialInfo.TrialDays,
            PlanType = State.FreeTrialInfo.PlanType,
            IsUltimate = State.FreeTrialInfo.IsUltimate,
            TransactionId = State.FreeTrialInfo.TransactionId
        });
    }

    #region TransitionState

    protected override void TransitionState(UserQuotaState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeCreditsEvent initializeCredits:
                state.Credits = initializeCredits.InitialCredits;
                state.HasInitialCredits = true;
                break;

            case SetShownCreditsToastEvent setShownCreditsToast:
                state.HasShownInitialCreditsToast = setShownCreditsToast.HasShownInitialCreditsToast;
                break;

            case UpdateRateLimitEvent updateRateLimit:
                state.RateLimits[updateRateLimit.ActionType] = updateRateLimit.RateLimitInfo;
                break;

            case ClearRateLimitEvent clearRateLimit:
                if (state.RateLimits.ContainsKey(clearRateLimit.ActionType))
                {
                    state.RateLimits.Remove(clearRateLimit.ActionType);
                }
                break;

            case UpdateSubscriptionEvent updateSubscription:
                var subscription = updateSubscription.IsUltimate ? state.UltimateSubscription : state.Subscription;
                
                if (subscription == null)
                {
                    subscription = new SubscriptionInfoProto();
                    if (updateSubscription.IsUltimate)
                    {
                        state.UltimateSubscription = subscription;
                    }
                    else
                    {
                        state.Subscription = subscription;
                    }
                }
                
                subscription.IsActive = updateSubscription.SubscriptionInfo.IsActive;
                subscription.PlanType = updateSubscription.SubscriptionInfo.PlanType;
                subscription.Status = updateSubscription.SubscriptionInfo.Status;
                subscription.StartDate = updateSubscription.SubscriptionInfo.StartDate;
                subscription.EndDate = updateSubscription.SubscriptionInfo.EndDate;
                subscription.SubscriptionIds.Clear();
                subscription.SubscriptionIds.AddRange(updateSubscription.SubscriptionInfo.SubscriptionIds);
                subscription.InvoiceIds.Clear();
                subscription.InvoiceIds.AddRange(updateSubscription.SubscriptionInfo.InvoiceIds);
                break;

            case CancelSubscriptionEvent cancelSubscription:
                var sub = cancelSubscription.IsUltimate ? state.UltimateSubscription : state.Subscription;
                if (sub != null)
                {
                    sub.IsActive = false;
                    sub.PlanType = QuotaPlanType.None;
                    sub.Status = QuotaPaymentStatus.None;
                }
                break;

            case UpdateCreditsEvent updateCredits:
                state.Credits = updateCredits.NewCredits;
                break;

            case UpdateCanReceiveInviteRewardEvent updateCanReceiveInviteReward:
                state.CanReceiveInviteReward = updateCanReceiveInviteReward.CanReceiveInviteReward;
                break;

            case UpdateDailyImageConversationEvent updateDailyImageConversation:
                state.DailyImageConversation = updateDailyImageConversation.DailyImageConversation;
                break;

            case ActivateFreeTrialEvent activateFreeTrialEvent:
                if (state.FreeTrialInfo == null)
                {
                    state.FreeTrialInfo = new FreeTrialInfoProto();
                }
                state.FreeTrialInfo.FreeTrialCode = string.Empty;
                state.FreeTrialInfo.TrialDays = activateFreeTrialEvent.TrialDays;
                state.FreeTrialInfo.PlanType = activateFreeTrialEvent.PlanType;
                state.FreeTrialInfo.IsUltimate = activateFreeTrialEvent.IsUltimate;
                break;

            case ClearAllQuotaEvent:
                state.Subscription = new SubscriptionInfoProto();
                state.RateLimits.Clear();
                state.UltimateSubscription = new SubscriptionInfoProto();
                state.CreatedAt = null;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }

    #endregion
}
