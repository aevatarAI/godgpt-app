using System.Threading.Tasks;
using Aevatar.App.HttpApi.Controllers;
using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc.ApplicationConfigurations;

namespace Aevatar.App.Controllers;

[RemoteService]
[ControllerName("Localization")]
[Route("api/localization")]
public class LocalizationController: AevatarController
{
    private readonly IAbpApplicationLocalizationAppService _localizationAppService;

    public LocalizationController(IAbpApplicationLocalizationAppService localizationAppService)
    {
        _localizationAppService = localizationAppService;
    }
    
    [HttpGet]
    public async Task<ApplicationLocalizationDto> GetAsync(ApplicationLocalizationRequestDto input)
    {
        return await _localizationAppService.GetAsync(input);
    }
}