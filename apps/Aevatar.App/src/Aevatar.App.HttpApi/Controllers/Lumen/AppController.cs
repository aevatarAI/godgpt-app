using Aevatar.App.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.Controllers.Lumen;

/// <summary>
/// Base controller for Lumen API controllers
/// </summary>
public abstract class AppController : AbpControllerBase
{
    protected AppController()
    {
        LocalizationResource = typeof(AevatarResource);
    }
}

