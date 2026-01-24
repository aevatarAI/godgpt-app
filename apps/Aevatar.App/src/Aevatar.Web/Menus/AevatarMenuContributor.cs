using System.Threading.Tasks;
using Aevatar.App.Localization;
using Aevatar.App.Permissions;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.Identity.Web.Navigation;
using Volo.Abp.SettingManagement.Web.Navigation;
using Volo.Abp.UI.Navigation;
using Volo.Abp.Users;


namespace Aevatar.Web.Menus;

public class AevatarMenuContributor : IMenuContributor
{
    public async Task ConfigureMenuAsync(MenuConfigurationContext context)
    {
        if (context.Menu.Name == StandardMenus.Main)
        {
            await ConfigureMainMenuAsync(context);
        }
    }

    private Task ConfigureMainMenuAsync(MenuConfigurationContext context)
    {
        var administration = context.Menu.GetAdministration();
        var l = context.GetLocalizer<AevatarResource>();
        var currentUser = context.ServiceProvider.GetRequiredService<ICurrentUser>();

        context.Menu.Items.Insert(
            0,
            new ApplicationMenuItem(
                AevatarMenus.Home,
                l["Menu:Home"],
                "~/",
                icon: "fas fa-home",
                order: 0
            )
        );

        var languageManagement = new ApplicationMenuItem(
            AevatarMenus.LanguageManagement,
            l["Menu:LanguageManagement"],
            icon: "fas fa-globe",
            order: 1
        );

        languageManagement.AddItem(
            new ApplicationMenuItem(
                AevatarMenus.Languages,
                l["Menu:Languages"],
                "~/Languages",
                icon: "fas fa-language",
                order: 1,
                requiredPermissionName: AppPermissions.LanguageManagement.Languages.Default
            )
        );

        languageManagement.AddItem(
            new ApplicationMenuItem(
                AevatarMenus.LanguageTexts,
                l["Menu:LanguageTexts"],
                "~/LanguageTexts",
                icon: "fas fa-file-alt",
                order: 2,
                requiredPermissionName: AppPermissions.LanguageManagement.LanguageTexts.Default
            )
        );

        context.Menu.Items.Insert(1, languageManagement);

        // Configure administration menu order
        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 1);
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 2);

        return Task.CompletedTask;
    }
}

