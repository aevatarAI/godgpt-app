using Aevatar.App.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.AuthServer.Pages;

public abstract class AuthServerPageModel : AbpPageModel
{
    protected AuthServerPageModel()
    {
        LocalizationResourceType = typeof(AevatarResource);
    }
}
