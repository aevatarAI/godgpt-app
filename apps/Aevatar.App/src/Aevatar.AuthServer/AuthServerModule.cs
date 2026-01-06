using System;
using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using StackExchange.Redis;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite;
using Volo.Abp.AspNetCore.Mvc.UI.Theme.LeptonXLite.Bundling;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Caching;
using Volo.Abp.Modularity;
using Volo.Abp.UI.Navigation;
using Volo.Abp.UI.Navigation.Urls;
using Volo.Abp.VirtualFileSystem;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Account.Web;
using Volo.Abp.Identity.Web;
using Volo.Abp.TenantManagement.Web;
using Volo.Abp.PermissionManagement.Web;
using Volo.Abp.PermissionManagement;
using Volo.Abp.OpenIddict;
using Volo.Abp.FeatureManagement;
using OpenIddict.Server.AspNetCore;
using OpenIddict.Validation.AspNetCore;
using Volo.Abp.AspNetCore.Mvc.UI.Bundling;
using Volo.Abp.Security.Claims;
using Aevatar.AuthServer.Menus;
using Aevatar.AuthServer.Grants;
using Aevatar.AuthServer.Account;
using Aevatar.AuthServer.Account.Templates;
using Volo.Abp.AspNetCore.Mvc.Localization;
using Volo.Abp.TextTemplating;
using Volo.Abp.Emailing;
using Aevatar.App;
using Aevatar.App.Localization;
using Aevatar.App.MongoDB;

namespace Aevatar.AuthServer;

[DependsOn(
    // Core ABP
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    
    // Shared App Modules (provides Domain, Application, MongoDB, HttpApi)
    typeof(AppMongoDbModule),
    typeof(AppApplicationModule),
    typeof(AppHttpApiModule),
    
    // UI
    typeof(AbpAspNetCoreMvcUiLeptonXLiteThemeModule),
    typeof(AbpAccountWebOpenIddictModule),
    typeof(AbpIdentityWebModule),
    typeof(AbpTenantManagementWebModule),
    typeof(AbpFeatureManagementWebModule),
    typeof(AbpPermissionManagementWebModule)
)]
public class AuthServerModule : AbpModule
{
    public override void PreConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        // Configure localization - using App's shared localization
        context.Services.PreConfigure<AbpMvcDataAnnotationsLocalizationOptions>(options =>
        {
            options.AddAssemblyResource(
                typeof(AppResource),
                typeof(AuthServerModule).Assembly
            );
        });

        var useProductionCert = configuration.GetValue<bool>("OpenIddict:Certificate:UseProductionCertificate");
        var certPath = configuration["OpenIddict:Certificate:CertificatePath"] ?? "openiddict.pfx";
        var certPassword = configuration["OpenIddict:Certificate:CertificatePassword"] ?? "";

        PreConfigure<OpenIddictBuilder>(builder =>
        {
            builder.AddServer(options =>
            {
                options.UseAspNetCore().DisableTransportSecurityRequirement();
                options.SetIssuer(new Uri(configuration["AuthServer:Authority"] 
                    ?? configuration["App:SelfUrl"] 
                    ?? "https://localhost:44320"));

                if (useProductionCert)
                {
                    PreConfigure<AbpOpenIddictAspNetCoreOptions>(opt =>
                    {
                        opt.AddDevelopmentEncryptionAndSigningCertificate = false;
                    });
                    if (File.Exists(certPath))
                    {
                        options.AddProductionEncryptionAndSigningCertificate(certPath, certPassword);
                    }
                    else
                    {
                        throw new FileNotFoundException($"OpenIddict certificate file not found: {certPath}");
                    }
                }

                options.DisableAccessTokenEncryption();

                if (int.TryParse(configuration["AccessTokenExpirationMinutes"], out int accessTokenMinutes) && accessTokenMinutes > 0)
                    options.SetAccessTokenLifetime(TimeSpan.FromMinutes(accessTokenMinutes));

                if (int.TryParse(configuration["RefreshTokenExpirationMinutes"], out int refreshTokenMinutes) && refreshTokenMinutes > 0)
                    options.SetRefreshTokenLifetime(TimeSpan.FromMinutes(refreshTokenMinutes));

                options.DisableRollingRefreshTokens();
            });
            
            builder.AddValidation(options =>
            {
                options.AddAudiences("Aevatar");
                options.UseLocalServer();
                options.UseAspNetCore();
            });
        });

        PreConfigure<OpenIddictServerBuilder>(builder =>
        {
            builder.Configure(opt =>
            {
                opt.GrantTypes.Add(GrantTypeConstants.GOOGLE);
                opt.GrantTypes.Add(GrantTypeConstants.APPLE);
            });
        });
    }

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();

        ConfigureBundles();
        ConfigureUrls(configuration);
        ConfigureAuthentication(context);
        ConfigureCors(context, configuration);
        ConfigureVirtualFileSystem();
        ConfigureNavigationServices();
        ConfigureSwaggerServices(context.Services);
        ConfigureDataProtection(context, configuration);
        ConfigureGrantHandlers(context, configuration);
        ConfigureAccountModule(context, configuration);

        // Configure distributed cache key prefix
        Configure<AbpDistributedCacheOptions>(options => 
        { 
            options.KeyPrefix = "GodGPT:"; 
        });

        Configure<PermissionManagementOptions>(options =>
        {
            options.IsDynamicPermissionStoreEnabled = true;
            
            // Configure permission management policies for providers
            options.ProviderPolicies["U"] = "AbpIdentity.Users.ManagePermissions";
            options.ProviderPolicies["R"] = "AbpIdentity.Roles.ManagePermissions";
            options.ProviderPolicies["C"] = "AbpIdentity.Clients.ManagePermissions";
        });

        context.Services.AddHealthChecks();
    }

    /// <summary>
    /// Configure Redis DataProtection for multi-machine token sharing
    /// </summary>
    private void ConfigureDataProtection(ServiceConfigurationContext context, IConfiguration configuration)
    {
        var redisConnection = configuration["Redis:Configuration"];
        if (!string.IsNullOrWhiteSpace(redisConnection))
        {
            var redis = ConnectionMultiplexer.Connect(redisConnection);
            context.Services
                .AddDataProtection()
                .PersistKeysToStackExchangeRedis(redis, "GodGPT-DataProtection-Keys")
                .SetApplicationName("GodGPTAuthServer");
        }
    }

    /// <summary>
    /// Configure Google/Apple OAuth grant handlers
    /// </summary>
    private void ConfigureGrantHandlers(ServiceConfigurationContext context, IConfiguration configuration)
    {
        // Configure Google options
        context.Services.Configure<GoogleOptions>(configuration.GetSection("Google"));
        
        // Configure Apple options
        context.Services.Configure<AppleOptions>(configuration.GetSection("Apple"));

        // Register grant handlers with OpenIddict
        context.Services.AddOptions<Volo.Abp.OpenIddict.ExtensionGrantTypes.AbpOpenIddictExtensionGrantsOptions>()
            .Configure<IServiceProvider>((options, serviceProvider) =>
            {
                options.Grants.Add(GrantTypeConstants.GOOGLE,
                    serviceProvider.GetRequiredService<GoogleGrantHandler>());
                options.Grants.Add(GrantTypeConstants.APPLE,
                    serviceProvider.GetRequiredService<AppleGrantHandler>());
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
            
            options.ScriptBundles.Configure(
                LeptonXLiteThemeBundles.Scripts.Global,
                bundle =>
                {
                    // CRITICAL: Load timeago compatibility layer BEFORE ABP framework scripts
                    // ABP 9.x dom-event-handlers.js calls $('time.timeago').timeago()
                    // but timeago.js was replaced with Luxon. This provides the missing jQuery plugin.
                    bundle.AddFiles("/libs/timeago/timeago-compat.js");
                    bundle.AddFiles("/global-scripts.js");
                }
            );
        });
    }

    private void ConfigureUrls(IConfiguration configuration)
    {
        Configure<AppUrlOptions>(options =>
        {
            options.Applications["MVC"].RootUrl = configuration["App:SelfUrl"];
        });
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context)
    {
        context.Services.Configure<AbpClaimsPrincipalFactoryOptions>(options =>
        {
            options.IsDynamicClaimsEnabled = true;
        });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                var origins = configuration["App:CorsOrigins"]?
                    .Split(",", StringSplitOptions.RemoveEmptyEntries)
                    .Select(o => o.RemovePostFix("/"))
                    .ToArray() ?? Array.Empty<string>();
                
                builder
                    .WithOrigins(origins)
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private void ConfigureVirtualFileSystem()
    {
        Configure<AbpVirtualFileSystemOptions>(options =>
        {
            // Explicitly specify base namespace to ensure correct virtual file path mapping
            // Embedded resource: Aevatar.AuthServer.Account.Templates.RegisterCode.tpl
            // Virtual path: /Account/Templates/RegisterCode.tpl
            options.FileSets.AddEmbedded<AuthServerModule>("Aevatar.AuthServer");
        });
    }

    private void ConfigureNavigationServices()
    {
        Configure<AbpNavigationOptions>(options =>
        {
            options.MenuContributors.Add(new AuthServerMenuContributor());
        });
    }

    private void ConfigureSwaggerServices(IServiceCollection services)
    {
        services.AddAbpSwaggerGen(
            options =>
            {
                options.SwaggerDoc("v1", new OpenApiInfo { Title = "AuthServer API", Version = "v1" });
                options.DocInclusionPredicate((docName, description) => true);
                options.CustomSchemaIds(type => type.FullName);
            }
        );
    }

    /// <summary>
    /// Configure Account module for registration, verification, and password reset
    /// </summary>
    private void ConfigureAccountModule(ServiceConfigurationContext context, IConfiguration configuration)
    {
        // Configure AccountOptions from appsettings
        context.Services.Configure<AccountOptions>(configuration.GetSection("Account"));

        // Register email template definitions
        Configure<AbpTextTemplatingOptions>(options =>
        {
            options.DefinitionProviders.Add<AccountEmailTemplateDefinitionProvider>();
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();


        app.UseForwardedHeaders();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();

        if (!env.IsDevelopment())
        {
            app.UseHsts();
        }

        app.UseCorrelationId();
        app.MapAbpStaticAssets();
        app.UseRouting();
        app.UseCors();
        app.UseAbpSecurityHeaders();
        app.UseAuthentication();
        app.UseAbpOpenIddictValidation();
        app.UseMultiTenancy();
        app.UseUnitOfWork();
        app.UseDynamicClaims();
        app.UseAuthorization();
        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "AuthServer API");
        });
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        app.UseConfiguredEndpoints();
    }
}

