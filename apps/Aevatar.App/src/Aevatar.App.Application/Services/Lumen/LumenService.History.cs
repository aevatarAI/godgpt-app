using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Services.Lumen;

public partial class LumenService
{
    #region History

    /// <inheritdoc />
    public async Task<GetTodayPredictionResult> GetPredictionByDateAsync(string userId, DateOnly date)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetPredictionByDateAsync] Getting prediction for date: {UserId}, {Date}", userId, date);

            var historyAgent = await GetHistoryAgentAsync(userId);
            var dateValue = new DateValue { Year = date.Year, Month = date.Month, Day = date.Day };
            var historyPrediction = await historyAgent.GetPredictionByDateAsync(dateValue);
            
            if (historyPrediction == null)
            {
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = $"No prediction found for date {date}"
                };
            }

            // Convert HistoryPredictionResultDto to PredictionResultDto
            var predictionResult = ConvertHistoryToPredictionResult(historyPrediction);
            
            return new GetTodayPredictionResult
            {
                Success = true,
                Prediction = predictionResult
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetPredictionByDateAsync] Error: {UserId}, {Date}", userId, date);
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"Error getting prediction by date: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetPredictionHistoryResult> GetPredictionHistoryAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetPredictionHistoryAsync] Getting history: {UserId}", userId);

            var historyAgent = await GetHistoryAgentAsync(userId);
            var predictions = await historyAgent.GetRecentPredictionsAsync(30); // Last 30 days
            
            return new GetPredictionHistoryResult
            {
                Success = true,
                Predictions = predictions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetPredictionHistoryAsync] Error: {UserId}", userId);
            return new GetPredictionHistoryResult
            {
                Success = false,
                Message = $"Error getting prediction history: {ex.Message}",
                Predictions = new List<HistoryPredictionResultDto>()
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetPredictionHistoryResult> GetMonthlyPredictionsAsync(string userId, DateOnly queryDate)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetMonthlyPredictionsAsync] Getting monthly predictions: {UserId}, {Month}/{Year}", 
                userId, queryDate.Month, queryDate.Year);

            var historyAgent = await GetHistoryAgentAsync(userId);
            var predictions = await historyAgent.GetMonthlyPredictionsAsync(queryDate.Year, queryDate.Month);
            
            return new GetPredictionHistoryResult
            {
                Success = true,
                Predictions = predictions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetMonthlyPredictionsAsync] Error: {UserId}, {Month}/{Year}", 
                userId, queryDate.Month, queryDate.Year);
            return new GetPredictionHistoryResult
            {
                Success = false,
                Message = $"Error getting monthly predictions: {ex.Message}",
                Predictions = new List<HistoryPredictionResultDto>()
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetPredictionHistoryResult> GetYearlyPredictionsAsync(string userId, int year)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetYearlyPredictionsAsync] Getting yearly predictions: {UserId}, {Year}", userId, year);

            // Use DailyYearly history agent for yearly queries
            var historyAgent = await GetDailyYearlyHistoryAgentAsync(userId);
            var dailyRecords = await historyAgent.GetAllDailyPredictionsAsync();
            
            // Convert DailyPredictionRecord to HistoryPredictionResultDto
            var yearlyPredictions = dailyRecords
                .Where(p => p.Date != null && p.Date.Year == year)
                .Select(p => ConvertDailyRecordToHistoryDto(p, userId))
                .ToList();
            
            return new GetPredictionHistoryResult
            {
                Success = true,
                Predictions = yearlyPredictions
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetYearlyPredictionsAsync] Error: {UserId}, {Year}", userId, year);
            return new GetPredictionHistoryResult
            {
                Success = false,
                Message = $"Error getting yearly predictions: {ex.Message}",
                Predictions = new List<HistoryPredictionResultDto>()
            };
        }
    }

    /// <summary>
    /// Convert HistoryPredictionResultDto to PredictionResultDto
    /// </summary>
    private PredictionResultDto? ConvertHistoryToPredictionResult(HistoryPredictionResultDto history)
    {
        if (history == null) return null;

        // Get the first available language result
        var firstResult = history.Results.Values.FirstOrDefault();
        if (firstResult == null) return null;

        return firstResult;
    }

    /// <summary>
    /// Convert DailyPredictionRecord to HistoryPredictionResultDto
    /// </summary>
    private HistoryPredictionResultDto ConvertDailyRecordToHistoryDto(DailyPredictionRecord record, string userId)
    {
        var dto = new HistoryPredictionResultDto
        {
            PredictionId = record.PredictionId,
            UserId = userId,
            PredictionDate = record.Date,
            CreatedAt = record.CreatedAt,
            Type = PredictionType.PredictionDaily,
            FromCache = false,
            AllLanguagesGenerated = record.AvailableLanguages.Count > 1
        };
        
        // Copy available languages
        dto.AvailableLanguages.AddRange(record.AvailableLanguages);
        
        // Copy multilingual results
        foreach (var kvp in record.MultilingualResults)
        {
            // LanguageResults contains field_values map
            var predictionResult = new PredictionResultDto
            {
                PredictionId = record.PredictionId,
                Type = PredictionType.PredictionDaily
            };
            
            // LanguageResults has FieldValues map
            if (kvp.Value.FieldValues.Count > 0)
            {
                foreach (var field in kvp.Value.FieldValues)
                {
                    predictionResult.FieldValues[field.Key] = field.Value;
                }
            }
            
            dto.Results[kvp.Key] = predictionResult;
        }
        
        return dto;
    }

    #endregion
}
