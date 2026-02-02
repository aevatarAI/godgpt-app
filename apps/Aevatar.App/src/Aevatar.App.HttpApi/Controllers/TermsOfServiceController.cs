using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.Services.Terms;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;

namespace Aevatar.App.Controllers;

/// <summary>
/// API controller for Terms of Service consent management.
/// </summary>
[RemoteService]
[Authorize]
[ControllerName("TermsOfService")]
[Area("terms")]
[Route("api/godgpt/terms")]
public class TermsOfServiceController : AevatarController
{
    private readonly ITermsOfServiceAppService _termsOfServiceAppService;

    public TermsOfServiceController(ITermsOfServiceAppService termsOfServiceAppService)
    {
        _termsOfServiceAppService = termsOfServiceAppService;
    }

    /// <summary>
    /// Gets the current user's consent status for Terms of Service.
    /// </summary>
    /// <returns>Consent status including whether reconsent is needed.</returns>
    [HttpGet("consent/status")]
    public async Task<ConsentStatusDto> GetConsentStatusAsync()
    {
        return await _termsOfServiceAppService.GetConsentStatusAsync();
    }

    /// <summary>
    /// Submits user consent for a specific ToS version.
    /// </summary>
    /// <param name="input">Consent details including version and device info.</param>
    /// <returns>Consent confirmation with ID and timestamp.</returns>
    [HttpPost("consent")]
    public async Task<SubmitConsentResponseDto> SubmitConsentAsync([FromBody] SubmitConsentRequestDto input)
    {
        return await _termsOfServiceAppService.SubmitConsentAsync(input);
    }
}
