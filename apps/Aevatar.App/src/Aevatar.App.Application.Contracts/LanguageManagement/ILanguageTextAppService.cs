using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Aevatar.App.LanguageManagement;

/// <summary>
/// Application service interface for language text management.
/// </summary>
public interface ILanguageTextAppService : IApplicationService
{
    /// <summary>
    /// Gets language texts with pagination and filtering.
    /// </summary>
    /// <param name="input">The query parameters.</param>
    /// <returns>Paged list of language texts.</returns>
    Task<PagedResultDto<LanguageTextDto>> GetListAsync(GetLanguageTextsInput input);

    /// <summary>
    /// Gets a single language text by resource name, name and culture.
    /// </summary>
    /// <param name="input">The input parameters.</param>
    /// <returns>The language text.</returns>
    Task<LanguageTextDto> GetAsync(GetLanguageTextInput input);

    /// <summary>
    /// Adds a language text.
    /// </summary>
    /// <param name="input">The input parameters.</param>
    Task AddAsync(CreateLanguageTextInput input);

    /// <summary>
    /// Adds or updates a language text.
    /// </summary>
    /// <param name="input">The input parameters.</param>
    Task AddOrUpdateAsync(UpdateLanguageTextInput input);

    /// <summary>
    /// Restores a language text to default.
    /// </summary>
    /// <param name="input">The input parameters.</param>
    Task RestoreAsync(RestoreLanguageTextInput input);
}
