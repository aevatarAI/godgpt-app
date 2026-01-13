using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using MongoDB.Driver;
using Aevatar.Silo.Extensions;
using Aevatar.App.Agents.Agents; // CRITICAL: Reference to force assembly load
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.MongoDB;
using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Aevatar.Agents.Orleans.MongoDB;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Runtime.Orleans.CQRS;
using Aevatar.Agents.Plugins.CQRS;
using Aevatar.Agents.Plugins.CQRS.Batching;
using Aevatar.Agents.Plugins.CQRS.Elasticsearch;  // Use Core's CQRS implementation
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.MEAI.DependencyInjection;
using Aevatar.Agents.GodGPT.Extensions;
using Aevatar.Agents.Lumen.Extensions;
using Aevatar.Agents.Core.EventSourcing;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Volo.Abp.BlobStoring;

namespace Aevatar.Silo;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Configure Serilog
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        try
        {
            Log.Information("🚀 Starting Aevatar Silo");
            Log.Information("  Environment: {Environment}", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production");
            Log.Information("  Storage Provider: {Provider}", configuration["Storage:Provider"] ?? "Memory");
            Log.Information("  Streaming Provider: {Provider}", configuration["Streaming:Provider"] ?? "OrleansStream");
            
            var host = CreateHostBuilder(args).Build();
            
            await host.RunAsync();
            
            Log.Information("✅ Silo shutdown completed");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "❌ Silo terminated unexpectedly!");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder(string[] args)
    {
        return Host.CreateDefaultBuilder(args)
            .UseSerilog()
            .UseOrleansConfiguration() // Extension method from OrleansHostExtension
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.Configure(app =>
                {
                    // Map health check endpoints for Kubernetes
                    app.MapOrleansHealthChecks();
                });
                
                webBuilder.ConfigureKestrel(options =>
                {
                    // Health check endpoint port
                    options.ListenAnyIP(8081);
                });
            })
            .ConfigureServices((context, services) =>
            {
                services.AddOrleansHealthChecks();
                
                // Configure OpenTelemetry
                ConfigureOpenTelemetry(context.Configuration, services);
                
                // Configure MongoDB BSON serializers (must be first)
                MongoDBServiceCollectionExtensions.ConfigureBsonSerializers();
                
                // MongoDB configuration
                var mongoConnectionString = context.Configuration.GetConnectionString("Orleans") 
                    ?? "mongodb://localhost:27017/AevatarBusiness";
                var databaseName = context.Configuration.GetSection("Storage")
                    .GetValue("DatabaseName", "AevatarBusiness");
                
                services.AddSingleton<IMongoClient>(sp => new MongoClient(mongoConnectionString));
                services.AddSingleton<IMongoDatabase>(sp => 
                    sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));
                
                // MongoDB Event Repository for EventStore
                services.AddSingleton<IEventRepository>(sp => new MongoEventRepository(
                    sp.GetRequiredService<IMongoClient>(),
                    new MongoEventRepositoryOptions
                    {
                        DatabaseName = databaseName,
                        CollectionName = "agent_events",
                        EnableDetailedLogging = true
                    },
                    sp.GetRequiredService<ILogger<MongoEventRepository>>()));

                // Configure MessageStreamProviderOptions
                services.Configure<MessageStreamProviderOptions>(context.Configuration.GetSection("MessageStream"));
                
                // Configure EventSourcing options
                services.Configure<EventSourcingOptions>(context.Configuration.GetSection(EventSourcingOptions.SectionName));
                var eventSourcingOptions = context.Configuration
                    .GetSection(EventSourcingOptions.SectionName)
                    .Get<EventSourcingOptions>() ?? new EventSourcingOptions();
                
                Log.Information("📚 EventSourcing Configuration:");
                Log.Information("  Enabled: {Enabled}", eventSourcingOptions.Enabled);
                Log.Information("  SnapshotFrequency: {SnapshotFrequency}", eventSourcingOptions.SnapshotFrequency);
                Log.Information("  Provider: {Provider}", eventSourcingOptions.Provider);
                
                // Configure LLM Providers for AI Agents
                services.Configure<LLMProvidersConfig>(context.Configuration.GetSection("LLMProviders"));
                services.AddMEAI();
                Log.Information("🤖 LLM Providers configured from appsettings.json");
                
                // Configure AWS S3 BlobContainer for image downloads
                // Uses shared implementation from Aevatar.App.Application
                services.Configure<App.Application.Services.AwsS3Options>(context.Configuration.GetSection("AwsS3"));
                services.AddSingleton<App.Application.Services.AwsS3BlobContainer>();
                services.AddSingleton<IBlobContainer>(sp => sp.GetRequiredService<App.Application.Services.AwsS3BlobContainer>());
                Log.Information("🗂️ AWS S3 BlobContainer configured for image downloads");

                // MassTransit Stream Plugin - ONLY if MessageStream.Provider is "MassTransit"
                var messageStreamProvider = context.Configuration.GetSection("MessageStream").GetValue("Provider", "Orleans");
                Log.Information("  MessageStream Provider: {Provider}", messageStreamProvider);
                
                if (messageStreamProvider == "MassTransit")
                {
                    Log.Information("🔌 Registering MassTransit Stream Plugin (Kafka Consumer + Producer)");
                    services.AddMassTransitStreamPlugin(context.Configuration);
                }
                else
                {
                    Log.Information("📡 Using Orleans Kafka Stream (no MassTransit)");
                }

                // Aevatar Agent System with MongoDB stores
                services.AddAevatarAgentSystem(options =>
                {
                    options.StateStoreType = typeof(MongoDBStateStore<>);
                    options.ConfigStoreType = typeof(MongoDbConfigStore<>);
                    options.EventRouterStoreType = typeof(MongoDBEventRouterStore);
                    
                    // Dynamic EventStore selection based on configuration
                    if (eventSourcingOptions.Enabled)
                    {
                        options.EventStoreType = eventSourcingOptions.Provider.ToUpperInvariant() switch
                        {
                            "MONGODB" or "ORLEANS" => typeof(OrleansEventStore),
                            "MEMORY" => typeof(InMemoryEventStore),
                            _ => typeof(OrleansEventStore) // Default to Orleans for production
                        };
                        Log.Information("✅ EventStore: {EventStoreType}", options.EventStoreType.Name);
                    }
                    else
                    {
                        // EventSourcing disabled - no EventStore registration
                        // Agents will use simple StateStore persistence
                        options.EventStoreType = null;
                        Log.Information("⏸️ EventSourcing disabled - using StateStore only");
                    }
                }, builder => builder.UseOrleansRuntime());
                
                Log.Information("✅ Aevatar Agent System configured with MongoDB stores");

                // CQRS State Projection (Orleans Stream)
                // When MessageStream.Provider=MassTransit, Orleans Streaming is disabled, so skip Orleans CQRS streaming.
                if (!string.Equals(messageStreamProvider, "MassTransit", StringComparison.OrdinalIgnoreCase))
                {
                    services.AddOrleansCQRS(options =>
                    {
                        options.StreamProviderName = "Default";
                        options.StreamNamespace = "StateProjection";
                    });
                }
                
                // Use Core's CQRS implementation (same as HttpApi.Host in Local mode)
                var esUrl = context.Configuration.GetValue<string>("Elasticsearch:Url") ?? "http://localhost:9200";
                var esPrefix = context.Configuration.GetValue<string>("Elasticsearch:IndexPrefix") ?? "aevatar-state";
                
                services.AddCQRS(options =>
                {
                    options.UseElasticsearch(es =>
                    {
                        es.Url = esUrl;
                        es.IndexPrefix = esPrefix;
                    });
                    options.UseBatchedProjection(batch =>
                    {
                        var cqrsConfig = context.Configuration.GetSection("CQRS:Projector");
                        batch.BatchSize = cqrsConfig.GetValue("BatchSize", 50);
                        batch.MaxBatchSize = cqrsConfig.GetValue("MaxBatchSize", 200);
                        batch.BatchTimeoutSeconds = Math.Max(1, cqrsConfig.GetValue("FlushIntervalMs", 1000) / 1000);
                        batch.MaxRetryCount = cqrsConfig.GetValue("MaxRetryCount", 3);
                    });
                });
                
                Log.Information("✅ CQRS configured with Core.BatchedStateProjector (ES: {EsUrl})", esUrl);
                
                // Register GodGPT Agent services and configuration (modularized)
                services.AddGodGPTServices(context.Configuration);
                
                // Register Lumen Agent services and configuration
                services.AddLumenServices(context.Configuration);
                
                // Configure ManagerOptions for admin operations (framework-level, not GodGPT-specific)
                services.Configure<Aevatar.Common.Options.ManagerOptions>(context.Configuration.GetSection("ManagerIds"));
                
                Log.Information("✅ GodGPT & Lumen Agent services registered");
            });
    }

    /// <summary>
    /// Configure OpenTelemetry for distributed tracing and metrics
    /// </summary>
    private static void ConfigureOpenTelemetry(IConfiguration config, IServiceCollection services)
    {
        var otelEnabled = config.GetValue("OpenTelemetry:Enabled", false);
        
        if (!otelEnabled)
        {
            Log.Information("📊 OpenTelemetry: Disabled (set OpenTelemetry:Enabled=true to enable)");
            return;
        }
        
        var serviceName = config.GetValue("OpenTelemetry:ServiceName", "Aevatar.Silo");
        var serviceVersion = config.GetValue("OpenTelemetry:ServiceVersion", "1.0.0");
        var collectorEndpoint = config.GetValue("OpenTelemetry:CollectorEndpoint", "http://localhost:4317");
        
        Log.Information("📊 Configuring OpenTelemetry");
        Log.Information("   ServiceName: {ServiceName}", serviceName);
        Log.Information("   ServiceVersion: {ServiceVersion}", serviceVersion);
        Log.Information("   CollectorEndpoint: {CollectorEndpoint}", collectorEndpoint);
        
        services.AddOpenTelemetry()
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

