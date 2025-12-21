using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
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

// Use QuotaPlanType from user_quota.proto as the unified plan type
using CsFeedbackReasonEnum = Aevatar.Application.Grains.Common.Constants.FeedbackReasonEnum;

namespace Aevatar.Application.Grains.UserFeedback;

/// <summary>
/// Interface for User Feedback GAgent - manages user feedback collection and frequency control
/// </summary>
public interface IUserFeedbackGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request);
    Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync();
    Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(GetFeedbackHistoryRequest request);
}

[GAgent(nameof(UserFeedbackGAgent))]
public class UserFeedbackGAgent : GAgentBase<UserFeedbackState>, IUserFeedbackGAgent
{
    private readonly ILocalizationService _localizationService;
    private readonly IOptionsMonitor<UserFeedbackOptions> _feedbackOptions;

    public UserFeedbackGAgent(
        Guid id,
        ILocalizationService localizationService,
        IOptionsMonitor<UserFeedbackOptions> feedbackOptions) : base(id)
    {
        _localizationService = localizationService;
        _feedbackOptions = feedbackOptions;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("User feedback management and collection");
    }

    public async Task<SubmitFeedbackResult> SubmitFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            Logger.LogDebug("[UserFeedbackGAgent][SubmitFeedbackAsync] Start - UserId: {UserId}, FeedbackType: {FeedbackType}",
                Id, request.FeedbackType);
            
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            
            // Validate request
            var validationResult = ValidateSubmitRequest(request, language);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning("[UserFeedbackGAgent][SubmitFeedbackAsync] Validation failed for user {UserId}: {ErrorCode} - {Message}",
                    Id, validationResult.ErrorCode, validationResult.Message);
                    
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = validationResult.Message,
                    ErrorCode = validationResult.ErrorCode
                };
            }

            if (request.SkippedFeedback)
            {
                Logger.LogDebug("[UserFeedbackGAgent][SubmitFeedbackAsync] User {UserId} skipped feedback", Id);
                
                RaiseEvent(new SkippedFeedbackEvent
                {
                    SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow)
                });

                await ConfirmEventsAsync();
                
                return new SubmitFeedbackResult
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
                    Id, eligibilityResult.LastFeedbackTime);
                    
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = eligibilityResult.Message,
                    ErrorCode = "FREQUENCY_LIMIT_EXCEEDED"
                };
            }

            // Generate English reason texts
            var englishReasonTexts = FeedbackReasonHelper.GetEnglishReasonTexts(request.Reasons, _localizationService);

            // Create feedback info (Protobuf)
            var feedbackInfo = new Aevatar.Agents.GodGPT.Protos.UserFeedback.UserFeedbackInfo
            {
                FeedbackId = Guid.NewGuid().ToString(),
                FeedbackType = request.FeedbackType,
                Response = request.Response?.Trim() ?? string.Empty,
                ContactRequested = request.ContactRequested,
                Email = request.Email?.Trim() ?? string.Empty,
                SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            // Map reasons
            foreach (var reason in request.Reasons)
            {
                feedbackInfo.Reasons.Add((FeedbackReason)reason);
            }
            
            // Map reason texts
            feedbackInfo.ReasonTextsEnglish.AddRange(englishReasonTexts);

            // Raise event to update state
            RaiseEvent(new SubmitFeedbackEvent
            {
                UserId = Id.ToString(),
                FeedbackInfo = feedbackInfo,
                SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                FeedbackCount = State.FeedbackCount + 1
            });

            await ConfirmEventsAsync();

            Logger.LogInformation("[UserFeedbackGAgent][SubmitFeedbackAsync] User feedback submitted successfully. UserId: {UserId}, FeedbackId: {FeedbackId}, Type: {Type}",
                Id, feedbackInfo.FeedbackId, request.FeedbackType);

            return new SubmitFeedbackResult
            {
                Success = true,
                Message = string.Empty,
            };
        }
        catch (Exception ex)
        {
            var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
            Logger.LogError(ex, "[UserFeedbackGAgent][SubmitFeedbackAsync] Error submitting feedback for user: {UserId}", Id);
            
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = _localizationService.GetLocalizedMessage("feedback_submission_failed", language),
                ErrorCode = "INTERNAL_ERROR"
            };
        }
    }

    public Task<CheckEligibilityResult> CheckFeedbackEligibilityAsync()
    {
        Logger.LogDebug("[UserFeedbackGAgent][CheckFeedbackEligibilityAsync] Checking eligibility for user {UserId}", Id);
            
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        
        // If no previous feedback, user is eligible
        if (State.LastFeedbackTime == null)
        {
            return Task.FromResult(new CheckEligibilityResult
            {
                Eligible = true,
                Message = string.Empty
            });
        }

        var lastFeedbackTime = State.LastFeedbackTime.ToDateTime();
        var timeSinceLastFeedback = DateTime.UtcNow - lastFeedbackTime;
        var frequencyDays = _feedbackOptions.CurrentValue.FeedbackFrequencyDays;
        var isEligible = timeSinceLastFeedback.TotalDays >= frequencyDays;
        
        if (isEligible)
        {
            return Task.FromResult(new CheckEligibilityResult
            {
                Eligible = true,
                LastFeedbackTime = lastFeedbackTime,
                Message = string.Empty
            });
        }

        var nextEligibleTime = lastFeedbackTime.AddDays(frequencyDays);
        var daysRemaining = (int)Math.Ceiling((nextEligibleTime - DateTime.UtcNow).TotalDays);
        
        return Task.FromResult(new CheckEligibilityResult
        {
            Eligible = false,
            LastFeedbackTime = lastFeedbackTime,
            NextEligibleTime = nextEligibleTime,
            Message = _localizationService.GetLocalizedMessage("feedback_frequency_limit", language, 
                new Dictionary<string, string> { { "days", daysRemaining.ToString() } })
        });
    }

    public Task<GetFeedbackHistoryResult> GetFeedbackHistoryAsync(GetFeedbackHistoryRequest request)
    {
        Logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Getting feedback history for user {UserId}. PageSize: {PageSize}, PageIndex: {PageIndex}",
            Id, request.PageSize, request.PageIndex);
            
        var allFeedbacks = new List<FeedbackHistoryItem>();
        
        // Add current feedback if exists
        if (State.CurrentFeedback != null)
        {
            allFeedbacks.Add(ConvertToHistoryItem(State.CurrentFeedback));
        }
        
        // Add archived feedbacks (stored as JSON strings for compatibility)
        foreach (var archivedJson in State.ArchivedFeedbacks)
        {
            var archivedFeedback = JsonConvert.DeserializeObject<Dtos.UserFeedbackInfo>(archivedJson);
            if (archivedFeedback != null)
            {
                allFeedbacks.Add(new FeedbackHistoryItem
                {
                    FeedbackId = archivedFeedback.FeedbackId,
                    FeedbackType = archivedFeedback.FeedbackType,
                    Reasons = archivedFeedback.Reasons,
                    Response = archivedFeedback.Response,
                    ContactRequested = archivedFeedback.ContactRequested,
                    Email = archivedFeedback.Email,
                    SubmittedAt = archivedFeedback.SubmittedAt
                });
            }
        }
        
        // Sort by submission time (newest first)
        allFeedbacks = allFeedbacks.OrderByDescending(f => f.SubmittedAt).ToList();
        
        // Apply pagination
        var totalCount = allFeedbacks.Count;
        var pagedFeedbacks = allFeedbacks
            .Skip(request.PageIndex * request.PageSize)
            .Take(request.PageSize)
            .ToList();
        
        var hasMore = (request.PageIndex + 1) * request.PageSize < totalCount;
        
        Logger.LogDebug("[UserFeedbackGAgent][GetFeedbackHistoryAsync] Retrieved {Count} feedbacks for user {UserId}. Total: {TotalCount}, HasMore: {HasMore}",
            pagedFeedbacks.Count, Id, totalCount, hasMore);
        
        return Task.FromResult(new GetFeedbackHistoryResult
        {
            Feedbacks = pagedFeedbacks,
            TotalCount = totalCount,
            HasMore = hasMore
        });
    }

    private FeedbackHistoryItem ConvertToHistoryItem(Aevatar.Agents.GodGPT.Protos.UserFeedback.UserFeedbackInfo info)
    {
        var item = new FeedbackHistoryItem
        {
            FeedbackId = info.FeedbackId,
            FeedbackType = info.FeedbackType,
            Response = info.Response,
            ContactRequested = info.ContactRequested,
            Email = info.Email,
            SubmittedAt = info.SubmittedAt?.ToDateTime() ?? DateTime.MinValue
        };
        
        // Convert reasons
        foreach (var reason in info.Reasons)
        {
            item.Reasons.Add((CsFeedbackReasonEnum)reason);
        }
        
        // Convert subscription if present
        if (info.Subscription != null)
        {
            item.Subscription = new Dtos.UserSubscription
            {
                PlanType = (QuotaPlanType)info.Subscription.PlanType,
                IsUltimate = info.Subscription.IsUltimate,
                StartDate = info.Subscription.StartDate?.ToDateTime() ?? DateTime.MinValue,
                EndDate = info.Subscription.EndDate?.ToDateTime() ?? DateTime.MinValue
            };
        }
        
        return item;
    }

    /// <summary>
    /// Validate submit feedback request
    /// </summary>
    private (bool IsValid, string Message, string ErrorCode) ValidateSubmitRequest(
        SubmitFeedbackRequest request, GodGPTLanguage language)
    {
        if (!IsValidFeedbackType(request.FeedbackType))
        {
            return (false, _localizationService.GetLocalizedValidationMessage("invalid_feedback_type", language), "INVALID_FEEDBACK_TYPE");
        }

        var maxResponseLength = _feedbackOptions.CurrentValue.MaxResponseLength;
        if (!string.IsNullOrEmpty(request.Response) && request.Response.Length > maxResponseLength)
        {
            return (false, _localizationService.GetLocalizedValidationMessage("response_too_long", language, 
                new Dictionary<string, string> { { "maxLength", maxResponseLength.ToString() } }), "RESPONSE_TOO_LONG");
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
                    var maxArchived = _feedbackOptions.CurrentValue.MaxArchivedFeedbacks;
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
