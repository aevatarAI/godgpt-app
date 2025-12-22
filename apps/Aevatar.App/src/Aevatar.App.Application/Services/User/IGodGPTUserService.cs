using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.SpeechChat;
using Aevatar.App.Domain.Shared;

namespace Aevatar.App.Application.Services.User;

/// <summary>
/// Service interface for managing user profile and account operations.
/// </summary>
public interface IGodGPTUserService
{
    /// <summary>
    /// Gets the user's profile information.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    /// <returns>User profile DTO</returns>
    Task<UserProfileDto> GetUserProfileAsync(Guid currentUserId);

    /// <summary>
    /// Updates the user's profile information.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    /// <param name="userProfileDto">The profile update input</param>
    /// <returns>The updated user's ID</returns>
    Task<Guid> SetUserProfileAsync(Guid currentUserId, SetUserProfileInput userProfileDto);

    /// <summary>
    /// Deletes the user's account and all associated data.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    /// <returns>The deleted user's ID</returns>
    Task<Guid> DeleteAccountAsync(Guid currentUserId);

    /// <summary>
    /// Sets the user's voice language preference.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    /// <param name="voiceLanguage">The voice language to set</param>
    /// <returns>Updated user profile DTO</returns>
    Task<UserProfileDto> SetVoiceLanguageAsync(Guid currentUserId, VoiceLanguageEnum voiceLanguage);

    /// <summary>
    /// Updates the show toast status for the user.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    Task UpdateShowToastAsync(Guid currentUserId);

    /// <summary>
    /// Checks if the user can upload images.
    /// </summary>
    /// <param name="currentUserId">The user's ID</param>
    /// <param name="language">The language for localization</param>
    /// <returns>Execution result indicating if upload is allowed</returns>
    Task<ExecuteActionResultDto> CanUploadImageAsync(Guid currentUserId, GodGPTChatLanguage language = GodGPTChatLanguage.English);
}
