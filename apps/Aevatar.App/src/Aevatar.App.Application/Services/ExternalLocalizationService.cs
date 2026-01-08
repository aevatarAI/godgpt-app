using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aevatar.App.Application.Contracts.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Volo.Abp.DependencyInjection;

namespace Aevatar.App.Application.Services;

/// <summary>
/// External localization service that loads texts from JSON files
/// Directory structure: Localization/{resource}/{culture}.json
/// Example: Localization/lumen/en.json, Localization/lumen/zh-Hans.json
/// </summary>
public class ExternalLocalizationService : IExternalLocalizationService, ISingletonDependency
{
    private readonly ILogger<ExternalLocalizationService> _logger;
    private readonly string _localizationPath;
    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _cache;
    private readonly object _loadLock = new();

    public ExternalLocalizationService(
        ILogger<ExternalLocalizationService> logger,
        IHostEnvironment hostEnvironment)
    {
        _logger = logger;
        _cache = new ConcurrentDictionary<string, Dictionary<string, string>>();
        
        // Localization files are in the Content root / Localization
        _localizationPath = Path.Combine(hostEnvironment.ContentRootPath, "Localization");
        
        // Load all resources at startup
        LoadAllResources();
    }

    /// <inheritdoc />
    public Dictionary<string, string>? GetTexts(string resource, string culture)
    {
        var cacheKey = GetCacheKey(resource, culture);
        
        if (_cache.TryGetValue(cacheKey, out var texts))
        {
            return texts;
        }
        
        // Try to load if not in cache
        var loaded = LoadResource(resource, culture);
        return loaded;
    }

    /// <inheritdoc />
    public string GetText(string resource, string culture, string key)
    {
        var texts = GetTexts(resource, culture);
        
        if (texts != null && texts.TryGetValue(key, out var value))
        {
            return value;
        }
        
        // Fallback to English if not found
        if (culture != "en")
        {
            var enTexts = GetTexts(resource, "en");
            if (enTexts != null && enTexts.TryGetValue(key, out var enValue))
            {
                _logger.LogDebug("Text not found for {Resource}/{Culture}/{Key}, using English fallback", 
                    resource, culture, key);
                return enValue;
            }
        }
        
        _logger.LogWarning("Text not found for {Resource}/{Culture}/{Key}, using key as fallback", 
            resource, culture, key);
        return key;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableResources()
    {
        if (!Directory.Exists(_localizationPath))
        {
            return Enumerable.Empty<string>();
        }
        
        return Directory.GetDirectories(_localizationPath)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))!;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetAvailableCultures(string resource)
    {
        var resourcePath = Path.Combine(_localizationPath, resource);
        
        if (!Directory.Exists(resourcePath))
        {
            return Enumerable.Empty<string>();
        }
        
        return Directory.GetFiles(resourcePath, "*.json")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Where(name => !string.IsNullOrEmpty(name))!;
    }

    /// <inheritdoc />
    public void Reload()
    {
        lock (_loadLock)
        {
            _cache.Clear();
            LoadAllResources();
        }
        
        _logger.LogInformation("External localization resources reloaded");
    }

    private void LoadAllResources()
    {
        if (!Directory.Exists(_localizationPath))
        {
            _logger.LogWarning("Localization directory not found: {Path}", _localizationPath);
            return;
        }

        var resources = GetAvailableResources();
        
        foreach (var resource in resources)
        {
            var cultures = GetAvailableCultures(resource);
            
            foreach (var culture in cultures)
            {
                LoadResource(resource, culture);
            }
        }
        
        _logger.LogInformation("Loaded {Count} localization resources", _cache.Count);
    }

    private Dictionary<string, string>? LoadResource(string resource, string culture)
    {
        var filePath = Path.Combine(_localizationPath, resource, $"{culture}.json");
        
        if (!File.Exists(filePath))
        {
            _logger.LogDebug("Localization file not found: {FilePath}", filePath);
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            
            if (texts != null)
            {
                var cacheKey = GetCacheKey(resource, culture);
                _cache[cacheKey] = texts;
                _logger.LogDebug("Loaded localization resource: {Resource}/{Culture} ({Count} keys)", 
                    resource, culture, texts.Count);
                return texts;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load localization file: {FilePath}", filePath);
        }

        return null;
    }

    private static string GetCacheKey(string resource, string culture)
    {
        return $"{resource}:{culture}";
    }
}

