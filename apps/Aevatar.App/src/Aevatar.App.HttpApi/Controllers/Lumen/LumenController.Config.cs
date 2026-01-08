using System.Collections.Generic;
using Aevatar.App.Application.Contracts.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.App.Controllers.Lumen;

public partial class LumenController
{
    #region Configuration

    /// <summary>
    /// Get feature flags configuration for frontend
    /// </summary>
    /// <returns>Dictionary of feature flags</returns>
    [HttpGet("flags")]
    [AllowAnonymous]
    public IActionResult GetFeatureFlags([FromServices] IOptionsSnapshot<LumenFeatureFlagsOptions> featureFlagsOptions)
    {
        try
        {
            _logger.LogDebug("[LumenController][GetFeatureFlags] Retrieving feature flags");

            var flags = featureFlagsOptions.Value.Flags ?? new Dictionary<string, string>();

            return Ok(new
            {
                Success = true,
                Flags = flags
            });
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "[LumenController][GetFeatureFlags] Error retrieving feature flags");
            return StatusCode(500, new { Success = false, Message = "Failed to retrieve feature flags" });
        }
    }

    #endregion

    #region Localization

    /// <summary>
    /// Get Lumen localization texts for all supported cultures
    /// Returns flat structure: Dictionary&lt;string, string&gt;
    /// </summary>
    [AllowAnonymous]
    [HttpGet("localization")]
    public IActionResult GetAllLocalizations([FromServices] IExternalLocalizationService externalLocalizationService)
    {
        var cultures = new[] { "en", "zh-Hans", "zh-Hant", "es" };
        var result = new Dictionary<string, Dictionary<string, string>>();
        
        foreach (var culture in cultures)
        {
            var texts = externalLocalizationService.GetTexts("lumen", culture);
            if (texts != null && texts.Count > 0)
            {
                result[culture] = texts;
            }
        }
        
        return Ok(new
        {
            resource = "lumen",
            cultures = result
        });
    }

    /// <summary>
    /// Get Lumen localization texts for a specific culture
    /// Returns flat structure: Dictionary&lt;string, string&gt;
    /// </summary>
    /// <param name="cultureName">Culture code (e.g., "en", "zh-Hans", "zh-Hant", "es")</param>
    /// <param name="externalLocalizationService">Localization service (injected)</param>
    [AllowAnonymous]
    [HttpGet("localization/{cultureName}")]
    public IActionResult GetLocalization(
        string cultureName,
        [FromServices] IExternalLocalizationService externalLocalizationService)
    {
        var texts = externalLocalizationService.GetTexts("lumen", cultureName);
        
        if (texts == null || texts.Count == 0)
        {
            return NotFound(new { message = $"Localization not found for lumen/{cultureName}" });
        }
        
        return Ok(new
        {
            culture = cultureName,
            texts = texts
        });
    }

    #endregion
}

/// <summary>
/// Lumen feature flags configuration options
/// </summary>
public class LumenFeatureFlagsOptions
{
    /// <summary>
    /// Feature flags dictionary
    /// </summary>
    public Dictionary<string, string> Flags { get; set; } = new();
}

