using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserProfile;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.Application.Grains.UserProfile;

/// <summary>
/// User Profile GAgent interface
/// Manages user basic information and voice preference settings
/// </summary>
public interface IUserProfileGAgent : IGAgent
{
    /// <summary>
    /// Sets user profile information
    /// </summary>
    /// <param name="gender">Gender</param>
    /// <param name="birthDate">Birth date</param>
    /// <param name="birthPlace">Birth place</param>
    /// <param name="fullName">Full name</param>
    /// <returns>User ID</returns>
    Task<Guid> SetUserProfileAsync(string gender, DateTime birthDate, string birthPlace, string fullName);

    /// <summary>
    /// Gets user basic profile information
    /// </summary>
    /// <returns>User basic profile</returns>
    Task<UserProfileDtoProto> GetUserProfileAsync();

    /// <summary>
    /// Sets the voice language preference for the user.
    /// </summary>
    /// <param name="voiceLanguage">The voice language to set</param>
    /// <returns>The user ID</returns>
    Task<Guid> SetVoiceLanguageAsync(VoiceLanguageEnum voiceLanguage);

    /// <summary>
    /// Clears user profile information
    /// </summary>
    Task ClearAsync();
}

