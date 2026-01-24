using Aevatar.App.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.RazorPages;

namespace Aevatar.Web.Pages;

/* Inherit your PageModel classes from this class.
 */
public abstract class AevatarPageModel : AbpPageModel
{
    protected AevatarPageModel()
    {
        LocalizationResourceType = typeof(AevatarResource);
    }
}

