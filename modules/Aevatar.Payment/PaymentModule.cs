using Aevatar.Payment.Abstractions;
using Aevatar.Payment.Agents;
using Aevatar.Payment.Analytics;
using Aevatar.Payment.Options;
using Aevatar.Payment.Providers;
using Aevatar.Payment.Services;
using Microsoft.Extensions.DependencyInjection;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;

namespace Aevatar.Payment;

/// <summary>
/// ABP Module for Payment functionality.
/// Provides payment processing with Stripe, Apple Pay, and Google Play.
/// Includes GA4 analytics reporting via event-driven architecture.
/// </summary>
[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAutofacModule)
)]
public class PaymentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        // Configure options
        context.Services.Configure<StripeOptions>(
            configuration.GetSection(StripeOptions.SectionName));
        context.Services.Configure<ApplePayOptions>(
            configuration.GetSection(ApplePayOptions.SectionName));
        context.Services.Configure<GooglePlayOptions>(
            configuration.GetSection(GooglePlayOptions.SectionName));
        context.Services.Configure<GA4Options>(
            configuration.GetSection(GA4Options.SectionName));

        // Register HttpClient factories
        context.Services.AddHttpClient("ApplePay");
        context.Services.AddHttpClient("GooglePlay");
        context.Services.AddHttpClient<IPaymentAnalyticsService, GA4AnalyticsService>();

        // Register payment providers (Strategy Pattern)
        context.Services.AddScoped<IPaymentProvider, StripeProvider>();
        context.Services.AddScoped<IPaymentProvider, ApplePayProvider>();
        context.Services.AddScoped<IPaymentProvider, GooglePlayProvider>();

        // Register payment service
        context.Services.AddScoped<IPaymentService, PaymentService>();
        
        // Register analytics service (for PaymentAnalyticsGAgent)
        context.Services.AddScoped<IPaymentAnalyticsService, GA4AnalyticsService>();
    }
}

