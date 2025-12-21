using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.GAgents.AI.Options;
using Aevatar.Quantum;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.GAgents.AI.Abstractions;

namespace Aevatar.App.Application.Services.Session;

/// <summary>
/// Service implementation for managing GodGPT chat sessions.
/// Handles session creation, messaging, and management through ChatGAgentManager.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTSessionService : ApplicationService, IGodGPTSessionService
{
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<GodGPTSessionService> _logger;

    public GodGPTSessionService(
        IGAgentFactory agentFactory,
        ILogger<GodGPTSessionService> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Guid> CreateSessionAsync(Guid userId, string systemLLM, string prompt, string? guider = null,
        DateTime? userLocalTime = null)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.CreateSessionAsync(systemLLM, prompt, null, guider, userLocalTime);
    }

    /// <inheritdoc />
    public async Task<Tuple<string, string>> ChatWithSessionAsync(Guid userId, Guid sessionId, string systemLLM,
        string content, ExecutionPromptSettings promptSettings = null)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.ChatWithSessionAsync(sessionId, systemLLM, content, promptSettings);
    }

    /// <inheritdoc />
    public async Task<List<SessionInfoDto>> GetSessionListAsync(Guid userId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.GetSessionListAsync();
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetSessionMessageListAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.GetSessionMessageListAsync(sessionId);
    }

    /// <inheritdoc />
    public async Task<Aevatar.Quantum.SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        var grainsResult = await manager.GetSessionCreationInfoAsync(sessionId);

        if (grainsResult != null)
        {
            return new Aevatar.Quantum.SessionCreationInfoDto
            {
                SessionId = grainsResult.SessionId,
                Title = grainsResult.Title,
                CreateAt = grainsResult.CreateAt,
                Guider = grainsResult.Guider
            };
        }

        return null;
    }

    /// <inheritdoc />
    public async Task<Guid> DeleteSessionAsync(Guid userId, Guid sessionId)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.DeleteSessionAsync(sessionId);
    }

    /// <inheritdoc />
    public async Task<Guid> RenameSessionAsync(Guid userId, Guid sessionId, string title)
    {
        var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
        return await manager.RenameSessionAsync(sessionId, title);
    }

    /// <inheritdoc />
    public async Task<List<SessionInfoDto>> SearchSessionsAsync(Guid userId, string keyword)
    {
        // Input validation according to downstream team requirements
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SessionInfoDto>(); // Return empty list for empty keyword
        }

        // Length limit validation
        if (keyword.Length > 200)
        {
            _logger.LogWarning("Search keyword too long: {Length} characters", keyword.Length);
            return new List<SessionInfoDto>();
        }

        try
        {
            var manager = _agentFactory.CreateGAgent<ChatGAgentManager>(userId);
            return await manager.SearchSessionsAsync(keyword.Trim(), 1000);
        }
        catch (Exception ex)
        {
            // Error handling according to downstream team requirements
            _logger.LogError(ex, "Search sessions failed for keyword: {Keyword}", keyword);
            return new List<SessionInfoDto>();
        }
    }
}
