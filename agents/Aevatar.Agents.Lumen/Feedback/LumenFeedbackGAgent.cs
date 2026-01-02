using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Feedback;

/// <summary>
/// Interface for Lumen Feedback GAgent - manages user feedback on predictions
/// </summary>
public interface ILumenFeedbackGAgent : IGAgent
{
    Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request);
    
    /// <summary>
    /// Get feedback - main feedback or specific prediction method feedback
    /// </summary>
    Task<FeedbackDto?> GetFeedbackAsync(string? predictionMethod = null);
    
    /// <summary>
    /// Update rating for a specific prediction method
    /// </summary>
    Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request);
}

/// <summary>
/// Lumen Feedback GAgent - manages user feedback on predictions
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class LumenFeedbackGAgent : GAgentBase<LumenFeedbackState>, ILumenFeedbackGAgent
{
    /// <summary>
    /// Required: Parameterless constructor for activation
    /// </summary>
    public LumenFeedbackGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Lumen feedback management - {State.MethodFeedbacks.Count} feedbacks");
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public void HandleFeedbackSubmittedEvent(FeedbackSubmittedEvent evt)
    {
        TransitionState(State, evt);
    }

    [EventHandler]
    public void HandleMethodRatingUpdatedEvent(MethodRatingUpdatedEvent evt)
    {
        TransitionState(State, evt);
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(LumenFeedbackState state, IMessage evt)
    {
        switch (evt)
        {
            case FeedbackSubmittedEvent submittedEvent:
                state.FeedbackId = submittedEvent.FeedbackId;
                state.UserId = submittedEvent.UserId;
                state.PredictionId = submittedEvent.PredictionId;
                state.MethodFeedbacks[submittedEvent.PredictionMethod] = submittedEvent.FeedbackDetail;
                break;
                
            case MethodRatingUpdatedEvent ratingUpdatedEvent:
                state.FeedbackId = ratingUpdatedEvent.FeedbackId;
                state.UserId = ratingUpdatedEvent.UserId;
                state.PredictionId = ratingUpdatedEvent.PredictionId;
                state.MethodFeedbacks[ratingUpdatedEvent.PredictionMethod] = ratingUpdatedEvent.FeedbackDetail;
                break;
        }
    }

    // ============================================================================
    // Public Methods (RPC Interface Implementation)
    // ============================================================================

    public async Task<SubmitFeedbackResult> SubmitOrUpdateFeedbackAsync(SubmitFeedbackRequest request)
    {
        try
        {
            Logger.LogDebug(
                "[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}",
                request.UserId, request.PredictionId);

            // Validate rating
            if (request.Rating < 0 || request.Rating > 1)
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "Rating must be 0 or 1"
                };
            }

            // Validate prediction method is required
            if (string.IsNullOrWhiteSpace(request.PredictionMethod))
            {
                return new SubmitFeedbackResult
                {
                    Success = false,
                    Message = "PredictionMethod is required"
                };
            }

            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            var methodKey = request.PredictionMethod;
            
            // Build complete FeedbackDetail object
            FeedbackDetail newFeedbackDetail;
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var existingFeedback))
            {
                // Update existing feedback with new values
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    Comment = request.Comment,
                    Email = request.Email,
                    AgreeToContact = request.AgreeToContact,
                    CreatedAt = existingFeedback.CreatedAt,
                    UpdatedAt = now
                };
                
                // Copy feedback types
                newFeedbackDetail.FeedbackTypes.AddRange(request.FeedbackTypes);
            }
            else
            {
                // Create new feedback
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    Comment = request.Comment,
                    Email = request.Email,
                    AgreeToContact = request.AgreeToContact,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                
                // Copy feedback types
                newFeedbackDetail.FeedbackTypes.AddRange(request.FeedbackTypes);
            }

            var feedbackId = State.FeedbackId;
            if (string.IsNullOrEmpty(feedbackId))
            {
                feedbackId = Guid.NewGuid().ToString();
            }

            Logger.LogInformation(
                "[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Submitting feedback: {FeedbackId}",
                feedbackId);

            // Raise submitted event with complete FeedbackDetail
            RaiseEvent(new FeedbackSubmittedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = methodKey,
                FeedbackDetail = newFeedbackDetail,
                CreatedAt = now
            });

            await ConfirmEventsAsync();

            return new SubmitFeedbackResult
            {
                Success = true,
                Message = string.Empty,
                FeedbackId = feedbackId
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenFeedbackGAgent][SubmitOrUpdateFeedbackAsync] Error submitting feedback");
            return new SubmitFeedbackResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<FeedbackDto?> GetFeedbackAsync(string? predictionMethod = null)
    {
        try
        {
            Logger.LogDebug(
                "[LumenFeedbackGAgent][GetFeedbackAsync] Getting feedback for method: {PredictionMethod}", 
                predictionMethod ?? "main");

            if (string.IsNullOrEmpty(State.FeedbackId))
            {
                return Task.FromResult<FeedbackDto?>(null);
            }

            // If no specific method requested, return all feedbacks
            if (string.IsNullOrWhiteSpace(predictionMethod))
            {
                var result = new FeedbackDto
                {
                    FeedbackId = State.FeedbackId,
                    UserId = State.UserId,
                    PredictionId = State.PredictionId
                };
                
                foreach (var kvp in State.MethodFeedbacks)
                {
                    result.MethodFeedbacks[kvp.Key] = kvp.Value;
                }
                
                return Task.FromResult<FeedbackDto?>(result);
            }

            // Get specific method feedback
            if (State.MethodFeedbacks.TryGetValue(predictionMethod, out var methodFeedback))
            {
                var result = new FeedbackDto
                {
                    FeedbackId = State.FeedbackId,
                    UserId = State.UserId,
                    PredictionId = State.PredictionId
                };
                result.MethodFeedbacks[predictionMethod] = methodFeedback;
                
                return Task.FromResult<FeedbackDto?>(result);
            }

            // Method not found, return null
            return Task.FromResult<FeedbackDto?>(null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenFeedbackGAgent][GetFeedbackAsync] Error getting feedback");
            return Task.FromResult<FeedbackDto?>(null);
        }
    }

    public async Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(UpdateMethodRatingRequest request)
    {
        try
        {
            Logger.LogDebug(
                "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Start - UserId: {UserId}, Method: {PredictionMethod}, Rating: {Rating}",
                request.UserId, request.PredictionMethod, request.Rating);

            // Validate request
            var validationResult = ValidateUpdateRatingRequest(request);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning(
                    "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Validation failed: {Message}",
                    validationResult.Message);
                return new UpdateMethodRatingResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }

            var methodKey = request.PredictionMethod;
            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            
            // Build complete FeedbackDetail object
            FeedbackDetail newFeedbackDetail;
            if (State.MethodFeedbacks.TryGetValue(methodKey, out var existingFeedback))
            {
                // Update existing feedback with new rating
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = existingFeedback.PredictionMethod,
                    Rating = request.Rating,
                    Comment = existingFeedback.Comment,
                    Email = existingFeedback.Email,
                    AgreeToContact = existingFeedback.AgreeToContact,
                    CreatedAt = existingFeedback.CreatedAt,
                    UpdatedAt = now
                };
                
                // Copy feedback types
                newFeedbackDetail.FeedbackTypes.AddRange(existingFeedback.FeedbackTypes);
                
                Logger.LogInformation(
                    "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Updating existing feedback: OldRating={OldRating}, NewRating={NewRating}",
                    existingFeedback.Rating, request.Rating);
            }
            else
            {
                // Create new feedback with only rating
                newFeedbackDetail = new FeedbackDetail
                {
                    PredictionMethod = methodKey,
                    Rating = request.Rating,
                    CreatedAt = now,
                    UpdatedAt = now
                };
                
                Logger.LogInformation(
                    "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Creating new feedback: Rating={Rating}",
                    request.Rating);
            }

            var feedbackId = State.FeedbackId;
            if (string.IsNullOrEmpty(feedbackId))
            {
                feedbackId = Guid.NewGuid().ToString();
            }
            
            // Raise event with complete FeedbackDetail
            RaiseEvent(new MethodRatingUpdatedEvent
            {
                FeedbackId = feedbackId,
                UserId = request.UserId,
                PredictionId = request.PredictionId,
                PredictionMethod = methodKey,
                FeedbackDetail = newFeedbackDetail,
                UpdatedAt = now
            });

            // Confirm events to persist state changes
            await ConfirmEventsAsync();

            Logger.LogInformation(
                "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Rating updated successfully - Method: {PredictionMethod}, Rating: {Rating}",
                request.PredictionMethod, request.Rating);

            return new UpdateMethodRatingResult
            {
                Success = true,
                Message = string.Empty,
                PredictionMethod = request.PredictionMethod,
                UpdatedRating = request.Rating,
                UpdatedAt = now
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenFeedbackGAgent][UpdateMethodRatingAsync] Error updating method rating");
            return new UpdateMethodRatingResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    // ============================================================================
    // Private Helper Methods
    // ============================================================================

    /// <summary>
    /// Validate update rating request
    /// </summary>
    private (bool IsValid, string Message) ValidateUpdateRatingRequest(UpdateMethodRatingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId))
        {
            return (false, "UserId is required");
        }

        if (string.IsNullOrWhiteSpace(request.PredictionId))
        {
            return (false, "PredictionId is required");
        }

        if (request.Rating < 0 || request.Rating > 1)
        {
            return (false, "Rating must be 0 or 1");
        }

        // Validate prediction method is required
        if (string.IsNullOrWhiteSpace(request.PredictionMethod))
        {
            return (false, "PredictionMethod is required");
        }

        return (true, string.Empty);
    }
}

