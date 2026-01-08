using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.App.Controllers.Lumen;

public partial class LumenController
{
    #region History

    /// <summary>
    /// Get prediction by specific date
    /// </summary>
    [HttpPost("history/by-date")]
    public virtual async Task<PredictionResultDto?> GetPredictionByDateAsync([FromBody] GetPredictionByDateApiRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetPredictionByDateAsync] Getting for: {UserId}, Date: {Date}",
                userId, request.Date);

            var result = await _lumenService.GetPredictionByDateAsync(userId, request.Date);

            if (!result.Success || result.Prediction == null)
            {
                _logger.LogWarning("[LumenController][GetPredictionByDateAsync] Failed: {Message}", result.Message);
                throw new UserFriendlyException(result.Message ?? "No prediction found for this date");
            }
            
            _logger.LogDebug("[LumenController][GetPredictionByDateAsync] userId: {UserId}, date: {Date}, duration: {Duration}ms",
                userId, request.Date, stopwatch.ElapsedMilliseconds);

            return result.Prediction;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetPredictionByDateAsync] Error");
            throw new UserFriendlyException("Failed to get prediction");
        }
    }

    /// <summary>
    /// Get monthly prediction history
    /// </summary>
    [HttpPost("history/recent")]
    public virtual async Task<List<HistoryPredictionResultDto>> GetRecentPredictionsAsync([FromBody] GetRecentPredictionsApiRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            // Parse and validate date
            if (!DateOnly.TryParse(request.Date, out var queryDate))
            {
                throw new UserFriendlyException("Invalid date format. Expected format: yyyy-MM-dd");
            }

            _logger.LogDebug("[LumenController][GetRecentPredictionsAsync] Getting for: {UserId}, date: {Date}",
                userId, request.Date);

            var result = await _lumenService.GetMonthlyPredictionsAsync(userId, queryDate);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to get history");
            }

            _logger.LogDebug("[LumenController][GetRecentPredictionsAsync] userId: {UserId}, count: {Count}, duration: {Duration}ms",
                userId, result.Predictions.Count, stopwatch.ElapsedMilliseconds);

            return result.Predictions;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetRecentPredictionsAsync] Error");
            throw new UserFriendlyException("Failed to get prediction history");
        }
    }

    /// <summary>
    /// Get all prediction history
    /// </summary>
    [HttpGet("history")]
    public virtual async Task<List<HistoryPredictionResultDto>> GetPredictionHistoryAsync()
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetPredictionHistoryAsync] Getting for: {UserId}", userId);

            var result = await _lumenService.GetPredictionHistoryAsync(userId);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to get history");
            }

            return result.Predictions;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetPredictionHistoryAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get prediction history");
        }
    }

    #endregion

    #region Feedback

    /// <summary>
    /// Submit feedback for a prediction
    /// </summary>
    [HttpPost("feedback")]
    public virtual async Task<SubmitFeedbackResult> SubmitFeedbackAsync([FromBody] SubmitFeedbackApiRequest request)
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][SubmitFeedbackAsync] Submitting for: {UserId}, Method: {Method}",
                userId, request.PredictionMethod);

            // If PredictionId is not provided, generate a fixed ID based on the method name
            var predictionId = request.PredictionId ?? StringToGuid(request.PredictionMethod).ToString();

            var feedbackRequest = new SubmitFeedbackRequest
            {
                UserId = userId,
                PredictionId = predictionId,
                PredictionMethod = request.PredictionMethod,
                Rating = request.Rating,
                Comment = request.Comment ?? "",
                Email = request.Email ?? "",
                AgreeToContact = request.AgreeToContact
            };
            
            // Add feedback types
            feedbackRequest.FeedbackTypes.AddRange(request.FeedbackTypes ?? new List<string>());

            var result = await _lumenService.SubmitFeedbackAsync(feedbackRequest);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to submit feedback");
            }

            return result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][SubmitFeedbackAsync] Error");
            throw new UserFriendlyException("Failed to submit feedback");
        }
    }

    /// <summary>
    /// Update rating for a specific prediction method
    /// </summary>
    [HttpPost("feedback/rating")]
    public virtual async Task<UpdateMethodRatingResult> UpdateMethodRatingAsync([FromBody] UpdateMethodRatingApiRequest request)
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][UpdateMethodRatingAsync] Updating for: {UserId}, Method: {Method}",
                userId, request.PredictionMethod);

            var ratingRequest = new UpdateMethodRatingRequest
            {
                UserId = userId,
                PredictionId = request.PredictionId,
                PredictionMethod = request.PredictionMethod,
                Rating = request.Rating
            };

            var result = await _lumenService.UpdateMethodRatingAsync(ratingRequest);

            if (!result.Success)
            {
                throw new UserFriendlyException(result.Message ?? "Failed to update rating");
            }

            return result;
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][UpdateMethodRatingAsync] Error");
            throw new UserFriendlyException("Failed to update rating");
        }
    }

    #endregion

    #region Favourites

    /// <summary>
    /// Toggle favourite prediction
    /// </summary>
    [HttpPost("favourite")]
    public virtual async Task<ToggleFavouriteResult> ToggleFavouriteAsync([FromBody] ToggleFavouriteApiRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogInformation("[LumenController][ToggleFavouriteAsync] Start - UserId: {UserId}, PredictionId: {PredictionId}, IsFavourite: {IsFavourite}",
                userId, request.PredictionId, request.IsFavourite);

            var result = await _lumenService.ToggleFavouriteAsync(new ToggleFavouriteRequest
            {
                UserId = userId,
                PredictionId = request.PredictionId,
                IsFavourite = request.IsFavourite
            });

            _logger.LogInformation("[LumenController][ToggleFavouriteAsync] Completed in {ElapsedMs}ms - Success: {Success}",
                stopwatch.ElapsedMilliseconds, result.Success);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][ToggleFavouriteAsync] Error in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return new ToggleFavouriteResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    /// <summary>
    /// Get all favourited predictions
    /// </summary>
    [HttpGet("favourites")]
    public virtual async Task<GetFavouritesResult> GetFavouritesAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogInformation("[LumenController][GetFavouritesAsync] Start - UserId: {UserId}", userId);

            var result = await _lumenService.GetFavouritesAsync(userId);

            _logger.LogInformation("[LumenController][GetFavouritesAsync] Completed in {ElapsedMs}ms - Count: {Count}",
                stopwatch.ElapsedMilliseconds, result.Favourites.Count);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetFavouritesAsync] Error in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return new GetFavouritesResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    #endregion

    #region Helpers

    private static Guid StringToGuid(string input)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        var inputBytes = System.Text.Encoding.ASCII.GetBytes(input);
        var hashBytes = md5.ComputeHash(inputBytes);
        return new Guid(hashBytes);
    }

    #endregion
}
