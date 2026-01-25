using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.Subscription;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Dtos;
using Aevatar.App.Services.Subscription.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers;

/// <summary>
/// Admin API for managing subscription features.
/// </summary>
[RemoteService]
[Route("api/admin/subscription/features")]
[Authorize]
public class SubscriptionFeatureAdminController : AbpController
{
    private readonly ISubscriptionFeatureService _featureService;

    public SubscriptionFeatureAdminController(ISubscriptionFeatureService featureService)
    {
        _featureService = featureService;
    }

    /// <summary>
    /// List all features.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Default)]
    public async Task<List<SubscriptionFeatureAdminDto>> GetListAsync(
        [FromQuery] SubscriptionFeatureType? type = null)
    {
        return await _featureService.GetAllFeaturesAdminAsync(type);
    }

    /// <summary>
    /// Get a feature by ID.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Default)]
    public async Task<ActionResult<SubscriptionFeatureAdminDto>> GetAsync(string id)
    {
        var feature = await _featureService.GetFeatureAsync(id);
        if (feature == null) return NotFound();
        return feature;
    }

    /// <summary>
    /// Create a feature.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Create)]
    public async Task<SubscriptionFeatureAdminDto> CreateAsync([FromBody] CreateSubscriptionFeatureDto input)
    {
        return await _featureService.CreateFeatureAsync(input);
    }

    /// <summary>
    /// Update a feature.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Update)]
    public async Task<SubscriptionFeatureAdminDto> UpdateAsync(
        string id, [FromBody] UpdateSubscriptionFeatureDto input)
    {
        return await _featureService.UpdateFeatureAsync(id, input);
    }

    /// <summary>
    /// Delete a feature.
    /// </summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Delete)]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        await _featureService.DeleteFeatureAsync(id);
        return NoContent();
    }

    /// <summary>
    /// Reorder features.
    /// </summary>
    [HttpPost("reorder")]
    [Authorize(Policy = SubscriptionProductPermissions.Features.Reorder)]
    public async Task<IActionResult> ReorderAsync([FromBody] List<SubscriptionFeatureOrderItemDto> orders)
    {
        await _featureService.ReorderFeaturesAsync(orders);
        return NoContent();
    }
}
