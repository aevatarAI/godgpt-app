using System;
using System.IO;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.App;
using Aevatar.App.HttpApi.Host.Extensions;
using Aevatar.App.Localization;
using Aevatar.App.MongoDB;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc;
using Aevatar.Web.Menus;
using Microsoft.OpenApi.Models;
using OpenIddict.Validation.AspNetCore;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.Account.Web;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.Shared;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.AutoMapper;
using Volo.Abp.Caching;
using Volo.Abp.Caching.StackExchangeRedis;
using Volo.Abp.Identity.Web;
using Volo.Abp.Modularity;
using Volo.Abp.Security.Claims;
using Volo.Abp.SettingManagement.Web;
using Volo.Abp.Swashbuckle;
using Volo.Abp.OpenIddict;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.UI.Navigation;
using Volo.Abp.VirtualFileSystem;

namespace Aevatar.Web;

[DependsOn(
    typeof(AppHttpApiModule),
    typeof(AppApplicationModule),
    typeof(AbpCachingStackExchangeRedisModule),
    typeof(AppMongoDbModule),
    typeof(AbpAutofacModule),
    typeof(AbpIdentityWebModule),
    typeof(AbpSettingManagementWebModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule)
)]
public class AevatarWebModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
        {
            options.AddAssemblyResource(
                typeof(AevatarResource),
                typeof(AppDomainModule).Assembly,
                typeof(AppDomainSharedModule).Assembly,
                typeof(AppApplicationModule).Assembly,
                typeof(AppApplicationContractsModule).Assembly,
                typeof(AevatarWebModule).Assembly
            );
        });

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddValidation(options =>
            {
                options.AddAudiences("Aevatar");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        if (!hostingEnvironment.IsDevelopment())
        {
            PreConfigure<AbpOpenIddictAspNetCoreOptions>(options =>
            {
                options.AddDevelopmentEncryptionAndSigningCertificate = false;
            });

            var certificatePassword = configuration["AuthServer:CertificatePassword"];
            if (string.IsNullOrEmpty(certificatePassword))
            {
                throw new InvalidOperationException(
                    "AuthServer:CertificatePassword must be configured in production environment. " +
                    "Please set this value in appsettings.json or environment variables.");
            }

            PreConfigure<OpenIddictServerBuilder>(serverBuilder =>
            {
                serverBuilder.AddProductionEncryptionAndSigningCertificate("openiddict.pfx", certificatePassword);
            });
        }
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var hostingEnvironment = context.Services.GetHostingEnvironment();
        var configuration = context.Services.GetConfiguration();

        ConfigureAuthentication(context);
        ConfigureUrls(configuration);
        ConfigureBundles();
        // Configure Agent Runtime (Local or Orleans)
        ConfigureAgentRuntime(context, configuration);
        ConfigureAutoMapper();
        ConfigureVirtualFileSystem(hostingEnvironment);
        ConfigureNavigationServices();
        ConfigureAutoApiControllers();
        ConfigureSwaggerServices(context.Services);
        ConfigCache(context, configuration);
        context.Services.AddMvc(options => { options.Filters.Add(new IgnoreAntiforgeryTokenAttribute()); });
        
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.ForwardIdentityAuthenticationForBearer(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme);
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
        });
    }

    private void ConfigureBundles()
    {
        Configure<AbpBundlingOptions>(options =>
        {
            options.StyleBundles.Configure(
                LeptonXLiteThemeBundles.Styles.Global,
                bundle =>
                {
                    bundle.AddFiles("/global-styles.css");
                }
            );
        });
    }

    private void ConfigureAutoMapper()
    {
        Configure<AbpAutoMapperOptions>(options =>
        {
            options.AddMaps<AevatarWebModule>();
        });
    }

    private void ConfigureVirtualFileSystem(IWebHostEnvironment hostingEnvironment)
    {
        if (hostingEnvironment.IsDevelopment())
        {
            Configure<AbpVirtualFileSystemOptions>(options =>
            {
                options.FileSets.ReplaceEmbeddedByPhysical<AppDomainSharedModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}Aevatar.App.Domain.Shared"));
                options.FileSets.ReplaceEmbeddedByPhysical<AppDomainModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}Aevatar.App.Domain"));
                options.FileSets.ReplaceEmbeddedByPhysical<AppApplicationContractsModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}Aevatar.App.Application.Contracts"));
                options.FileSets.ReplaceEmbeddedByPhysical<AppApplicationModule>(
                    Path.Combine(hostingEnvironment.ContentRootPath, $"..{Path.DirectorySeparatorChar}Aevatar.App.Application"));
                options.FileSets.ReplaceEmbeddedByPhysical<AevatarWebModule>(hostingEnvironment.ContentRootPath);
            });
        }
    }

    private void ConfigureNavigationServices()
    {
        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new AevatarMenuContributor());
        });
    }

    private void ConfigureAutoApiControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AppApplicationModule).Assembly);
        });
    }

    private void ConfigureSwaggerServices(IServiceCollection services)
    {
        services.AddAbpSwaggerGen(
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "Aevatar API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            }
        );
    }
    
    private void ConfigCache(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var redisOptions = ConfigurationOptions.Parse(configuration["Redis:Configuration"]);
        context.Services.AddSingleton<IConnectionMultiplexer>(provider => ConnectionMultiplexer.Connect(redisOptions));
        Configure<AbpDistributedCacheOptions>(options => { options.KeyPrefix = "GodGPT:"; });
    }
    
    private void ConfigureAgentRuntime(ServiceConfigurationContext context, IConfiguration configuration)
    {
        // Add Agent Runtime based on configuration
        context.Services.AddAgentRuntime(configuration);
        
        // Configure MassTransit Stream Plugin only when MessageStream.Provider=MassTransit
        var messageStreamProvider = configuration.GetSection("MessageStream").GetValue("Provider", "");
        if (string.Equals(messageStreamProvider, "MassTransit", StringComparison.OrdinalIgnoreCase))
        {
            context.Services.AddMassTransitStreamPlugin(configuration);
        }
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        //TODO 
        // AsyncHelper.RunSync(async () =>
        // {
        //     await context.AddBackgroundWorkerAsync<PlatformPriceSyncWorker>();
        // }) ;
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseErrorPage();
        }

        app.UseCorrelationId();
        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();
        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();

        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "Aevatar API");
        });

        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}

