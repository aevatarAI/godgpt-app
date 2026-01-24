using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Application service interface for language management.
/// </summary>
public interface ILanguageAppService : IApplicationService
{
    /// <summary>
    /// Gets languages with pagination and filtering.
    /// </summary>
    Task<PagedResultDto<LanguageDto>> GetListAsync(GetLanguagesInput input);

    /// <summary>
    /// Gets all languages without pagination.
    /// </summary>
    Task<ListResultDto<LanguageDto>> GetAllListAsync();

    /// <summary>
    /// Creates a new language.
    /// </summary>
    /// <param name="input">The language creation data.</param>
    /// <returns>The created language.</returns>
    Task<LanguageDto> CreateAsync(CreateLanguageDto input);

    /// <summary>
    /// Gets a language by id.
    /// </summary>
    /// <param name="id">The language id.</param>
    /// <returns>The language.</returns>
    Task<LanguageDto> GetAsync(Guid id);

    /// <summary>
    /// Updates a language.
    /// </summary>
    /// <param name="id">The language id.</param>
    /// <param name="input">The language update data.</param>
    /// <returns>The updated language.</returns>
    Task<LanguageDto> UpdateAsync(Guid id, UpdateLanguageDto input);

    /// <summary>
    /// Deletes a language.
    /// </summary>
    /// <param name="id">The language id.</param>
    Task DeleteAsync(Guid id);

    /// <summary>
    /// Gets all localization resources.
    /// </summary>
    /// <returns>List of localization resources.</returns>
    Task<List<LanguageResourceDto>> GetResourcesAsync();

    /// <summary>
    /// Gets all available cultures.
    /// </summary>
    /// <returns>List of culture information.</returns>
    Task<List<CultureInfoDto>> GetCultureListAsync();
}
