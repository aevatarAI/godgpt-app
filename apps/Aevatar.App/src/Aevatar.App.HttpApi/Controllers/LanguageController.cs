using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.LanguageManagement;
using Aevatar.App.Permissions;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.Application.Dtos;

namespace Aevatar.App.Controllers;

/// <summary>
/// API controller for language management.
/// </summary>
[RemoteService]
[Authorize(Policy = AppPermissions.LanguageManagement.Languages.Default)]
[ControllerName("Language")]
[Area("language-management")]
[Route("api/language-management/languages")]
public class LanguageController : AevatarController
{
    private readonly ILanguageAppService _languageAppService;

    public LanguageController(ILanguageAppService languageAppService)
    {
        _languageAppService = languageAppService;
    }

    /// <summary>
    /// Gets all languages without pagination.
    /// </summary>
    [HttpGet("all")]
    public async Task<ListResultDto<LanguageDto>> GetAllListAsync()
    {
        return await _languageAppService.GetAllListAsync();
    }

    /// <summary>
    /// Gets a language by id.
    /// </summary>
    /// <param name="id">The language id.</param>
    [HttpGet("{id}")]
    public async Task<LanguageDto> GetAsync(Guid id)
    {
        return await _languageAppService.GetAsync(id);
    }

    /// <summary>
    /// Creates a new language.
    /// </summary>
    /// <param name="input">The language creation data.</param>
    [HttpPost]
    [Authorize(Policy = AppPermissions.LanguageManagement.Languages.Create)]
    public async Task<LanguageDto> CreateAsync([FromBody] CreateLanguageDto input)
    {
        return await _languageAppService.CreateAsync(input);
    }

    /// <summary>
    /// Updates a language.
    /// </summary>
    /// <param name="id">The language id.</param>
    /// <param name="input">The language update data.</param>
    [HttpPut("{id}")]
    [Authorize(Policy = AppPermissions.LanguageManagement.Languages.Edit)]
    public async Task<LanguageDto> UpdateAsync(Guid id, [FromBody] UpdateLanguageDto input)
    {
        return await _languageAppService.UpdateAsync(id, input);
    }

    /// <summary>
    /// Deletes a language.
    /// </summary>
    /// <param name="id">The language id.</param>
    [HttpDelete("{id}")]
    [Authorize(Policy = AppPermissions.LanguageManagement.Languages.Delete)]
    public async Task DeleteAsync(Guid id)
    {
        await _languageAppService.DeleteAsync(id);
    }

    /// <summary>
    /// Gets all localization resources.
    /// </summary>
    [HttpGet("resources")]
    public async Task<List<LanguageResourceDto>> GetResourcesAsync()
    {
        return await _languageAppService.GetResourcesAsync();
    }

    /// <summary>
    /// Gets all available cultures.
    /// </summary>
    [HttpGet("culture-list")]
    public async Task<List<CultureInfoDto>> GetCultureListAsync()
    {
        return await _languageAppService.GetCultureListAsync();
    }
}
