using System.Collections.Generic;

namespace Aevatar.App.Application.Contracts.Services;

/// <summary>
/// External localization service interface for loading texts from external resources (JSON files, etc.)
/// Supports both flat (Lumen) and nested (GodGPT) JSON structures
/// </summary>
public interface IExternalLocalizationService
{
    /// <summary>
    /// Get localized texts for a specific resource and culture
    /// Returns appropriate structure based on resource type:
    /// - Lumen: flat Dictionary&lt;string, string&gt; ({"key": "value"})
    /// - GodGPT: nested object ({"section": {"key": "value"}})
    /// </summary>
    /// <param name="resourceName">Resource name (e.g., "lumen", "godgpt")</param>
    /// <param name="cultureName">Culture code (e.g., "en", "zh-Hans", "zh-Hant", "es")</param>
    /// <returns>Localization texts, or null/empty if not found</returns>
    object GetTexts(string resourceName, string cultureName);
    
    /// <summary>
    /// Get all localization data for a culture (all resources combined)
    /// </summary>
    /// <param name="cultureName">Culture code</param>
    /// <returns>Dictionary of resource name to texts</returns>
    Dictionary<string, object> GetAllTexts(string cultureName);
}
