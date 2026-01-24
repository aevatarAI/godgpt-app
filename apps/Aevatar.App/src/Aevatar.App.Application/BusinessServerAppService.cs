using Aevatar.App.Localization;
using Volo.Abp.Application.Services;

namespace Aevatar.App;

/* Inherit your application services from this class.
 */
public abstract class AppAppService : ApplicationService
{
    protected AppAppService()
    {
        LocalizationResource = typeof(AevatarResource);
    }
}
