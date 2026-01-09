using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Options;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Volo.Abp.DependencyInjection;

namespace Aevatar.App.Application.Services;

/// <summary>
/// Service for loading external localization JSON files from disk
/// Supports both flat (Lumen) and nested (GodGPT) JSON structures
/// </summary>
public class ExternalLocalizationService : IExternalLocalizationService, ISingletonDependency
{
    private readonly ILogger<ExternalLocalizationService> _logger;
    private readonly string _basePath;
    
    // Unified cache (stores as object to support both flat and nested)
    private readonly Dictionary<string, Dictionary<string, object>> _cache = new();
    private readonly Dictionary<string, Dictionary<string, DateTime>> _cacheTimestamps = new();
    private readonly object _cacheLock = new();

    public ExternalLocalizationService(
        ILogger<ExternalLocalizationService> logger,
        IHostEnvironment hostEnvironment,
        IOptions<ExternalLocalizationOptions> options)
    {
        _logger = logger;
        
        // Use configured BasePath, or fallback to ContentRootPath/Localization
        var configuredPath = options.Value.BasePath;
        if (!string.IsNullOrEmpty(configuredPath) && Directory.Exists(configuredPath))
        {
            _basePath = configuredPath;
        }
        else
        {
            // Fallback to local Localization folder (for development)
            _basePath = Path.Combine(hostEnvironment.ContentRootPath, "Localization");
        }
        
        _logger.LogInformation("[ExternalLocalization] Initialized with BasePath: {BasePath}", _basePath);
    }

    public object GetTexts(string resourceName, string cultureName)
    {
        lock (_cacheLock)
        {
            var filePath = Path.Combine(_basePath, resourceName.ToLower(), $"{cultureName}.json");
            
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("[ExternalLocalization] File not found: {FilePath}", filePath);
                // Return appropriate empty structure based on resource type
                return IsNestedResource(resourceName) 
                    ? new Dictionary<string, object>() 
                    : new Dictionary<string, string>();
            }
            
            var currentFileTime = File.GetLastWriteTimeUtc(filePath);
            
            // Check cache validity
            if (_cache.TryGetValue(resourceName, out var resourceCache) &&
                resourceCache.TryGetValue(cultureName, out var cachedTexts))
            {
                if (_cacheTimestamps.TryGetValue(resourceName, out var timestampCache) &&
                    timestampCache.TryGetValue(cultureName, out var cachedTime))
                {
                    if (cachedTime == currentFileTime)
                    {
                        return cachedTexts;
                    }
                    
                    _logger.LogInformation("[ExternalLocalization] File modified, reloading {Resource}/{Culture}", 
                        resourceName, cultureName);
                }
            }
            
            // Load from file
            var loadedTexts = LoadTextsFromFile(resourceName, cultureName, filePath);
            
            // Update cache
            if (!_cache.ContainsKey(resourceName))
            {
                _cache[resourceName] = new Dictionary<string, object>();
            }
            _cache[resourceName][cultureName] = loadedTexts;
            
            // Update timestamp
            if (!_cacheTimestamps.ContainsKey(resourceName))
            {
                _cacheTimestamps[resourceName] = new Dictionary<string, DateTime>();
            }
            _cacheTimestamps[resourceName][cultureName] = currentFileTime;
            
            return loadedTexts;
        }
    }

    public Dictionary<string, object> GetAllTexts(string cultureName)
    {
        var result = new Dictionary<string, object>();
        var resourceNames = new[] { "godgpt", "lumen" };
        
        foreach (var resourceName in resourceNames)
        {
            var texts = GetTexts(resourceName, cultureName);
            if (texts != null)
            {
                result[resourceName] = texts;
            }
        }
        
        return result;
    }

    /// <summary>
    /// Determine if a resource uses nested structure
    /// - GodGPT: nested structure
    /// - Lumen: flat structure
    /// </summary>
    private bool IsNestedResource(string resourceName)
    {
        return resourceName.Equals("godgpt", StringComparison.OrdinalIgnoreCase);
    }

    private object LoadTextsFromFile(string resourceName, string cultureName, string filePath)
    {
        try
        {
            _logger.LogInformation("[ExternalLocalization] Loading: {Resource}/{Culture} from {FilePath}", 
                resourceName, cultureName, filePath);
            
            var jsonContent = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(jsonContent);
            
            // Check for "texts" property (ABP format: { "culture": "en", "texts": {...} })
            JsonElement textsElement;
            if (doc.RootElement.TryGetProperty("texts", out textsElement))
            {
                // Use the "texts" content
            }
            else
            {
                // Use root content directly
                textsElement = doc.RootElement;
            }
            
            var rawJson = textsElement.GetRawText();
            
            // Determine structure based on resource type
            if (IsNestedResource(resourceName))
            {
                // GodGPT: nested structure
                // Use Newtonsoft.Json JObject to avoid circular reference issues
                var nestedTexts = JObject.Parse(rawJson);
                
                _logger.LogInformation("[ExternalLocalization] Loaded {Resource}/{Culture} (nested structure)", 
                    resourceName, cultureName);
                
                return nestedTexts ?? new JObject();
            }
            else
            {
                // Lumen: flat structure
                var flatTexts = JsonSerializer.Deserialize<Dictionary<string, string>>(
                    rawJson,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                _logger.LogInformation("[ExternalLocalization] Loaded {Resource}/{Culture} (flat structure)", 
                    resourceName, cultureName);
                
                return flatTexts ?? new Dictionary<string, string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ExternalLocalization] Error loading {Resource}/{Culture}", 
                resourceName, cultureName);
            
            // Return appropriate empty structure
            return IsNestedResource(resourceName) 
                ? new JObject() 
                : new Dictionary<string, string>();
        }
    }
}
