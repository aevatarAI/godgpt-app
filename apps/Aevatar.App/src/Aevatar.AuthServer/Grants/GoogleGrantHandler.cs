using System;
using System.Threading.Tasks;
using Aevatar.AuthServer.Grants.Providers;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using IdentityUser = Volo.Abp.Identity.IdentityUser;
using SignInResult = Microsoft.AspNetCore.Mvc.SignInResult;

namespace Aevatar.AuthServer.Grants;

/// <summary>
/// Grant handler for Google Sign In OAuth flow
/// </summary>
public class GoogleGrantHandler : GrantHandlerBase, ITransientDependency
{
    private readonly ILogger<GoogleGrantHandler> _logger;
    private readonly IGoogleProvider _googleProvider;

    public override string Name => GrantTypeConstants.GOOGLE;

    public GoogleGrantHandler(IGoogleProvider googleProvider, ILogger<GoogleGrantHandler> logger)
    {
        _googleProvider = googleProvider;
        _logger = logger;
    }

    public override async Task<IActionResult> HandleAsync(ExtensionGrantContext context)
    {
        var idToken = context.Request.GetParameter("id_token")?.ToString();
        var source = context.Request.GetParameter("source")?.ToString();
        var appId = context.Request.GetParameter("google_app_id")?.ToString();

        _logger.LogDebug("[GoogleGrantHandler] HandleAsync: source={Source}, appId={AppId}", source, appId);

        if (string.IsNullOrEmpty(idToken))
        {
            return CreateForbidResult("Missing id_token parameter");
        }

        var clientId = await _googleProvider.GetClientIdAsync(source, appId);
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogWarning("[GoogleGrantHandler] Client ID not found for source={Source}, appId={AppId}", source, appId);
            return CreateForbidResult("Client ID not found");
        }

        _logger.LogDebug("[GoogleGrantHandler] Using clientId={ClientId}", clientId);

        var payload = await _googleProvider.ValidateGoogleTokenAsync(idToken, clientId);
        if (payload == null)
        {
            return CreateForbidResult(OpenIddictConstants.Errors.InvalidGrant, "Invalid Google token");
        }

        var email = payload.Email;
        _logger.LogDebug("[GoogleGrantHandler] Validated token for email={Email}", email);

        var userManager = context.HttpContext.RequestServices.GetRequiredService<IdentityUserManager>();

        var isNewUser = false;
        var user = await userManager.FindByLoginAsync(GrantTypeConstants.GOOGLE, payload.Subject);
        
        if (user == null)
        {
            // Try to find by username (legacy compatibility)
            var name = email + "@" + GrantTypeConstants.GOOGLE;
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
                    email: string.IsNullOrWhiteSpace(email) ? $"{name}@google.com" : email);
                    
                await userManager.CreateAsync(user);
                _logger.LogInformation("[GoogleGrantHandler] Created new user: {UserId}", user.Id);
            }

            // Link Google login to user
            await userManager.AddLoginAsync(user, new UserLoginInfo(
                GrantTypeConstants.GOOGLE,
                payload.Subject,
                GrantTypeConstants.GOOGLE));
        }

        var claimsPrincipal = await CreateUserClaimsPrincipalWithFactoryAsync(context, user, isNewUser);
        return new SignInResult(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme, claimsPrincipal);
    }
}

