using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Aevatar.AuthServer.Grants.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace Aevatar.AuthServer.Grants;

/// <summary>
/// Grant handler for Apple Sign In OAuth flow
/// </summary>
public class AppleGrantHandler : GrantHandlerBase, ITransientDependency
{
    private readonly ILogger<AppleGrantHandler> _logger;
    private readonly IAppleProvider _appleProvider;

    public override string Name => GrantTypeConstants.APPLE;

    public AppleGrantHandler(IAppleProvider appleProvider, ILogger<AppleGrantHandler> logger)
    {
        _appleProvider = appleProvider;
        _logger = logger;
    }

    public override async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        try
        {
            var code = context.Request.GetParameter("code")?.ToString();
            var idToken = context.Request.GetParameter("id_token")?.ToString();
            var source = context.Request.GetParameter("source")?.ToString();
            var platform = context.Request.GetParameter("platform")?.ToString() ?? string.Empty;
            var appId = context.Request.GetParameter("apple_app_id")?.ToString();

            // Default app ID for backward compatibility
            if (string.IsNullOrWhiteSpace(appId))
            {
                appId = "com.gpt.god";
            }

            _logger.LogDebug("[AppleGrantHandler] HandleAsync: source={Source}, platform={Platform}, appId={AppId}",
                source, platform, appId);

            var appleOptions = context.HttpContext.RequestServices
                .GetRequiredService<IOptionsMonitor<AppleOptions>>();
                
            if (!appleOptions.CurrentValue.APPs.TryGetValue(appId, out var appOptions))
            {
                _logger.LogWarning("[AppleGrantHandler] Invalid apple_app_id: {AppId}", appId);
                return CreateForbidResult("Invalid apple_app_id");
            }

            // Exchange code for token if no id_token provided
            if (string.IsNullOrEmpty(idToken))
            {
                if (string.IsNullOrEmpty(code))
                {
                    _logger.LogDebug("[AppleGrantHandler] Missing both id_token and code");
                    return CreateForbidResult("Missing both id_token and code");
                }

                idToken = await _appleProvider.ExchangeCodeForTokenAsync(code, source, platform, appOptions);
                if (string.IsNullOrEmpty(idToken))
                {
                    return CreateForbidResult("Code invalid or expired");
                }
            }

            // Validate Apple token
            var (isValid, principal) = await _appleProvider.ValidateAppleTokenAsync(idToken, source, appOptions);
            if (!isValid || principal == null)
            {
                return CreateForbidResult("Invalid Apple token");
            }

            var appleUser = ExtractAppleUser(principal);
            var email = appleUser.Email;
            
            _logger.LogDebug("[AppleGrantHandler] Validated token for email={Email}", email);

            var userManager = context.HttpContext.RequestServices.GetRequiredService<IdentityUserManager>();

            var isNewUser = false;
            var user = await userManager.FindByLoginAsync(GrantTypeConstants.APPLE, appleUser.SubjectId!);

            if (user == null)
            {
                // Try to find by username (legacy compatibility)
                var name = email + "@" + GrantTypeConstants.APPLE;
                user = await userManager.FindByNameAsync(name);

                // Try to find by email
                if (user == null && !string.IsNullOrWhiteSpace(email))
                {
                    user = await userManager.FindByEmailAsync(email);
                }

                // Create new user if not found
                if (user == null)
                {
                    isNewUser = true;
                    name = Guid.NewGuid().ToString("N");
                    user = new IdentityUser(
                        Guid.NewGuid(),
                        name,
                        email: string.IsNullOrWhiteSpace(email) ? $"{name}@apple.com" : email);

                    await userManager.CreateAsync(user);
                    _logger.LogInformation("[AppleGrantHandler] Created new user: {UserId}", user.Id);
                }

                // Link Apple login to user
                await userManager.AddLoginAsync(user, new UserLoginInfo(
                    GrantTypeConstants.APPLE,
                    appleUser.SubjectId!,
                    GrantTypeConstants.APPLE));
            }

            var claimsPrincipal = await CreateUserClaimsPrincipalWithFactoryAsync(context, user, isNewUser);
            return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, claimsPrincipal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AppleGrantHandler] Apple login failed");
            return CreateForbidResult("Internal server error");
        }
    }

    private AppleUserInfo ExtractAppleUser(ClaimsPrincipal principal)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var firstName = principal.FindFirstValue(ClaimTypes.GivenName);
        var lastName = principal.FindFirstValue(ClaimTypes.Surname);
        var sub = principal.FindFirstValue(ClaimTypes.NameIdentifier);

        // Handle Apple's private relay email
        if (string.IsNullOrWhiteSpace(email) || email.EndsWith("@privaterelay.appleid.com"))
        {
            email = $"{sub}@apple.privaterelay.com";
        }

        return new AppleUserInfo
        {
            SubjectId = sub,
            Email = email,
            FirstName = firstName,
            LastName = lastName
        };
    }
}

