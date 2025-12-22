using System;
using System.Threading.Tasks;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.App.Application.Services.Awakening;

/// <summary>
/// Service interface for managing user awakening content.
/// Provides daily awakening messages and state management.
/// </summary>
public interface IGodGPTAwakeningService
{
    /// <summary>
    /// Gets today's awakening content for the user.
    /// </summary>
    /// <param name="currentUserId">Current user ID</param>
    /// <param name="language">Voice language preference</param>
    /// <param name="region">Optional region parameter</param>
    /// <returns>Awakening content DTO or null if not available</returns>
    Task<AwakeningContentDto?> GetTodayAwakeningAsync(Guid currentUserId, VoiceLanguageEnum language, string? region);

    /// <summary>
    /// Resets awakening state for testing purposes (Admin only).
    /// </summary>
    /// <param name="userId">User ID to reset awakening state for</param>
    /// <returns>True if reset was successful, false otherwise</returns>
    Task<bool> ResetAwakeningStateForTestingAsync(Guid userId);
}
