using System.Collections.Generic;
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
