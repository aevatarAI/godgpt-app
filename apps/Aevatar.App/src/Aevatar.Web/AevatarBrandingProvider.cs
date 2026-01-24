using Volo.Abp.Ui.Branding;
using Volo.Abp.DependencyInjection;

namespace Aevatar.Web;

[Dependency(ReplaceServices = true)]
public class AevatarBrandingProvider : DefaultBrandingProvider
{
    public override string AppName => "Aevatar";
}

