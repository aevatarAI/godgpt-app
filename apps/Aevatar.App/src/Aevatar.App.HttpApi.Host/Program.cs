using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
using MongoDB.Driver;
using Orleans.Providers.MongoDB.Configuration;

namespace Aevatar.App.HttpApi.Host;

public class Program
{
    public async static Task<int> Main(string[] args)
    {
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(c => c.File("Logs/logs.txt"))
            .WriteTo.Async(c => c.Console())
            .CreateBootstrapLogger();

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
            
            builder.Host
                .AddAppSettingsSecretsJson()
                .UseAutofac()
                .UseSerilog((context, services, loggerConfiguration) =>
                {
                    loggerConfiguration
                    #if DEBUG
                        .MinimumLevel.Debug()
                    #else
                        .MinimumLevel.Information()
                    #endif
                        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
                        .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
                        .Enrich.FromLogContext()
                        .WriteTo.Async(c => c.File("Logs/logs.txt"))
                        .WriteTo.Async(c => c.Console())
                        .WriteTo.Async(c => c.AbpStudio(services));
                });
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
            var databaseName = "AevatarBusiness"; // Should match Silo config
            
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
            
            // 4. Configure Kafka Stream Provider (MUST match Silo configuration!)
                var bootstrapServers = config.GetValue<string>("Kafka:BootstrapServers") ?? "localhost:9092";
                var consumerGroupId = config.GetValue<string>("Kafka:ConsumerGroupId") ?? "aevatar-client-consumers";
            var defaultPartitions = config.GetValue<int>("Kafka:DefaultPartitions", 8);
            var defaultReplicationFactor = config.GetValue<int>("Kafka:DefaultReplicationFactor", 1);
                
                // Read topics from config (comma-separated list or fallback to DefaultNamespace)
                var topicsConfig = config.GetValue<string>("Streaming:Topics");
                if (string.IsNullOrEmpty(topicsConfig))
                {
                    topicsConfig = config.GetValue<string>("Streaming:DefaultNamespace") ?? "agent-events";
                }
                
            Log.Information("🌊 Configuring Kafka Stream Provider");
                Log.Information("   BootstrapServers: {Servers}", bootstrapServers);
                Log.Information("   ConsumerGroupId: {GroupId}", consumerGroupId);
                Log.Information("   Topics: {Topics}", topicsConfig);
                
            // Primary stream provider
                clientBuilder
                    .AddKafka(orleansOptions.StreamProviderName)
                    .WithOptions(options =>
                    {
                        options.BrokerList = new List<string> { bootstrapServers };
                        options.ConsumerGroupId = consumerGroupId;
                        options.ConsumeMode = ConsumeMode.LastCommittedMessage;
                        
                        foreach (var topic in topicsConfig.Split(','))
                        {
                            var topicName = topic.Trim();
                            if (!string.IsNullOrEmpty(topicName))
                            {
                                options.AddTopic(topicName, new TopicCreationConfig
                                {
                                    AutoCreate = true,
                                Partitions = defaultPartitions,
                                ReplicationFactor = (short)defaultReplicationFactor
                                });
                                Log.Information("      ✅ Configured topic: {Topic}", topicName);
                            }
                        }
                    })
                    .AddJson()
                    .Build();
            
            Log.Information("   ✅ Added primary Kafka stream provider: {Provider}", orleansOptions.StreamProviderName);
            
            // AevatarAgents stream provider for GodChat client streaming
            // (used by ChatMiddleware to subscribe to GodChatGAgent responses)
            const string agentStreamProvider = "AevatarAgents";
            if (orleansOptions.StreamProviderName != agentStreamProvider)
            {
                clientBuilder
                    .AddKafka(agentStreamProvider)
                    .WithOptions(options =>
                    {
                        options.BrokerList = new List<string> { bootstrapServers };
                        options.ConsumerGroupId = $"{consumerGroupId}-agents";
                        options.ConsumeMode = ConsumeMode.LastCommittedMessage;
                        
                        // AevatarAgents topic for client streaming
                        options.AddTopic(agentStreamProvider, new TopicCreationConfig
                        {
                            AutoCreate = true,
                            Partitions = defaultPartitions,
                            ReplicationFactor = (short)defaultReplicationFactor
                        });
                    })
                    .AddJson()
                    .Build();
                
                Log.Information("   ✅ Added agent Kafka stream provider: {Provider}", agentStreamProvider);
            }

            // 5. Add Protobuf serializer
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
}
