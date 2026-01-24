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
/// API controller for language text management.
/// </summary>
[RemoteService]
[Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Default)]
[ControllerName("LanguageText")]
[Area("language-management")]
[Route("api/language-management/language-texts")]
public class LanguageTextController : AevatarController
{
    private readonly ILanguageTextAppService _languageTextAppService;

    public LanguageTextController(ILanguageTextAppService languageTextAppService)
    {
        _languageTextAppService = languageTextAppService;
    }

    /// <summary>
    /// Gets language texts with pagination and filtering.
    /// </summary>
    [HttpGet]
    public async Task<PagedResultDto<LanguageTextDto>> GetListAsync([FromQuery] GetLanguageTextsInput input)
    {
        return await _languageTextAppService.GetListAsync(input);
    }

    /// <summary>
    /// Gets a single language text by resource name, name and culture.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="name">The text key name.</param>
    /// <param name="cultureName">The culture name.</param>
    /// <param name="baseCultureName">The base culture name.</param>
    [HttpGet("{resourceName}/{name}/{cultureName}")]
    public async Task<LanguageTextDto> GetAsync(
        string resourceName,
        string name,
        string cultureName,
        [FromQuery] string baseCultureName)
    {
        return await _languageTextAppService.GetAsync(new GetLanguageTextInput
        {
            ResourceName = resourceName,
            Name = name,
            CultureName = cultureName,
            BaseCultureName = baseCultureName
        });
    }

    /// <summary>
    /// Adds a language text value.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="cultureName">The culture name.</param>
    /// <param name="name">The text key name.</param>
    /// <param name="value">The localized value.</param>
    [HttpPost("{resourceName}/{cultureName}/{name}")]
    [Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Create)]
    public async Task AddAsync(
        string resourceName,
        string cultureName,
        string name,
        [FromBody] string value)
    {
        await _languageTextAppService.AddAsync(new CreateLanguageTextInput
        {
            ResourceName = resourceName,
            CultureName = cultureName,
            Name = name,
            Value = value
        });
    }

    /// <summary>
    /// Adds or updates a language text value.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="cultureName">The culture name.</param>
    /// <param name="name">The text key name.</param>
    /// <param name="value">The localized value.</param>
    [HttpPut("{resourceName}/{cultureName}/{name}")]
    [Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Edit)]
    public async Task AddOrUpdateAsync(
        string resourceName,
        string cultureName,
        string name,
        [FromBody] string value)
    {
        await _languageTextAppService.AddOrUpdateAsync(new UpdateLanguageTextInput
        {
            ResourceName = resourceName,
            CultureName = cultureName,
            Name = name,
            Value = value
        });
    }

    /// <summary>
    /// Restores a language text to default.
    /// </summary>
    /// <param name="resourceName">The resource name.</param>
    /// <param name="cultureName">The culture name.</param>
    /// <param name="name">The text key name.</param>
    [HttpPut("{resourceName}/{cultureName}/{name}/restore")]
    [Authorize(Policy = AppPermissions.LanguageManagement.LanguageTexts.Restore)]
    public async Task RestoreAsync(
        string resourceName,
        string cultureName,
        string name)
    {
        await _languageTextAppService.RestoreAsync(new RestoreLanguageTextInput
        {
            ResourceName = resourceName,
            CultureName = cultureName,
            Name = name
        });
    }
}
