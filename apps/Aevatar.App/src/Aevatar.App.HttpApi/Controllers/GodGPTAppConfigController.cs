using Aevatar.App.HttpApi.Controllers;
using System.Threading.Tasks;
using Aevatar.GodGPT.Dtos;
using Aevatar.Options;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Volo.Abp;
using Aevatar.Quantum;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for GodGPT application version and feature configuration.
/// All endpoints are anonymous access.
/// </summary>
[RemoteService]
[ControllerName("GodGPTAppConfig")]
[Route("api")]
[AllowAnonymous]
public class GodGPTAppConfigController : AevatarController
{
    private readonly IOptions<GodGPTOptions> _godGptOptions;

    public GodGPTAppConfigController(IOptions<GodGPTOptions> godGptOptions)
    {
        _godGptOptions = godGptOptions;
    }

    /// <summary>
    /// Query GodGPT application version
    /// </summary>
    /// <returns>Version string</returns>
    [HttpGet("godgpt/query-version")]
    public Task<string> QueryVersion()
    {
        return Task.FromResult(_godGptOptions.Value.Version);
    }

    /// <summary>
    /// Query GodGPT configuration information including feature flags.
    /// Fully configuration-driven, backend doesn't care about specific features,
    /// just modify config file to add new features.
    /// </summary>
    /// <returns>Configuration information</returns>
    [HttpGet("godgpt/query-config")]
    public Task<GodGPTConfigResponseDto> QueryConfig()
    {
        var config = new GodGPTConfigResponseDto
        {
            Version = _godGptOptions.Value.Version,
            Features = _godGptOptions.Value.Features,
        };

        return Task.FromResult(config);
    }
}
