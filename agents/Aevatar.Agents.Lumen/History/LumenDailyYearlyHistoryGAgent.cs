using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.History;

/// <summary>
/// Interface for Lumen daily yearly history GAgent
/// Manages yearly archive of daily predictions
/// GrainId format: Guid derived from {UserId}-{YYYY}
/// </summary>
public interface ILumenDailyYearlyHistoryGAgent : IGAgent
{
    /// <summary>
    /// Add or update a daily prediction in yearly history
    /// </summary>
    Task AddOrUpdateDailyPredictionAsync(
        string userId,
        string predictionId,
        DateValue date,
        Dictionary<string, LanguageResults> multilingualResults,
        IEnumerable<string> availableLanguages);
    
    /// <summary>
    /// Get a specific daily prediction by date
    /// </summary>
    Task<DailyPredictionRecord?> GetDailyPredictionAsync(DateValue date);
    
    /// <summary>
    /// Get all daily predictions for this year
    /// </summary>
    Task<List<DailyPredictionRecord>> GetAllDailyPredictionsAsync();
    
    /// <summary>
    /// Get daily predictions for a date range
    /// </summary>
    Task<List<DailyPredictionRecord>> GetDailyPredictionsByRangeAsync(DateValue startDate, DateValue endDate);
    
    /// <summary>
    /// Clear all daily predictions for this year
    /// </summary>
    Task ClearYearlyHistoryAsync();
}

/// <summary>
/// Lumen Daily Yearly History GAgent - manages yearly archive of daily predictions
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class LumenDailyYearlyHistoryGAgent : GAgentBase<LumenDailyYearlyHistoryState>, ILumenDailyYearlyHistoryGAgent
{
    /// <summary>
    /// Required: Parameterless constructor for activation
    /// </summary>
    public LumenDailyYearlyHistoryGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Lumen daily prediction yearly history - Year: {State.Year}, Predictions: {State.Predictions.Count}");
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public void HandleDailyPredictionAddedEvent(DailyPredictionAddedEvent evt)
    {
        TransitionState(State, evt);
    }

    [EventHandler]
    public void HandleDailyYearlyHistoryClearedEvent(DailyYearlyHistoryClearedEvent evt)
    {
        TransitionState(State, evt);
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(LumenDailyYearlyHistoryState state, IMessage evt)
    {
        switch (evt)
        {
            case DailyPredictionAddedEvent addedEvent:
                state.UserId = addedEvent.UserId;
                state.Year = addedEvent.Year;
                state.LastUpdatedAt = addedEvent.AddedAt;
                
                // Create date key (YYYY-MM-DD format)
                var dateKey = $"{addedEvent.Date.Year:D4}-{addedEvent.Date.Month:D2}-{addedEvent.Date.Day:D2}";
                
                // Create or update prediction record
                var record = new DailyPredictionRecord
                {
                    PredictionId = addedEvent.PredictionId,
                    Date = addedEvent.Date,
                    CreatedAt = addedEvent.CreatedAt
                };
                
                foreach (var kvp in addedEvent.MultilingualResults)
                {
                    record.MultilingualResults[kvp.Key] = kvp.Value;
                }
                
                record.AvailableLanguages.AddRange(addedEvent.AvailableLanguages);
                
                state.Predictions[dateKey] = record;
                break;
                
            case DailyYearlyHistoryClearedEvent clearEvent:
                // Clear all predictions data
                state.UserId = string.Empty;
                state.Year = 0;
                state.Predictions.Clear();
                state.LastUpdatedAt = clearEvent.ClearedAt;
                break;
        }
    }

    // ============================================================================
    // Public Methods (RPC Interface Implementation)
    // ============================================================================

    public async Task AddOrUpdateDailyPredictionAsync(
        string userId,
        string predictionId,
        DateValue date,
        Dictionary<string, LanguageResults> multilingualResults,
        IEnumerable<string> availableLanguages)
    {
        try
        {
            // Extract year from date
            var year = date.Year;
            
            Logger.LogDebug(
                "[LumenDailyYearlyHistory] Adding prediction - UserId: {UserId}, Year: {Year}, Date: {Date}",
                userId, year, $"{date.Year}-{date.Month:D2}-{date.Day:D2}");
            
            var now = Timestamp.FromDateTime(DateTime.UtcNow);
            
            // Create event
            var evt = new DailyPredictionAddedEvent
            {
                UserId = userId,
                Year = year,
                PredictionId = predictionId,
                Date = date,
                CreatedAt = now,
                AddedAt = now
            };
            
            foreach (var kvp in multilingualResults)
            {
                evt.MultilingualResults[kvp.Key] = kvp.Value;
            }
            
            evt.AvailableLanguages.AddRange(availableLanguages);
            
            RaiseEvent(evt);
            await ConfirmEventsAsync();
            
            Logger.LogInformation(
                "[LumenDailyYearlyHistory] Prediction added successfully - UserId: {UserId}, Date: {Date}, Total: {Total}",
                userId, $"{date.Year}-{date.Month:D2}-{date.Day:D2}", State.Predictions.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "[LumenDailyYearlyHistory] Error adding prediction - Date: {Date}", 
                $"{date.Year}-{date.Month:D2}-{date.Day:D2}");
            throw;
        }
    }
    
    public Task<DailyPredictionRecord?> GetDailyPredictionAsync(DateValue date)
    {
        var dateKey = $"{date.Year:D4}-{date.Month:D2}-{date.Day:D2}";
        
        if (State.Predictions.TryGetValue(dateKey, out var prediction))
        {
            Logger.LogDebug(
                "[LumenDailyYearlyHistory] Found prediction for date: {Date}", dateKey);
            return Task.FromResult<DailyPredictionRecord?>(prediction);
        }
        
        Logger.LogDebug(
            "[LumenDailyYearlyHistory] No prediction found for date: {Date}", dateKey);
        return Task.FromResult<DailyPredictionRecord?>(null);
    }
    
    public Task<List<DailyPredictionRecord>> GetAllDailyPredictionsAsync()
    {
        var predictions = State.Predictions.Values
            .OrderBy(p => p.Date != null 
                ? new DateOnly(p.Date.Year, p.Date.Month, p.Date.Day) 
                : DateOnly.MinValue)
            .ToList();
        
        Logger.LogDebug(
            "[LumenDailyYearlyHistory] Retrieved all predictions - Year: {Year}, Count: {Count}",
            State.Year, predictions.Count);
        
        return Task.FromResult(predictions);
    }
    
    public Task<List<DailyPredictionRecord>> GetDailyPredictionsByRangeAsync(DateValue startDate, DateValue endDate)
    {
        var start = new DateOnly(startDate.Year, startDate.Month, startDate.Day);
        var end = new DateOnly(endDate.Year, endDate.Month, endDate.Day);
        
        var predictions = State.Predictions.Values
            .Where(p => 
            {
                if (p.Date == null) return false;
                var d = new DateOnly(p.Date.Year, p.Date.Month, p.Date.Day);
                return d >= start && d <= end;
            })
            .OrderBy(p => p.Date != null 
                ? new DateOnly(p.Date.Year, p.Date.Month, p.Date.Day) 
                : DateOnly.MinValue)
            .ToList();
        
        Logger.LogDebug(
            "[LumenDailyYearlyHistory] Retrieved predictions by range - Start: {Start}, End: {End}, Count: {Count}",
            start, end, predictions.Count);
        
        return Task.FromResult(predictions);
    }
    
    public async Task ClearYearlyHistoryAsync()
    {
        try
        {
            Logger.LogDebug(
                "[LumenDailyYearlyHistory] Clearing yearly history - Year: {Year}, Count: {Count}",
                State.Year, State.Predictions.Count);
            
            RaiseEvent(new DailyYearlyHistoryClearedEvent
            {
                ClearedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            
            await ConfirmEventsAsync();
            
            Logger.LogInformation("[LumenDailyYearlyHistory] Yearly history cleared successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenDailyYearlyHistory] Error clearing yearly history");
            throw;
        }
    }
}

