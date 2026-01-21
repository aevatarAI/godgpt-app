using System.Threading.Tasks;
using Aevatar.App.Domain.Shared;
using Aevatar.AuthServer.Account.Dtos;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Identity;

namespace Aevatar.AuthServer.Account;

/// <summary>
/// Account management API controller.
/// Provides endpoints for user registration, email verification, password reset, and logout.
/// Supports multiple applications via appName parameter.
/// Note: Uses 'api/app/account' prefix to avoid conflicts with ABP's built-in AccountController
/// </summary>
[RemoteService]
[Route("api/app/account")]
public class AppAccountController : AbpController
{
    private readonly IAccountService _accountService;
    private readonly ILogger<AppAccountController> _logger;

    public AppAccountController(
        IAccountService accountService,
        ILogger<AppAccountController> logger)
    {
        _accountService = accountService;
        _logger = logger;
    }

    /// <summary>
    /// Register a new user account
    /// </summary>
    [HttpPost("register")]
    public async Task<IdentityUserDto> RegisterAsync([FromBody] RegisterDto input)
    {
        var language = GetLanguageFromHeader();
        return await _accountService.RegisterAsync(input, language);
    }

    /// <summary>
    /// Send registration verification code to email
    /// </summary>
    [HttpPost("send-register-code")]
    public async Task<SendRegisterCodeResponseDto> SendRegisterCodeAsync([FromBody] SendRegisterCodeDto input)
    {
        var language = GetLanguageFromHeader();

        try
        {
            await _accountService.SendRegisterCodeAsync(input, language);

            _logger.LogInformation("[AccountController] Verification code sent to {Email} for {AppName}",
                input.Email, input.AppName);

            return new SendRegisterCodeResponseDto
            {
                Success = true,
                Message = "Verification code sent successfully"
            };
        }
        catch (UserFriendlyException)
        {
            throw;
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "[AccountController] Error sending code to {Email}", input.Email);
            throw new UserFriendlyException("Failed to send verification code");
        }
    }

    /// <summary>
    /// Verify registration code
    /// </summary>
    [HttpPost("verify-register-code")]
    public async Task<bool> VerifyRegisterCodeAsync([FromBody] VerifyRegisterCodeDto input)
    {
        return await _accountService.VerifyRegisterCodeAsync(input);
    }

    /// <summary>
    /// Check if email is already registered
    /// </summary>
    [HttpPost("check-email-registered")]
    public async Task<bool> CheckEmailRegisteredAsync([FromBody] CheckEmailRegisteredDto input)
    {
        return await _accountService.CheckEmailRegisteredAsync(input);
    }

    /// <summary>
    /// Send password reset link to email
    /// Reads X-Is-CN header from proxy to determine reset URL (CN vs global)
    /// </summary>
    [HttpPost("send-password-reset-code")]
    public async Task SendPasswordResetCodeAsync([FromBody] SendPasswordResetCodeDto input)
    {
        var language = GetLanguageFromHeader();
        
        // Read CN location flag from proxy header (set by AccountProxyController)
        if (HttpContext.Request.Headers.TryGetValue("X-Is-CN", out var isCNHeader))
        {
            var isCN = string.Equals(isCNHeader.ToString(), "true", System.StringComparison.OrdinalIgnoreCase);
            RequestContext.Set("IsCN", isCN);
            _logger.LogDebug("[AccountController] X-Is-CN header: {IsCN}", isCN);
        }
        
        await _accountService.SendPasswordResetCodeAsync(input, language);
    }

    /// <summary>
    /// Verify password reset token is valid
    /// </summary>
    [HttpPost("verify-password-reset-token")]
    public async Task<bool> VerifyPasswordResetTokenAsync([FromBody] VerifyPasswordResetTokenDto input)
    {
        return await _accountService.VerifyPasswordResetTokenAsync(input);
    }

    /// <summary>
    /// Reset password using token from email
    /// </summary>
    [HttpPost("reset-password")]
    public async Task ResetPasswordAsync([FromBody] ResetPasswordDto input)
    {
        await _accountService.ResetPasswordAsync(input);
    }

    /// <summary>
    /// Logout user session.
    /// For JWT-based authentication, actual token invalidation happens client-side.
    /// This endpoint provides server-side audit logging.
    /// </summary>
    [HttpPost("logout")]
    public Task<LogoutResultDto> LogoutAsync()
    {
        var userId = CurrentUser.Id?.ToString() ?? "anonymous";
        _logger.LogInformation("[AccountController][LogoutAsync] User logged out: {UserId}", userId);

        return Task.FromResult(new LogoutResultDto
        {
            Success = true,
            Message = "Logged out successfully"
        });
    }

    /// <summary>
    /// Get language from request header (GodGPTLanguage)
    /// </summary>
    private GodGPTChatLanguage GetLanguageFromHeader()
    {
        if (HttpContext.Request.Headers.TryGetValue("GodGPTLanguage", out var languageHeader))
        {
            var langStr = languageHeader.ToString().ToLower();
            return langStr switch
            {
                "zh-cn" or "cn" or "chinese" => GodGPTChatLanguage.CN,
                "zh-tw" or "traditional" => GodGPTChatLanguage.TraditionalChinese,
                "es" or "spanish" => GodGPTChatLanguage.Spanish,
                _ => GodGPTChatLanguage.English
            };
        }
        return GodGPTChatLanguage.English;
    }
}

