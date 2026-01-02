using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.Agents.Lumen.UserProfile;

/// <summary>
/// Interface for Lumen User Profile GAgent - manages user profile with FullName
/// </summary>
public interface ILumenUserProfileGAgent
{
    /// <summary>
    /// Update user profile (with rate limiting)
    /// </summary>
    Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request);
    
    /// <summary>
    /// Get user profile with calculated zodiac info and welcome note
    /// </summary>
    Task<GetUserProfileResult> GetUserProfileAsync(string userId, string userLanguage = "en");
    
    /// <summary>
    /// Get raw state data directly without any migration logic
    /// </summary>
    Task<LumenUserProfileDto?> GetRawStateAsync();
    
    /// <summary>
    /// Clear user profile data (for testing purposes)
    /// </summary>
    Task<ClearUserResult> ClearUserAsync();
    
    /// <summary>
    /// Get remaining profile update count for the current week
    /// </summary>
    Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync();
    
    /// <summary>
    /// Update user icon (with daily upload limit)
    /// </summary>
    Task<UpdateIconResult> UpdateIconAsync(string? iconUrl);
    
    /// <summary>
    /// Set user's current language (triggers translation for today's predictions)
    /// </summary>
    Task<SetLanguageResult> SetLanguageAsync(string newLanguage);
    
    /// <summary>
    /// Get user's language information (current language and remaining daily changes)
    /// </summary>
    Task<GetLanguageInfoResult> GetLanguageInfoAsync();
    
    /// <summary>
    /// Initialize user's language on registration (does not count as a switch)
    /// </summary>
    Task InitializeLanguageAsync(string initialLanguage);
    
    /// <summary>
    /// Save LLM-inferred LatLong from BirthCity
    /// </summary>
    Task SaveInferredLatLongAsync(string latLongInferred, string birthCity);
    
    /// <summary>
    /// Update user's time zone (does NOT count as profile update)
    /// </summary>
    Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(UpdateTimeZoneRequest request);
}


