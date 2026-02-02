using System.Threading.Tasks;
using Aevatar.App.Localization;
using Aevatar.App.Permissions;
using Aevatar.App.Services.Subscription.Permissions;
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
        
        // Subscription Management Menu
        var subscriptionManagement = new ApplicationMenuItem(
            AevatarMenus.SubscriptionManagement,
            l["Menu:SubscriptionManagement"],
            icon: "fas fa-shopping-cart",
            order: 2
        );

        subscriptionManagement.AddItem(
            new ApplicationMenuItem(
                AevatarMenus.SubscriptionProducts,
                l["Menu:SubscriptionProducts"],
                "~/SubscriptionProducts",
                icon: "fas fa-box",
                order: 1,
                requiredPermissionName: SubscriptionProductPermissions.Products.Default
            )
        );

        subscriptionManagement.AddItem(
            new ApplicationMenuItem(
                AevatarMenus.SubscriptionFeatures,
                l["Menu:SubscriptionFeatures"],
                "~/SubscriptionFeatures",
                icon: "fas fa-list-check",
                order: 2,
                requiredPermissionName: SubscriptionProductPermissions.Features.Default
            )
        );

        subscriptionManagement.AddItem(
            new ApplicationMenuItem(
                AevatarMenus.SubscriptionLabels,
                l["Menu:SubscriptionLabels"],
                "~/SubscriptionLabels",
                icon: "fas fa-tags",
                order: 3,
                requiredPermissionName: SubscriptionProductPermissions.Labels.Default
            )
        );

        context.Menu.Items.Insert(2, subscriptionManagement);

        // Configure administration menu order
        administration.SetSubItemOrder(IdentityMenuNames.GroupName, 1);
        administration.SetSubItemOrder(SettingManagementMenuNames.GroupName, 2);

        return Task.CompletedTask;
    }
}

