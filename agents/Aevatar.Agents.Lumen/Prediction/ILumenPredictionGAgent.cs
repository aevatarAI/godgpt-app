using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// Interface for Lumen Prediction GAgent - manages prediction generation
/// </summary>
public interface ILumenPredictionGAgent
{
    /// <summary>
    /// Get or generate today's prediction for a user
    /// </summary>
    Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(
        LumenUserDto userInfo,
        PredictionType type = PredictionType.PredictionDaily,
        string userLanguage = "en",
        DateValue? predictionDate = null);
    
    /// <summary>
    /// Get current prediction without generating
    /// </summary>
    Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en");
    
    /// <summary>
    /// Get prediction status
    /// </summary>
    Task<PredictionStatusDto?> GetPredictionStatusAsync(
        DateTime? profileUpdatedAt = null, 
        string? userTimeZone = null);
    
    /// <summary>
    /// Clear current prediction
    /// </summary>
    Task ClearCurrentPredictionAsync();
    
    /// <summary>
    /// Get calculated values (zodiac, Chinese zodiac, etc.)
    /// </summary>
    Task<CalculatedValuesDto> GetCalculatedValuesAsync(
        LumenUserDto userInfo, 
        string userLanguage = "en");
    
    /// <summary>
    /// Trigger translation to a target language
    /// </summary>
    Task<TriggerTranslationResult> TriggerTranslationAsync(
        LumenUserDto userInfo, 
        string targetLanguage);
    
    /// <summary>
    /// Update timezone for reminder
    /// </summary>
    Task<UpdateTimeZoneReminderResult> UpdateTimeZoneReminderAsync(string timeZoneId);
    
    /// <summary>
    /// Update user activity timestamp
    /// </summary>
    Task UpdateUserActivityAsync(string? userTimeZone = null);
    
    /// <summary>
    /// Check if prediction has been generated for a specific date
    /// Used by background reminder job to avoid duplicate generation
    /// </summary>
    Task<bool> HasGeneratedForDateAsync(DateOnly date);
    
    /// <summary>
    /// Trigger background prediction generation
    /// Used by background reminder job for daily auto-generation
    /// </summary>
    Task TriggerBackgroundGenerationAsync(string language);
}
