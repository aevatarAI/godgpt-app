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
using Aevatar.App.MongoDB;
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
