using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Common;
using Aevatar.Agents.GodGPT.Protos.FreeTrialCode;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.Invitation;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Helpers;
using Aevatar.Application.Grains.Common.Observability;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.FreeTrialCode;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Application.Grains.Invitation;
using Aevatar.Application.Grains.UserInvitation;
using Aevatar.Payment.Agents;
using Aevatar.Payment.Agents.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using static Aevatar.Application.Grains.Common.Helpers.ProtoConversions;

// Alias to avoid conflicts with proto-generated types
using PlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using PaymentStatus = Aevatar.Application.Grains.Common.Constants.PaymentStatus;

namespace Aevatar.Application.Grains.UserQuota;

/// <summary>
/// User Quota Agent interface - manages user credits, subscriptions, and quota limits.
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IUserQuotaGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Set shown credits toast flag (uses Protobuf type for RPC)
    /// </summary>
    Task SetShownCreditsToastAsync(SetShownCreditsToastRequestProto request);
    
    /// <summary>
    /// Update user credits (uses Protobuf type for RPC)
    /// </summary>
    Task<UpdateCreditsResponseProto> UpdateCreditsAsync(UpdateCreditsRequestProto request);
    
    /// <summary>
    /// Update user subscription (uses Protobuf type for RPC)
    /// </summary>
    Task<UpdateSubscriptionResponseProto> UpdateSubscriptionAsync(UpdateSubscriptionRequestProto request);
    
    /// <summary>
    /// Check if user can upload image (uses Protobuf type for RPC)
    /// </summary>
    Task<CanUploadImageResponseProto> CanUploadImageAsync();
    
    // Note: Other methods remain unchanged for now as they are used internally
    // They can be migrated later if needed
    Task<bool> InitializeCreditsAsync();
    Task<CreditsInfoProto> GetCreditsAsync();
    Task<bool> IsSubscribedAsync(bool ultimate = false);
    
    // RPC-compatible Protobuf methods
    Task<SubscriptionInfoProto> GetSubscriptionProtoAsync(bool ultimate = false);
    Task<SubscriptionInfoProto> GetAndSetSubscriptionProtoAsync(bool ultimate = false);
    Task CancelSubscriptionAsync();
    Task<ExecuteActionResultProto> ExecuteActionAsync(string sessionId, string chatManagerGuid, ActionType actionType = ActionType.Conversation);
    Task<ExecuteActionResultProto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid);
    Task ResetRateLimitsAsync(string actionType = "conversation");
    Task ClearAllAsync();
    Task UpdateQuotaAsync(string productId, DateTime expiresDate);
    Task ResetQuotaAsync();
    Task AddCreditsAsync(int credits);
    Task<bool> RedeemInitialRewardAsync(string userId, DateTime dateTime);
    Task<UserQuotaState> GetUserQuotaStateAsync();
    Task<bool> ActivateFreeTrialAsync(int trialDays, PlanType planType, bool isUltimate);
    Task<FreeTrialInfoDto> GetFreeTrialInfoAsync();
}

public class UserQuotaGAgent : GAgentBase<UserQuotaState>, IUserQuotaGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public IOptionsMonitor<CreditsOptions>? CreditsOptions { get; set; }
    public IOptionsMonitor<RateLimitOptions>? RateLimiterOptions { get; set; }
    public ILocalizationService? LocalizationService { get; set; }
    
    // Injected for trial code management (Agent RPC)
    public IGAgentActorFactory? ActorFactory { get; set; }

    // Parameterless constructor required for Orleans activation
    public UserQuotaGAgent() : base()
    {
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        // New users should be eligible for invite rewards. Guard to avoid reactivations
        // overriding previously consumed/blocked state.
        if (State.CreatedAt == null)
        {
            RaiseEvent(new UpdateCanReceiveInviteRewardEvent
            {
                CanReceiveInviteReward = true,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await ConfirmEventsAsync();
        }
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

        var initialCredits = CreditsOptions?.CurrentValue.InitialCreditsAmount ?? 0;

        RaiseEvent(new InitializeCreditsEvent { InitialCredits = initialCredits });
        await ConfirmEventsAsync();

        Logger.LogDebug("[UserQuotaGAgent][InitializeCreditsAsync] User {UserId} received {Credits} initial credits.", Id, initialCredits);
        return true;
    }

    public async Task<CreditsInfoProto> GetCreditsAsync()
    {
        await InitializeCreditsAsync();
        var creditsInfoProto = new CreditsInfoProto
        {
            IsInitialized = State.HasInitialCredits,
            Credits = State.Credits,
            ShouldShowToast = State.HasShownInitialCreditsToast
        };
        if (State.HasInitialCredits && !State.HasShownInitialCreditsToast)
        {
            creditsInfoProto.ShouldShowToast = true;
        }
        else
        {
            creditsInfoProto.ShouldShowToast = false;
        }

        return creditsInfoProto;
    }

    public async Task SetShownCreditsToastAsync(SetShownCreditsToastRequestProto request)
    {
        RaiseEvent(new SetShownCreditsToastEvent { HasShownInitialCreditsToast = request.HasShownInitialCreditsToast });
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
        
        Logger.LogInformation("[UserQuotaGAgent][IsSubscribedAsync] UserId={UserId}, Ultimate={Ultimate}, IsActive={IsActive}, StartDate={StartDate}, EndDate={EndDate}, Now={Now}, Result={Result}",
            Id, ultimate, subscriptionInfo.IsActive, startDate, endDate, now, isSubscribed);

        if (subscriptionInfo.IsActive && endDate <= now)
        {
            Logger.LogDebug("[UserQuotaGAgent][IsSubscribedAsync] Subscription for user {UserId} expired. Ultimate: {Ultimate}", Id, ultimate);

            var subscriptionDto = new SubscriptionInfoDto
            {
                IsActive = false,
                PlanType = (PlanType)(int)subscriptionInfo.PlanType,
                Status = (PaymentStatus)(int)subscriptionInfo.Status,
                StartDate = startDate,
                EndDate = endDate,
                SubscriptionIds = subscriptionInfo.SubscriptionIds.ToList(),
                InvoiceIds = subscriptionInfo.InvoiceIds.ToList(),
                PlatformProductId = !string.IsNullOrWhiteSpace(subscriptionInfo!.PlatformProductId) ? subscriptionInfo.PlatformProductId : null
            };

            if (subscriptionInfo.HasPlatform)
            {
                subscriptionDto.Platform = subscriptionInfo.Platform;
            }  

            RaiseEvent(new UpdateSubscriptionEvent
            {
                SubscriptionInfo = MapToProtoSubscriptionFromDto(subscriptionDto),
                IsUltimate = ultimate
            });

            if (State.RateLimits.ContainsKey("conversation"))
            {
                RaiseEvent(new ClearRateLimitEvent { ActionType = "conversation" });
            }
            await ConfirmEventsAsync();
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

    public async Task<SubscriptionInfoDto> GetSubscriptionAsync(bool ultimate = false)
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

        return new SubscriptionInfoDto
        {
            IsActive = subscriptionInfo?.IsActive ?? false,
            PlanType = subscriptionInfo != null ? (PlanType)(int)subscriptionInfo.PlanType : PlanType.None,
            Status = subscriptionInfo != null ? (PaymentStatus)(int)subscriptionInfo.Status : PaymentStatus.None,
            StartDate = subscriptionInfo?.StartDate?.ToDateTime() ?? DateTime.MinValue,
            EndDate = subscriptionInfo?.EndDate?.ToDateTime() ?? DateTime.MinValue,
            SubscriptionIds = subscriptionInfo?.SubscriptionIds.ToList() ?? new List<string>(),
            InvoiceIds = subscriptionInfo?.InvoiceIds.ToList() ?? new List<string>(),
            PlatformProductId = !string.IsNullOrWhiteSpace(subscriptionInfo!.PlatformProductId) ? subscriptionInfo.PlatformProductId : null,
            Platform = subscriptionInfo.HasPlatform ? subscriptionInfo.Platform : null
        };
    }
    
    // RPC-compatible Protobuf method
    public async Task<SubscriptionInfoProto> GetSubscriptionProtoAsync(bool ultimate = false)
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

        return subscriptionInfo?.Clone() ?? new SubscriptionInfoProto();
    }

    public async Task<SubscriptionInfoDto> GetAndSetSubscriptionAsync(bool ultimate = false)
    {
        await IsSubscribedAsync(ultimate);
        return await GetSubscriptionAsync(ultimate);
    }
    
    // RPC-compatible Protobuf method
    public async Task<SubscriptionInfoProto> GetAndSetSubscriptionProtoAsync(bool ultimate = false)
    {
        await IsSubscribedAsync(ultimate);
        return await GetSubscriptionProtoAsync(ultimate);
    }

    public async Task UpdateSubscriptionAsync(SubscriptionInfoDto subscriptionInfoDto, bool ultimate = false)
    {
        Logger.LogInformation("[UserQuotaGAgent][UpdateSubscriptionAsync] Updated subscription for user {UserId}: Data={PlanType}", Id, JsonConvert.SerializeObject(subscriptionInfoDto));

        RaiseEvent(new UpdateSubscriptionEvent
        {
            SubscriptionInfo = MapToProtoSubscriptionFromDto(subscriptionInfoDto),
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

    public async Task<ExecuteActionResultProto> ExecuteActionAsync(string sessionId, string chatManagerGuid, ActionType actionType = ActionType.Conversation)
    {
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        Logger.LogDebug($"[ExecuteActionAsync] Language from sessionId:{sessionId} chatManagerGuid: {chatManagerGuid}, language:{language}");
        
        if (actionType == ActionType.ImageConversation)
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
                var localizedMessage = LocalizationService != null
                    ? LocalizationService.GetLocalizedException(ExceptionMessageKeys.DailyUpdateLimit, language)
                    : "Daily update limit exceeded";
                return new ExecuteActionResultProto
                {
                    Code = ExecuteActionStatus.RateLimitExceeded,
                    Message = localizedMessage
                };
            }

            RaiseEvent(new UpdateDailyImageConversationEvent { DailyImageConversation = dailyInfo });
            return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, ActionType.Conversation);
        }
        
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, actionType);
    }

    public async Task<ExecuteActionResultProto> ExecuteVoiceActionAsync(string sessionId, string chatManagerGuid)
    {
        return await ExecuteStandardActionAsync(sessionId, chatManagerGuid, ActionType.VoiceConversation);
    }

    public async Task<CanUploadImageResponseProto> CanUploadImageAsync()
    {
        if (await IsSubscribedAsync(true) || await IsSubscribedAsync(false))
        {
            return new CanUploadImageResponseProto { Success = true, CanUpload = true };
        }

        var today = DateTime.UtcNow.Date;
        var dailyInfo = State.DailyImageConversation;
        var lastTime = dailyInfo?.LastConversationTime?.ToDateTime() ?? DateTime.MinValue;

        if (lastTime.Date != today)
        {
            return new CanUploadImageResponseProto { Success = true, CanUpload = true };
        }

        if ((dailyInfo?.Count ?? 0) >= 1)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
            var localizedMessage = LocalizationService != null
                ? LocalizationService.GetLocalizedException(ExceptionMessageKeys.DailyUpdateLimit, language)
                : "Daily upload limit exceeded";
            return new CanUploadImageResponseProto
            {
                Success = false,
                Message = localizedMessage,
                CanUpload = false
            };
        }

        return new CanUploadImageResponseProto { Success = true, CanUpload = true };
    }

    private async Task<ExecuteActionResultProto> ExecuteStandardActionAsync(string sessionId, string chatManagerGuid, ActionType actionTypeEnum)
    {
        var now = DateTime.UtcNow;
        var isVoiceMessage = actionTypeEnum == ActionType.VoiceConversation;
        var actionType = actionTypeEnum.ToString().ToLowerInvariant();

        var isUltimate = await IsSubscribedAsync(true);
        if (isUltimate)
        {
            Logger.LogInformation("[UserQuotaGAgent][ExecuteStandardActionAsync] UserId={UserId} is Ultimate subscriber, skipping credits deduction", Id);
            return new ExecuteActionResultProto { Success = true };
        }

        var isSubscribed = await IsSubscribedAsync(false);
        var creditsPerConversation = CreditsOptions?.CurrentValue.CreditsPerConversation ?? -1;
        Logger.LogInformation("[UserQuotaGAgent][ExecuteStandardActionAsync] UserId={UserId}, IsSubscribed={IsSubscribed}, CreditsPerConversation={CreditsPerConversation}, CurrentCredits={CurrentCredits}",
            Id, isSubscribed, creditsPerConversation, State.Credits);
        var maxTokens = isSubscribed
            ? (isVoiceMessage ? (RateLimiterOptions?.CurrentValue.VoiceSubscribedUserMaxRequests ?? 0) : (RateLimiterOptions?.CurrentValue.SubscribedUserMaxRequests ?? 0))
            : (isVoiceMessage ? (RateLimiterOptions?.CurrentValue.VoiceUserMaxRequests ?? 0) : (RateLimiterOptions?.CurrentValue.UserMaxRequests ?? 0));
        var timeWindow = isSubscribed
            ? (isVoiceMessage ? (RateLimiterOptions?.CurrentValue.VoiceSubscribedUserTimeWindowSeconds ?? 0) : (RateLimiterOptions?.CurrentValue.SubscribedUserTimeWindowSeconds ?? 0))
            : (isVoiceMessage ? (RateLimiterOptions?.CurrentValue.VoiceUserTimeWindowSeconds ?? 0) : (RateLimiterOptions?.CurrentValue.UserTimeWindowSeconds ?? 0));

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
            var requiredCredits = CreditsOptions?.CurrentValue.CreditsPerConversation ?? 0;
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
            var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
            var localizedMessage = LocalizationService != null
                ? LocalizationService.GetLocalizedException(ExceptionMessageKeys.ChatRateLimit, language)
                : "Chat rate limit exceeded";
            var voiceLocalizedMessage = LocalizationService != null
                ? LocalizationService.GetLocalizedException(ExceptionMessageKeys.VoiceChatRateLimit, language)
                : "Voice chat rate limit exceeded";

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
                var deductAmount = CreditsOptions?.CurrentValue.CreditsPerConversation ?? 0;
                var newCredits = State.Credits - deductAmount;
                Logger.LogInformation("[UserQuotaGAgent][ExecuteStandardActionAsync] Deducting credits: UserId={UserId}, Before={Before}, Deduct={Deduct}, After={After}",
                    Id, State.Credits, deductAmount, newCredits);
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
        else
        {
            Logger.LogInformation("[UserQuotaGAgent][ExecuteStandardActionAsync] UserId={UserId} is subscribed, skipping credits deduction", Id);
        }

        var updatedRateLimitInfo = State.RateLimits[actionType];
        var newRateLimitInfo = new RateLimitInfoProto
        {
            Count = updatedRateLimitInfo.Count - 1,
            LastTime = updatedRateLimitInfo.LastTime
        };
        RaiseEvent(new UpdateRateLimitEvent { ActionType = actionType, RateLimitInfo = newRateLimitInfo });

        // CRITICAL: Persist credits deduction and rate limit update events
        await ConfirmEventsAsync();
        
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

        PlanType planType = DeterminePlanTypeFromProductId(productId);

        var subscriptionDto = new SubscriptionInfoDto
        {
            PlanType = planType,
            IsActive = true,
            StartDate = DateTime.UtcNow,
            EndDate = expiresDate,
            Status = PaymentStatus.Completed,
            SubscriptionIds = State.Subscription?.SubscriptionIds.ToList() ?? new List<string>(),
            InvoiceIds = State.Subscription?.InvoiceIds.ToList() ?? new List<string>(),
            PlatformProductId = !string.IsNullOrWhiteSpace(State.Subscription?.PlatformProductId)
                ? State.Subscription?.PlatformProductId
                : null,
            Platform = State.Subscription != null && State.Subscription.HasPlatform ? State.Subscription.Platform : null
        };

        RaiseEvent(new UpdateSubscriptionEvent { SubscriptionInfo = MapToProtoSubscriptionFromDto(subscriptionDto), IsUltimate = false });
        await ConfirmEventsAsync();
        await ResetRateLimitsAsync();
    }

    public async Task ResetQuotaAsync()
    {
        Logger.LogInformation("[UserQuotaGAgent][ResetQuotaAsync] Resetting quota for user {UserId}", Id);

        var subscriptionDto = new SubscriptionInfoDto
        {
            IsActive = false,
            PlanType = State.Subscription != null ? (PlanType)(int)State.Subscription.PlanType : PlanType.None,
            Status = PaymentStatus.None,
            StartDate = State.Subscription?.StartDate?.ToDateTime() ?? DateTime.MinValue,
            EndDate = State.Subscription?.EndDate?.ToDateTime() ?? DateTime.MinValue,
            SubscriptionIds = State.Subscription?.SubscriptionIds.ToList() ?? new List<string>(),
            InvoiceIds = State.Subscription?.InvoiceIds.ToList() ?? new List<string>(),
            PlatformProductId = !string.IsNullOrWhiteSpace(State.Subscription?.PlatformProductId)
                ? State.Subscription?.PlatformProductId
                : null,
            Platform = State.Subscription != null && State.Subscription.HasPlatform ? State.Subscription.Platform : null
        };

        RaiseEvent(new UpdateSubscriptionEvent { SubscriptionInfo = MapToProtoSubscriptionFromDto(subscriptionDto), IsUltimate = false });
        await ConfirmEventsAsync();
        await ResetRateLimitsAsync();
    }

    private PlanType DeterminePlanTypeFromProductId(string productId)
    {
        var lowerProductId = productId.ToLowerInvariant();
        if (lowerProductId.Contains("weekly") || lowerProductId.Contains("week"))
            return PlanType.Week;
        if (lowerProductId.Contains("monthly") || lowerProductId.Contains("month"))
            return PlanType.Month;
        if (lowerProductId.Contains("yearly") || lowerProductId.Contains("year") || lowerProductId.Contains("annual"))
            return PlanType.Year;
        if (lowerProductId.Contains("daily") || lowerProductId.Contains("day"))
            return PlanType.Day;
        return PlanType.Month;
    }

    public async Task<UpdateCreditsResponseProto> UpdateCreditsAsync(UpdateCreditsRequestProto request)
    {
        if (!IsUserAuthorizedToUpdateCredits(request.OperatorUserId))
        {
            return new UpdateCreditsResponseProto
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update credits",
                Data = State.Credits
            };
        }

        var newCredits = Math.Max(0, State.Credits + request.CreditsChange);
        RaiseEvent(new UpdateCreditsEvent { NewCredits = newCredits });
        await ConfirmEventsAsync();

        return new UpdateCreditsResponseProto
        {
            Success = true,
            Message = $"Credits successfully updated by {request.CreditsChange}",
            Data = State.Credits
        };
    }

    public async Task<UpdateSubscriptionResponseProto> UpdateSubscriptionAsync(UpdateSubscriptionRequestProto request)
    {
        var planType = (PlanType)request.PlanType;
        var ultimate = request.IsUltimate;
        
        if (!IsUserAuthorizedToUpdateCredits(request.OperatorUserId))
        {
            return new UpdateSubscriptionResponseProto
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update subscription"
            };
        }

        var oldSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        if (await IsSubscribedAsync(ultimate))
        {
            var subscriptionInfoDto = await GetSubscriptionAsync(ultimate);
            if (SubscriptionHelper.IsUpgradeOrSameLevel(subscriptionInfoDto.PlanType, planType))
            {
                subscriptionInfoDto.PlanType = planType;
            }
            subscriptionInfoDto.EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, subscriptionInfoDto.EndDate);
            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
        }
        else
        {
            var startDate = DateTime.UtcNow;
            var subscriptionInfoDto = new SubscriptionInfoDto
            {
                IsActive = true,
                PlanType = planType,
                Status = PaymentStatus.Completed,
                StartDate = startDate,
                EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, startDate),
                SubscriptionIds = null,
                InvoiceIds = null
            };
            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
        }

        if (ultimate && await IsSubscribedAsync(false))
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            premiumSubscription.StartDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.StartDate);
            premiumSubscription.EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.EndDate);
            await UpdateSubscriptionAsync(premiumSubscription, false);
        }
        await ConfirmEventsAsync();

        var currentSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        var result = new UpdateSubscriptionResponseProto
        {
            Success = true,
            Message = "Subscription updated successfully"
        };
        
        // Add both subscriptions to result
        var currentProto = MapToProtoSubscriptionFromDto(currentSubscriptionInfoDto);
        result.Data.Add(currentProto);
        
        if (ultimate)
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            var premiumProto = MapToProtoSubscriptionFromDto(premiumSubscription);
            result.Data.Add(premiumProto);
        }
        
        return result;
    }
    
    // Keep the old method for backward compatibility (internal use)
    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateSubscriptionAsync(string operatorUserId, PlanType planType, bool ultimate = false)
    {
        if (!IsUserAuthorizedToUpdateCredits(operatorUserId))
        {
            return new GrainResultDto<List<SubscriptionInfoDto>>
            {
                Success = false,
                Message = "Unauthorized: User does not have permission to update subscription",
                Data = new List<SubscriptionInfoDto>()
            };
        }

        var oldSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        if (await IsSubscribedAsync(ultimate))
        {
            var subscriptionInfoDto = await GetSubscriptionAsync(ultimate);
            if (SubscriptionHelper.IsUpgradeOrSameLevel(subscriptionInfoDto.PlanType, planType))
            {
                subscriptionInfoDto.PlanType = planType;
            }
            subscriptionInfoDto.EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, subscriptionInfoDto.EndDate);
            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
        }
        else
        {
            var startDate = DateTime.UtcNow;
            var subscriptionInfoDto = new SubscriptionInfoDto
            {
                IsActive = true,
                PlanType = planType,
                Status = PaymentStatus.Completed,
                StartDate = startDate,
                EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, startDate),
                SubscriptionIds = null,
                InvoiceIds = null
            };
            await UpdateSubscriptionAsync(subscriptionInfoDto, ultimate);
        }

        if (ultimate && await IsSubscribedAsync(false))
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            premiumSubscription.StartDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.StartDate);
            premiumSubscription.EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.EndDate);
            await UpdateSubscriptionAsync(premiumSubscription, false);
        }
        await ConfirmEventsAsync();

        var currentSubscriptionInfoDto = await GetSubscriptionAsync(ultimate);
        return new GrainResultDto<List<SubscriptionInfoDto>>
        {
            Data = new List<SubscriptionInfoDto> { oldSubscriptionInfoDto, currentSubscriptionInfoDto }
        };
    }

    private bool IsUserAuthorizedToUpdateCredits(string operatorUserId)
    {
        var authorizedUsers = CreditsOptions?.CurrentValue.OperatorUserId ?? new List<string>();
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
        await UpdateSubscriptionAsync(new SubscriptionInfoDto
        {
            IsActive = true,
            PlanType = PlanType.Week,
            Status = PaymentStatus.Completed,
            StartDate = DateTime.UtcNow,
            EndDate = SubscriptionHelper.GetSubscriptionEndDate(PlanType.Week, startDate),
            SubscriptionIds = null,
            InvoiceIds = null
        }, false);

        RaiseEvent(new UpdateCanReceiveInviteRewardEvent { CanReceiveInviteReward = false });
        await ConfirmEventsAsync();
        return true;
    }

    public Task<UserQuotaState> GetUserQuotaStateAsync()
    {
        return Task.FromResult(State);
    }

    public async Task<bool> ActivateFreeTrialAsync(int trialDays, PlanType planType, bool isUltimate)
    {
        var startDate = DateTime.UtcNow;
        var endDate = startDate.AddDays(trialDays);

        RaiseEvent(new ActivateFreeTrialEvent
        {
            TrialDays = trialDays,
            PlanType = (QuotaPlanType)(int)planType,
            IsUltimate = isUltimate,
            StartDate = Timestamp.FromDateTime(startDate),
            EndDate = Timestamp.FromDateTime(endDate)
        });

        await ConfirmEventsAsync();
        Logger.LogInformation("Free trial activated for user {UserId}. Days: {TrialDays}, PlanType: {PlanType}, IsUltimate: {IsUltimate}", Id, trialDays, planType, isUltimate);
        return true;
    }

    public Task<FreeTrialInfoDto> GetFreeTrialInfoAsync()
    {
        if (State.FreeTrialInfo == null)
        {
            return Task.FromResult(new FreeTrialInfoDto());
        }

        return Task.FromResult(new FreeTrialInfoDto
        {
            FreeTrialCode = State.FreeTrialInfo.FreeTrialCode,
            TrialDays = State.FreeTrialInfo.TrialDays,
            PlanType = State.FreeTrialInfo.PlanType,  // Already QuotaPlanType
            IsUltimate = State.FreeTrialInfo.IsUltimate,
            TransactionId = State.FreeTrialInfo.TransactionId
        });
    }

    #region Payment Event Handlers

    /// <summary>
    /// Handle payment completed event - update user subscription and quota
    /// </summary>
    [EventHandler]
    public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
    {
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] === EVENT RECEIVED === " +
            "AgentId={AgentId}, TransactionId={TransactionId}, BusinessType={BusinessType}",
            Id, evt.TransactionId, evt.Context?.BusinessType);
        
        // Only process events for godgpt business type
        if (evt.Context?.BusinessType != "godgpt")
        {
            Logger.LogDebug(
                "[UserQuotaGAgent][HandlePaymentCompleted] Skipping non-godgpt event. BusinessType={BusinessType}",
                evt.Context?.BusinessType);
            return;
        }

        var userId = Guid.Parse(evt.Context.UserId);
        // Extract actual user GUID from Agent Id (format: "UserQuotaGAgent:guid")
        var agentUserId = Id.Contains(':') ? Id.Split(':').Last() : Id;
        if (userId.ToString() != agentUserId)
        {
            Logger.LogWarning(
                "[UserQuotaGAgent][HandlePaymentCompleted] UserId mismatch. Event UserId: {EventUserId}, Agent UserId: {AgentUserId}",
                evt.Context.UserId, agentUserId);
            return;
        }

        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Processing payment for user {UserId}, TransactionId: {TransactionId}, IsRenewal: {IsRenewal}",
            userId, evt.TransactionId, evt.IsRenewal);

        // Extract product information from business metadata
        var metadataDict = evt.Context.BusinessMetadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) 
            ?? new Dictionary<string, string>();
        
        // Debug: log raw metadata to trace plan_type mapping issues
        var rawPlanType = metadataDict.TryGetValue("plan_type", out var pt) ? pt : "not_found";
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Raw metadata: plan_type={RawPlanType}, MetadataKeys={Keys}",
            rawPlanType, string.Join(",", metadataDict.Keys));
        
        var planType = GetPlanTypeFromMetadata(metadataDict);
        var isUltimate = GetIsUltimateFromMetadata(metadataDict);
        var trialDays = GetTrialDaysFromMetadata(metadataDict);

        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Extracted plan info: PlanType={PlanType}, IsUltimate={IsUltimate}, TrialDays={TrialDays}",
            planType, isUltimate, trialDays);

        // Get current subscription
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Getting current subscription for IsUltimate={IsUltimate}",
            isUltimate);
        var subscriptionInfo = await GetSubscriptionAsync(isUltimate);
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Got subscription: IsActive={IsActive}, PlanType={PlanType}, SubscriptionIds.Count={Count}",
            subscriptionInfo.IsActive, subscriptionInfo.PlanType, subscriptionInfo.SubscriptionIds?.Count ?? 0);
        var subscriptionIds = subscriptionInfo.SubscriptionIds ?? new List<string>();
        var invoiceIds = subscriptionInfo.InvoiceIds ?? new List<string>();

        // Handle subscription IDs based on subscription state
        // New subscription: replace mode (clear old IDs) - same as old code
        // Renewal: accumulate mode (keep existing IDs)
        if (!subscriptionInfo.IsActive)
        {
            // New subscription: clear and add new (replace mode)
            subscriptionIds.Clear();
            if (!string.IsNullOrEmpty(evt.Context.SubscriptionId))
            {
                subscriptionIds.Add(evt.Context.SubscriptionId);
            }
        }
        else
        {
            // Renewal: accumulate mode (add if not exists)
            if (!string.IsNullOrEmpty(evt.Context.SubscriptionId) && !subscriptionIds.Contains(evt.Context.SubscriptionId))
            {
                subscriptionIds.Add(evt.Context.SubscriptionId);
            }
        }

        // Add invoice ID (always accumulate)
        if (!string.IsNullOrEmpty(evt.InvoiceId) && !invoiceIds.Contains(evt.InvoiceId))
        {
            invoiceIds.Add(evt.InvoiceId);
        }

        // Calculate subscription end date
        // Logic from old code:
        // - If evt.PeriodEnd is provided (e.g., from Stripe renewal), use it directly
        // - Otherwise, if subscription is active, extend from current EndDate (cumulative)
        // - Otherwise, start from current time (new subscription)
        DateTime periodEnd;
        if (evt.PeriodEnd != null)
        {
            periodEnd = evt.PeriodEnd.ToDateTime();
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCompleted] Using PeriodEnd from event: {PeriodEnd}",
                periodEnd);
        }
        else
        {
            // Cumulative logic from old code: extend from current EndDate if active
            var startDate = subscriptionInfo.IsActive && subscriptionInfo.EndDate > DateTime.UtcNow
                ? subscriptionInfo.EndDate
                : DateTime.UtcNow;
            periodEnd = SubscriptionHelper.GetSubscriptionEndDate(planType, startDate);
            if (trialDays > 0)
            {
                periodEnd = periodEnd.AddDays(trialDays);
            }
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCompleted] Calculated PeriodEnd (no event PeriodEnd): " +
                "SubscriptionIsActive={IsActive}, CurrentEndDate={CurrentEndDate}, StartDate={StartDate}, PlanType={PlanType}, TrialDays={TrialDays}, PeriodEnd={PeriodEnd}",
                subscriptionInfo.IsActive, subscriptionInfo.EndDate, startDate, planType, trialDays, periodEnd);
        }

        // Update subscription
        // Convert PlanType to QuotaPlanType for comparison
        var quotaPlanType = planType.ToQuotaPlanType();
        if (subscriptionInfo.IsActive)
        {
            // Existing subscription - update plan type if upgrade, extend end date
            var currentQuotaPlanType = subscriptionInfo.PlanType.ToQuotaPlanType();
            if (SubscriptionHelper.GetPlanTypeLogicalOrder(currentQuotaPlanType) <= 
                SubscriptionHelper.GetPlanTypeLogicalOrder(quotaPlanType))
            {
                subscriptionInfo.PlanType = planType;
            }
            subscriptionInfo.EndDate = periodEnd;
        }
        else
        {
            // New subscription
            subscriptionInfo.IsActive = true;
            subscriptionInfo.PlanType = planType;
            subscriptionInfo.StartDate = evt.PeriodStart?.ToDateTime() ?? DateTime.UtcNow;
            subscriptionInfo.EndDate = periodEnd;

            // Reset rate limits for new subscription
            if (!evt.IsRenewal)
            {
                await ResetRateLimitsAsync("conversation");
            }
        }

        subscriptionInfo.Status = PaymentStatus.Completed;
        subscriptionInfo.SubscriptionIds = subscriptionIds;
        subscriptionInfo.InvoiceIds = invoiceIds;
        if (!string.IsNullOrWhiteSpace(evt.Context.ProductId))
        {
            subscriptionInfo.PlatformProductId = evt.Context.ProductId;
        }
        subscriptionInfo.Platform = evt.Context.Platform;

        await UpdateSubscriptionAsync(subscriptionInfo, isUltimate);

        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Updated subscription for user {UserId}, PlanType: {PlanType}, IsUltimate: {IsUltimate}, IsActive: {IsActive}, StartDate: {StartDate}, EndDate: {EndDate}, SubscriptionIds: [{SubscriptionIds}]",
            userId, planType, isUltimate, subscriptionInfo.IsActive, subscriptionInfo.StartDate, periodEnd, string.Join(", ", subscriptionIds));

        // Verify subscription was saved correctly
        var verifySubscription = await GetSubscriptionAsync(isUltimate);
        var isSubscribed = await IsSubscribedAsync(isUltimate);
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCompleted] Verification - IsSubscribed: {IsSubscribed}, VerifyIsActive: {VerifyIsActive}, VerifyStartDate: {VerifyStartDate}, VerifyEndDate: {VerifyEndDate}",
            isSubscribed, verifySubscription.IsActive, verifySubscription.StartDate, verifySubscription.EndDate);

        // ========== Payment Analytics (from old code) ==========
        // Record payment success to OpenTelemetry metrics
        var purchaseType = evt.IsRenewal ? "renewal" : "new_subscription";
        var platformStr = evt.Context.Platform switch
        {
            1 => "Stripe",
            2 => "AppStore",
            3 => "GooglePlay",
            _ => "Unknown"
        };
        PaymentTelemetryMetrics.RecordPaymentSuccess(
            platformStr,
            purchaseType,
            userId.ToString(),
            evt.Context.ProductId ?? string.Empty,
            Logger);

        // ========== Ultimate/Premium Sync Logic (from old code) ==========
        // When purchasing Ultimate, extend Premium subscription time if active
        // Because Ultimate users also get Premium benefits, the Premium time should be extended
        if (isUltimate)
        {
            var premiumSubscription = await GetSubscriptionAsync(false);
            if (premiumSubscription.IsActive)
            {
                premiumSubscription.StartDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.StartDate);
                premiumSubscription.EndDate = SubscriptionHelper.GetSubscriptionEndDate(planType, premiumSubscription.EndDate);
                await UpdateSubscriptionAsync(premiumSubscription, false);
                
                Logger.LogInformation(
                    "[UserQuotaGAgent][HandlePaymentCompleted] Extended Premium subscription for Ultimate user {UserId}, NewEndDate: {EndDate}",
                    userId, premiumSubscription.EndDate);
            }
        }

        // ========== Side Effects: Mark trial code as used ==========
        // NOTE: Subscription cancellation is handled in HttpApi layer (GodGPTPaymentBusinessService)
        // because it requires Stripe API which is not available in Silo.
        
        var trialCode = GetTrialCodeFromMetadata(metadataDict);
        if (!string.IsNullOrEmpty(trialCode))
        {
            // Fire-and-forget: Don't block the main flow
            _ = Task.Run(async () =>
            {
                try
                {
                    await MarkTrialCodeUsedAsync(trialCode, userId, evt.TransactionId);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex,
                        "[UserQuotaGAgent][HandlePaymentCompleted] Failed to mark trial code {TrialCode} as used",
                        trialCode);
                }
            });
        }
        
        // ========== Inviter Reward Processing (from old code: ProcessInviteeSubscriptionAsync) ==========
        // Find inviter and notify them about invitee's payment for reward processing
        _ = Task.Run(async () =>
        {
            try
            {
                await ProcessInviterRewardAsync(userId, planType, isUltimate, evt.InvoiceId ?? evt.TransactionId);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex,
                    "[UserQuotaGAgent][HandlePaymentCompleted] Failed to process inviter reward for user {UserId}",
                    userId);
            }
        });
    }

    /// <summary>
    /// Handle payment cancelled event - update user subscription status
    /// Only processes: refund (rollback EndDate) and grace_period_expired (immediate revocation)
    /// EXPIRED/Cancel events are NOT sent here - user membership expires naturally via EndDate
    /// </summary>
    [EventHandler]
    public async Task HandlePaymentCancelled(PaymentCancelledEvent evt)
    {
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCancelled] === EVENT RECEIVED === " +
            "AgentId={AgentId}, PaymentId={PaymentId}, SubscriptionId={SubscriptionId}, BusinessType={BusinessType}, Reason={Reason}",
            Id, evt.Context?.PaymentId, evt.Context?.SubscriptionId, evt.Context?.BusinessType, evt.Reason);
        
        // Only process events for godgpt business type
        if (evt.Context?.BusinessType != "godgpt")
        {
            Logger.LogDebug(
                "[UserQuotaGAgent][HandlePaymentCancelled] Skipping non-godgpt event. BusinessType={BusinessType}",
                evt.Context?.BusinessType);
            return;
        }
        
        // Only process "refund" and "grace_period_expired" events
        // PaymentService only sends these two types now
        if (evt.Reason != "refund" && evt.Reason != "grace_period_expired")
        {
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCancelled] Skipping unknown Reason={Reason}",
                evt.Reason);
            return;
        }

        var userId = Guid.Parse(evt.Context.UserId);
        // Extract actual user GUID from Agent Id (format: "UserQuotaGAgent:guid")
        var agentUserId = Id.Contains(':') ? Id.Split(':').Last() : Id;
        if (userId.ToString() != agentUserId)
        {
            Logger.LogWarning(
                "[UserQuotaGAgent][HandlePaymentCancelled] UserId mismatch. Event UserId: {EventUserId}, Agent UserId: {AgentUserId}",
                evt.Context.UserId, agentUserId);
            return;
        }

        var subId = evt.Context.SubscriptionId;
        
        // SubscriptionId is required
        if (string.IsNullOrEmpty(subId))
        {
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCancelled] Skipping - Empty SubscriptionId for user {UserId}",
                userId);
            return;
        }

        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCancelled] Processing {Reason} for user {UserId}, SubscriptionId: {SubscriptionId}",
            evt.Reason, userId, subId);

        // Find which subscription contains this SubscriptionId
        bool? isUltimate = null;
        
        // Check Premium subscription
        var premiumSubscription = await GetSubscriptionAsync(false);
        if (premiumSubscription?.SubscriptionIds?.Contains(subId) == true)
        {
            isUltimate = false;
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCancelled] Found SubscriptionId {SubscriptionId} in Premium subscription",
                subId);
        }
        
        // Check Ultimate subscription
        if (!isUltimate.HasValue)
        {
            var ultimateSubscription = await GetSubscriptionAsync(true);
            if (ultimateSubscription?.SubscriptionIds?.Contains(subId) == true)
            {
                isUltimate = true;
                Logger.LogInformation(
                    "[UserQuotaGAgent][HandlePaymentCancelled] Found SubscriptionId {SubscriptionId} in Ultimate subscription",
                    subId);
            }
        }
        
        // SubscriptionId not found in any subscription list - already processed or invalid
        if (!isUltimate.HasValue)
        {
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCancelled] Skipping - SubscriptionId {SubscriptionId} not found in any subscription list for user {UserId}",
                subId, userId);
            return;
        }

        // Calculate rollback days for refunds
        int rollbackDays = 0;
        if (evt.Reason == "refund")
        {
            var metadataDict = evt.Context.BusinessMetadata?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value) 
                ?? new Dictionary<string, string>();
            var planType = GetPlanTypeFromMetadata(metadataDict);
            var quotaPlanType = (QuotaPlanType)(int)planType;
            rollbackDays = SubscriptionHelper.GetDaysForPlanType(quotaPlanType);
            
            Logger.LogInformation(
                "[UserQuotaGAgent][HandlePaymentCancelled] Refund - PlanType={PlanType}, RollbackDays={RollbackDays}",
                planType, rollbackDays);
        }
        
        Logger.LogInformation(
            "[UserQuotaGAgent][HandlePaymentCancelled] Processing {Reason} for {SubscriptionType} subscription, UserId={UserId}, SubscriptionId={SubscriptionId}, RollbackDays={RollbackDays}",
            evt.Reason, isUltimate.Value ? "Ultimate" : "Premium", userId, subId, rollbackDays);
        
        RaiseEvent(new CancelSubscriptionEvent 
        { 
            IsUltimate = isUltimate.Value, 
            SubscriptionId = subId,
            Reason = evt.Reason,
            RollbackDays = rollbackDays
        });
        await ConfirmEventsAsync();
    }
    
    /// <summary>
    /// Process inviter reward when invitee pays (same as old code ProcessInviteeSubscriptionAsync)
    /// </summary>
    private async Task ProcessInviterRewardAsync(Guid inviteeUserId, PlanType planType, bool isUltimate, string invoiceId)
    {
        try
        {
            // Get invitee's UserInvitationGAgent to find their inviter
            var userInvitationActor = await ActorFactory.CreateGAgentActorAsync<UserInvitationGAgent>(inviteeUserId.ToString());
            var userInvitationAgent = userInvitationActor.As<IUserInvitationGAgent>();
            var inviterId = await userInvitationAgent.GetInviterAsync();
            
            if (inviterId == null || inviterId == Guid.Empty)
            {
                Logger.LogDebug(
                    "[UserQuotaGAgent][ProcessInviterRewardAsync] User {UserId} has no inviter, skipping reward",
                    inviteeUserId);
                return;
            }
            
            Logger.LogInformation(
                "[UserQuotaGAgent][ProcessInviterRewardAsync] Processing inviter reward. Invitee: {InviteeId}, Inviter: {InviterId}, PlanType: {PlanType}, IsUltimate: {IsUltimate}",
                inviteeUserId, inviterId, planType, isUltimate);
            
            // Call inviter's InvitationGAgent to process the reward
            var inviterActor = await ActorFactory.CreateGAgentActorAsync<InvitationGAgent>(inviterId.Value.ToString());
            var inviterAgent = inviterActor.As<IInvitationGAgent>();
            await inviterAgent.ProcessInviteeSubscriptionAsync(inviteeUserId.ToString(), (int)planType, isUltimate, invoiceId);
            
            Logger.LogInformation(
                "[UserQuotaGAgent][ProcessInviterRewardAsync] Successfully processed inviter reward. Inviter: {InviterId}",
                inviterId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[UserQuotaGAgent][ProcessInviterRewardAsync] Error processing inviter reward for invitee {InviteeId}",
                inviteeUserId);
        }
    }

    private PlanType GetPlanTypeFromMetadata(Dictionary<string, string> metadata)
    {
        if (metadata == null) return PlanType.Month;

        if (metadata.TryGetValue("plan_type", out var planTypeStr) && 
            int.TryParse(planTypeStr, out var planTypeInt) &&
            planTypeInt > 0 && planTypeInt <= 4)
        {
            return (PlanType)planTypeInt;
        }

        if (metadata.TryGetValue("originalPlanType", out var originalPlanTypeStr) && 
            int.TryParse(originalPlanTypeStr, out var originalPlanTypeInt) &&
            originalPlanTypeInt > 0 && originalPlanTypeInt <= 4)
        {
            return (PlanType)originalPlanTypeInt;
        }

        return PlanType.Month; // Default to Monthly
    }

    private bool GetIsUltimateFromMetadata(Dictionary<string, string> metadata)
    {
        if (metadata == null) return false;

        if (metadata.TryGetValue("is_ultimate", out var isUltimateStr))
        {
            return bool.TryParse(isUltimateStr, out var result) && result;
        }

        if (metadata.TryGetValue("isUltimate", out var isUltimateStr2))
        {
            return bool.TryParse(isUltimateStr2, out var result2) && result2;
        }

        return false;
    }

    private int GetTrialDaysFromMetadata(Dictionary<string, string> metadata)
    {
        if (metadata == null) return 0;

        if (metadata.TryGetValue("trial_days", out var trialDaysStr) && 
            int.TryParse(trialDaysStr, out var trialDays))
        {
            return trialDays;
        }

        return 0;
    }

    private string? GetTrialCodeFromMetadata(Dictionary<string, string>? metadata)
    {
        if (metadata == null) return null;

        if (metadata.TryGetValue("trial_code", out var trialCode))
        {
            return trialCode;
        }

        if (metadata.TryGetValue("trialCode", out var trialCode2))
        {
            return trialCode2;
        }

        return null;
    }

    #endregion

    #region Subscription Management

    /// <summary>
    /// Mark trial code as used when payment is completed
    /// </summary>
    private async Task MarkTrialCodeUsedAsync(string trialCode, Guid userId, string transactionId)
    {
        try
        {
            Logger.LogInformation(
                "[UserQuotaGAgent][MarkTrialCodeUsedAsync] Marking trial code {TrialCode} as used for user {UserId}",
                trialCode, userId);

            if (ActorFactory == null)
            {
                Logger.LogWarning("[UserQuotaGAgent][MarkTrialCodeUsedAsync] ActorFactory not injected");
                return;
            }

            // Parse code info to get batch ID
            var batchId = InvitationCodeHelper.ParseBatchTimestampFromCode(trialCode);
            
            // Mark in FreeTrialCodeFactoryGAgent
            var factoryAgentId = CommonHelper.GetFreeTrialCodeFactoryGAgentId(batchId);
            var factoryActor = await ActorFactory.CreateGAgentActorAsync<FreeTrialCodeFactoryGAgent>(factoryAgentId.ToString());
            var factoryAgent = factoryActor.As<IFreeTrialCodeFactoryGAgent>();
            
            await factoryAgent.MarkCodeAsUsedAsync(new MarkCodeUsedRequestProto
            {
                Code = trialCode,
                UserId = userId.ToString()
            });

            // Mark in InviteCodeGAgent
            var inviteCodeGuid = CommonHelper.StringToGuid(trialCode);
            var inviteCodeActor = await ActorFactory.CreateGAgentActorAsync<InviteCodeGAgent>(inviteCodeGuid.ToString());
            var inviteCodeAgent = inviteCodeActor.As<IInviteCodeGAgent>();
            await inviteCodeAgent.MarkCodeAsUsedAsync();

            Logger.LogInformation(
                "[UserQuotaGAgent][MarkTrialCodeUsedAsync] Successfully marked trial code {TrialCode} as used",
                trialCode);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[UserQuotaGAgent][MarkTrialCodeUsedAsync] Failed to mark trial code {TrialCode} as used",
                trialCode);
        }
    }

    // NOTE: Subscription cancellation logic has been moved to HttpApi layer
    // (GodGPTPaymentBusinessService) because it requires Stripe API which is
    // not available in Silo. Agent only handles state management.

    #endregion

    #region Helper Methods
    
    private static SubscriptionInfoProto MapToProtoSubscriptionFromDto(SubscriptionInfoDto sub)
    {
        if (sub == null) return new SubscriptionInfoProto();
        var result = new SubscriptionInfoProto
        {
            IsActive = sub.IsActive,
            PlanType = (QuotaPlanType)(int)sub.PlanType,
            Status = (QuotaPaymentStatus)(int)sub.Status,
            StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(sub.StartDate, DateTimeKind.Utc)),
            EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(sub.EndDate, DateTimeKind.Utc)),
        };
        if (!string.IsNullOrWhiteSpace(sub.PlatformProductId)) result.PlatformProductId = sub.PlatformProductId;
        if (sub.Platform.HasValue) result.Platform = sub.Platform.Value;
        if (sub.SubscriptionIds != null) result.SubscriptionIds.AddRange(sub.SubscriptionIds);
        if (sub.InvoiceIds != null) result.InvoiceIds.AddRange(sub.InvoiceIds);
        return result;
    }
    
    #endregion

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
                if (!string.IsNullOrWhiteSpace(updateSubscription.SubscriptionInfo.PlatformProductId))
                {
                    subscription.PlatformProductId = updateSubscription.SubscriptionInfo.PlatformProductId;
                }

                if (updateSubscription.SubscriptionInfo.HasPlatform)
                {
                    subscription.Platform = updateSubscription.SubscriptionInfo.Platform;
                }
                
                break;

            case CancelSubscriptionEvent cancelSubscription:
                var sub = cancelSubscription.IsUltimate ? state.UltimateSubscription : state.Subscription;
                if (sub != null)
                {
                    // Always remove SubscriptionId from list (both refund and grace_period_expired)
                    if (!string.IsNullOrEmpty(cancelSubscription.SubscriptionId))
                    {
                        sub.SubscriptionIds.Remove(cancelSubscription.SubscriptionId);
                    }
                    
                    // Handle based on Reason:
                    // - refund: Rollback EndDate (user got money back), revoke if EndDate is now in past
                    // - grace_period_expired: Immediate revocation (user failed to pay)
                    if (cancelSubscription.Reason == "refund")
                    {
                        // Refund: rollback EndDate by specified days
                        if (cancelSubscription.RollbackDays > 0 && sub.EndDate != null)
                        {
                            var currentEndDate = sub.EndDate.ToDateTime();
                            var newEndDate = currentEndDate.AddDays(-cancelSubscription.RollbackDays);
                            sub.EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(newEndDate, DateTimeKind.Utc));
                        }
                        
                        // If EndDate is now in the past after rollback, revoke immediately
                        var endDate = sub.EndDate?.ToDateTime() ?? DateTime.MinValue;
                        if (endDate <= DateTime.UtcNow)
                        {
                            sub.IsActive = false;
                            sub.PlanType = QuotaPlanType.None;
                            sub.Status = QuotaPaymentStatus.None;
                        }
                    }
                    else if (cancelSubscription.Reason == "grace_period_expired")
                    {
                        // Grace period expired: immediate revocation
                        // User failed to pay during grace period, revoke access now
                        sub.IsActive = false;
                        sub.PlanType = QuotaPlanType.None;
                        sub.Status = QuotaPaymentStatus.None;
                    }
                }
                break;

            case UpdateCreditsEvent updateCredits:
                state.Credits = updateCredits.NewCredits;
                break;

            case UpdateCanReceiveInviteRewardEvent updateCanReceiveInviteReward:
                state.CanReceiveInviteReward = updateCanReceiveInviteReward.CanReceiveInviteReward;
                if (updateCanReceiveInviteReward.CreatedAt != null)
                {
                    state.CreatedAt = updateCanReceiveInviteReward.CreatedAt;
                }
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
