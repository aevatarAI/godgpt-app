using System.Collections.Generic;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// External localization service interface for loading texts from external resources (JSON files, etc.)
/// This design is more flexible than hardcoded dictionaries
/// </summary>
public interface IExternalLocalizationService
{
    /// <summary>
    /// Get all localization texts for a specific resource and culture
    /// </summary>
    /// <param name="resource">Resource name (e.g., "lumen", "godgpt")</param>
    /// <param name="culture">Culture code (e.g., "en", "zh-Hans", "zh-Hant", "es")</param>
    /// <returns>Dictionary of key-value pairs, or null if not found</returns>
    Dictionary<string, string>? GetTexts(string resource, string culture);
    
    /// <summary>
    /// Get a single localized text by key
    /// </summary>
    /// <param name="resource">Resource name (e.g., "lumen")</param>
    /// <param name="culture">Culture code (e.g., "en", "zh-Hans")</param>
    /// <param name="key">Text key</param>
    /// <returns>Localized text, or key itself if not found</returns>
    string GetText(string resource, string culture, string key);
    
    /// <summary>
    /// Get all available resources
    /// </summary>
    /// <returns>List of resource names</returns>
    IEnumerable<string> GetAvailableResources();
    
    /// <summary>
    /// Get all available cultures for a resource
    /// </summary>
    /// <param name="resource">Resource name</param>
    /// <returns>List of culture codes</returns>
    IEnumerable<string> GetAvailableCultures(string resource);
    
    /// <summary>
    /// Reload all resources from files (useful for hot-reload)
    /// </summary>
    void Reload();
}

