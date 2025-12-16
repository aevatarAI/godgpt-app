using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Common.Constants;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Aevatar.Application.Grains.UserFeedback.Options;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

using CsFeedbackReasonEnum = Aevatar.Application.Grains.Common.Constants.FeedbackReasonEnum;
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using ProtoUserFeedbackInfo = Aevatar.Agents.GodGPT.Protos.UserFeedback.UserFeedbackInfo;

namespace Aevatar.Application.Grains.UserFeedback;

/// <summary>
/// User Feedback Agent interface - manages user feedback collection and frequency control.
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IUserFeedbackGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Submit user feedback (uses Protobuf type for RPC)
    /// </summary>
    Task<SubmitFeedbackResultProto> SubmitFeedbackAsync(SubmitFeedbackRequestProto request);
    
    /// <summary>
    /// Check if user is eligible to submit feedback (uses Protobuf type for RPC)
    /// </summary>
    Task<CheckEligibilityResultProto> CheckFeedbackEligibilityAsync();
    
    /// <summary>
    /// Get feedback history with pagination (uses Protobuf type for RPC)
    /// </summary>
    Task<GetFeedbackHistoryResultProto> GetFeedbackHistoryAsync(GetFeedbackHistoryRequestProto request);
}

[GAgent(nameof(UserFeedbackGAgent))]
public class UserFeedbackGAgent : GAgentBase<UserFeedbackState>, IUserFeedbackGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public ILocalizationService? LocalizationService { get; set; }
    public IOptionsMonitor<UserFeedbackOptions>? FeedbackOptions { get; set; }

    // Parameterless constructor required for Orleans activation
    public UserFeedbackGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User feedback management and collection");
    }

    public async Task<SubmitFeedbackResultProto> SubmitFeedbackAsync(SubmitFeedbackRequestProto request)
    {
        try
        {
            Logger.LogDebug("[UserFeedbackGAgent][SubmitFeedbackAsync] Start - UserId: {UserId}, FeedbackType: {FeedbackType}",
                request.UserId, request.FeedbackType);
            
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            
            // Validate request
            var validationResult = ValidateSubmitRequest(request, language);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning("[UserFeedbackGAgent][SubmitFeedbackAsync] Validation failed for user {UserId}: {ErrorCode} - {Message}",
                    request.UserId, validationResult.ErrorCode, validationResult.Message);
                    
                return new SubmitFeedbackResultProto
                {
                    Success = false,
                    Message = validationResult.Message,
                    ErrorCode = validationResult.ErrorCode
                };
            }

            if (request.SkippedFeedback)
            {
                Logger.LogDebug("[UserFeedbackGAgent][SubmitFeedbackAsync] User {UserId} skipped feedback", request.UserId);
                
                RaiseEvent(new SkippedFeedbackEvent
                {
                    SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                await ConfirmEventsAsync();
                
                return new SubmitFeedbackResultProto
                {
                    Success = true,
                    Message = string.Empty
                };
            }

            // Check frequency limit
            var eligibilityResult = await CheckFeedbackEligibilityAsync();
            if (!eligibilityResult.Eligible)
            {
                Logger.LogWarning("[UserFeedbackGAgent][SubmitFeedbackAsync] Frequency limit exceeded for user {UserId}. Last feedback: {LastFeedbackTime}",
                    request.UserId, eligibilityResult.LastFeedbackTime?.ToDateTime());
                    
                return new SubmitFeedbackResultProto
                {
                    Success = false,
                    Message = eligibilityResult.Message,
                    ErrorCode = "FREQUENCY_LIMIT_EXCEEDED"
                };
            }

            // Convert reasons from int to enum for helper method
            var reasonEnums = request.Reasons.Select(r => (CsFeedbackReasonEnum)r).ToList();
            
            // Generate English reason texts
            var englishReasonTexts = LocalizationService != null 
                ? FeedbackReasonHelper.GetEnglishReasonTexts(reasonEnums, LocalizationService)
                : new List<string>();

            // Create feedback info (Protobuf)
            var feedbackInfo = new ProtoUserFeedbackInfo
            {
                FeedbackId = Guid.NewGuid().ToString(),
                FeedbackType = request.FeedbackType,
                Response = request.Response?.Trim() ?? string.Empty,
                ContactRequested = request.ContactRequested,
                Email = request.Email?.Trim() ?? string.Empty,
                SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            // Map reasons
            foreach (var reasonValue in request.Reasons)
            {
                feedbackInfo.Reasons.Add((FeedbackReason)reasonValue);
            }
            
            // Map reason texts
            feedbackInfo.ReasonTextsEnglish.AddRange(englishReasonTexts);

            // Map subscription if present
            if (request.Subscription != null)
            {
                feedbackInfo.Subscription = new UserSubscriptionInfo
                {
                    PlanType = (FeedbackPlanType)request.Subscription.PlanType,
                    IsUltimate = request.Subscription.IsUltimate,
                    StartDate = request.Subscription.StartDate,
                    EndDate = request.Subscription.EndDate
                };
            }

            // Raise event to update state
            RaiseEvent(new SubmitFeedbackEvent
            {
                UserId = request.UserId,
                FeedbackInfo = feedbackInfo,
                SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                FeedbackCount = State.FeedbackCount + 1
            });

            await ConfirmEventsAsync();

            Logger.LogInformation("[UserFeedbackGAgent][SubmitFeedbackAsync] User feedback submitted successfully. UserId: {UserId}, FeedbackId: {FeedbackId}, Type: {Type}",
                request.UserId, feedbackInfo.FeedbackId, request.FeedbackType);

            return new SubmitFeedbackResultProto
            {
                Success = true,
                Message = string.Empty
            };
        }
        catch (Exception ex)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            Logger.LogError(ex, "[UserFeedbackGAgent][SubmitFeedbackAsync] Error submitting feedback for user: {UserId}", request.UserId);
            
            var errorMessage = LocalizationService != null
                ? LocalizationService.GetLocalizedMessage("feedback_submission_failed", language)
                : "Feedback submission failed";
            
            return new SubmitFeedbackResultProto
            {
                Success = false,
                Message = errorMessage,
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    public Task<CheckEligibilityResultProto> CheckFeedbackEligibilityAsync()
    {
        Logger.LogDebug("[UserFeedbackGAgent][CheckFeedbackEligibilityAsync] Checking eligibility for user {UserId}", Id);
            
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // If no previous feedback, user is eligible
        if (State.LastFeedbackTime == null)
        {
            return Task.FromResult(new CheckEligibilityResultProto
            {
                Eligible = true,
                Message = string.Empty
            });
        }

        var lastFeedbackTime = State.LastFeedbackTime.ToDateTime();
        var timeSinceLastFeedback = DateTime.UtcNow - lastFeedbackTime;
        var frequencyDays = FeedbackOptions?.CurrentValue.FeedbackFrequencyDays ?? 30;
        var isEligible = timeSinceLastFeedback.TotalDays >= frequencyDays;
        
        if (isEligible)
        {
            return Task.FromResult(new CheckEligibilityResultProto
            {
                Eligible = true,
                LastFeedbackTime = Timestamp.FromDateTime(lastFeedbackTime),
                Message = string.Empty
            });
        }

        var nextEligibleTime = lastFeedbackTime.AddDays(frequencyDays);
        var daysRemaining = (int)Math.Ceiling((nextEligibleTime - DateTime.UtcNow).TotalDays);
        
        var message = LocalizationService != null
            ? LocalizationService.GetLocalizedMessage("feedback_frequency_limit", language, 
                new Dictionary<string, string> { { "days", daysRemaining.ToString() } })
            : $"Please wait {daysRemaining} days before submitting feedback again";
        
        return Task.FromResult(new CheckEligibilityResultProto
        {
            Eligible = false,
            LastFeedbackTime = Timestamp.FromDateTime(lastFeedbackTime),
            NextEligibleTime = Timestamp.FromDateTime(nextEligibleTime),
            Message = message
        });
    }

    public Task<GetFeedbackHistoryResultProto> GetFeedbackHistoryAsync(GetFeedbackHistoryRequestProto request)
    {
        Logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Getting feedback history for user {UserId}. PageSize: {PageSize}, PageIndex: {PageIndex}",
            Id, request.PageSize, request.PageIndex);
            
        var allFeedbacks = new List<FeedbackHistoryItemProto>();
        
        // Add current feedback if exists
        if (State.CurrentFeedback != null)
        {
            allFeedbacks.Add(ConvertToHistoryItemProto(State.CurrentFeedback));
        }
        
        // Add archived feedbacks (stored as JSON strings for compatibility)
        foreach (var archivedJson in State.ArchivedFeedbacks)
        {
            var archivedFeedback = JsonConvert.DeserializeObject<Dtos.UserFeedbackInfo>(archivedJson);
            if (archivedFeedback != null)
            {
                var item = new FeedbackHistoryItemProto
                {
                    FeedbackId = archivedFeedback.FeedbackId,
                    FeedbackType = archivedFeedback.FeedbackType,
                    Response = archivedFeedback.Response,
                    ContactRequested = archivedFeedback.ContactRequested,
                    Email = archivedFeedback.Email,
                    SubmittedAt = Timestamp.FromDateTime(archivedFeedback.SubmittedAt)
                };
                
                // Convert reasons
                foreach (var reason in archivedFeedback.Reasons)
                {
                    item.Reasons.Add((int)reason);
                }
                
                // Convert subscription if present
                if (archivedFeedback.Subscription != null)
                {
                    item.Subscription = new UserSubscriptionInfoProto
                    {
                        PlanType = (int)archivedFeedback.Subscription.PlanType,
                        IsUltimate = archivedFeedback.Subscription.IsUltimate,
                        StartDate = Timestamp.FromDateTime(DateTime.SpecifyKind(archivedFeedback.Subscription.StartDate, DateTimeKind.Utc)),
                        EndDate = Timestamp.FromDateTime(DateTime.SpecifyKind(archivedFeedback.Subscription.EndDate, DateTimeKind.Utc))
                    };
                }
                
                allFeedbacks.Add(item);
            }
        }
        
        // Sort by submission time (newest first)
        allFeedbacks = allFeedbacks.OrderByDescending(f => f.SubmittedAt.ToDateTime()).ToList();
        
        // Apply pagination
        var totalCount = allFeedbacks.Count;
        var pagedFeedbacks = allFeedbacks
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();
        
        var hasMore = (request.PageIndex + 1) * request.PageSize < totalCount;
        
        Logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Retrieved {Count} feedbacks for user {UserId}. Total: {TotalCount}, HasMore: {HasMore}",
            pagedFeedbacks.Count, Id, totalCount, hasMore);
        
        var result = new GetFeedbackHistoryResultProto
        {
            TotalCount = totalCount,
            HasMore = hasMore
        };
        result.Feedbacks.AddRange(pagedFeedbacks);
        
        return Task.FromResult(result);
    }

    private FeedbackHistoryItemProto ConvertToHistoryItemProto(ProtoUserFeedbackInfo info)
    {
        var item = new FeedbackHistoryItemProto
        {
            FeedbackId = info.FeedbackId,
            FeedbackType = info.FeedbackType,
            Response = info.Response,
            ContactRequested = info.ContactRequested,
            Email = info.Email,
            SubmittedAt = info.SubmittedAt ?? Timestamp.FromDateTime(DateTime.MinValue)
        };
        
        // Convert reasons
        foreach (var reason in info.Reasons)
        {
            item.Reasons.Add((int)reason);
        }
        
        // Convert subscription if present
        if (info.Subscription != null)
        {
            item.Subscription = new UserSubscriptionInfoProto
            {
                PlanType = (int)info.Subscription.PlanType,
                IsUltimate = info.Subscription.IsUltimate,
                StartDate = info.Subscription.StartDate,
                EndDate = info.Subscription.EndDate
            };
        }
        
        return item;
    }

    /// <summary>
    /// Validate submit feedback request
    /// </summary>
    private (bool IsValid, string Message, string ErrorCode) ValidateSubmitRequest(
        SubmitFeedbackRequestProto request, GodGPTLanguage language)
    {
        if (!IsValidFeedbackType(request.FeedbackType))
        {
            var message = LocalizationService != null
                ? LocalizationService.GetLocalizedValidationMessage("invalid_feedback_type", language)
                : "Invalid feedback type";
            return (false, message, "INVALID_FEEDBACK_TYPE");
        }

        var maxResponseLength = FeedbackOptions?.CurrentValue.MaxResponseLength ?? 512;
        if (!string.IsNullOrEmpty(request.Response) && request.Response.Length > maxResponseLength)
        {
            var message = LocalizationService != null
                ? LocalizationService.GetLocalizedValidationMessage("response_too_long", language, 
                    new Dictionary<string, string> { { "maxLength", maxResponseLength.ToString() } })
                : $"Response too long (max {maxResponseLength} characters)";
            return (false, message, "RESPONSE_TOO_LONG");
        }
        
        return (true, string.Empty, string.Empty);
    }

    /// <summary>
    /// Check if feedback type is valid
    /// </summary>
    private static bool IsValidFeedbackType(string feedbackType)
    {
        return feedbackType.Equals(FeedbackTypeConstants.Cancel, StringComparison.OrdinalIgnoreCase) ||
               feedbackType.Equals(FeedbackTypeConstants.Change, StringComparison.OrdinalIgnoreCase);
    }

    #region EventHandlers

    [EventHandler]
    public void HandleSubmitFeedbackEvent(SubmitFeedbackEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleSkippedFeedbackEvent(SkippedFeedbackEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    protected override void TransitionState(UserFeedbackState state, IMessage evt)
    {
        switch (evt)
        {
            case SubmitFeedbackEvent submitEvent:
                // Archive current feedback before adding new one
                if (state.CurrentFeedback != null)
                {
                    // Convert Protobuf to old format JSON for archival compatibility
                    var oldFormatFeedback = new Dtos.UserFeedbackInfo
                    {
                        FeedbackId = state.CurrentFeedback.FeedbackId,
                        FeedbackType = state.CurrentFeedback.FeedbackType,
                        Reasons = state.CurrentFeedback.Reasons.Select(r => (CsFeedbackReasonEnum)r).ToList(),
                        Response = state.CurrentFeedback.Response,
                        ContactRequested = state.CurrentFeedback.ContactRequested,
                        Email = state.CurrentFeedback.Email,
                        SubmittedAt = state.CurrentFeedback.SubmittedAt?.ToDateTime() ?? DateTime.MinValue,
                        ReasonTextsEnglish = state.CurrentFeedback.ReasonTextsEnglish.ToList()
                    };
                    
                    var archivedData = JsonConvert.SerializeObject(oldFormatFeedback);
                    state.ArchivedFeedbacks.Add(archivedData);
                    
                    // Limit archived data count
                    var maxArchived = FeedbackOptions?.CurrentValue.MaxArchivedFeedbacks ?? 100;
                    if (state.ArchivedFeedbacks.Count > maxArchived)
                    {
                        state.ArchivedFeedbacks.RemoveAt(0);
                    }
                }

                state.UserId = submitEvent.UserId;
                state.CurrentFeedback = submitEvent.FeedbackInfo;
                state.LastFeedbackTime = submitEvent.SubmittedAt;
                state.FeedbackCount = submitEvent.FeedbackCount;
                if (state.CreatedAt == null)
                {
                    state.CreatedAt = submitEvent.SubmittedAt;
                }
                state.UpdatedAt = submitEvent.SubmittedAt;
                break;
                
            case SkippedFeedbackEvent skippedEvent:
                state.LastFeedbackTime = skippedEvent.SubmittedAt;
                if (state.CreatedAt == null)
                {
                    state.CreatedAt = skippedEvent.SubmittedAt;
                }
                state.UpdatedAt = skippedEvent.SubmittedAt;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}
