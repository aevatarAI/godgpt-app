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
/// Admin API for managing subscription labels.
/// </summary>
[RemoteService]
[Route("api/admin/subscription/labels")]
[Authorize]
public class SubscriptionLabelAdminController : AbpController
{
    private readonly ISubscriptionLabelService _labelService;

    public SubscriptionLabelAdminController(ISubscriptionLabelService labelService)
    {
        _labelService = labelService;
    }

    /// <summary>
    /// List all labels.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = SubscriptionProductPermissions.Labels.Default)]
    public async Task<List<SubscriptionLabelDto>> GetListAsync()
    {
        return await _labelService.GetAllLabelsAsync();
    }

    /// <summary>
    /// Get a label by ID.
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Labels.Default)]
    public async Task<ActionResult<SubscriptionLabelDto>> GetAsync(string id)
    {
        var label = await _labelService.GetLabelAsync(id);
        if (label == null) return NotFound();
        return label;
    }

    /// <summary>
    /// Create a label.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = SubscriptionProductPermissions.Labels.Create)]
    public async Task<SubscriptionLabelDto> CreateAsync([FromBody] CreateSubscriptionLabelDto input)
    {
        return await _labelService.CreateLabelAsync(input);
    }

    /// <summary>
    /// Update a label.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Labels.Update)]
    public async Task<SubscriptionLabelDto> UpdateAsync(string id, [FromBody] UpdateSubscriptionLabelDto input)
    {
        return await _labelService.UpdateLabelAsync(id, input);
    }

    /// <summary>
    /// Delete a label.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = SubscriptionProductPermissions.Labels.Delete)]
    public async Task<IActionResult> DeleteAsync(string id)
    {
        await _labelService.DeleteLabelAsync(id);
        return NoContent();
    }
}
