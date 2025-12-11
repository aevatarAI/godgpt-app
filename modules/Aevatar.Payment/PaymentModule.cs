using Aevatar.Payment.Abstractions;
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
/// 
/// Note: IPaymentEventPublisher must be registered by the consuming application.
/// This keeps the Payment module decoupled from specific event bus implementations.
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

        // Register HttpClient factories
        context.Services.AddHttpClient("ApplePay");
        context.Services.AddHttpClient("GooglePlay");

        // Register payment providers (Strategy Pattern)
        context.Services.AddScoped<IPaymentProvider, StripeProvider>();
        context.Services.AddScoped<IPaymentProvider, ApplePayProvider>();
        context.Services.AddScoped<IPaymentProvider, GooglePlayProvider>();

        // Register payment service
        context.Services.AddScoped<IPaymentService, PaymentService>();
        
        // Note: IPaymentEventPublisher is NOT registered here.
        // The consuming application must provide its own implementation.
        // Example implementations:
        // - AbpPaymentEventPublisher (using Volo.Abp.EventBus)
        // - OrleansPaymentEventPublisher (using Orleans Streams)
        // - MediatRPaymentEventPublisher (using MediatR)
    }
}

