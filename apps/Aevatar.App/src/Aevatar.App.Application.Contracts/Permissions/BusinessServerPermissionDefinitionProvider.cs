using Aevatar.App.Localization;
using Volo.Abp.Authorization.Permissions;
using Volo.Abp.Localization;
using Volo.Abp.MultiTenancy;

namespace Aevatar.App.Permissions;

public class AppPermissionDefinitionProvider : PermissionDefinitionProvider
{
    public override void Define(IPermissionDefinitionContext context)
    {
        var myGroup = context.AddGroup(AppPermissions.GroupName);

        // Language Management permissions
        DefineLanguageManagementPermissions(context);

         // Terms of Service Management permission
        DefineTermsManagementPermissions(context);

        // Push Notification Management permissions
        DefinePushNotificationPermissions(context);
    }
    
    private void DefineLanguageManagementPermissions(IPermissionDefinitionContext context)
    {
        var languageManagementGroup = context.AddGroup(
            AppPermissions.LanguageManagement.GroupName,
            L("Permission:LanguageManagement"));
        
        // Languages permissions (LanguageController)
        var languagesPermission = languageManagementGroup.AddPermission(
            AppPermissions.LanguageManagement.Languages.Default,
            L("Permission:LanguageManagement.Languages"));
        
        languagesPermission.AddChild(
            AppPermissions.LanguageManagement.Languages.Create,
            L("Permission:LanguageManagement.Languages.Create"));
        
        languagesPermission.AddChild(
            AppPermissions.LanguageManagement.Languages.Edit,
            L("Permission:LanguageManagement.Languages.Edit"));
        
        languagesPermission.AddChild(
            AppPermissions.LanguageManagement.Languages.Delete,
            L("Permission:LanguageManagement.Languages.Delete"));
        
        // LanguageTexts permissions (LanguageTextController)
        var languageTextsPermission = languageManagementGroup.AddPermission(
            AppPermissions.LanguageManagement.LanguageTexts.Default,
            L("Permission:LanguageManagement.LanguageTexts"));
        
        languageTextsPermission.AddChild(
            AppPermissions.LanguageManagement.LanguageTexts.Create,
            L("Permission:LanguageManagement.LanguageTexts.Create"));
        
        languageTextsPermission.AddChild(
            AppPermissions.LanguageManagement.LanguageTexts.Edit,
            L("Permission:LanguageManagement.LanguageTexts.Edit"));
        
        languageTextsPermission.AddChild(
            AppPermissions.LanguageManagement.LanguageTexts.Restore,
            L("Permission:LanguageManagement.LanguageTexts.Restore"));
    }

    private void DefineTermsManagementPermissions(IPermissionDefinitionContext context)
    {
        var termsManagementGroup = context.AddGroup(
            AppPermissions.TermsManagement.GroupName,
            L("Permission:TermsManagement"));

        // ToS Versions permissions
        var versionsPermission = termsManagementGroup.AddPermission(
            AppPermissions.TermsManagement.Versions.Default,
            L("Permission:TermsManagement.Versions"));

        versionsPermission.AddChild(
            AppPermissions.TermsManagement.Versions.Create,
            L("Permission:TermsManagement.Versions.Create"));

        versionsPermission.AddChild(
            AppPermissions.TermsManagement.Versions.Edit,
            L("Permission:TermsManagement.Versions.Edit"));

        versionsPermission.AddChild(
            AppPermissions.TermsManagement.Versions.Delete,
            L("Permission:TermsManagement.Versions.Delete"));
    }

    private void DefinePushNotificationPermissions(IPermissionDefinitionContext context)
    {
        var pushNotificationGroup = context.AddGroup(
            AppPermissions.PushNotification.GroupName,
            L("Permission:PushNotificationManagement"));

        // Base permission for push notification management
        var pushPermission = pushNotificationGroup.AddPermission(
            AppPermissions.PushNotification.Default,
            L("Permission:PushNotificationManagement.Default"));

        pushPermission.AddChild(
            AppPermissions.PushNotification.SendByTimezone,
            L("Permission:PushNotificationManagement.SendByTimezone"));
    }

    private static LocalizableString L(string name)
    {
        return LocalizableString.Create<AevatarResource>(name);
    }
}
