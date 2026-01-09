using System.Collections.Generic;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.App.Controllers;

/// <summary>
/// Unified localization API controller for both Lumen and GodGPT
/// - Lumen: returns flat JSON structure ({"key": "value"})
/// - GodGPT: returns nested JSON structure ({"section": {"key": "value"}})
/// </summary>
[RemoteService]
[Route("api/{resource}")]
public class AppLocalizationController : AevatarController
{
    private readonly ILogger<AppLocalizationController> _logger;
    private readonly IExternalLocalizationService _externalLocalizationService;
    
    private static readonly string[] SupportedCultures = { "en", "zh-Hans", "zh-Hant", "es" };

    public AppLocalizationController(
        ILogger<AppLocalizationController> logger,
        IExternalLocalizationService externalLocalizationService)
    {
        _logger = logger;
        _externalLocalizationService = externalLocalizationService;
    }

    /// <summary>
    /// Get localization texts for all supported cultures
    /// - For Lumen: returns flat structure Dictionary&lt;string, string&gt;
    /// - For GodGPT: returns nested structure for i18n libraries
    /// </summary>
    /// <param name="resource">Resource name: "lumen" or "godgpt"</param>
    [AllowAnonymous]
    [HttpGet("localization")]
    public IActionResult GetAllLocalizations(string resource)
    {
        if (!IsValidResource(resource))
        {
            return NotFound(new { message = $"Unknown resource: {resource}" });
        }
        
        var result = new Dictionary<string, object>();
        
        foreach (var culture in SupportedCultures)
        {
            var texts = _externalLocalizationService.GetTexts(resource, culture);
            
            // For lumen, check if it's a valid flat dictionary
            if (IsLumenResource(resource))
            {
                if (texts is Dictionary<string, string> flatTexts && flatTexts.Count > 0)
                {
                    result[culture] = flatTexts;
                }
            }
            else
            {
                // For godgpt, accept any non-null value
                if (texts != null)
                {
                    result[culture] = texts;
                }
            }
        }
        
        return Ok(new
        {
            resource = resource.ToLower(),
            cultures = result
        });
    }

    /// <summary>
    /// Get localization texts for a specific culture
    /// - For Lumen: returns flat structure Dictionary&lt;string, string&gt;
    /// - For GodGPT: returns nested structure for i18n libraries
    /// </summary>
    /// <param name="resource">Resource name: "lumen" or "godgpt"</param>
    /// <param name="cultureName">Culture code (e.g., "en", "zh-Hans", "zh-Hant", "es")</param>
    [AllowAnonymous]
    [HttpGet("localization/{cultureName}")]
    public IActionResult GetLocalization(string resource, string cultureName)
    {
        if (!IsValidResource(resource))
        {
            return NotFound(new { message = $"Unknown resource: {resource}" });
        }
        
        var texts = _externalLocalizationService.GetTexts(resource, cultureName);
        
        // Check if texts are valid based on resource type
        var isEmpty = IsLumenResource(resource)
            ? texts == null || (texts is Dictionary<string, string> flat && flat.Count == 0)
            : texts == null;
        
        if (isEmpty)
        {
            return NotFound(new { message = $"Localization not found for {resource}/{cultureName}" });
        }
        
        return Ok(new
        {
            culture = cultureName,
            texts = texts
        });
    }
    
    private static bool IsValidResource(string resource)
    {
        return resource.Equals("lumen", System.StringComparison.OrdinalIgnoreCase) ||
               resource.Equals("godgpt", System.StringComparison.OrdinalIgnoreCase);
    }
    
    private static bool IsLumenResource(string resource)
    {
        return resource.Equals("lumen", System.StringComparison.OrdinalIgnoreCase);
    }
}

