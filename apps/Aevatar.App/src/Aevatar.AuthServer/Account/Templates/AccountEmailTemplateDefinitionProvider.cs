using Volo.Abp.Emailing.Templates;
using Volo.Abp.TextTemplating;

namespace Aevatar.AuthServer.Account.Templates;

/// <summary>
/// Defines email templates for account operations.
/// Supports multiple languages: English (default), Simplified Chinese, Traditional Chinese, Spanish
/// </summary>
public class AccountEmailTemplateDefinitionProvider : TemplateDefinitionProvider
{
    public override void Define(ITemplateDefinitionContext context)
    {
        DefineRegisterCodeTemplates(context);
        DefinePasswordResetTemplates(context);
    }

    private void DefineRegisterCodeTemplates(ITemplateDefinitionContext context)
    {
        // English (default)
        context.Add(
            new TemplateDefinition(
                AccountEmailTemplates.RegisterCode,
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/RegisterCode.tpl", true)
        );

        // Simplified Chinese
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.RegisterCode}_zh-cn",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/RegisterCode_zh-cn.tpl", true)
        );

        // Traditional Chinese
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.RegisterCode}_zh-tw",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/RegisterCode_zh-tw.tpl", true)
        );

        // Spanish
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.RegisterCode}_es",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/RegisterCode_es.tpl", true)
        );
    }

    private void DefinePasswordResetTemplates(ITemplateDefinitionContext context)
    {
        // English (default)
        context.Add(
            new TemplateDefinition(
                AccountEmailTemplates.PasswordResetLink,
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/PasswordResetLink.tpl", true)
        );

        // Simplified Chinese
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.PasswordResetLink}_zh-cn",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/PasswordResetLink_zh-cn.tpl", true)
        );

        // Traditional Chinese
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.PasswordResetLink}_zh-tw",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/PasswordResetLink_zh-tw.tpl", true)
        );

        // Spanish
        context.Add(
            new TemplateDefinition(
                $"{AccountEmailTemplates.PasswordResetLink}_es",
                layout: StandardEmailTemplates.Layout
            ).WithVirtualFilePath("/Account/Templates/PasswordResetLink_es.tpl", true)
        );
    }
}

