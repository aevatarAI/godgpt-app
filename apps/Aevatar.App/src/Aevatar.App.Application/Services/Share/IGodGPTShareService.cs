using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.GAgents.AI.Abstractions;
using Aevatar.GodGPT.Dtos;
using Aevatar.App.Domain.Shared;
using Aevatar.Quantum;

namespace Aevatar.App.Application.Services.Share;

/// <summary>
/// Service interface for managing conversation sharing functionality.
/// Handles share link generation, shared content retrieval, and AI-powered keyword generation.
/// </summary>
public interface IGodGPTShareService
{
    /// <summary>
    /// Generates a shareable link for a conversation session.
    /// </summary>
    /// <param name="currentUserId">The current user's ID</param>
    /// <param name="request">The share request containing session ID</param>
    /// <param name="language">The language for localization</param>
    /// <returns>The share response containing the compressed share ID</returns>
    Task<CreateShareIdResponse> GenerateShareContentAsync(Guid currentUserId, CreateShareIdRequest request, GodGPTChatLanguage language = GodGPTChatLanguage.English);

    /// <summary>
    /// Gets the message list from a shared conversation.
    /// </summary>
    /// <param name="shareString">The compressed share string</param>
    /// <param name="language">The language for localization</param>
    /// <returns>List of chat messages from the shared session</returns>
    Task<List<ChatMessage>> GetShareMessageListAsync(string shareString, GodGPTChatLanguage language = GodGPTChatLanguage.English);

    /// <summary>
    /// Gets AI-generated keywords for a session.
    /// </summary>
    /// <param name="sessionId">The session ID</param>
    /// <param name="content">Optional content to process</param>
    /// <param name="region">Optional region parameter</param>
    /// <param name="sessionType">The type of session</param>
    /// <param name="language">The language for response</param>
    /// <returns>Share response with AI-generated content</returns>
    Task<QuantumShareResponseDto> GetShareKeyWordWithAIAsync(Guid sessionId, string? content, string? region, SessionType sessionType, GodGPTChatLanguage language = GodGPTChatLanguage.English);
}
