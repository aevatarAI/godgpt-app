using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.App.Common;
using Aevatar.App.HttpApi.Host.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Streams.Kafka.Config;
using Serilog;
using Serilog.Events;
using Orleans.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;
using Orleans.Providers.MongoDB.Configuration;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Aevatar.App.HttpApi.Host;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        ConfigureLogger();
        ConfigureMongoGuidSerialization();

        try
        {
            Log.Information("Starting App HttpApi.Host.");
            var builder = WebApplication.CreateBuilder(args);
            
            // Read Agent Runtime configuration
            var runtimeOptions = builder.Configuration
                .GetSection(AgentRuntimeOptions.SectionName)
                .Get<AgentRuntimeOptions>() ?? new AgentRuntimeOptions();
            
            // Configure Orleans if using Orleans runtime
            if (runtimeOptions.RuntimeType == AgentRuntimeType.Orleans)
            {
                ConfigureOrleans(builder, runtimeOptions.Orleans);
            }
            
            // Configure OpenTelemetry
            ConfigureOpenTelemetry(builder);
            
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog();
            await builder.AddApplicationAsync<AppHttpApiHostModule>();
            var app = builder.Build();
            await app.InitializeApplicationAsync();
            await app.RunAsync();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly!");
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
    /// Configure Orleans when using Orleans runtime
    /// Matches Legacy Silo configuration (MongoDB Clustering)
    /// </summary>
    private static void ConfigureOrleans(WebApplicationBuilder builder, OrleansRuntimeOptions orleansOptions)
    {
        builder.Host.UseOrleansClient((context, clientBuilder) =>
        {
            var config = context.Configuration;
            // Use Orleans connection string for MongoDB clustering
            var connectionString = config.GetConnectionString("Orleans") ?? "mongodb://localhost:27017/AevatarBusiness";
            
            // Get database name from config, or parse from connection string
            var databaseName = config.GetSection("Storage")
                .GetValue("DatabaseName", string.Empty);
            
            if (string.IsNullOrEmpty(databaseName))
            {
                // Parse database name from connection string as fallback
                var mongoUrl = new MongoUrl(connectionString);
                databaseName = mongoUrl.DatabaseName ?? "AevatarBusiness";
            }
            
            Log.Information("🌐 Configuring Orleans Client with MongoDB Clustering");
            Log.Information("   ConnectionString: {ConnectionString}", connectionString);
            Log.Information("   DatabaseName: {DatabaseName}", databaseName);

            // 1. Configure MongoDB Client
            clientBuilder.UseMongoDBClient(connectionString);

            // 2. Configure Clustering (Must match Silo)
            clientBuilder.UseMongoDBClustering(options =>
            {
                options.DatabaseName = databaseName;
                options.Strategy = MongoDBMembershipStrategy.SingleDocument;
                options.CollectionPrefix = "OrleansAevatar"; 
            });

            // 3. Configure Cluster Options
            clientBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = orleansOptions.ClusterId;
                options.ServiceId = orleansOptions.ServiceId;
            });

            // 4. Add Protobuf serializer
            clientBuilder.ConfigureServices(services =>
            {
                services.AddSerializer(serializerBuilder =>
                {
                    serializerBuilder.AddProtobufSerializer();
                });
            });
            
            Log.Information("   ClusterId: {ClusterId}", orleansOptions.ClusterId);
            Log.Information("   ServiceId: {ServiceId}", orleansOptions.ServiceId);
        });
    }

    /// <summary>
    /// Configure MongoDB GUID serialization for ABP data compatibility.
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
            // Register null provider so MetricsRecorder can still be resolved
            builder.Services.AddSingleton<IInstrumentationProvider>(_ => new NullInstrumentationProvider());
            return;
        }
        
        var serviceName = config.GetValue("OpenTelemetry:ServiceName", "Aevatar.App.HttpApi.Host");
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
                })
                .AddMeter(serviceName));
        builder.Services.AddSingleton<IInstrumentationProvider>(_ =>
            new InstrumentationProvider(serviceName, serviceVersion));
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
