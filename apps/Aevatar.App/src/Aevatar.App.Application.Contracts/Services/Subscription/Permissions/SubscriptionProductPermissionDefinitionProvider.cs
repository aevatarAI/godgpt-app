using Aevatar.App.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;

namespace Aevatar.App.Services.Subscription.Permissions;

/// <summary>
/// Defines permission structure for subscription product management.
/// </summary>
public class SubscriptionProductPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var subscriptionGroup = context.AddGroup(
            SubscriptionProductPermissions.GroupName,
            L("Permission:SubscriptionProductManagement"));
        
        // Products
        var productsPermission = subscriptionGroup.AddPermission(
            SubscriptionProductPermissions.Products.Default,
            L("Permission:SubscriptionProductManagement.Products"));
        productsPermission.AddChild(
            SubscriptionProductPermissions.Products.Create,
            L("Permission:SubscriptionProductManagement.Products.Create"));
        productsPermission.AddChild(
            SubscriptionProductPermissions.Products.Update,
            L("Permission:SubscriptionProductManagement.Products.Update"));
        productsPermission.AddChild(
            SubscriptionProductPermissions.Products.Delete,
            L("Permission:SubscriptionProductManagement.Products.Delete"));
        productsPermission.AddChild(
            SubscriptionProductPermissions.Products.SetListed,
            L("Permission:SubscriptionProductManagement.Products.SetListed"));
        
        // Labels
        var labelsPermission = subscriptionGroup.AddPermission(
            SubscriptionProductPermissions.Labels.Default,
            L("Permission:SubscriptionProductManagement.Labels"));
        labelsPermission.AddChild(
            SubscriptionProductPermissions.Labels.Create,
            L("Permission:SubscriptionProductManagement.Labels.Create"));
        labelsPermission.AddChild(
            SubscriptionProductPermissions.Labels.Update,
            L("Permission:SubscriptionProductManagement.Labels.Update"));
        labelsPermission.AddChild(
            SubscriptionProductPermissions.Labels.Delete,
            L("Permission:SubscriptionProductManagement.Labels.Delete"));
        
        // Features
        var featuresPermission = subscriptionGroup.AddPermission(
            SubscriptionProductPermissions.Features.Default,
            L("Permission:SubscriptionProductManagement.Features"));
        featuresPermission.AddChild(
            SubscriptionProductPermissions.Features.Create,
            L("Permission:SubscriptionProductManagement.Features.Create"));
        featuresPermission.AddChild(
            SubscriptionProductPermissions.Features.Update,
            L("Permission:SubscriptionProductManagement.Features.Update"));
        featuresPermission.AddChild(
            SubscriptionProductPermissions.Features.Delete,
            L("Permission:SubscriptionProductManagement.Features.Delete"));
        featuresPermission.AddChild(
            SubscriptionProductPermissions.Features.Reorder,
            L("Permission:SubscriptionProductManagement.Features.Reorder"));
        
        // Prices
        var pricesPermission = subscriptionGroup.AddPermission(
            SubscriptionProductPermissions.Prices.Default,
            L("Permission:SubscriptionProductManagement.Prices"));
        pricesPermission.AddChild(
            SubscriptionProductPermissions.Prices.Set,
            L("Permission:SubscriptionProductManagement.Prices.Set"));
        pricesPermission.AddChild(
            SubscriptionProductPermissions.Prices.Delete,
            L("Permission:SubscriptionProductManagement.Prices.Delete"));
        pricesPermission.AddChild(
            SubscriptionProductPermissions.Prices.SyncPrice,
            L("Permission:SubscriptionProductManagement.Prices.SyncPrice"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AevatarResource>(name);
    }
}
