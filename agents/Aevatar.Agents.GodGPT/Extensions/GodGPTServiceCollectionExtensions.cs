using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.UserFeedback.Options;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.GodGPT.Extensions;

/// <summary>
/// Extension methods for registering GodGPT Agent services and configuration.
/// Use this in non-ABP hosts like Orleans Silo.
/// </summary>
public static class GodGPTServiceCollectionExtensions
{
    private static readonly IReadOnlyDictionary<string, string> OptionsSectionNameOverrides =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The configuration section is pluralized in appsettings.json
            [nameof(RolePromptOptions)] = "RolePrompts",
        };

    /// <summary>
    /// Adds GodGPT Agent services and configuration to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration root.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddGodGPTServices(
        this IServiceCollection services, 
        IConfiguration configuration)
    {
        AddGodGPTOptionsByConvention(services, configuration);
        
        // Post processors for complex configuration
        services.AddSingleton<IPostConfigureOptions<GooglePayOptions>, GooglePayOptionsPostProcessor>();
        
        // Register services
        services.AddSingleton<ISpeechService, SpeechService>();
        services.AddSingleton<IGooglePayService, GooglePayService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        
        // HttpClient factory
        services.AddHttpClient();
        
        return services;
    }

    private static void AddGodGPTOptionsByConvention(IServiceCollection services, IConfiguration configuration)
    {
        // We scan a small, explicit set of assemblies to keep startup predictable.
        // Add more assemblies here only if new Options types are introduced outside these.
        var assembliesToScan = new[]
        {
            typeof(CreditsOptions).Assembly,  // Aevatar.Application.Grains.* options
            typeof(AwakeningOptions).Assembly, // GodGPT.GAgents.* options
            typeof(SpeechOptions).Assembly, // GodGPT.GAgents.* options
        };

        var optionTypes = assembliesToScan
            .Distinct()
            .SelectMany(a => a.ExportedTypes)
            .Where(t => t is { IsClass: true, IsAbstract: false, IsGenericType: false })
            .Where(t => t.Name.EndsWith("Options", StringComparison.Ordinal))
            .ToList();

        var configureMethod = ResolveConfigureGenericMethod();

        foreach (var optionType in optionTypes)
        {
            var sectionName = ResolveSectionName(optionType);
            var section = configuration.GetSection(sectionName);
            if (!section.Exists())
            {
                continue;
            }

            configureMethod
                .MakeGenericMethod(optionType)
                .Invoke(null, new object[] { services, section });
        }
    }

    private static string ResolveSectionName(Type optionType)
    {
        if (OptionsSectionNameOverrides.TryGetValue(optionType.Name, out var overrideName))
        {
            return overrideName;
        }

        const string suffix = "Options";
        return optionType.Name.EndsWith(suffix, StringComparison.Ordinal)
            ? optionType.Name[..^suffix.Length]
            : optionType.Name;
    }

    private static MethodInfo ResolveConfigureGenericMethod()
    {
        // We want: public static IServiceCollection Configure<TOptions>(this IServiceCollection services, IConfiguration config)
        // from Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions
        var candidates = typeof(OptionsConfigurationServiceCollectionExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name == "Configure")
            .Where(m => m.IsGenericMethodDefinition)
            .Where(m =>
            {
                var parameters = m.GetParameters();
                return parameters.Length == 2
                       && parameters[0].ParameterType == typeof(IServiceCollection)
                       && typeof(IConfiguration).IsAssignableFrom(parameters[1].ParameterType);
            })
            .ToList();

        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                $"Unable to resolve Configure<TOptions>(IServiceCollection, IConfiguration). Found {candidates.Count} candidates.");
        }

        return candidates[0];
    }
}

