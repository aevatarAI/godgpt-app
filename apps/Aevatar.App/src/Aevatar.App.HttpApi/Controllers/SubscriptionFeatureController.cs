using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// Public features API (no authentication required).
/// </summary>
[RemoteService]
[Route("api/subscription")]
[Authorize]
public class SubscriptionFeatureController : AbpController
{
    private readonly ISubscriptionFeatureService _featureService;

    public SubscriptionFeatureController(ISubscriptionFeatureService featureService)
    {
        _featureService = featureService;
    }

    /// <summary>
    /// Get all features ordered by DisplayOrder.
    /// </summary>
    /// <param name="type">Optional filter by feature type</param>
    /// <returns>List of features</returns>
    [HttpGet("features")]
    public async Task<List<SubscriptionFeatureDto>> GetFeaturesAsync(
        [FromQuery] SubscriptionFeatureType? type = null)
    {
        return await _featureService.GetAllFeaturesAsync(type);
    }
}
