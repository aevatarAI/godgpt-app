using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.Anonymous;
using Aevatar.Application.Constants;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Services;
using Aevatar.App.Application.Contracts.Services.Guest;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.App.HttpApi.Extensions;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT guest/anonymous user chat functionality.
/// All endpoints allow anonymous access based on IP identification.
/// </summary>
[RemoteService]
[ControllerName("GodGPTGuest")]
[Route("api")]
[AllowAnonymous]
public class GodGPTGuestController : AevatarController
{
    private readonly IGodGPTGuestService _guestService;
    private readonly ILogger<GodGPTGuestController> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IIpLocationService _ipLocationService;

    public GodGPTGuestController(
        IGodGPTGuestService guestService,
        ILogger<GodGPTGuestController> logger,
        ILocalizationService localizationService,
        IIpLocationService ipLocationService)
    {
        _guestService = guestService;
        _logger = logger;
        _localizationService = localizationService;
        _ipLocationService = ipLocationService;
    }

    /// <summary>
    /// Create guest session for anonymous users
    /// </summary>
    /// <param name="request">Guest session creation request</param>
    /// <returns>Session creation result with remaining chat limits</returns>
    [HttpPost("godgpt/guest/create-session")]
    public async Task<IActionResult> CreateGuestSessionAsync([FromBody] CreateGuestSessionRequestDto request)
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = HttpContext.GetClientIpAddress();
        var userHashId = CommonHelper.GetAnonymousUserGAgentId(clientIp).Replace("AnonymousUser_", "");

        try
        {
            var appType = HttpContext.GetGodGPTAppType();
            var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
            RequestContext.Set("IsCN", isCN);
            
            // Always check limits first to provide graceful response
            var limits = await _guestService.GetGuestChatLimitsAsync(clientIp);

            // If no remaining chats, return limits info without creating session
            if (limits.RemainingChats <= 0)
            {
                _logger.LogDebug("[GodGPTGuestController][CreateGuestSessionAsync] User: {0} has no remaining chats, returning limits", userHashId);
                return Ok(new CreateGuestSessionResponseDto
                {
                    RemainingChats = limits.RemainingChats,
                    TotalAllowed = limits.TotalAllowed
                });
            }

            // User has remaining chats, proceed with session creation
            var result = await _guestService.CreateGuestSessionAsync(clientIp, request.Guider);
            _logger.LogDebug("[GodGPTGuestController][CreateGuestSessionAsync] User: {0}, guider: {1}, remaining: {2}, duration: {3}ms",
                userHashId, request.Guider, result.RemainingChats, stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTGuestController][CreateGuestSessionAsync] User: {0}, unexpected error", userHashId);
            // Return default limits instead of error
            return Ok(new CreateGuestSessionResponseDto
            {
                RemainingChats = 0,
                TotalAllowed = 3
            });
        }
    }

    /// <summary>
    /// Get chat limits for anonymous users
    /// </summary>
    /// <returns>Guest chat limits information</returns>
    [HttpGet("godgpt/guest/limits")]
    public async Task<IActionResult> GetGuestChatLimitsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var clientIp = HttpContext.GetClientIpAddress();
        var userHashId = CommonHelper.GetAnonymousUserGAgentId(clientIp).Replace("AnonymousUser_", "");
        var language = HttpContext.GetGodGPTLanguage();
        
        try
        {
            var result = await _guestService.GetGuestChatLimitsAsync(clientIp);
            _logger.LogDebug("[GodGPTGuestController][GetGuestChatLimitsAsync] User: {0}, remaining: {1}, duration: {2}ms",
                userHashId, result.RemainingChats, stopwatch.ElapsedMilliseconds);

            return Ok(result);
        }
        catch (Exception ex)
        {
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language);
            _logger.LogError(ex, "[GodGPTGuestController][GetGuestChatLimitsAsync] User: {0}, unexpected error", userHashId);
            return StatusCode(500, new { error = localizedMessage });
        }
    }
}
