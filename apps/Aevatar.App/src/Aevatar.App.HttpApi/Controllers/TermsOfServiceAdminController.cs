using System;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.Permissions;
using Aevatar.App.Services.Terms;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Aevatar.App.Controllers;

/// <summary>
/// Admin API controller for Terms of Service version management.
/// </summary>
[RemoteService]
[Authorize(Policy = AppPermissions.TermsManagement.Versions.Default)]
[ControllerName("TermsOfServiceAdmin")]
[Area("admin")]
[Route("api/admin/terms/versions")]
public class TermsOfServiceAdminController : AevatarController
{
    private readonly ITermsOfServiceAppService _termsOfServiceAppService;

    public TermsOfServiceAdminController(ITermsOfServiceAppService termsOfServiceAppService)
    {
        _termsOfServiceAppService = termsOfServiceAppService;
    }

    /// <summary>
    /// Gets a paged list of ToS versions.
    /// </summary>
    [HttpGet]
    public async Task<PagedResultDto<TermsVersionDto>> GetVersionsAsync([FromQuery] GetTermsVersionsInput input)
    {
        return await _termsOfServiceAppService.GetVersionsAsync(input);
    }

    /// <summary>
    /// Gets a specific ToS version by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<TermsVersionDto> GetVersionAsync(Guid id)
    {
        return await _termsOfServiceAppService.GetVersionAsync(id);
    }

    /// <summary>
    /// Creates a new ToS version.
    /// </summary>
    [HttpPost]
    [Authorize(Policy = AppPermissions.TermsManagement.Versions.Create)]
    public async Task<TermsVersionDto> CreateVersionAsync([FromBody] CreateTermsVersionDto input)
    {
        return await _termsOfServiceAppService.CreateVersionAsync(input);
    }

    /// <summary>
    /// Updates a ToS version.
    /// </summary>
    [HttpPut("{id}")]
    [Authorize(Policy = AppPermissions.TermsManagement.Versions.Edit)]
    public async Task<TermsVersionDto> UpdateVersionAsync(Guid id, [FromBody] UpdateTermsVersionDto input)
    {
        return await _termsOfServiceAppService.UpdateVersionAsync(id, input);
    }

    /// <summary>
    /// Deletes a ToS version.
    /// </summary>
    [HttpDelete("{id}")]
    [Authorize(Policy = AppPermissions.TermsManagement.Versions.Delete)]
    public async Task DeleteVersionAsync(Guid id)
    {
        await _termsOfServiceAppService.DeleteVersionAsync(id);
    }

    /// <summary>
    /// Activates a ToS version (deactivates all others).
    /// </summary>
    [HttpPost("{id}/activate")]
    [Authorize(Policy = AppPermissions.TermsManagement.Versions.Edit)]
    public async Task<IActionResult> ActivateVersionAsync(Guid id)
    {
        await _termsOfServiceAppService.ActivateVersionAsync(id);
        return Ok(new { success = true, message = "Version activated successfully" });
    }

    /// <summary>
    /// Gets the consent count for a specific version.
    /// </summary>
    [HttpGet("{version}/consent-count")]
    public async Task<IActionResult> GetConsentCountAsync(string version)
    {
        var count = await _termsOfServiceAppService.GetVersionConsentCountAsync(version);
        return Ok(new { version, consentCount = count });
    }
}
