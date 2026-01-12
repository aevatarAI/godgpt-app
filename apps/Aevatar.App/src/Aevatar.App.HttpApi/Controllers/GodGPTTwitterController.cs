using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.GodGPT.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// GodGPT Twitter Controller - User-level Twitter OAuth2 operations
/// Separated from InvitationController to avoid PaymentService dependency chain
/// </summary>
[RemoteService]
[ControllerName("Twitter")]
[Route("api/godgpt/invitation/twitter")]
[Authorize]
public class GodGPTTwitterController : AevatarController
{
    private readonly ILogger<GodGPTTwitterController> _logger;
    private readonly ITwitterService _twitterService;

    public GodGPTTwitterController(
        ILogger<GodGPTTwitterController> logger,
        ITwitterService twitterService)
    {
        _logger = logger;
        _twitterService = twitterService;
    }
    
    /// <summary>
    /// Get Twitter OAuth2 PKCE authentication parameters
    /// </summary>
    [HttpGet("params")]
    public async Task<TwitterAuthParamsDto> GetTwitterAuthParamsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.GetAuthParamsAsync(currentUserId);
        _logger.LogDebug("[GodGPTTwitterController][GetTwitterAuthParamsAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Verify Twitter OAuth2 authorization code and bind account
    /// </summary>
    [HttpPost("verify")]
    public async Task<TwitterAuthResultDto> TwitterAuthVerifyAsync(TwitterAuthVerifyInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.VerifyAuthCodeAsync(currentUserId, input);
        _logger.LogDebug("[GodGPTTwitterController][TwitterAuthVerifyAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Get current Twitter account bind status
    /// </summary>
    [HttpGet("bind-status")]
    public async Task<TwitterBindStatusDto> GetTwitterBindStatusAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.GetBindStatusAsync(currentUserId);
        _logger.LogDebug("[GodGPTTwitterController][GetTwitterBindStatusAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
    
    /// <summary>
    /// Unbind Twitter account from user
    /// </summary>
    [HttpPost("unbind")]
    public async Task<TwitterOperationResultDto> UnbindTwitterAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var response = await _twitterService.UnbindTwitterAsync(currentUserId);
        _logger.LogDebug("[GodGPTTwitterController][UnbindTwitterAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        return response;
    }
}
