using Microsoft.Extensions.Localization;
using Aevatar.App.Localization;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Ui.Branding;

namespace Aevatar.App.HttpApi.Host;

[Dependency(ReplaceServices = true)]
public class AppHttpApiHostBrandingProvider : DefaultBrandingProvider
{
    private IStringLocalizer<AevatarResource> _localizer;

    public AppHttpApiHostBrandingProvider(IStringLocalizer<AevatarResource> localizer)
    {
        _localizer = localizer;
    }

    public override string AppName => _localizer["AppName"];
}
