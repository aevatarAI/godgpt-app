using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Serialization;
using Orleans.Streams;
using Orleans.Streams.Kafka.Config;
using MongoDB.Driver;
using Orleans.Providers.MongoDB.Configuration;
using Orleans.Providers.MongoDB.StorageProviders.Serializers;
using Confluent.Kafka;
using Serilog;

namespace Aevatar.Silo.Extensions;

/// <summary>
/// Orleans Host Configuration Extensions
/// Aligned with Legacy Aevatar.Silo configuration pattern
/// </summary>
public static class OrleansHostExtension
{
    /// <summary>
    /// Use Orleans with automatic configuration from appsettings.json
    /// Pattern: UseMongoDBClient -> UseMongoDBClustering -> AddMongoDBGrainStorage (using shared client)
    /// </summary>
    public static IHostBuilder UseOrleansConfiguration(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseOrleans((context, siloBuilder) =>
        {
            var configuration = context.Configuration;
            var orleansConfig = configuration.GetSection("Orleans");
            var clusterId = orleansConfig.GetValue("ClusterId", "aevatar-cluster");
            var serviceId = orleansConfig.GetValue("ServiceId", "aevatar-service");
            var siloPort = orleansConfig.GetValue("SiloPort", 11111);
            var gatewayPort = orleansConfig.GetValue("GatewayPort", 30000);
            
            Log.Information("📋 Orleans Cluster Configuration:");
            Log.Information("  ClusterId: {ClusterId}", clusterId);
            Log.Information("  ServiceId: {ServiceId}", serviceId);
            Log.Information("  SiloPort: {SiloPort}", siloPort);
            Log.Information("  GatewayPort: {GatewayPort}", gatewayPort);
            
            // 1. Configure Endpoints
            siloBuilder.ConfigureEndpoints(
                siloPort: siloPort,
                gatewayPort: gatewayPort,
                listenOnAnyHostAddress: true // Bind to 0.0.0.0
            );
            
            // 2. Configure MongoDB Client (Shared)
            var connectionString = configuration.GetConnectionString("Default") 
                ?? "mongodb://localhost:27017/AevatarBusiness";
            var databaseName = configuration.GetSection("Storage")
                .GetValue("DatabaseName", "AevatarBusiness");
                
            Log.Information("🗄️  Configuring MongoDB Client (Shared):");
            Log.Information("  ConnectionString: {ConnectionString}", connectionString);
            Log.Information("  DatabaseName: {DatabaseName}", databaseName);

            siloBuilder.UseMongoDBClient(connectionString);

            // 3. Configure Clustering (MongoDB)
            Log.Information("🤝 Configuring MongoDB Clustering...");
            siloBuilder.UseMongoDBClustering(options =>
            {
                options.DatabaseName = databaseName;
                options.Strategy = MongoDBMembershipStrategy.SingleDocument; // Legacy uses SingleDocument
                // Prefix for membership table
                options.CollectionPrefix = "OrleansAevatar"; 
            });

            // 4. Configure Cluster Options
            siloBuilder.Configure<ClusterOptions>(options =>
            {
                options.ClusterId = clusterId;
                options.ServiceId = serviceId;
            });

            // 5. Configure Storage (Using shared client)
            Log.Information("💾 Configuring MongoDB Storage (Using Shared Client)...");
            
            // Default Storage
            siloBuilder.AddMongoDBGrainStorage("Default", options => 
            {
                options.DatabaseName = databaseName;
                options.CollectionPrefix = "OrleansAevatar";
            });

            // PubSubStore
            siloBuilder.AddMongoDBGrainStorage("PubSubStore", options => 
            {
                options.DatabaseName = databaseName;
                options.CollectionPrefix = "StreamStorage";
            });

            // Note: EventStoreStorage removed - EventStorageGrain no longer used
            // Snapshots are now handled by IStateStore<TState> (stored in agent_states_{StateType})
            // Events are stored via IEventRepository (stored in agent_events_{AgentType})

            // 6. Configure Streaming (Orleans Memory Stream for now, Kafka later)
            ConfigureStreaming(siloBuilder, configuration);
            
            // 7. Configure Serializer
            siloBuilder.ConfigureServices(services => 
            {
                // Orleans internal serializer (for RPC communication)
                services.AddSerializer(serializerBuilder => 
                {
                    serializerBuilder.AddProtobufSerializer();
                });
                
                // MongoDB GrainStorage serializer: Use Binary (Orleans serializer) instead of JSON
                // This enables Protobuf binary storage for Grain State, reducing storage size ~30-50%
                services.AddSingleton<IGrainStateSerializer, BinaryGrainStateSerializer>();
            });
            
            // 8. Logging & Timeouts
            siloBuilder.Configure<SiloMessagingOptions>(options =>
            {
                options.ResponseTimeout = TimeSpan.FromMinutes(5);
                options.SystemResponseTimeout = TimeSpan.FromMinutes(5);
            });
            
            Log.Information("✅ Orleans configuration completed (Legacy Pattern)");
        });
    }

    /// <summary>
    /// Configure streaming providers (Orleans Memory or Kafka)
    /// </summary>
    private static void ConfigureStreaming(ISiloBuilder siloBuilder, IConfiguration configuration)
    {
        var streamConfig = configuration.GetSection("Streaming");
        var provider = streamConfig.GetValue("Provider", "OrleansStream");
        var providerName = streamConfig.GetValue("ProviderName", "Default");
        
        if (provider == "Kafka")
        {
            ConfigureKafkaStreaming(siloBuilder, configuration, providerName);
        }
        else
        {
            ConfigureOrleansMemoryStreaming(siloBuilder, configuration, providerName);
        }
    }

    private static void ConfigureOrleansMemoryStreaming(ISiloBuilder siloBuilder, IConfiguration configuration, string providerName)
    {
        Log.Information("📡 Configuring Orleans Memory Stream");
        
        // Primary stream provider (configurable name, default: "Default")
        siloBuilder.AddMemoryStreams(providerName, streamConfig =>
        {
            streamConfig.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
            streamConfig.ConfigurePullingAgent(pullingAgentConfig => pullingAgentConfig.Configure(options =>
            {
                options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(50);
            }));
        });
        Log.Information("   ✅ Added stream provider: {ProviderName}", providerName);
        
        // Agent event stream provider (used by OrleansGAgentGrain for agent communication)
        // This is the default provider name in AevatarAgentsOrleansConstants.StreamProviderName
        const string agentStreamProvider = "AevatarAgents";
        if (providerName != agentStreamProvider)
        {
            siloBuilder.AddMemoryStreams(agentStreamProvider, streamConfig =>
            {
                streamConfig.ConfigureStreamPubSub(StreamPubSubType.ExplicitGrainBasedAndImplicit);
                streamConfig.ConfigurePullingAgent(pullingAgentConfig => pullingAgentConfig.Configure(options =>
                {
                    options.GetQueueMsgsTimerPeriod = TimeSpan.FromMilliseconds(50);
                }));
            });
            Log.Information("   ✅ Added stream provider: {ProviderName}", agentStreamProvider);
        }
    }

    private static void ConfigureKafkaStreaming(ISiloBuilder siloBuilder, IConfiguration configuration, string providerName)
    {
        var kafkaConfig = configuration.GetSection("Kafka");
        var bootstrapServers = kafkaConfig.GetValue("BootstrapServers", "localhost:9092");
        var consumerGroupId = kafkaConfig.GetValue("ConsumerGroupId", "aevatar-silo-consumers");
        
        // 从配置读取默认的 Partition 和 Replication 设置
        var defaultPartitions = kafkaConfig.GetValue("DefaultPartitions", 8);
        var defaultReplicationFactor = kafkaConfig.GetValue("DefaultReplicationFactor", 1);
        
        // 从 Streaming 配置中读取 DefaultNamespace 作为 Kafka Topic 名称
        var streamingConfig = configuration.GetSection("Streaming");
        var defaultNamespace = streamingConfig.GetValue("DefaultNamespace", "agent-events");
        
        Log.Information("📡 Configuring Kafka Stream Provider");
        Log.Information("   Bootstrap Servers: {Servers}", bootstrapServers);
        Log.Information("   Consumer Group: {Group}", consumerGroupId);
        Log.Information("   Default Namespace (Kafka Topic): {Namespace}", defaultNamespace);
        Log.Information("   Default Partitions: {Partitions}", defaultPartitions);
        Log.Information("   Default Replication Factor: {ReplicationFactor}", defaultReplicationFactor);

        siloBuilder.AddKafka(providerName)
            .WithOptions(options =>
            {
                options.BrokerList = new List<string> { bootstrapServers };
                options.ConsumerGroupId = consumerGroupId;
                
                // ========== Producer Performance Configuration (Custom Orleans.Streams.Kafka) ==========
                // NOTE: These properties were removed in Orleans.Streams.Kafka 8.0.3+
                // Commented out for compatibility with newer versions
                /*
                var producerConfig = kafkaConfig.GetSection("Producer");
                
                // Acks (None/Leader/All)
                var acksStr = producerConfig.GetValue<string>("Acks");
                if (!string.IsNullOrEmpty(acksStr))
                {
                    options.ProducerAcks = acksStr.ToLower() switch
                    {
                        "none" => Acks.None,
                        "leader" => Acks.Leader,
                        "all" => Acks.All,
                        _ => Acks.Leader
                    };
                    Log.Information("   🔧 Producer Acks: {Acks}", options.ProducerAcks);
                }
                
                // LingerMs
                var lingerMs = producerConfig.GetValue<int?>("LingerMs");
                if (lingerMs.HasValue)
                {
                    options.ProducerLingerMs = lingerMs.Value;
                    Log.Information("   🔧 Producer LingerMs: {LingerMs}", lingerMs);
                }
                
                // BatchSize
                var batchSize = producerConfig.GetValue<int?>("BatchSize");
                if (batchSize.HasValue)
                {
                    options.ProducerBatchSize = batchSize.Value;
                    Log.Information("   🔧 Producer BatchSize: {BatchSize}", batchSize);
                }
                
                // CompressionType
                var compressionStr = producerConfig.GetValue<string>("CompressionType");
                if (!string.IsNullOrEmpty(compressionStr))
                {
                    options.ProducerCompressionType = compressionStr.ToLower() switch
                    {
                        "none" => CompressionType.None,
                        "gzip" => CompressionType.Gzip,
                        "snappy" => CompressionType.Snappy,
                        "lz4" => CompressionType.Lz4,
                        "zstd" => CompressionType.Zstd,
                        _ => CompressionType.None
                    };
                    Log.Information("   🔧 Producer CompressionType: {CompressionType}", options.ProducerCompressionType);
                }
                
                // MaxInFlight
                var maxInFlight = producerConfig.GetValue<int?>("MaxInFlight");
                if (maxInFlight.HasValue)
                {
                    options.ProducerMaxInFlight = maxInFlight.Value;
                    Log.Information("   🔧 Producer MaxInFlight: {MaxInFlight}", maxInFlight);
                }
                */
                
                // 添加默认 Topic
                options.AddTopic(defaultNamespace, new Orleans.Streams.Kafka.Config.TopicCreationConfig
                {
                    AutoCreate = true,
                    Partitions = (short)defaultPartitions,
                    ReplicationFactor = (short)defaultReplicationFactor
                });
                
                // 添加 Benchmark 测试所需的 Topics (Multi-Topic Test)
                var benchmarkTopics = new[]
                {
                    "AevatarAgents-Shared",
                    "AevatarAgents-TypeA",
                    "AevatarAgents-TypeB",
                    "AevatarAgents-TypeC",
                    "AevatarAgents-TypeD",
                    "AevatarAgents-TypeE"
                };
                
                foreach (var topic in benchmarkTopics)
                {
                    options.AddTopic(topic, new Orleans.Streams.Kafka.Config.TopicCreationConfig
                    {
                        AutoCreate = true,
                        Partitions = (short)defaultPartitions,
                        ReplicationFactor = (short)defaultReplicationFactor
                    });
                }
                
                Log.Information("   📋 Configured {Count} additional benchmark topics", benchmarkTopics.Length);
            })
            .AddLoggingTracker()
            .Build();
    }
}
