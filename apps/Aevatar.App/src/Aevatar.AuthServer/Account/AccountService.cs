using System;
using System.Threading.Tasks;
using Aevatar.App.Domain.Shared;
using Aevatar.AuthServer.Account.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Guids;
using Volo.Abp.Identity;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Aevatar.AuthServer.Account;

/// <summary>
/// Interface for account management operations
/// </summary>
public interface IAccountService
{
    Task<IdentityUserDto> RegisterAsync(RegisterDto input, GodGPTChatLanguage language);
    Task SendRegisterCodeAsync(SendRegisterCodeDto input, GodGPTChatLanguage language);
    Task<bool> VerifyRegisterCodeAsync(VerifyRegisterCodeDto input);
    Task<bool> CheckEmailRegisteredAsync(CheckEmailRegisteredDto input);
    Task SendPasswordResetCodeAsync(SendPasswordResetCodeDto input, GodGPTChatLanguage language);
    Task<bool> VerifyPasswordResetTokenAsync(VerifyPasswordResetTokenDto input);
    Task ResetPasswordAsync(ResetPasswordDto input);
}

/// <summary>
/// Account management service with multi-app support
/// </summary>
public class AccountService : IAccountService, ITransientDependency
{
    private readonly IdentityUserManager _userManager;
    private readonly IAccountEmailer _accountEmailer;
    private readonly AccountOptions _accountOptions;
    private readonly IDistributedCache<string, string> _registerCodeCache;
    private readonly DistributedCacheEntryOptions _codeCacheOptions;
    private readonly ILogger<AccountService> _logger;
    private readonly IGuidGenerator _guidGenerator;
    private readonly IOptions<IdentityOptions> _identityOptions;

    public AccountService(
        IdentityUserManager userManager,
        IAccountEmailer accountEmailer,
        IOptionsSnapshot<AccountOptions> accountOptions,
        IDistributedCache<string, string> registerCodeCache,
        ILogger<AccountService> logger,
        IGuidGenerator guidGenerator,
        IOptions<IdentityOptions> identityOptions)
    {
        _userManager = userManager;
        _accountEmailer = accountEmailer;
        _registerCodeCache = registerCodeCache;
        _accountOptions = accountOptions.Value;
        _logger = logger;
        _guidGenerator = guidGenerator;
        _identityOptions = identityOptions;

        _codeCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_accountOptions.RegisterCodeDuration)
        };
    }

    public async Task<IdentityUserDto> RegisterAsync(RegisterDto input, GodGPTChatLanguage language)
    {
        // Verify code
        var cacheKey = GetRegisterCodeKey(input.AppName, input.EmailAddress);
        var storedCode = await _registerCodeCache.GetAsync(cacheKey);
        
        _logger.LogInformation(
            "[AccountService][RegisterAsync] Verifying code for Email={Email}, AppName={AppName}, CacheKey={CacheKey}, StoredCode={StoredCode}, InputCode={InputCode}",
            input.EmailAddress, input.AppName, cacheKey, 
            storedCode != null ? "EXISTS" : "NULL", 
            input.Code);
        
        if (storedCode != input.Code)
        {
            _logger.LogWarning(
                "[AccountService][RegisterAsync] Code mismatch! StoredCode={StoredCode}, InputCode={InputCode}",
                storedCode ?? "NULL", input.Code);
            var message = GetLocalizedMessage("InvalidVerificationCode", language);
            throw new UserFriendlyException(message);
        }

        // Create user
        var userName = string.IsNullOrWhiteSpace(input.UserName) 
            ? _guidGenerator.Create().ToString() 
            : input.UserName;
            
        var user = new IdentityUser(_guidGenerator.Create(), userName, input.EmailAddress);

        try
        {
            var result = await _userManager.CreateAsync(user, input.Password);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors);
                _logger.LogError("[AccountService][RegisterAsync] Failed to create user: {Errors}", errors);
                throw new UserFriendlyException(GetLocalizedMessage("RegistrationFailed", language));
            }
        }
        catch (Exception ex) when (ex is not UserFriendlyException)
        {
            var errorMessage = ex.Message.ToLower();
            if (errorMessage.Contains("username") && errorMessage.Contains("invalid"))
            {
                throw new UserFriendlyException(GetLocalizedMessage("InvalidUserName", language));
            }
            _logger.LogError(ex, "[AccountService][RegisterAsync] Error for email {Email}", input.EmailAddress);
            throw;
        }

        await _userManager.SetEmailAsync(user, input.EmailAddress);
        await _userManager.AddDefaultRolesAsync(user);

        // Remove used verification code
        await _registerCodeCache.RemoveAsync(GetRegisterCodeKey(input.AppName, input.EmailAddress));

        _logger.LogInformation("[AccountService][RegisterAsync] User registered: {Email}, App: {AppName}", 
            input.EmailAddress, input.AppName);

        return new IdentityUserDto
        {
            Id = user.Id,
            UserName = user.UserName,
            Email = user.Email
        };
    }

    public async Task SendRegisterCodeAsync(SendRegisterCodeDto input, GodGPTChatLanguage language)
    {
        // Check if email already registered
        var existingUser = await _userManager.FindByEmailAsync(input.Email);
        if (existingUser != null)
        {
            var message = GetLocalizedMessage("EmailAlreadyRegistered", language);
            throw new UserFriendlyException(message);
        }

        // Generate and store code
        var code = GenerateVerificationCode();
        var cacheKey = GetRegisterCodeKey(input.AppName, input.Email);
        
        await _registerCodeCache.SetAsync(cacheKey, code, _codeCacheOptions);
        
        // DEBUG: Log the actual code (remove in production!)
        _logger.LogWarning(
            "[DEBUG] Verification code for {Email}: {Code} (CacheKey={CacheKey})",
            input.Email, code, cacheKey);
        
        _logger.LogInformation(
            "[AccountService][SendRegisterCodeAsync] Code stored: Email={Email}, AppName={AppName}, CacheKey={CacheKey}, Duration={Duration}min",
            input.Email, input.AppName, cacheKey, _accountOptions.RegisterCodeDuration);

        // Send email
        await _accountEmailer.SendRegisterCodeAsync(input.Email, code, input.AppName, language);

        _logger.LogInformation("[AccountService][SendRegisterCodeAsync] Code sent to {Email} for app {AppName}", 
            input.Email, input.AppName);
    }

    public async Task<bool> VerifyRegisterCodeAsync(VerifyRegisterCodeDto input)
    {
        var appName = string.IsNullOrEmpty(input.AppName) ? "default" : input.AppName;
        var storedCode = await _registerCodeCache.GetAsync(GetRegisterCodeKey(appName, input.Email));
        return storedCode == input.Code;
    }

    public async Task<bool> CheckEmailRegisteredAsync(CheckEmailRegisteredDto input)
    {
        var existingUser = await _userManager.FindByEmailAsync(input.EmailAddress);
        return existingUser != null;
    }

    public async Task SendPasswordResetCodeAsync(SendPasswordResetCodeDto input, GodGPTChatLanguage language)
    {
        var user = await _userManager.FindByEmailAsync(input.Email);
        if (user == null)
        {
            _logger.LogWarning("[AccountService][SendPasswordResetCodeAsync] User not found: {Email}", input.Email);
            // Don't reveal whether user exists - silently return
            return;
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        await _accountEmailer.SendPasswordResetLinkAsync(user, input.Email, resetToken, input.AppName, language);

        _logger.LogInformation("[AccountService][SendPasswordResetCodeAsync] Reset link sent to {Email} for app {AppName}", 
            input.Email, input.AppName);
    }

    public async Task<bool> VerifyPasswordResetTokenAsync(VerifyPasswordResetTokenDto input)
    {
        var user = await _userManager.FindByIdAsync(input.UserId.ToString());
        if (user == null)
        {
            return false;
        }

        var isValid = await _userManager.VerifyUserTokenAsync(
            user,
            _identityOptions.Value.Tokens.PasswordResetTokenProvider,
            "ResetPassword",
            input.ResetToken);

        return isValid;
    }

    public async Task ResetPasswordAsync(ResetPasswordDto input)
    {
        var user = await _userManager.FindByIdAsync(input.UserId.ToString());
        if (user == null)
        {
            throw new UserFriendlyException("User not found");
        }

        var result = await _userManager.ResetPasswordAsync(user, input.ResetToken, input.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors);
            _logger.LogError("[AccountService][ResetPasswordAsync] Failed: {Errors}", errors);
            throw new UserFriendlyException("Failed to reset password");
        }

        _logger.LogInformation("[AccountService][ResetPasswordAsync] Password reset for user {UserId}", input.UserId);
    }

    private static string GenerateVerificationCode()
    {
        var random = new Random();
        var code = random.Next(0, 999999);
        return code.ToString("D6");
    }

    private static string GetRegisterCodeKey(string appName, string email)
    {
        // Normalize both appName and email to lowercase for consistent cache key
        var normalizedAppName = string.IsNullOrEmpty(appName) ? "default" : appName.ToLower();
        return $"RegisterCode_{normalizedAppName}_{email.ToLower()}";
    }

    private static string GetLocalizedMessage(string key, GodGPTChatLanguage language)
    {
        return (key, language) switch
        {
            ("InvalidVerificationCode", GodGPTChatLanguage.CN) => "验证码无效或已过期",
            ("InvalidVerificationCode", GodGPTChatLanguage.TraditionalChinese) => "驗證碼無效或已過期",
            ("InvalidVerificationCode", GodGPTChatLanguage.Spanish) => "Código de verificación inválido o caducado",
            ("InvalidVerificationCode", _) => "Invalid or expired verification code",

            ("EmailAlreadyRegistered", GodGPTChatLanguage.CN) => "该邮箱已注册",
            ("EmailAlreadyRegistered", GodGPTChatLanguage.TraditionalChinese) => "該郵箱已註冊",
            ("EmailAlreadyRegistered", GodGPTChatLanguage.Spanish) => "Este correo electrónico ya está registrado",
            ("EmailAlreadyRegistered", _) => "This email is already registered",

            ("RegistrationFailed", GodGPTChatLanguage.CN) => "注册失败，请重试",
            ("RegistrationFailed", GodGPTChatLanguage.TraditionalChinese) => "註冊失敗，請重試",
            ("RegistrationFailed", GodGPTChatLanguage.Spanish) => "Error en el registro, por favor intente de nuevo",
            ("RegistrationFailed", _) => "Registration failed, please try again",

            ("InvalidUserName", GodGPTChatLanguage.CN) => "用户名无效",
            ("InvalidUserName", GodGPTChatLanguage.TraditionalChinese) => "用戶名無效",
            ("InvalidUserName", GodGPTChatLanguage.Spanish) => "Nombre de usuario inválido",
            ("InvalidUserName", _) => "Invalid username",

            _ => key
        };
    }
}

