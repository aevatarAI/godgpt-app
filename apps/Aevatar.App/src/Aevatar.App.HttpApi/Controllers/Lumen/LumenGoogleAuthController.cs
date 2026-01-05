using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Lumen;
using Aevatar.App.Lumen.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers.Lumen;

/// <summary>
/// Lumen Google Auth Controller
/// </summary>
[RemoteService]
[Route("api/lumen/google")]
[Authorize]
public class LumenGoogleAuthController : AppController
{
    private readonly ILogger<LumenGoogleAuthController> _logger;
    private readonly ILumenService _lumenService;

    public LumenGoogleAuthController(
        ILogger<LumenGoogleAuthController> logger, 
        ILumenService lumenService)
    {
        _logger = logger;
        _lumenService = lumenService;
    }
    
    /// <summary>
    /// Verify Google OAuth authorization code and bind Google account
    /// </summary>
    [HttpPost("verify-code")]
    public async Task<GoogleAuthResultDto> GoogleAuthVerifyCodeAsync([FromBody] GoogleAuthVerifyCodeInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = CurrentUser.Id?.ToString() ?? throw new UserFriendlyException("User not authenticated");
        
        // Transform platform to "lumen.{platform}" format
        var lumenPlatform = $"lumen.{input.Platform}";
        var modifiedInput = new GoogleAuthVerifyCodeInput
        {
            Platform = lumenPlatform,
            Code = input.Code,
            RedirectUri = input.RedirectUri,
            CodeVerifier = input.CodeVerifier
        };
        
        var resultDto = await _lumenService.GoogleAuthVerifyCodeAsync(currentUserId, modifiedInput);
        
        _logger.LogDebug("[LumenGoogleAuthController][GoogleAuthVerifyCodeAsync] userId: {UserId}, platform: {Platform}, duration: {Duration}ms",
            currentUserId, lumenPlatform, stopwatch.ElapsedMilliseconds);
        
        return resultDto;
    }
    
    /// <summary>
    /// Unbind Google account for Lumen user
    /// </summary>
    [HttpDelete("unbind")]
    public async Task<bool> GoogleAuthUnbindAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = CurrentUser.Id?.ToString() ?? throw new UserFriendlyException("User not authenticated");
        
        var result = await _lumenService.GoogleAuthUnbindAsync(currentUserId);
        
        _logger.LogDebug("[LumenGoogleAuthController][GoogleAuthUnbindAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
    
    /// <summary>
    /// Get Google account binding status for Lumen user
    /// </summary>
    [HttpGet("bind-status")]
    public async Task<GoogleBindStatusDto> GoogleAuthBindStatusAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = CurrentUser.Id?.ToString() ?? throw new UserFriendlyException("User not authenticated");
        
        var result = await _lumenService.GoogleAuthBindStatusAsync(currentUserId);
        
        _logger.LogDebug("[LumenGoogleAuthController][GoogleAuthBindStatusAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
}
