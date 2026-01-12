using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using Aevatar.App.HttpApi.Host.Extensions;
using Aevatar.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.OpenApi.Models;
using Aevatar.App.MongoDB;
using Aevatar.Payment;
using Volo.Abp;
using Volo.Abp.AspNetCore.Authentication.JwtBearer;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Serilog;
using Volo.Abp.Autofac;
using Volo.Abp.Modularity;
using Volo.Abp.Swashbuckle;
using Volo.Abp.Studio.Client.AspNetCore;
using Volo.Abp.BlobStoring;
using Volo.Abp.BlobStoring.Aws;
using Aevatar.App.HttpApi.Host.Handler;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.App.HttpApi.Host.BackgroundJobs;
using Aevatar.App.Application.Options;
using Aevatar.Controllers;
using AutoResponseWrapper;

namespace Aevatar.App.HttpApi.Host;

[DependsOn(
    typeof(AppHttpApiModule),
    typeof(AppApplicationModule),
    typeof(AppMongoDbModule),
    typeof(PaymentModule),
    typeof(AbpAspNetCoreAuthenticationJwtBearerModule),
    typeof(AbpAutofacModule),
    typeof(AbpAspNetCoreSerilogModule),
    typeof(AbpSwashbuckleModule),
    typeof(AbpStudioClientAspNetCoreModule),
    typeof(AbpBlobStoringAwsModule)
)]
public class AppHttpApiHostModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var configuration = context.Services.GetConfiguration();
        var hostingEnvironment = context.Services.GetHostingEnvironment();

        if (!configuration.GetValue<bool>("App:DisablePII"))
        {
            Microsoft.IdentityModel.Logging.IdentityModelEventSource.ShowPII = true;
        }

        ConfigureConventionalControllers();
        ConfigureAuthentication(context, configuration);
        ConfigureCors(context, configuration);
        ConfigureSwaggerServices(context, configuration);
        
        // Configure GodGPT Options
        context.Services.Configure<GodGPTOptions>(configuration.GetSection("GodGPT"));
        
        // Configure ExternalLocalization Options
        context.Services.Configure<ExternalLocalizationOptions>(
            configuration.GetSection(ExternalLocalizationOptions.SectionName));
        
        // Configure ManagerOptions for admin operations
        context.Services.Configure<Aevatar.Common.Options.ManagerOptions>(options =>
        {
            var managerIds = configuration.GetSection("ManagerIds").Get<List<string>>();
            if (managerIds != null)
            {
                options.ManagerIds = managerIds;
            }
        });
        
        // Configure Agent Runtime (Local or Orleans)
        ConfigureAgentRuntime(context, configuration);
        
        // Configure AWS S3 Blob Storage
        ConfigureBlobStorage(context, configuration);
        
        // Configure AutoResponseWrapper for consistent API response formatting
        ConfigureAutoResponseWrapper(context);
        
        // Configure Health Checks
        context.Services.AddHealthChecks();
        
        // Configure Hangfire background job processing
        context.Services.AddHangfireWithMongo(configuration);
        
        // Configure AuthServer Proxy for backward compatibility with /api/account routes
        ConfigureAuthServerProxy(context, configuration);
    }
    
    /// <summary>
    /// Configure HttpClient for proxying requests to AuthServer.
    /// This enables backward compatibility for old /api/account routes.
    /// </summary>
    private void ConfigureAuthServerProxy(ServiceConfigurationContext context, IConfiguration configuration)
    {
        // Configure AuthServerProxyOptions from appsettings
        context.Services.Configure<AuthServerProxyOptions>(options =>
        {
            // Use AuthServer:Authority URL, removing trailing slash
            var authority = configuration["AuthServer:Authority"]?.TrimEnd('/') ?? "http://localhost:8001";
            options.BaseUrl = authority;
        });
        
        // Register named HttpClient for AuthServer communication
        context.Services.AddHttpClient("AuthServer", client =>
        {
            var authority = configuration["AuthServer:Authority"]?.TrimEnd('/') ?? "http://localhost:8001";
            client.BaseAddress = new Uri(authority);
            client.Timeout = TimeSpan.FromSeconds(30);
        }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            // Allow self-signed certificates in development
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        });
    }
    
    private static void ConfigureAutoResponseWrapper(ServiceConfigurationContext context)
    {
        context.Services.AddAutoResponseWrapper();
    }
    
    private void ConfigureBlobStorage(ServiceConfigurationContext context, IConfiguration configuration)
    {
        Configure<AbpBlobStoringOptions>(options =>
        {
            options.Containers.ConfigureDefault(container =>
            {
                var configSection = configuration.GetSection("AwsS3");
                container.UseAws(o =>
                {
                    o.AccessKeyId = configSection.GetValue<string>("AccessKeyId", "None");
                    o.SecretAccessKey = configSection.GetValue<string>("SecretAccessKey", "None");
                    o.Region = configSection.GetValue<string>("Region", "None");
                    o.ContainerName = configSection.GetValue<string>("ContainerName", "None");
                });
            });
        });
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

    private void ConfigureConventionalControllers()
    {
        Configure<AbpAspNetCoreMvcOptions>(options =>
        {
            options.ConventionalControllers.Create(typeof(AppApplicationModule).Assembly);
        });
    }

    private void ConfigureAuthentication(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddAbpJwtBearer(options =>
            {
                options.Authority = configuration["AuthServer:Authority"];
                options.RequireHttpsMetadata = false;
                options.Audience = "Aevatar";
                
                // Allow self-signed certificates in development
                options.BackchannelHttpHandler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
                };
            });
    }

    private void ConfigureCors(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder =>
            {
                builder
                    .WithOrigins(
                        configuration["App:CorsOrigins"]?
                            .Split(",", StringSplitOptions.RemoveEmptyEntries)
                            .Select(o => o.RemovePostFix("/"))
                            .ToArray() ?? Array.Empty<string>()
                    )
                    .WithAbpExposedHeaders()
                    .SetIsOriginAllowedToAllowWildcardSubdomains()
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            });
        });
    }

    private void ConfigureSwaggerServices(ServiceConfigurationContext context, IConfiguration configuration)
    {
        context.Services.AddAbpSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "App API",
                Version = "v1"
            });
            options.DocInclusionPredicate((docName, description) => true);
            options.CustomSchemaIds(type => type.FullName);
            
            // Add Bearer Token Authentication
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter your Bearer token in the format: {your_token}"
            });
            
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var env = context.GetEnvironment();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }

        app.UseAbpRequestLocalization();
        app.UseCorrelationId();
        app.UseStaticFiles();
        app.UseRouting();

        // ============================================================
        //  Health / Smoke endpoint
        //
        //  WHY:
        //  - ABP HttpApi.Host is "API-first"; "/" may legitimately be unmapped.
        //  - Our integration test expects "/" to return 200 OK.
        // ============================================================
        app.Use(async (httpContext, next) =>
        {
            if (HttpMethods.IsGet(httpContext.Request.Method) && httpContext.Request.Path == "/")
            {
                httpContext.Response.StatusCode = StatusCodes.Status200OK;
                await httpContext.Response.WriteAsync("OK");
                return;
            }

            await next();
        });

        app.UseCors();
        app.UseAuthentication();
        app.UseAuthorization();
        
        // ChatMiddleware for streaming AI chat (SSE)
        // Must be after Authentication/Authorization to access user claims
        app.UseMiddleware<ChatMiddleware>();
        
        app.UseSwagger();
        app.UseAbpSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/swagger/v1/swagger.json", "App API");
            var configuration = context.ServiceProvider.GetRequiredService<IConfiguration>();
            var swaggerClientId = configuration["AuthServer:SwaggerClientId"];
            if (!string.IsNullOrWhiteSpace(swaggerClientId))
            {
                options.OAuthClientId(swaggerClientId);
            }
        });
        app.UseHealthChecks("/health");
        app.UseAuditing();
        app.UseAbpSerilogEnrichers();
        
        // Configure Hangfire and register recurring jobs
        app.UseHangfireWithJobs(env);
        
        app.UseConfiguredEndpoints();
    }
}
