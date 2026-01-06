using System;
using System.Net.Mail;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Aevatar.App.Domain.Shared;
using Aevatar.AuthServer.Account.Templates;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Runtime;
using Volo.Abp;
using Volo.Abp.Caching;
using Volo.Abp.DependencyInjection;
using Volo.Abp.Emailing;
using Volo.Abp.Identity;
using Volo.Abp.TextTemplating;

namespace Aevatar.AuthServer.Account;

/// <summary>
/// Interface for account-related email operations
/// </summary>
public interface IAccountEmailer
{
    Task SendRegisterCodeAsync(string email, string code, string appName, GodGPTChatLanguage language = GodGPTChatLanguage.English);
    Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetToken, string appName, GodGPTChatLanguage language = GodGPTChatLanguage.English);
}

/// <summary>
/// Handles sending account-related emails with multi-language and multi-app support
/// </summary>
public class AccountEmailer : IAccountEmailer, ITransientDependency
{
    private readonly ITemplateRenderer _templateRenderer;
    private readonly IEmailSender _emailSender;
    private readonly AccountOptions _accountOptions;
    private readonly IDistributedCache<string, string> _lastEmailCache;
    private readonly DistributedCacheEntryOptions _defaultCacheOptions;
    private readonly ILogger<AccountEmailer> _logger;

    public AccountEmailer(
        IEmailSender emailSender,
        ITemplateRenderer templateRenderer,
        IOptionsSnapshot<AccountOptions> accountOptions,
        IDistributedCache<string, string> lastEmailCache,
        ILogger<AccountEmailer> logger)
    {
        _emailSender = emailSender;
        _templateRenderer = templateRenderer;
        _lastEmailCache = lastEmailCache;
        _accountOptions = accountOptions.Value;
        _logger = logger;

        _defaultCacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(_accountOptions.MailSendingInterval)
        };
    }

    public async Task SendRegisterCodeAsync(string email, string code, string appName, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        var templateName = GetTemplateNameByLanguage(AccountEmailTemplates.RegisterCode, language);
        var emailContent = await _templateRenderer.RenderAsync(
            templateName,
            new { code }
        );

        await CheckSendEmailAsync(email, appName, language);

        var subject = GetEmailSubjectByLanguage("Registration Verification Code", language);
        await SendEmailWithAppSenderAsync(email, subject, emailContent, appName);

        _logger.LogInformation("[AccountEmailer] Register code sent to {Email} for app {AppName}", email, appName);
    }

    public async Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetToken, string appName, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        var url = GetResetPasswordUrl(appName);
        var context = RequestContext.Get("IsCN");
        var isCN = RequestContext.Get("IsCN") is bool cnValue and true;

        if (isCN)
        {
            var cnUrl = GetCNResetPasswordUrl(appName);
            if (!string.IsNullOrEmpty(cnUrl))
            {
                url = cnUrl;
            }
        }

        _logger.LogDebug("[AccountEmailer] Reset URL: {Url}, IsCN: {IsCN}, App: {AppName}", url, isCN, appName);

        var link = $"{url}?userId={user.Id}&email={UrlEncoder.Default.Encode(email)}&resetToken={UrlEncoder.Default.Encode(resetToken)}";

        var templateName = GetTemplateNameByLanguage(AccountEmailTemplates.PasswordResetLink, language);
        var emailContent = await _templateRenderer.RenderAsync(
            templateName,
            new { link }
        );

        await CheckSendEmailAsync(email, appName, language);

        var subject = GetEmailSubjectByLanguage("Password Reset", language);
        await SendEmailWithAppSenderAsync(email, subject, emailContent, appName);

        _logger.LogInformation("[AccountEmailer] Password reset link sent to {Email} for app {AppName}", email, appName);
    }

    private async Task CheckSendEmailAsync(string email, string appName, GodGPTChatLanguage language)
    {
        var key = $"LastEmail_{appName}_{email.ToLower()}";
        var lastSend = await _lastEmailCache.GetAsync(key);

        if (!string.IsNullOrWhiteSpace(lastSend))
        {
            var message = language switch
            {
                GodGPTChatLanguage.CN => "发送邮件过于频繁，请稍后再试",
                GodGPTChatLanguage.TraditionalChinese => "發送郵件過於頻繁，請稍後再試",
                GodGPTChatLanguage.Spanish => "Enviando correos electrónicos con demasiada frecuencia, por favor intente más tarde",
                _ => "Sending emails too frequently, please try again later"
            };
            throw new UserFriendlyException(message);
        }

        await _lastEmailCache.SetAsync(key, email, _defaultCacheOptions);
    }

    private string GetResetPasswordUrl(string appName)
    {
        if (_accountOptions.Apps.TryGetValue(appName, out var appOptions) && !string.IsNullOrEmpty(appOptions.ResetPasswordUrl))
        {
            return appOptions.ResetPasswordUrl;
        }
        return _accountOptions.DefaultResetPasswordUrl;
    }

    private string GetCNResetPasswordUrl(string appName)
    {
        if (_accountOptions.Apps.TryGetValue(appName, out var appOptions))
        {
            return appOptions.CNResetPasswordUrl;
        }
        return string.Empty;
    }

    /// <summary>
    /// Send email with app-specific sender address and display name
    /// </summary>
    private async Task SendEmailWithAppSenderAsync(string toEmail, string subject, string body, string appName)
    {
        if (_accountOptions.Apps.TryGetValue(appName, out var appOptions) 
            && !string.IsNullOrEmpty(appOptions.EmailFromAddress))
        {
            // Use app-specific sender
            var fromAddress = new MailAddress(
                appOptions.EmailFromAddress, 
                string.IsNullOrEmpty(appOptions.EmailFromName) ? appName : appOptions.EmailFromName
            );

            var mailMessage = new MailMessage
            {
                From = fromAddress,
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mailMessage.To.Add(toEmail);

            await _emailSender.SendAsync(mailMessage);
            _logger.LogDebug("[AccountEmailer] Email sent with app-specific sender: {FromAddress} ({FromName})", 
                appOptions.EmailFromAddress, appOptions.EmailFromName);
        }
        else
        {
            // Use default sender from ABP settings
            await _emailSender.SendAsync(toEmail, subject, body);
            _logger.LogDebug("[AccountEmailer] Email sent with default sender for app: {AppName}", appName);
        }
    }

    private static string GetTemplateNameByLanguage(string baseName, GodGPTChatLanguage language)
    {
        return language switch
        {
            GodGPTChatLanguage.CN => $"{baseName}_zh-cn",
            GodGPTChatLanguage.TraditionalChinese => $"{baseName}_zh-tw",
            GodGPTChatLanguage.Spanish => $"{baseName}_es",
            _ => baseName
        };
    }

    private static string GetEmailSubjectByLanguage(string baseSubject, GodGPTChatLanguage language)
    {
        return (baseSubject, language) switch
        {
            ("Registration Verification Code", GodGPTChatLanguage.CN) => "注册验证码",
            ("Registration Verification Code", GodGPTChatLanguage.TraditionalChinese) => "註冊驗證碼",
            ("Registration Verification Code", GodGPTChatLanguage.Spanish) => "Código de verificación de registro",
            ("Password Reset", GodGPTChatLanguage.CN) => "密码重置",
            ("Password Reset", GodGPTChatLanguage.TraditionalChinese) => "密碼重置",
            ("Password Reset", GodGPTChatLanguage.Spanish) => "Restablecimiento de contraseña",
            _ => baseSubject
        };
    }
}

