using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.Account;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Domain.Shared;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.App.Application.Services;
using Aevatar.Application.Constants;
using Aevatar.Common;
using Aevatar.Extensions;
using Aevatar.Services;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;
using Volo.Abp.Account;
using Volo.Abp.Identity;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for account registration and password management.
/// Handles user registration, email verification, and password reset.
/// </summary>
[RemoteService]
[ControllerName("Account")]
[Route("api/account")]
public class AccountController : AevatarController
{
    private readonly IAccountService _accountService;
    private readonly ISecurityService _securityService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<AccountController> _logger;
    private readonly IIpLocationService _ipLocationService;

    public AccountController(
        IAccountService accountService,
        ISecurityService securityService,
        ILocalizationService localizationService,
        ILogger<AccountController> logger,
        IIpLocationService ipLocationService)
    {
        _accountService = accountService;
        _securityService = securityService;
        _localizationService = localizationService;
        _logger = logger;
        _ipLocationService = ipLocationService;
    }
    
    /// <summary>
    /// Register a new account
    /// </summary>
    [HttpPost("register")]
    public virtual Task<IdentityUserDto> RegisterAsync(AevatarRegisterDto input)
    {
        var language = HttpContext.GetGodGPTLanguage();
        return _accountService.RegisterAsync(input, language);
    }
    
    /// <summary>
    /// Register a new GodGPT account
    /// </summary>
    [HttpPost("godgpt-register")]
    public virtual Task<IdentityUserDto> GodgptRegisterAsync(GodGptRegisterDto input)
    {
        var language = HttpContext.GetGodGPTLanguage();
        return _accountService.GodgptRegisterAsync(input, language);
    }
    
    /// <summary>
    /// Send registration verification code with security verification
    /// </summary>
    [HttpPost("send-register-code")]
    public virtual async Task<SendRegisterCodeResponseDto> SendRegisterCodeAsync(SendRegisterCodeDto input)
    {
        var language = HttpContext.GetGodGPTLanguage();

        try
        {
            // Perform security verification (rate limiting + platform-based verification)
            await _securityService.ValidateSecurityAsync(
                HttpContext, 
                input.Platform, 
                input.RecaptchaToken, 
                nameof(SendRegisterCodeAsync),
                _localizationService,
                language,
                _logger);
            
            // Call the account service to send verification code
            await _accountService.SendRegisterCodeAsync(input, language);
            
            _logger.LogInformation("Verification code sent successfully for email {email}", input.Email);
            
            return new SendRegisterCodeResponseDto
            {
                Success = true,
                Message = "Verification code sent successfully"
            };
        }
        catch (UserFriendlyException)
        {
            // Re-throw UserFriendlyException as-is (already localized)
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending registration code for email {email}", 
                input.Email);
            
            // Return localized internal server error
            throw new UserFriendlyException(
                _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, 
                    language, new Dictionary<string, string>()));
        }
    }

    /// <summary>
    /// Verify registration code
    /// </summary>
    [HttpPost("verify-register-code")]
    public virtual Task<bool> VerifyRegisterCodeAsync(VerifyRegisterCodeDto input)
    {
        return _accountService.VerifyRegisterCodeAsync(input);
    }

    /// <summary>
    /// Send password reset code
    /// </summary>
    [HttpPost("send-password-reset-code")]
    public virtual async Task SendPasswordResetCodeAsync(SendPasswordResetCodeDto input)
    {
        var clientIp = HttpContext.GetClientIpAddress();
        var appType = HttpContext.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        RequestContext.Set("IsCN", isCN);
        var language = HttpContext.GetGodGPTLanguage();
        await _accountService.SendPasswordResetCodeAsync(input, language);
    }

    /// <summary>
    /// Verify password reset token
    /// </summary>
    [HttpPost("verify-password-reset-token")]
    public virtual Task<bool> VerifyPasswordResetTokenAsync(VerifyPasswordResetTokenInput input)
    {
        return _accountService.VerifyPasswordResetTokenAsync(input);
    }

    /// <summary>
    /// Reset password
    /// </summary>
    [HttpPost("reset-password")]
    public virtual Task ResetPasswordAsync(ResetPasswordDto input)
    {
        return _accountService.ResetPasswordAsync(input);
    }
    
    /// <summary>
    /// Check if email is already registered
    /// </summary>
    [HttpPost("check-email-registered")]
    public virtual Task<bool> CheckEmailRegisteredAsync(CheckEmailRegisteredDto input)
    {
        return _accountService.CheckEmailRegisteredAsync(input);
    }

    /// <summary>
    /// Apple authentication callback endpoint (AppleAuthController compatibility)
    /// Note: Apple Sign In is primarily handled through OpenIddict Grant Handler.
    /// This endpoint is kept for backward compatibility.
    /// </summary>
    [HttpPost("/api/apple/{platform}/callback")]
    public Task<IActionResult> AppleAuthCallbackAsync(string platform, [FromForm] object appleAuthCallbackDto)
    {
        // Apple authentication is handled through OpenIddict Grant Handler
        // This endpoint is kept for backward compatibility
        return Task.FromResult<IActionResult>(BadRequest("Apple authentication is handled through OpenIddict. Please use the standard OAuth2 flow."));
    }
}

