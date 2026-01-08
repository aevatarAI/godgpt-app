using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.History;

/// <summary>
/// Interface for Lumen Prediction History GAgent - manages prediction history
/// </summary>
public interface ILumenPredictionHistoryGAgent : IGAgent
{
    Task AddPredictionAsync(HistoryPredictionResultDto prediction);
    
    Task<GetPredictionByDateResult> GetPredictionByDateAsync(DateValue date);
    
    Task<GetRecentPredictionsResult> GetRecentPredictionsAsync(int days = 7);
    
    Task<GetMonthlyPredictionsResult> GetMonthlyPredictionsAsync(int year, int month);
    
    Task ClearHistoryAsync();
}

/// <summary>
/// Lumen Prediction History GAgent - manages prediction history (last 30 days)
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class LumenPredictionHistoryGAgent : GAgentBase<LumenPredictionHistoryState>, ILumenPredictionHistoryGAgent
{
    private const int MaxHistoryDays = 30; // Keep last 30 days

    /// <summary>
    /// Required: Parameterless constructor for activation
    /// </summary>
    public LumenPredictionHistoryGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Lumen prediction history - {State.RecentPredictions.Count} records");
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public void HandlePredictionAddedToHistoryEvent(PredictionAddedToHistoryEvent evt)
    {
        TransitionState(State, evt);
    }

    [EventHandler]
    public void HandlePredictionHistoryClearedEvent(PredictionHistoryClearedEvent evt)
    {
        TransitionState(State, evt);
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(LumenPredictionHistoryState state, IMessage evt)
    {
        switch (evt)
        {
            case PredictionAddedToHistoryEvent addedEvent:
                // Remove old prediction for the same date (if exists)
                var existingIndex = -1;
                for (int i = 0; i < state.RecentPredictions.Count; i++)
                {
                    var p = state.RecentPredictions[i];
                    if (p.PredictionDate != null && addedEvent.PredictionDate != null &&
                        p.PredictionDate.Year == addedEvent.PredictionDate.Year &&
                        p.PredictionDate.Month == addedEvent.PredictionDate.Month &&
                        p.PredictionDate.Day == addedEvent.PredictionDate.Day)
                    {
                        existingIndex = i;
                        break;
                    }
                }
                
                if (existingIndex >= 0)
                {
                    state.RecentPredictions.RemoveAt(existingIndex);
                }
                
                // Add new prediction with complete data
                var record = new PredictionHistoryRecord
                {
                    PredictionId = addedEvent.PredictionId,
                    PredictionDate = addedEvent.PredictionDate,
                    CreatedAt = addedEvent.CreatedAt,
                    Type = addedEvent.Type,
                    RequestedLanguage = addedEvent.RequestedLanguage,
                    ReturnedLanguage = addedEvent.ReturnedLanguage,
                    FromCache = addedEvent.FromCache,
                    AllLanguagesGenerated = addedEvent.AllLanguagesGenerated,
                    IsFallback = addedEvent.IsFallback
                };
                
                foreach (var kvp in addedEvent.Results)
                {
                    record.Results[kvp.Key] = kvp.Value;
                }
                
                record.AvailableLanguages.AddRange(addedEvent.AvailableLanguages);
                
                state.RecentPredictions.Add(record);
                
                // Sort by date descending (newest first)
                var sorted = state.RecentPredictions
                    .OrderByDescending(p => p.PredictionDate != null 
                        ? new DateOnly(p.PredictionDate.Year, p.PredictionDate.Month, p.PredictionDate.Day) 
                        : DateOnly.MinValue)
                    .ToList();
                
                state.RecentPredictions.Clear();
                
                // Keep only last MaxHistoryDays
                foreach (var item in sorted.Take(MaxHistoryDays))
                {
                    state.RecentPredictions.Add(item);
                }
                
                state.LastUpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
                break;
                
            case PredictionHistoryClearedEvent clearEvent:
                // Clear all history data
                state.UserId = string.Empty;
                state.RecentPredictions.Clear();
                state.LastUpdatedAt = clearEvent.ClearedAt;
                break;
        }
    }

    // ============================================================================
    // Public Methods (RPC Interface Implementation)
    // ============================================================================

    public async Task AddPredictionAsync(HistoryPredictionResultDto prediction)
    {
        try
        {
            Logger.LogDebug(
                "[LumenPredictionHistoryGAgent][AddPredictionAsync] Adding prediction: {PredictionId}, Date: {Date}, Type: {Type}",
                prediction.PredictionId, 
                prediction.PredictionDate != null 
                    ? $"{prediction.PredictionDate.Year}-{prediction.PredictionDate.Month:D2}-{prediction.PredictionDate.Day:D2}" 
                    : "null", 
                prediction.Type);

            // Set UserId if State is empty (first time)
            if (string.IsNullOrEmpty(State.UserId))
            {
                State.UserId = prediction.UserId;
            }

            // Create event
            var evt = new PredictionAddedToHistoryEvent
            {
                PredictionId = prediction.PredictionId,
                PredictionDate = prediction.PredictionDate,
                CreatedAt = prediction.CreatedAt,
                Type = prediction.Type,
                UserId = prediction.UserId,
                RequestedLanguage = prediction.RequestedLanguage,
                ReturnedLanguage = prediction.ReturnedLanguage,
                FromCache = prediction.FromCache,
                AllLanguagesGenerated = prediction.AllLanguagesGenerated,
                IsFallback = prediction.IsFallback
            };
            
            foreach (var kvp in prediction.Results)
            {
                evt.Results[kvp.Key] = kvp.Value;
            }
            
            evt.AvailableLanguages.AddRange(prediction.AvailableLanguages);

            RaiseEvent(evt);
            await ConfirmEventsAsync();

            Logger.LogInformation(
                "[LumenPredictionHistoryGAgent][AddPredictionAsync] Prediction added to history: {PredictionId}",
                prediction.PredictionId);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionHistoryGAgent][AddPredictionAsync] Error adding prediction to history");
            throw;
        }
    }

    public Task<GetPredictionByDateResult> GetPredictionByDateAsync(DateValue date)
    {
        try
        {
            var dateKey = $"{date.Year}-{date.Month:D2}-{date.Day:D2}";
            Logger.LogDebug("[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] Getting prediction for date: {Date}", dateKey);

            // Only return Daily predictions (history is for daily predictions only)
            var prediction = State.RecentPredictions.FirstOrDefault(p => 
                p.PredictionDate != null &&
                p.PredictionDate.Year == date.Year &&
                p.PredictionDate.Month == date.Month &&
                p.PredictionDate.Day == date.Day &&
                p.Type == PredictionType.PredictionDaily);
            
            if (prediction == null)
            {
                Logger.LogInformation("[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] No prediction found for date: {Date}", dateKey);
                return Task.FromResult(new GetPredictionByDateResult { Success = true, Message = "No prediction found" });
            }

            var result = new HistoryPredictionResultDto
            {
                PredictionId = prediction.PredictionId,
                UserId = State.UserId,
                PredictionDate = prediction.PredictionDate,
                CreatedAt = prediction.CreatedAt,
                Type = prediction.Type,
                FromCache = prediction.FromCache,
                AllLanguagesGenerated = prediction.AllLanguagesGenerated,
                RequestedLanguage = prediction.RequestedLanguage,
                ReturnedLanguage = prediction.ReturnedLanguage,
                IsFallback = prediction.IsFallback
            };
            
            foreach (var kvp in prediction.Results)
            {
                result.Results[kvp.Key] = kvp.Value;
            }
            
            result.AvailableLanguages.AddRange(prediction.AvailableLanguages);

            return Task.FromResult(new GetPredictionByDateResult { Success = true, Prediction = result });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetPredictionByDateAsync] Error getting prediction by date");
            return Task.FromResult(new GetPredictionByDateResult { Success = false, Message = ex.Message });
        }
    }

    public Task<GetRecentPredictionsResult> GetRecentPredictionsAsync(int days = 7)
    {
        try
        {
            Logger.LogDebug("[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Getting recent {Days} days predictions", days);

            if (days < 1 || days > MaxHistoryDays)
            {
                days = Math.Clamp(days, 1, MaxHistoryDays);
            }

            var cutoffDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-days + 1);
            
            // Only return Daily predictions (history is for daily predictions only)
            var recentPredictions = State.RecentPredictions
                .Where(p => 
                {
                    if (p.PredictionDate == null) return false;
                    var d = new DateOnly(p.PredictionDate.Year, p.PredictionDate.Month, p.PredictionDate.Day);
                    return d >= cutoffDate && p.Type == PredictionType.PredictionDaily;
                })
                .OrderByDescending(p => p.PredictionDate != null 
                    ? new DateOnly(p.PredictionDate.Year, p.PredictionDate.Month, p.PredictionDate.Day) 
                    : DateOnly.MinValue)
                .Take(days)
                .Select(p => 
                {
                    var result = new HistoryPredictionResultDto
                    {
                        PredictionId = p.PredictionId,
                        UserId = State.UserId,
                        PredictionDate = p.PredictionDate,
                        CreatedAt = p.CreatedAt,
                        Type = p.Type,
                        FromCache = p.FromCache,
                        AllLanguagesGenerated = p.AllLanguagesGenerated,
                        RequestedLanguage = p.RequestedLanguage,
                        ReturnedLanguage = p.ReturnedLanguage,
                        IsFallback = p.IsFallback
                    };
                    
                    foreach (var kvp in p.Results)
                    {
                        result.Results[kvp.Key] = kvp.Value;
                    }
                    
                    result.AvailableLanguages.AddRange(p.AvailableLanguages);
                    
                    return result;
                })
                .ToList();

            Logger.LogInformation("[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Found {Count} predictions", 
                recentPredictions.Count);

            var response = new GetRecentPredictionsResult { Success = true };
            response.Predictions.AddRange(recentPredictions);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetRecentPredictionsAsync] Error getting recent predictions");
            return Task.FromResult(new GetRecentPredictionsResult { Success = false, Message = ex.Message });
        }
    }

    public Task<GetMonthlyPredictionsResult> GetMonthlyPredictionsAsync(int year, int month)
    {
        try
        {
            Logger.LogDebug("[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Getting predictions for {Year}-{Month}", 
                year, month);

            // Get first and last day of the month
            var firstDayOfMonth = new DateOnly(year, month, 1);
            var lastDayOfMonth = firstDayOfMonth.AddMonths(1).AddDays(-1);

            // Only return Daily predictions (history is for daily predictions only)
            var monthlyPredictions = State.RecentPredictions
                .Where(p => 
                {
                    if (p.PredictionDate == null) return false;
                    var d = new DateOnly(p.PredictionDate.Year, p.PredictionDate.Month, p.PredictionDate.Day);
                    return d >= firstDayOfMonth && d <= lastDayOfMonth && p.Type == PredictionType.PredictionDaily;
                })
                .OrderByDescending(p => p.PredictionDate != null 
                    ? new DateOnly(p.PredictionDate.Year, p.PredictionDate.Month, p.PredictionDate.Day) 
                    : DateOnly.MinValue)
                .Select(p => 
                {
                    var result = new HistoryPredictionResultDto
                    {
                        PredictionId = p.PredictionId,
                        UserId = State.UserId,
                        PredictionDate = p.PredictionDate,
                        CreatedAt = p.CreatedAt,
                        Type = p.Type,
                        FromCache = p.FromCache,
                        AllLanguagesGenerated = p.AllLanguagesGenerated,
                        RequestedLanguage = p.RequestedLanguage,
                        ReturnedLanguage = p.ReturnedLanguage,
                        IsFallback = p.IsFallback
                    };
                    
                    foreach (var kvp in p.Results)
                    {
                        result.Results[kvp.Key] = kvp.Value;
                    }
                    
                    result.AvailableLanguages.AddRange(p.AvailableLanguages);
                    
                    return result;
                })
                .ToList();

            Logger.LogInformation("[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Found {Count} predictions for {Year}-{Month}", 
                monthlyPredictions.Count, year, month);

            var response = new GetMonthlyPredictionsResult { Success = true };
            response.Predictions.AddRange(monthlyPredictions);
            return Task.FromResult(response);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionHistoryGAgent][GetMonthlyPredictionsAsync] Error getting monthly predictions");
            return Task.FromResult(new GetMonthlyPredictionsResult { Success = false, Message = ex.Message });
        }
    }
    
    public async Task ClearHistoryAsync()
    {
        try
        {
            Logger.LogDebug("[LumenPredictionHistoryGAgent][ClearHistoryAsync] Clearing prediction history");

            RaiseEvent(new PredictionHistoryClearedEvent
            {
                ClearedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await ConfirmEventsAsync();

            Logger.LogInformation("[LumenPredictionHistoryGAgent][ClearHistoryAsync] Prediction history cleared successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionHistoryGAgent][ClearHistoryAsync] Error clearing prediction history");
            throw;
        }
    }
}

