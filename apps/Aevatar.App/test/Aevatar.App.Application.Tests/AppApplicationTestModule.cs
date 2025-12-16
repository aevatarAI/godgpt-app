using Volo.Abp.Modularity;

namespace Aevatar.App;

[DependsOn(
    typeof(AppApplicationModule),
    typeof(AppTestBaseModule)
)]
public class AppApplicationTestModule : AbpModule
{

}

