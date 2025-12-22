using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Common;
using Aevatar.App.Application.Services;
using Aevatar.App.Application.Contracts.Services.Session;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.Quantum;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT chat session management.
/// Handles session creation, listing, search, rename, and deletion.
/// </summary>
[RemoteService]
[ControllerName("GodGPTSession")]
[Route("api")]
[Authorize]
public class GodGPTSessionController : AevatarController
{
    private readonly IGodGPTSessionService _sessionService;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<GodGPTSessionController> _logger;
    private readonly IIpLocationService _ipLocationService;
    private readonly string _defaultLLM = "OpenAI";
    private readonly string _defaultPrompt = "you are a robot";

    public GodGPTSessionController(
        IGodGPTSessionService sessionService,
        IClusterClient clusterClient,
        ILogger<GodGPTSessionController> logger,
        IIpLocationService ipLocationService)
    {
        _sessionService = sessionService;
        _clusterClient = clusterClient;
        _logger = logger;
        _ipLocationService = ipLocationService;
    }

    /// <summary>
    /// Create a new chat session
    /// </summary>
    [HttpPost("godgpt/create-session")]
    public async Task<Guid> CreateSessionAsync(CreateSessionRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = HttpContext.GetClientIpAddress();
        var appType = HttpContext.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        RequestContext.Set("IsCN", isCN);
        var sessionId = await _sessionService.CreateSessionAsync((Guid)CurrentUser.Id!, _defaultLLM, _defaultPrompt, request.Guider, request.UserLocalTime);
        _logger.LogDebug("[GodGPTSessionController][CreateSessionAsync] sessionId: {0}, duration: {1}ms",
            sessionId.ToString(), stopwatch.ElapsedMilliseconds);
        return sessionId;
    }

    /// <summary>
    /// Create a new chat session (deprecated - use create-session with request body)
    /// </summary>
    [Obsolete("Use CreateSessionAsync with CreateSessionRequestDto instead")]
    [HttpPost("gotgpt/create-session")]
    public async Task<Guid> CreateSessionAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = HttpContext.GetClientIpAddress();
        var appType = HttpContext.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        RequestContext.Set("IsCN", isCN);
        var sessionId = await _sessionService.CreateSessionAsync((Guid)CurrentUser.Id!, _defaultLLM, _defaultPrompt, "");
        _logger.LogDebug("[GodGPTSessionController][CreateSessionAsync] sessionId: {0}, duration: {1}ms",
            sessionId.ToString(), stopwatch.ElapsedMilliseconds);
        return sessionId;
    }

    /// <summary>
    /// Get all sessions for current user
    /// </summary>
    [HttpGet("godgpt/session-list")]
    public async Task<List<SessionInfoDto>> GetSessionListAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var sessionList = await _sessionService.GetSessionListAsync(currentUserId);
        _logger.LogDebug("[GodGPTSessionController][GetSessionListAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return sessionList;
    }

    /// <summary>
    /// Search sessions by keyword
    /// </summary>
    [HttpGet("godgpt/sessions/search")]
    public async Task<List<SessionInfoDto>> SearchSessionsAsync([FromQuery] string keyword)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        if (string.IsNullOrWhiteSpace(keyword))
        {
            return new List<SessionInfoDto>();
        }

        var searchResults = await _sessionService.SearchSessionsAsync(currentUserId, keyword);
        _logger.LogDebug("[GodGPTSessionController][SearchSessionsAsync] userId: {0}, keyword: {1}, results: {2}, duration: {3}ms",
            currentUserId, keyword, searchResults.Count, stopwatch.ElapsedMilliseconds);
        return searchResults;
    }

    /// <summary>
    /// Get session creation info (supports anonymous access with shareId)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("godgpt/session-info/{sessionId}")]
    public async Task<Aevatar.Quantum.SessionCreationInfoDto?> GetSessionCreationInfoAsync(Guid sessionId, [FromQuery] string? shareId = null)
    {
        var stopwatch = Stopwatch.StartNew();
        Guid currentUserId;

        // Check if shareId is provided
        if (!string.IsNullOrWhiteSpace(shareId))
        {
            try
            {
                // Extract userId from shareId using GuidCompressor
                (currentUserId, var extractedSessionId, var extractedShareId) = GuidCompressor.DecompressGuids(shareId);

                // Validate that the sessionId matches
                if (extractedSessionId != sessionId)
                {
                    _logger.LogWarning("[GodGPTSessionController][GetSessionCreationInfoAsync] SessionId mismatch. URL sessionId: {0}, extracted sessionId: {1}",
                        sessionId, extractedSessionId);
                    return new Aevatar.Quantum.SessionCreationInfoDto();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[GodGPTSessionController][GetSessionCreationInfoAsync] Failed to decompress shareId: {0}", shareId);
                return new Aevatar.Quantum.SessionCreationInfoDto();
            }
        }
        else
        {
            // Regular sessionId format - requires authentication
            if (CurrentUser?.Id == null)
            {
                _logger.LogWarning("[GodGPTSessionController][GetSessionCreationInfoAsync] Authentication required for regular sessionId access");
                return new Aevatar.Quantum.SessionCreationInfoDto();
            }
            currentUserId = (Guid)CurrentUser.Id!;
        }

        // Validate sessionId format (Guid validation is automatic by ASP.NET Core)
        if (sessionId == Guid.Empty)
        {
            _logger.LogWarning("[GodGPTSessionController][GetSessionCreationInfoAsync] Invalid sessionId: {0}", sessionId);
            return new Aevatar.Quantum.SessionCreationInfoDto();
        }

        var sessionInfo = await _sessionService.GetSessionCreationInfoAsync(currentUserId, sessionId);
        _logger.LogDebug("[GodGPTSessionController][GetSessionCreationInfoAsync] sessionId: {0}, userId: {1}, found: {2}, duration: {3}ms",
            sessionId, currentUserId, sessionInfo != null, stopwatch.ElapsedMilliseconds);

        return sessionInfo;
    }

    /// <summary>
    /// Get message list for a session
    /// </summary>
    [HttpGet("godgpt/chat/{sessionId}")]
    public async Task<List<ChatMessageWithMetaDto>> GetSessionMessageListAsync(Guid sessionId)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var chatMessages = new List<ChatMessageWithMetaDto>();
        try
        {
            var manager = _clusterClient.GetGrain<IChatManagerGAgent>(currentUserId);
            var language = HttpContext.GetGodGPTLanguage();
            _logger.LogDebug(
                $"[GodGPTSessionController][GetSessionMessageListAsync] sessionId: {sessionId}, language:{language}");
            RequestContext.Set("GodGPTLanguage", language.ToString());
            chatMessages = await manager.GetSessionMessageListWithMetaAsync(sessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError($"[GodGPTSessionController][GetSessionMessageListAsync] exception sessionId: {sessionId}, , duration: {stopwatch.ElapsedMilliseconds}ms, error:{ex.Message}");
            throw ex;
        }

        _logger.LogDebug(
            $"[GodGPTSessionController][GetSessionMessageListAsync] sessionId: {sessionId}, messageCount: {chatMessages.Count}, duration: {stopwatch.ElapsedMilliseconds}ms ");
        return chatMessages;
    }

    /// <summary>
    /// Rename a session
    /// </summary>
    [HttpPut("godgpt/chat/rename")]
    public async Task<Guid> RenameSessionAsync(QuantumRenameDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        var sessionId =
            await _sessionService.RenameSessionAsync((Guid)CurrentUser.Id!, request.SessionId, request.Title);
        _logger.LogDebug("[GodGPTSessionController][RenameSessionAsync] sessionId: {0}, duration: {1}ms",
            sessionId, stopwatch.ElapsedMilliseconds);
        return sessionId;
    }

    /// <summary>
    /// Delete a session
    /// </summary>
    [HttpDelete("godgpt/chat/{sessionId}")]
    public async Task<Guid> DeleteSessionAsync(Guid sessionId)
    {
        var stopwatch = Stopwatch.StartNew();
        var deleteSessionId = await _sessionService.DeleteSessionAsync((Guid)CurrentUser.Id!, sessionId);
        _logger.LogDebug("[GodGPTSessionController][DeleteSessionAsync] sessionId: {0}, duration: {1}ms",
            deleteSessionId, stopwatch.ElapsedMilliseconds);
        return deleteSessionId;
    }
}
