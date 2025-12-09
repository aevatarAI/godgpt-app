using System;
using System.Threading.Tasks;
using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Volo.Abp.DependencyInjection;

namespace Aevatar.AuthServer.Grants.Providers;

/// <summary>
/// Provider for Google OAuth token validation
/// </summary>
public class GoogleProvider : IGoogleProvider, ITransientDependency
{
    private readonly ILogger<GoogleProvider> _logger;
    private readonly IOptionsMonitor<GoogleOptions> _googleOptions;

    public GoogleProvider(
        ILogger<GoogleProvider> logger, 
        IOptionsMonitor<GoogleOptions> googleOptions)
    {
        _logger = logger;
        _googleOptions = googleOptions;
    }

    public async Task<GoogleJsonWebSignature.Payload?> ValidateGoogleTokenAsync(string idToken, string clientId)
    {
        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                Audience = new[] { clientId }
            };
            return await GoogleJsonWebSignature.ValidateAsync(idToken, settings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GoogleProvider] Validate Google token failed");
            return null;
        }
    }

    public Task<string> GetClientIdAsync(string? source, string? appId = null)
    {
        var options = _googleOptions.CurrentValue;

        // Check app-specific configuration first
        if (!string.IsNullOrWhiteSpace(appId) && 
            options.AppConfigs.TryGetValue(appId, out var appConfig))
        {
            var clientId = source switch
            {
                "ios" => !string.IsNullOrEmpty(appConfig.IOSClientId) 
                    ? appConfig.IOSClientId 
                    : options.IOSClientId,
                "android" => !string.IsNullOrEmpty(appConfig.AndroidClientId) 
                    ? appConfig.AndroidClientId 
                    : options.AndroidClientId,
                _ => !string.IsNullOrEmpty(appConfig.ClientId) 
                    ? appConfig.ClientId 
                    : options.ClientId
            };
            return Task.FromResult(clientId);
        }

        // Fall back to default configuration
        var defaultClientId = source switch
        {
            "ios" => options.IOSClientId,
            "android" => options.AndroidClientId,
            _ => options.ClientId
        };

        return Task.FromResult(defaultClientId);
    }
}

