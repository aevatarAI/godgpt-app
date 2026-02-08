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
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

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

    /// <summary>
    /// Configure MongoDB GUID serialization to use Legacy format for backward compatibility.
    /// Uses ConventionPack (ABP recommended approach for MongoDB Driver 3.x).
    /// Must be called before any MongoDB operations.
    /// </summary>
    private static void ConfigureMongoGuidSerialization()
    {
        try
        {
            // Register convention pack for legacy GUID handling (ABP recommended approach)
            var conventionPack = new ConventionPack { new LegacyGuidConvention() };
            ConventionRegistry.Register("LegacyGuidConvention", conventionPack, _ => true);
            Log.Information("✅ MongoDB GUID serialization configured: CSharpLegacy (Convention)");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "MongoDB GUID convention may already be registered, continuing...");
        }
    }

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

/// <summary>
/// Convention to serialize all GUID properties using CSharpLegacy representation.
/// This is the ABP recommended approach for MongoDB Driver 3.x compatibility.
/// </summary>
public class LegacyGuidConvention : ConventionBase, IMemberMapConvention
{
    public void Apply(BsonMemberMap memberMap)
    {
        if (memberMap.MemberType == typeof(Guid))
        {
            memberMap.SetSerializer(new GuidSerializer(GuidRepresentation.CSharpLegacy));
        }
        else if (memberMap.MemberType == typeof(Guid?))
        {
            var guidSerializer = new GuidSerializer(GuidRepresentation.CSharpLegacy);
            memberMap.SetSerializer(new NullableSerializer<Guid>(guidSerializer));
        }
    }
}
