using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.App.Lumen.Dtos;
using Aevatar.Agents.Lumen.Protos;

// Type aliases to clarify which types come from where
using ProtoUpdateUserProfileRequest = Aevatar.Agents.Lumen.Protos.UpdateUserProfileRequest;
using ProtoSubmitFeedbackRequest = Aevatar.Agents.Lumen.Protos.SubmitFeedbackRequest;
using ProtoUpdateMethodRatingRequest = Aevatar.Agents.Lumen.Protos.UpdateMethodRatingRequest;
using ProtoToggleFavouriteRequest = Aevatar.Agents.Lumen.Protos.ToggleFavouriteRequest;

namespace Aevatar.App.Lumen;

/// <summary>
/// Lumen Service interface - manages Lumen prediction and user operations
/// </summary>
public interface ILumenService
{
    #region User Management
    
    /// <summary>
    /// Update user profile (V2 with FullName)
    /// </summary>
    Task<UpdateUserProfileResult> UpdateUserProfileAsync(ProtoUpdateUserProfileRequest request, string userPreferredLanguage = "en");
    
    /// <summary>
    /// Get user profile (V2 with FullName)
    /// </summary>
    Task<GetUserProfileResult> GetUserProfileAsync(string userId, string userLanguage = "en");
    
    /// <summary>
    /// Clear user data (for testing)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync(string userId);
    
    /// <summary>
    /// Get remaining profile updates for the week
    /// </summary>
    Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync(string userId);
    
    /// <summary>
    /// Update user icon/avatar
    /// </summary>
    Task<UpdateIconResult> UpdateUserIconAsync(string userId, string? iconUrl);
    
    #endregion
    
    #region Language Management
    
    /// <summary>
    /// Set user language (triggers translation)
    /// </summary>
    Task<SetLanguageResult> SetLanguageAsync(string userId, string newLanguage);
    
    /// <summary>
    /// Get user language information
    /// </summary>
    Task<GetLanguageInfoResult> GetLanguageInfoAsync(string userId);
    
    #endregion
    
    #region Timezone Management
    
    /// <summary>
    /// Update user timezone (does NOT count as profile update)
    /// </summary>
    Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(string userId, string timeZoneId);
    
    /// <summary>
    /// Get user's local date based on their timezone
    /// </summary>
    Task<DateOnly> GetUserLocalDateAsync(string userId);
    
    #endregion
    
    #region Predictions
    
    /// <summary>
    /// Get lifetime prediction (query only, no auto-generation)
    /// </summary>
    Task<GetTodayPredictionResult> GetLifetimePredictionAsync(string userId, string userLanguage = "en");
    
    /// <summary>
    /// Get yearly prediction (query only, no auto-generation)
    /// </summary>
    Task<GetTodayPredictionResult> GetYearlyPredictionAsync(string userId, string userLanguage = "en");
    
    /// <summary>
    /// Get daily prediction (query only, no auto-generation)
    /// </summary>
    Task<GetTodayPredictionResult> GetTodayPredictionAsync(string userId, string userLanguage = "en");
    
    #endregion
    
    #region History
    
    /// <summary>
    /// Get prediction by specific date
    /// </summary>
    Task<GetTodayPredictionResult> GetPredictionByDateAsync(string userId, DateOnly date);
    
    /// <summary>
    /// Get all prediction history
    /// </summary>
    Task<GetPredictionHistoryResult> GetPredictionHistoryAsync(string userId);
    
    /// <summary>
    /// Get monthly predictions for a specific month
    /// </summary>
    Task<GetPredictionHistoryResult> GetMonthlyPredictionsAsync(string userId, DateOnly queryDate);
    
    /// <summary>
    /// Get yearly predictions for a specific year
    /// </summary>
    Task<GetPredictionHistoryResult> GetYearlyPredictionsAsync(string userId, int year);
    
    #endregion
    
    #region Generation & Status
    
    /// <summary>
    /// Trigger prediction generation in background
    /// </summary>
    Task TriggerPredictionGenerationAsync(string userId, List<PredictionType> types);
    
    /// <summary>
    /// Check if user profile exists
    /// </summary>
    Task<bool> CheckUserProfileExistsAsync(string userId);
    
    /// <summary>
    /// Get prediction generation status
    /// </summary>
    Task<GetPredictionStatusResult> GetPredictionStatusAsync(string userId);
    
    #endregion
    
    #region Feedback
    
    /// <summary>
    /// Submit feedback for a prediction
    /// </summary>
    Task<SubmitFeedbackResult> SubmitFeedbackAsync(ProtoSubmitFeedbackRequest request);
    
    /// <summary>
    /// Update rating for a specific prediction method
    /// </summary>
    Task<UpdateMethodRatingResult> UpdateMethodRatingAsync(ProtoUpdateMethodRatingRequest request);
    
    #endregion
    
    #region Favourites
    
    /// <summary>
    /// Toggle favourite for a prediction
    /// </summary>
    Task<ToggleFavouriteResult> ToggleFavouriteAsync(ProtoToggleFavouriteRequest request);
    
    /// <summary>
    /// Get all favourites for a user
    /// </summary>
    Task<GetFavouritesResult> GetFavouritesAsync(string userId);
    
    #endregion
    
    #region Calculated Values
    
    /// <summary>
    /// Get all backend-calculated values for a user
    /// </summary>
    Task<GetCalculatedValuesResult> GetCalculatedValuesAsync(string userId, string userLanguage = "en");
    
    #endregion
    
    #region Google Auth
    
    /// <summary>
    /// Verify Google OAuth code and bind account
    /// </summary>
    Task<GoogleAuthResultDto> GoogleAuthVerifyCodeAsync(string userId, GoogleAuthVerifyCodeInput input);
    
    /// <summary>
    /// Unbind Google account
    /// </summary>
    Task<bool> GoogleAuthUnbindAsync(string userId);
    
    /// <summary>
    /// Get Google account binding status
    /// </summary>
    Task<GoogleBindStatusDto> GoogleAuthBindStatusAsync(string userId);
    
    #endregion
}
