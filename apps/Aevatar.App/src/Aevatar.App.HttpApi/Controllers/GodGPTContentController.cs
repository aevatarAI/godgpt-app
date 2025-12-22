using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services.Awakening;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.SpeechChat;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT content services.
/// Handles awakening content and other dynamic content delivery.
/// </summary>
[RemoteService]
[ControllerName("GodGPTContent")]
[Route("api")]
[Authorize]
public class GodGPTContentController : AevatarController
{
    private readonly IGodGPTAwakeningService _awakeningService;
    private readonly ILogger<GodGPTContentController> _logger;

    public GodGPTContentController(
        IGodGPTAwakeningService awakeningService,
        ILogger<GodGPTContentController> logger)
    {
        _awakeningService = awakeningService;
        _logger = logger;
    }

    /// <summary>
    /// Get today's awakening content
    /// </summary>
    /// <param name="region">Optional region parameter</param>
    /// <returns>Awakening content DTO</returns>
    [HttpGet("godgpt/awakening/today")]
    public async Task<AwakeningContentDto?> GetTodayAwakeningAsync([FromQuery] string? region)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        // Get language from header, default to English if not provided
        var languageHeader = HttpContext.Request.Headers["GodgptLanguage"].FirstOrDefault();
        var language = VoiceLanguageEnum.English; // Default value

        if (!string.IsNullOrEmpty(languageHeader))
        {
            // Try to parse the language from header
            if (Enum.TryParse<VoiceLanguageEnum>(languageHeader, true, out var parsedLanguage))
            {
                language = parsedLanguage;
            }
            else
            {
                _logger.LogWarning("[GodGPTContentController][GetTodayAwakeningAsync] Invalid language header: {Language}, using default: {Default}",
                    languageHeader, language);
            }
        }

        try
        {
            var result = await _awakeningService.GetTodayAwakeningAsync(currentUserId, language, region);

            _logger.LogDebug("[GodGPTContentController][GetTodayAwakeningAsync] userId: {UserId}, language: {Language}, region: {Region}, hasResult: {HasResult}, duration: {Duration}ms",
                currentUserId, language, region, result != null, stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTContentController][GetTodayAwakeningAsync] Error for userId: {UserId}, language: {Language}, region: {Region}",
                currentUserId, language, region);
            throw;
        }
    }
}
