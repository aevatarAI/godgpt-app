using Aevatar.App.HttpApi.Controllers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services.Share;
using Aevatar.Quantum;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.GodGPT.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.GAgents.AI.Abstractions;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT conversation sharing functionality.
/// Handles share link generation and shared content retrieval.
/// </summary>
[RemoteService]
[ControllerName("GodGPTShare")]
[Route("api")]
[Authorize]
public class GodGPTShareController : AevatarController
{
    private readonly IGodGPTShareService _shareService;
    private readonly ILogger<GodGPTShareController> _logger;
    private readonly IIpLocationService _ipLocationService;

    public GodGPTShareController(
        IGodGPTShareService shareService,
        ILogger<GodGPTShareController> logger,
        IIpLocationService ipLocationService)
    {
        _shareService = shareService;
        _logger = logger;
        _ipLocationService = ipLocationService;
    }

    /// <summary>
    /// Create a share link for a session
    /// </summary>
    [HttpPost("godgpt/share")]
    public async Task<CreateShareIdResponse> CreateShareStringAsync(CreateShareIdRequest request)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var language = HttpContext.GetGodGPTLanguage();
        var response = await _shareService.GenerateShareContentAsync(currentUserId, request, language);
        _logger.LogDebug("[GodGPTShareController][CreateShareStringAsync] userId: {0} sessionId: {1}, ShareId={2}, duration: {3}ms",
            currentUserId, request.SessionId, response.ShareId, stopwatch.ElapsedMilliseconds);
        return response;
    }

    /// <summary>
    /// Get shared message list (anonymous access)
    /// </summary>
    [AllowAnonymous]
    [HttpGet("godgpt/share/{shareString}")]
    public async Task<List<ChatMessage>> GetShareMessageListAsync(string shareString)
    {
        var stopwatch = Stopwatch.StartNew();
        var language = HttpContext.GetGodGPTLanguage();
        var response = await _shareService.GetShareMessageListAsync(shareString, language);
        _logger.LogDebug("[GodGPTShareController][GetShareMessageListAsync] shareString: {0} duration: {1}ms",
            shareString, stopwatch.ElapsedMilliseconds);
        return response;
    }

    /// <summary>
    /// Get AI-generated share keywords for a session
    /// </summary>
    [HttpGet("godgpt/share/keyword")]
    public async Task<QuantumShareResponseDto> GetShareKeyWordWithAIAsync(
        [FromQuery] Guid sessionId,
        [FromQuery] string? content,
        [FromQuery] string? region,
        [FromQuery] SessionType sessionType)
    {
        var stopwatch = Stopwatch.StartNew();

        // Get language from request headers
        var language = HttpContext.GetGodGPTLanguage();

        // Append language-specific prompt requirement if content is provided
        var processedContent = SessionTypeExtensions.SharePrompt;
        processedContent = processedContent.AppendLanguagePrompt(language);
        var clientIp = HttpContext.GetClientIpAddress();
        var appType = HttpContext.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        RequestContext.Set("IsCN", isCN);
        var response = await _shareService.GetShareKeyWordWithAIAsync(sessionId, processedContent, region, sessionType, language);
        _logger.LogDebug(
            $"[GodGPTShareController][GetShareKeyWordWithAIAsync] completed for sessionId={sessionId}, language={language},processedContent={processedContent}, duration: {stopwatch.ElapsedMilliseconds}ms");
        return response;
    }
}
