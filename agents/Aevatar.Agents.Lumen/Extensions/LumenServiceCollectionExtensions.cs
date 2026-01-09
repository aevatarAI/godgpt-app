using Aevatar.Agents.Lumen.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Lumen.Extensions;

/// <summary>
/// Extension methods for registering Lumen Agent services and configuration.
/// Use this in non-ABP hosts like Orleans Silo.
/// </summary>
public static class LumenServiceCollectionExtensions
{
    /// <summary>
    /// Adds Lumen Agent configuration options to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddLumenServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        // Configure Lumen Options from "Lumen" section in appsettings.json
        // Expected structure:
        // "Lumen": {
        //   "Prediction": { "PromptVersion": 28, "MaxRetryCount": 3, ... },
        //   "UserProfile": { "MaxProfileUpdatesPerWeek": 3, ... },
        //   "SolarTerm": { "DataFilePath": "..." }
        // }
        
        var lumenSection = configuration.GetSection("Lumen");
        
        // Prediction options
        var predictionSection = lumenSection.GetSection("Prediction");
        if (predictionSection.Exists())
        {
            services.Configure<LumenPredictionOptions>(predictionSection);
        }
        
        // UserProfile options
        var userProfileSection = lumenSection.GetSection("UserProfile");
        if (userProfileSection.Exists())
        {
            services.Configure<LumenUserProfileOptions>(userProfileSection);
        }
        
        // SolarTerm options (less commonly configured, uses environment variable as primary)
        var solarTermSection = lumenSection.GetSection("SolarTerm");
        if (solarTermSection.Exists())
        {
            services.Configure<LumenSolarTermOptions>(solarTermSection);
        }
        
        return services;
    }
}

