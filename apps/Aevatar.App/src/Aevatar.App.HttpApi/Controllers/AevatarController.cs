using Aevatar.App.Localization;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.App.HttpApi.Controllers;

/* Inherit your controllers from this class.
 */
public abstract class AevatarController : AbpControllerBase
{
    protected AevatarController()
    {
        LocalizationResource = typeof(AevatarResource);
    }
}
