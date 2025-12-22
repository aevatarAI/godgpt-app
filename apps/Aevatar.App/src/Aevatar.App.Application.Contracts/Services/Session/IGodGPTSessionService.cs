using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.GAgents.AI.Options;
using Aevatar.Quantum;
using Aevatar.GAgents.AI.Abstractions;

namespace Aevatar.App.Application.Contracts.Services.Session;

/// <summary>
/// Service interface for managing GodGPT chat sessions.
/// </summary>
public interface IGodGPTSessionService
{
    /// <summary>
    /// Creates a new chat session for a user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="systemLLM">System LLM type</param>
    /// <param name="prompt">Prompt</param>
    /// <param name="guider">Optional guider identifier</param>
    /// <param name="userLocalTime">Optional user local time</param>
    /// <returns>The new session ID</returns>
    Task<Guid> CreateSessionAsync(Guid userId, string systemLLM, string prompt, string? guider = null,
        DateTime? userLocalTime = null);

    /// <summary>
    /// Sends a message in a session and gets AI response.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="sessionId">Session ID</param>
    /// <param name="systemLLM">System LLM type</param>
    /// <param name="content">Message content</param>
    /// <param name="promptSettings">Optional prompt settings</param>
    /// <returns>Tuple of response content and additional info</returns>
    Task<Tuple<string, string>> ChatWithSessionAsync(Guid userId, Guid sessionId, string systemLLM, string content,
        ExecutionPromptSettings promptSettings = null);

    /// <summary>
    /// Gets the list of all sessions for a user.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <returns>List of session info</returns>
    Task<List<SessionInfoDto>> GetSessionListAsync(Guid userId);

    /// <summary>
    /// Gets the message history for a session.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="sessionId">Session ID</param>
    /// <returns>List of chat messages</returns>
    Task<List<ChatMessage>> GetSessionMessageListAsync(Guid userId, Guid sessionId);

    /// <summary>
    /// Gets session creation information.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="sessionId">Session ID</param>
    /// <returns>Session creation info or null</returns>
    Task<Aevatar.Quantum.SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid userId, Guid sessionId);

    /// <summary>
    /// Deletes a session.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="sessionId">Session ID to delete</param>
    /// <returns>The deleted session ID</returns>
    Task<Guid> DeleteSessionAsync(Guid userId, Guid sessionId);

    /// <summary>
    /// Renames a session.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="sessionId">Session ID</param>
    /// <param name="title">New title</param>
    /// <returns>The renamed session ID</returns>
    Task<Guid> RenameSessionAsync(Guid userId, Guid sessionId, string title);

    /// <summary>
    /// Searches sessions by keyword.
    /// </summary>
    /// <param name="userId">User ID</param>
    /// <param name="keyword">Search keyword</param>
    /// <returns>List of matching sessions</returns>
    Task<List<SessionInfoDto>> SearchSessionsAsync(Guid userId, string keyword);
}
