using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using Volo.Abp.Identity;
using Volo.Abp.OpenIddict;
using Volo.Abp.OpenIddict.ExtensionGrantTypes;
using IdentityUser = Volo.Abp.Identity.IdentityUser;

namespace Aevatar.AuthServer.Grants;

/// <summary>
/// Base class for OAuth extension grant handlers
/// </summary>
public abstract class GrantHandlerBase : ITokenExtensionGrant
{
    public abstract string Name { get; }

    public abstract Task<IActionResult> HandleAsync(ExtensionGrantContext context);

    protected ForbidResult CreateForbidResult(string errorDescription)
    {
        return CreateForbidResult(OpenIddictConstants.Errors.InvalidRequest, errorDescription);
    }

    protected ForbidResult CreateForbidResult(string errorType, string errorDescription)
    {
        return new ForbidResult(
            new[] { OpenIddictServerAspNetCoreDefaults.AuthenticationScheme },
            properties: new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = errorType,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = errorDescription
            }));
    }

    protected async Task<IEnumerable<string>> GetResourcesAsync(
        ExtensionGrantContext context, ImmutableArray<string> scopes)
    {
        var resources = new List<string>();
        if (!scopes.Any())
        {
            return resources;
        }

        var scopeManager = context.HttpContext.RequestServices
            .GetRequiredService<IOpenIddictScopeManager>();
            
        await foreach (var resource in scopeManager.ListResourcesAsync(scopes))
        {
            resources.Add(resource);
        }

        return resources;
    }

    /// <summary>
    /// Create claims principal for user with factory support
    /// </summary>
    protected async Task<ClaimsPrincipal> CreateUserClaimsPrincipalWithFactoryAsync(
        ExtensionGrantContext context, IdentityUser user, bool isNewUser = false)
    {
        var signInManager = context.HttpContext.RequestServices
            .GetRequiredService<SignInManager<IdentityUser>>();
        var claimsPrincipal = await signInManager.CreateUserPrincipalAsync(user);

        // Add custom claim for new user detection
        claimsPrincipal.AddClaim("is_new_user", isNewUser);

        claimsPrincipal.SetScopes(context.Request.GetScopes());
        claimsPrincipal.SetResources(await GetResourcesAsync(context, claimsPrincipal.GetScopes()));
        claimsPrincipal.SetAudiences("Aevatar");

        await context.HttpContext.RequestServices
            .GetRequiredService<AbpOpenIddictClaimsPrincipalManager>()
            .HandleAsync(context.Request, claimsPrincipal);

        return claimsPrincipal;
    }
}

/// <summary>
/// Extension methods for ClaimsPrincipal
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public static void AddClaim(this ClaimsPrincipal principal, string type, bool value)
    {
        var identity = principal.Identity as ClaimsIdentity;
        identity?.AddClaim(new Claim(type, value.ToString().ToLowerInvariant()));
    }
}

