using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Aevatar.App.MongoDB;

namespace Aevatar.AuthServer;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        ConfigureLogger();

        try
        {
            Log.Information("Starting Aevatar.AuthServer.");
            
            // Configure MongoDB GUID serialization BEFORE any ABP modules are loaded
            // This must happen before ABP MongoDB modules initialize
            ConfigureMongoGuidSerialization();
            
            var builder = WebApplication.CreateBuilder(args);
            
            // Configure OpenTelemetry
            ConfigureOpenTelemetry(builder);
            
            builder.Host.AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            await builder.AddApplicationAsync<AuthServerModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Aevatar.AuthServer terminated unexpectedly!");
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLogger(LoggerConfiguration? loggerConfiguration = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();
        Log.Logger = (loggerConfiguration ?? new LoggerConfiguration())
            .ReadFrom.Configuration(configuration)
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .CreateLogger();
    }

    private static void ConfigureMongoGuidSerialization()
        => MongoGuidSerialization.Configure();

    /// <summary>
    /// Configure OpenTelemetry for distributed tracing and metrics
    /// </summary>
    private static void ConfigureOpenTelemetry(WebApplicationBuilder builder)
    {
        var config = builder.Configuration;
        var otelEnabled = config.GetValue("OpenTelemetry:Enabled", false);
        
        if (!otelEnabled)
        {
            Log.Information("📊 OpenTelemetry: Disabled (set OpenTelemetry:Enabled=true to enable)");
            return;
        }
        
        var serviceName = config.GetValue("OpenTelemetry:ServiceName", "Aevatar.AuthServer");
        var serviceVersion = config.GetValue("OpenTelemetry:ServiceVersion", "1.0.0");
        var collectorEndpoint = config.GetValue("OpenTelemetry:CollectorEndpoint", "http://localhost:4317");
        
        Log.Information("📊 Configuring OpenTelemetry");
        Log.Information("   ServiceName: {ServiceName}", serviceName);
        Log.Information("   ServiceVersion: {ServiceVersion}", serviceVersion);
        Log.Information("   CollectorEndpoint: {CollectorEndpoint}", collectorEndpoint);
        
        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(collectorEndpoint);
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(collectorEndpoint);
                }));
    }
}
