using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.App.Controllers.Lumen;

public partial class LumenController
{
    #region Predictions

    /// <summary>
    /// Get lifetime lumen prediction
    /// </summary>
    [HttpGet("lifetime-predict")]
    public virtual async Task<PredictionResultDto?> GetLifetimePredictionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetLifetimePredictionAsync] Getting for: {UserId}", userId);

            var result = await _lumenService.GetLifetimePredictionAsync(userId, "en");

            if (!result.Success || result.Prediction == null)
            {
                _logger.LogInformation("[LumenController][GetLifetimePredictionAsync] NO DATA - UserId: {UserId}, Duration: {Duration}ms",
                    userId, stopwatch.ElapsedMilliseconds);
                return null;
            }

            _logger.LogInformation("[LumenController][GetLifetimePredictionAsync] SUCCESS - UserId: {UserId}, Duration: {Duration}ms",
                userId, stopwatch.ElapsedMilliseconds);

            return result.Prediction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetLifetimePredictionAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get lifetime prediction");
        }
    }

    /// <summary>
    /// Get yearly lumen prediction
    /// </summary>
    [HttpGet("yearly-predict")]
    public virtual async Task<PredictionResultDto?> GetYearlyPredictionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetYearlyPredictionAsync] Getting for: {UserId}", userId);

            var result = await _lumenService.GetYearlyPredictionAsync(userId, "en");

            if (!result.Success || result.Prediction == null)
            {
                _logger.LogInformation("[LumenController][GetYearlyPredictionAsync] NO DATA - UserId: {UserId}, Duration: {Duration}ms",
                    userId, stopwatch.ElapsedMilliseconds);
                return null;
            }

            _logger.LogInformation("[LumenController][GetYearlyPredictionAsync] SUCCESS - UserId: {UserId}, Duration: {Duration}ms",
                userId, stopwatch.ElapsedMilliseconds);

            return result.Prediction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetYearlyPredictionAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get yearly prediction");
        }
    }

    /// <summary>
    /// Get today's Daily lumen prediction
    /// </summary>
    [HttpGet("predict")]
    public virtual async Task<PredictionResultDto?> GetTodayPredictionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetTodayPredictionAsync] Getting for: {UserId}", userId);

            var result = await _lumenService.GetTodayPredictionAsync(userId, "en");
            var today = await _lumenService.GetUserLocalDateAsync(userId);

            if (!result.Success || result.Prediction == null)
            {
                _logger.LogInformation("[LumenController][GetTodayPredictionAsync] NO DATA - UserId: {UserId}, Duration: {Duration}ms",
                    userId, stopwatch.ElapsedMilliseconds);
                
                // Trigger async generation
                _ = _lumenService.TriggerPredictionGenerationAsync(userId, new List<PredictionType> { PredictionType.PredictionDaily });
                
                return null;
            }
            
            // Check if prediction date matches today (compare DateValue with DateOnly)
            var predictionDate = result.Prediction.PredictionDate;
            var isSameDate = predictionDate != null && 
                             predictionDate.Year == today.Year && 
                             predictionDate.Month == today.Month && 
                             predictionDate.Day == today.Day;
            
            if (!isSameDate)
            {
                _logger.LogInformation("[LumenController][GetTodayPredictionAsync] OUTDATED DATA - UserId: {UserId}, Date: {Date}, Today: {Today}",
                    userId, $"{predictionDate?.Year}-{predictionDate?.Month}-{predictionDate?.Day}", today);
                
                _ = _lumenService.TriggerPredictionGenerationAsync(userId, new List<PredictionType> { PredictionType.PredictionDaily });
                
                return null;
            }

            _logger.LogInformation("[LumenController][GetTodayPredictionAsync] SUCCESS - UserId: {UserId}, Date: {Date}, Duration: {Duration}ms",
                userId, $"{predictionDate?.Year}-{predictionDate?.Month}-{predictionDate?.Day}", stopwatch.ElapsedMilliseconds);

            return result.Prediction;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetTodayPredictionAsync] Error: {UserId}", userId);
            throw new UserFriendlyException("Failed to get today prediction");
        }
    }

    /// <summary>
    /// Trigger prediction generation in background
    /// </summary>
    [HttpPost("trigger-generation")]
    public virtual async Task<IActionResult> TriggerPredictionGenerationAsync([FromBody] TriggerGenerationRequest? request)
    {
        var userId = GetCurrentUserId();
        try
        {
            if (request?.Types == null || request.Types.Count == 0)
            {
                return BadRequest(new { Success = false, Message = "At least one prediction type is required" });
            }

            _logger.LogInformation("[LumenController][TriggerPredictionGenerationAsync] Triggering for {UserId}, Types: {Types}",
                userId, string.Join(", ", request.Types));

            // Check if user profile exists
            var profileExists = await _lumenService.CheckUserProfileExistsAsync(userId);
            if (!profileExists)
            {
                return BadRequest(new 
                { 
                    Success = false, 
                    Message = "User profile not found. Please create your profile first." 
                });
            }

            // Fire and forget
            _ = _lumenService.TriggerPredictionGenerationAsync(userId, request.Types);

            return Ok(new 
            { 
                Success = true, 
                Message = "Prediction generation triggered successfully",
                Types = request.Types
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][TriggerPredictionGenerationAsync] Error: {UserId}", userId);
            return StatusCode(500, new { Success = false, Message = "Failed to trigger generation" });
        }
    }

    /// <summary>
    /// Get prediction generation status
    /// </summary>
    [HttpGet("status")]
    public virtual async Task<GetPredictionStatusResult> GetPredictionStatusAsync()
    {
        var userId = GetCurrentUserId();
        try
        {
            _logger.LogDebug("[LumenController][GetPredictionStatusAsync] Querying for: {UserId}", userId);
            var result = await _lumenService.GetPredictionStatusAsync(userId);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetPredictionStatusAsync] Error: {UserId}", userId);
            return new GetPredictionStatusResult
            {
                Success = false,
                Message = "Failed to query prediction status"
            };
        }
    }

    /// <summary>
    /// Get all backend-calculated values
    /// </summary>
    [HttpGet("calculated-values")]
    public virtual async Task<GetCalculatedValuesResult> GetCalculatedValuesAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var userId = GetCurrentUserId();
        
        try
        {
            _logger.LogInformation("[LumenController][GetCalculatedValuesAsync] User {UserId} requesting calculated values", userId);
            
            var result = await _lumenService.GetCalculatedValuesAsync(userId);
            
            _logger.LogInformation("[LumenController][GetCalculatedValuesAsync] User {UserId} completed in {ElapsedMs}ms, Success: {Success}",
                userId, stopwatch.ElapsedMilliseconds, result.Success);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetCalculatedValuesAsync] Error in {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
            return new GetCalculatedValuesResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    #endregion
}
