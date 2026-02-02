using System;
using System.Threading.Tasks;
using Volo.Abp.Application.Dtos;
using Volo.Abp.Application.Services;

namespace Aevatar.App.Services.Terms;

/// <summary>
/// Application service interface for Terms of Service management.
/// </summary>
public interface ITermsOfServiceAppService : IApplicationService
{
    #region User APIs

    /// <summary>
    /// Gets the current user's consent status.
    /// </summary>
    Task<ConsentStatusDto> GetConsentStatusAsync();

    /// <summary>
    /// Submits user consent for a specific ToS version.
    /// </summary>
    Task<SubmitConsentResponseDto> SubmitConsentAsync(SubmitConsentRequestDto input);

    #endregion

    #region Admin APIs

    /// <summary>
    /// Gets a paged list of ToS versions (Admin).
    /// </summary>
    Task<PagedResultDto<TermsVersionDto>> GetVersionsAsync(GetTermsVersionsInput input);

    /// <summary>
    /// Gets a specific ToS version by ID (Admin).
    /// </summary>
    Task<TermsVersionDto> GetVersionAsync(Guid id);

    /// <summary>
    /// Creates a new ToS version (Admin).
    /// </summary>
    Task<TermsVersionDto> CreateVersionAsync(CreateTermsVersionDto input);

    /// <summary>
    /// Updates a ToS version (Admin).
    /// </summary>
    Task<TermsVersionDto> UpdateVersionAsync(Guid id, UpdateTermsVersionDto input);

    /// <summary>
    /// Deletes a ToS version (Admin).
    /// </summary>
    Task DeleteVersionAsync(Guid id);

    /// <summary>
    /// Activates a ToS version (deactivates others) (Admin).
    /// </summary>
    Task ActivateVersionAsync(Guid id);

    /// <summary>
    /// Gets the consent count for a specific version (Admin).
    /// </summary>
    Task<long> GetVersionConsentCountAsync(string version);

    #endregion
}
