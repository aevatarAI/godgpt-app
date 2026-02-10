using Aevatar.App.Application.Services;
using Aevatar.App.Application.Services.Payment;
using Aevatar.App.Application.Services.Push;
using Aevatar.Payment.Abstractions;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Services.Subscription;
using Aevatar.App.Services.Subscription.Options;
using Aevatar.App.Services.Subscription.Providers;
using Aevatar.App.Application.Contracts.Services.Push;
using Aevatar.Application.Grains;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.PermissionManagement;
using Volo.Abp.SettingManagement;
using Volo.Abp.Account;
using Volo.Abp.Identity;
using Volo.Abp.AutoMapper;
using Volo.Abp.FeatureManagement;
using Volo.Abp.Modularity;
using Volo.Abp.TenantManagement;

namespace Aevatar.App;

[DependsOn(
    typeof(AppDomainModule),
    typeof(AppApplicationContractsModule),
    typeof(AbpPermissionManagementApplicationModule),
    typeof(AbpFeatureManagementApplicationModule),
    typeof(AbpIdentityApplicationModule),
    typeof(AbpAccountApplicationModule),
    typeof(AbpTenantManagementApplicationModule),
    typeof(AbpSettingManagementApplicationModule),
    typeof(GodGPTGAgentModule)
    )]
public class AppApplicationModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<AppApplicationModule>();
        });
        
        // Register InvitationService
        context.Services.AddScoped<IInvitationService, InvitationService>();
        
        // Register UserStatisticsService
        context.Services.AddScoped<IUserStatisticsService, UserStatisticsService>();
        
        // Register UserFeedbackService
        context.Services.AddScoped<IUserFeedbackService, UserFeedbackService>();
        
        // Register UserInfoService
        context.Services.AddScoped<IUserInfoService, UserInfoService>();
        
        // Register UserQuotaService
        context.Services.AddScoped<IUserQuotaService, UserQuotaService>();
        
        // Register PaymentBusinessRegistrationService (registers business agents for payment events)
        context.Services.AddScoped<PaymentBusinessRegistrationService>();
        
        // Register GodGPTPaymentBusinessService (handles Stripe operations in HttpApi layer)
        context.Services.AddScoped<IGodGPTPaymentBusinessService, GodGPTPaymentBusinessService>();
        
        // Register payment event pre-handler (ensures agent links exist before events are broadcast)
        context.Services.AddScoped<IPaymentEventPreHandler, GodGPTPaymentEventPreHandler>();
        
        var configuration = context.Services.GetConfiguration();
        Configure<PlatformPriceSyncOptions>(configuration.GetSection(PlatformPriceSyncOptions.SectionName));
        context.Services.AddTransient<IPlatformPriceSyncService, PlatformPriceSyncService>();
        context.Services.AddTransient<ISubscriptionProductService, SubscriptionProductService>();
        context.Services.AddTransient<ISubscriptionLabelService, SubscriptionLabelService>();
        context.Services.AddTransient<ISubscriptionFeatureService, SubscriptionFeatureService>();
        // Register platform price providers (Strategy Pattern)
        context.Services.AddSingleton<IPlatformPriceProvider, StripePriceProvider>();
        context.Services.AddSingleton<IPlatformPriceProviderFactory, PlatformPriceProviderFactory>();

        // User Subscription
        Configure<UserSubscriptionOptions>(configuration.GetSection(UserSubscriptionOptions.SectionName));
        context.Services.AddTransient<IUserSubscriptionService, UserSubscriptionService>();

        // Push Notification Services
        Configure<FirebaseMessagingOptions>(configuration.GetSection(FirebaseMessagingOptions.SectionName));
        context.Services.AddHttpClient<IFirebaseMessagingClient, FirebaseMessagingClient>();
        context.Services.AddScoped<IUserDeviceService, UserDeviceService>();
        context.Services.AddScoped<IPushNotificationService, PushNotificationService>();
    }
}
