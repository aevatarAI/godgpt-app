using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.EventRouting;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.Agents.Plugins.MassTransit;
using Business.Server;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Orleans;
using MassTransit;
using Aevatar.Agents;
using Aevatar.App.Agents.Agents;
using TypedAgent = Aevatar.App.Agents.Agents.TypedAgent;
using TypeAAgent = Aevatar.App.Agents.Agents.TypeAAgent;
using TypeBAgent = Aevatar.App.Agents.Agents.TypeBAgent;
using TypeCAgent = Aevatar.App.Agents.Agents.TypeCAgent;
using TypeDAgent = Aevatar.App.Agents.Agents.TypeDAgent;
using TypeEAgent = Aevatar.App.Agents.Agents.TypeEAgent;

namespace Aevatar.App.PerformanceTests;

/// <summary>
/// Multi-Topic Multi-Agent Benchmark
/// Tests TRUE multi-topic Orleans Stream behavior vs MassTransit
/// </summary>
public class MultiTopicBenchmark
{
    private readonly Dictionary<string, IGAgentActorManager> _actorManagersByType;
    private readonly ILogger _logger;
    private readonly IClusterClient _clusterClient;
    private readonly string _providerName;
    
    // Test configuration
    private const int AGENT_TYPES = 5;
    private const int AGENTS_PER_TYPE = 20;
    
    // Scenario configuration
    private int _messagesPerType = 100;  // Can be adjusted per test
    
    // Stream namespaces for each type
    private static readonly Dictionary<string, string> TypeNamespaces = new()
    {
        { "TypeA", "AevatarAgents-TypeA" },
        { "TypeB", "AevatarAgents-TypeB" },
        { "TypeC", "AevatarAgents-TypeC" },
        { "TypeD", "AevatarAgents-TypeD" },
        { "TypeE", "AevatarAgents-TypeE" }
    };
    
    public MultiTopicBenchmark(IClusterClient clusterClient, ILogger logger, string providerName)
    {
        _clusterClient = clusterClient;
        _logger = logger;
        _providerName = providerName;
        _actorManagersByType = new Dictionary<string, IGAgentActorManager>();
    }
    
    private async Task InitializeActorManagersAsync()
    {
        Console.WriteLine("🔧 Initializing Actor Managers...");

        if (_providerName == "MassTransit")
        {
            // MassTransit: Single Manager, Dynamic Routing via TopicMapping
            Console.WriteLine("   ✅ MassTransit Mode: Using SINGLE Manager with Dynamic Routing");
            
            var sharedManager = await CreateMassTransitManager();
            
            // Assign the same manager to all types
            foreach (var typeName in TypeNamespaces.Keys)
            {
                _actorManagersByType[typeName] = sharedManager;
            }
        }
        else
        {
            // Orleans Stream: Multiple Managers (Old Architecture)
            Console.WriteLine("   ✅ Orleans Stream Mode: Using Multiple Managers (Isolated Namespace)");
            
            foreach (var kvp in TypeNamespaces)
            {
                var typeName = kvp.Key;
                var namespace_ = kvp.Value;
                
                var manager = await CreateOrleansManager(namespace_);
                _actorManagersByType[typeName] = manager;
                
                Console.WriteLine($"      Type: {typeName} -> Namespace: {namespace_}");
            }
        }
        
        Console.WriteLine();
    }

    private async Task<IGAgentActorManager> CreateMassTransitManager()
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_clusterClient);
        services.AddSingleton<IGrainFactory>(_clusterClient);
        services.AddSingleton<IClusterClient>(_clusterClient);

        // Build config for MassTransit
        var configBuilder = new ConfigurationBuilder();
        var configDict = new Dictionary<string, string>
        {
            ["MessageStream:Provider"] = "MassTransit",
            ["MassTransit:Stream:TransportType"] = "Kafka",
            ["MassTransit:Stream:TopicPrefix"] = "agent-events", // Default fallback
            ["MassTransit:Stream:RuntimeName"] = "BenchmarkClient-" + Guid.NewGuid().ToString("N"),
            ["MassTransit:Stream:Kafka:BootstrapServers"] = "localhost:9092",
            ["MassTransit:Stream:Kafka:ConsumerGroupId"] = "benchmark-client-consumers"
        };

        // Add Topic Mappings
        foreach (var kvp in TypeNamespaces)
        {
            // Map Category (e.g., "TypeAAgent") to Topic (e.g., "AevatarAgents-TypeA")
            // Note: Category is the class name of the Agent
            configDict[$"MassTransit:Stream:TopicMapping:{kvp.Key}Agent"] = kvp.Value;
        }

        configBuilder.AddInMemoryCollection(configDict);
        var configuration = configBuilder.Build();
        services.AddSingleton<IConfiguration>(configuration);

        services.Configure<MessageStreamProviderOptions>(options =>
        {
            options.Provider = "MassTransit";
        });

        // Add MassTransit Plugin
        services.AddMassTransitStreamPlugin(configuration);

        // Use Orleans runtime
        services.AddAevatarAgentSystem(builder => builder.UseOrleansRuntime());

        var serviceProvider = services.BuildServiceProvider();
        var manager = serviceProvider.GetRequiredService<IGAgentActorManager>();
        
        var busControl = serviceProvider.GetRequiredService<IBusControl>();
        await busControl.StartAsync();
        
        await Task.Delay(100);
        return manager;
    }

    private async Task<IGAgentActorManager> CreateOrleansManager(string streamNamespace)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(_clusterClient);
        services.AddSingleton<IGrainFactory>(_clusterClient);
        services.AddSingleton<IClusterClient>(_clusterClient);

        // Orleans Stream configuration - must match Silo's StreamProviderName
        services.Configure<Aevatar.Agents.StreamingOptions>(options =>
        {
            options.StreamProviderName = "AevatarAgents";  // Match Silo's ProviderName
            options.DefaultStreamNamespace = streamNamespace;  // Type-specific namespace!
        });

        services.AddAevatarAgentSystem(builder => builder.UseOrleansRuntime());

        var serviceProvider = services.BuildServiceProvider();
        var manager = serviceProvider.GetRequiredService<IGAgentActorManager>();
        
        await Task.Delay(100);
        return manager;
    }
    
    // Legacy method signature support, but implementation logic moved to above methods
    private async Task<IGAgentActorManager> CreateActorManagerForNamespace(string streamNamespace)
    {
        return await CreateOrleansManager(streamNamespace);
    }
    
    /// <summary>
    /// Scenario 1: Two types SHARE ONE Topic (Single Topic Mode)
    /// </summary>
    public async Task<MultiTopicResults> RunSingleTopicTestAsync(int messagesPerType)
    {
        // For Scenario 1, we still want to simulate "Shared Topic".
        // In MassTransit, we can map both TypeA and TypeB to the same Shared Topic.
        
        _messagesPerType = messagesPerType;
        Console.WriteLine("\n🎯 Scenario 1: Single Topic (Shared) Test");
        Console.WriteLine("=" .PadRight(60, '='));
        
        var results = new MultiTopicResults();
        var sw = Stopwatch.StartNew();
        
        // Step 1: Create agents
        Console.WriteLine("📦 Step 1: Creating agents with SHARED namespace/topic...");
        
        IGAgentActorManager sharedManager;

        if (_providerName == "MassTransit")
        {
            // For MassTransit Shared Topic Test, we create a Manager where TypeA and TypeB map to SAME topic
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
            services.AddSingleton(_clusterClient);
            services.AddSingleton<IGrainFactory>(_clusterClient);
            services.AddSingleton<IClusterClient>(_clusterClient);

            var configBuilder = new ConfigurationBuilder();
            var configDict = new Dictionary<string, string>
            {
                ["MessageStream:Provider"] = "MassTransit",
                ["MassTransit:Stream:TransportType"] = "Kafka",
                ["MassTransit:Stream:TopicPrefix"] = "agent-events",
                ["MassTransit:Stream:RuntimeName"] = "BenchmarkClient-Shared-" + Guid.NewGuid().ToString("N"),
                ["MassTransit:Stream:Kafka:BootstrapServers"] = "localhost:9092",
                ["MassTransit:Stream:Kafka:ConsumerGroupId"] = "benchmark-client-shared-consumers",
                // MAP BOTH TO SAME TOPIC
                ["MassTransit:Stream:TopicMapping:TypeAAgent"] = "AevatarAgents-Shared",
                ["MassTransit:Stream:TopicMapping:TypeBAgent"] = "AevatarAgents-Shared",
                ["MassTransit:Stream:TopicMapping:TypedAgent"] = "AevatarAgents-Shared"
            };
            configBuilder.AddInMemoryCollection(configDict);
            var configuration = configBuilder.Build();
            services.AddSingleton<IConfiguration>(configuration);
            services.Configure<MessageStreamProviderOptions>(o => o.Provider = "MassTransit");
            services.AddMassTransitStreamPlugin(configuration);
            services.AddAevatarAgentSystem(b => b.UseOrleansRuntime());
            
            var sp = services.BuildServiceProvider();
            sharedManager = sp.GetRequiredService<IGAgentActorManager>();
            await sp.GetRequiredService<IBusControl>().StartAsync();
        }
        else
        {
            sharedManager = await CreateOrleansManager("AevatarAgents-Shared");
        }

        var typeNames = new[] { "TypeA", "TypeB" };
        
        for (int typeIndex = 0; typeIndex < 2; typeIndex++)
        {
            var typeName = typeNames[typeIndex];
            var typeAgents = new List<TypedAgentInfo>();
            
            Console.WriteLine($"   Creating {typeName} agents...");
            
            for (int i = 0; i < AGENTS_PER_TYPE; i++)
            {
                var agentId = Guid.NewGuid().ToString();
                IGAgentActor actor;

                // Use specific sub-types even for Shared Test to be consistent
                // Agent properties are set in OnActivateAsync, no need to set here
                if (typeName == "TypeA") actor = await sharedManager.CreateAndRegisterAsync<TypeAAgent>(agentId);
                else actor = await sharedManager.CreateAndRegisterAsync<TypeBAgent>(agentId);
                
                typeAgents.Add(new TypedAgentInfo { AgentId = agentId, Actor = actor, TypeName = typeName });
            }
            results.AgentsByType[typeName] = typeAgents;
            results.TotalAgentsCreated += typeAgents.Count;
        }
        Console.WriteLine($"   ✅ Created {results.TotalAgentsCreated} agents in {sw.ElapsedMilliseconds}ms\n");
        
        // Step 2: Create publishers
        sw.Restart();
        Console.WriteLine("📡 Step 2: Creating publisher agents...");
        
        var publisherA_Id = Guid.NewGuid().ToString();
        var publisherA = await sharedManager.CreateAndRegisterAsync<TypedAgent>(publisherA_Id); // Use base TypedAgent for publisher
        
        var publisherB_Id = Guid.NewGuid().ToString();
        var publisherB = await sharedManager.CreateAndRegisterAsync<TypedAgent>(publisherB_Id);
        
        // Step 3: Subscriptions
        sw.Restart();
        Console.WriteLine("🔗 Step 3: Setting up subscriptions...");
        var typeAAgents = results.AgentsByType["TypeA"];
        foreach (var agentInfo in typeAAgents) await ActorHierarchyCoordinator.LinkAsync(publisherA, agentInfo.Actor, _logger);
        var typeBAgents = results.AgentsByType["TypeB"];
        foreach (var agentInfo in typeBAgents) await ActorHierarchyCoordinator.LinkAsync(publisherB, agentInfo.Actor, _logger);
        results.SubscribedAgents = typeAAgents.Count + typeBAgents.Count;
        
        Console.WriteLine("⏳ Waiting 3s...");
        await Task.Delay(3000);
        
        // Step 4: Publish
        sw.Restart();
        Console.WriteLine($"📤 Step 4: Publishing...");
        
        var publishTaskA = Task.Run(async () =>
        {
            // Use Task.WhenAll for performance (MassTransit Optimization)
            if (_providerName == "MassTransit")
            {
                 var tasks = new List<Task>();
                 for(int i=1; i<=messagesPerType; i++) {
                     var msg = new BusinessMessageEvent { Message = $"Test message {i} for TypeA", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) };
                     tasks.Add(publisherA.PublishEventAsync(msg, EventDirection.Down));
                 }
                 await Task.WhenAll(tasks);
            }
            else
            {
                await PublishMessagesAsync(publisherA, results, "TypeA", messagesPerType);
            }
        });
        
        var publishTaskB = Task.Run(async () =>
        {
             if (_providerName == "MassTransit")
            {
                 var tasks = new List<Task>();
                 for(int i=1; i<=messagesPerType; i++) {
                     var msg = new BusinessMessageEvent { Message = $"Test message {i} for TypeB", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) };
                     tasks.Add(publisherB.PublishEventAsync(msg, EventDirection.Down));
                 }
                 await Task.WhenAll(tasks);
            }
            else
            {
                await PublishMessagesAsync(publisherB, results, "TypeB", messagesPerType);
            }
        });
        
        await Task.WhenAll(publishTaskA, publishTaskB);
        results.PublishDurationMs = (int)sw.ElapsedMilliseconds;
        
        // Step 5: Collect
        Console.WriteLine("⏳ Waiting 5s...");
        await Task.Delay(5000);
        await CollectStatisticsAsync(results);
        results.TotalDurationMs = (int)sw.ElapsedMilliseconds;
        
        return results;
    }

    private async Task CreateAgentsAsync(MultiTopicResults results)
    {
        await InitializeActorManagersAsync();
        
        var typeNames = new[] { "TypeA", "TypeB", "TypeC", "TypeD", "TypeE" };
        
        for (int typeIndex = 0; typeIndex < AGENT_TYPES; typeIndex++)
        {
            var typeName = typeNames[typeIndex];
            var typeAgents = new List<TypedAgentInfo>();
            var manager = _actorManagersByType[typeName];
            
            Console.WriteLine($"   Creating {typeName} agents...");
            
            for (int i = 0; i < AGENTS_PER_TYPE; i++)
            {
                var agentId = Guid.NewGuid().ToString();
                IGAgentActor actor = null!;
                
                // Use specific sub-types for dynamic routing
                // Agent properties are set in OnActivateAsync, no need to set here
                switch (typeName)
                {
                    case "TypeA": actor = await manager.CreateAndRegisterAsync<TypeAAgent>(agentId); break;
                    case "TypeB": actor = await manager.CreateAndRegisterAsync<TypeBAgent>(agentId); break;
                    case "TypeC": actor = await manager.CreateAndRegisterAsync<TypeCAgent>(agentId); break;
                    case "TypeD": actor = await manager.CreateAndRegisterAsync<TypeDAgent>(agentId); break;
                    case "TypeE": actor = await manager.CreateAndRegisterAsync<TypeEAgent>(agentId); break;
                }
                
                typeAgents.Add(new TypedAgentInfo { AgentId = agentId, Actor = actor, TypeName = typeName });
            }
            results.AgentsByType[typeName] = typeAgents;
            results.TotalAgentsCreated += typeAgents.Count;
        }
    }
    
    // ... (Keep existing helper methods like SetupSubscriptionsAsync, CollectStatisticsAsync, PrintResults, etc.)
    
    private async Task SetupSubscriptionsAsync(MultiTopicResults results, IGAgentActor publisher)
    {
        // Only Type A agents subscribe to publisher
        var typeAAgents = results.AgentsByType["TypeA"];
        
        foreach (var agentInfo in typeAAgents)
        {
            await ActorHierarchyCoordinator.LinkAsync(publisher, agentInfo.Actor, _logger);
        }
        
        results.SubscribedAgents = typeAAgents.Count;
    }
    
    private async Task PublishMessagesAsync(IGAgentActor publisher, MultiTopicResults results, string targetType, int messageCount)
    {
        for (int i = 1; i <= messageCount; i++)
        {
            var message = new BusinessMessageEvent
            {
                Message = $"Test message {i} for {targetType} agents",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            await publisher.PublishEventAsync(message, Aevatar.Agents.EventDirection.Down);
            
            if (i % 100 == 0 || i == messageCount)
            {
                Console.WriteLine($"      Published: {i}/{messageCount}");
            }
        }
        
        results.MessagesSent += messageCount;
    }
    
    /// <summary>
    /// Scenario 2: Two types send messages concurrently
    /// </summary>
    public async Task<MultiTopicResults> RunDualTypeTestAsync(int messagesPerType)
    {
        _messagesPerType = messagesPerType;
        Console.WriteLine("\n🎯 Scenario 2: Dual Type Test");
        Console.WriteLine("=" .PadRight(60, '='));
        
        var results = new MultiTopicResults();
        var sw = Stopwatch.StartNew();
        
        // Step 1: Create 500 agents
        Console.WriteLine("📦 Step 1: Creating agents...");
        await CreateAgentsAsync(results);
        Console.WriteLine($"   ✅ Created {results.TotalAgentsCreated} agents in {sw.ElapsedMilliseconds}ms\n");
        
        // Step 2: Create two publishers (for TypeA and TypeB)
        sw.Restart();
        Console.WriteLine("📡 Step 2: Creating publisher agents...");
        
        // Use specific TypeAAgent/TypeBAgent for publishers to ensure correct topic routing
        // MassTransit TopicMapping routes based on Agent class name
        var publisherA_Id = Guid.NewGuid().ToString();
        var publisherA_Manager = _actorManagersByType["TypeA"]; 
        var publisherA = await publisherA_Manager.CreateAndRegisterAsync<TypeAAgent>(publisherA_Id); 
        
        var publisherB_Id = Guid.NewGuid().ToString();
        var publisherB_Manager = _actorManagersByType["TypeB"];
        var publisherB = await publisherB_Manager.CreateAndRegisterAsync<TypeBAgent>(publisherB_Id);
        
        // Step 3: Subscribe Type A to PublisherA, Type B to PublisherB
        sw.Restart();
        Console.WriteLine("🔗 Step 3: Setting up subscriptions...");
        var typeAAgents = results.AgentsByType["TypeA"];
        foreach (var agentInfo in typeAAgents) await ActorHierarchyCoordinator.LinkAsync(publisherA, agentInfo.Actor, _logger);
        var typeBAgents = results.AgentsByType["TypeB"];
        foreach (var agentInfo in typeBAgents) await ActorHierarchyCoordinator.LinkAsync(publisherB, agentInfo.Actor, _logger);
        results.SubscribedAgents = typeAAgents.Count + typeBAgents.Count;
        
        Console.WriteLine("⏳ Waiting 3s...");
        await Task.Delay(3000);
        
        // Step 4: Publish
        sw.Restart();
        Console.WriteLine($"📤 Step 4: Publishing...");
        
        var sentCountA = 0;
        var sentCountB = 0;
        
        var publishTaskA = Task.Run(async () =>
        {
             if (_providerName == "MassTransit")
            {
                 var tasks = new List<Task>();
                 for(int i=1; i<=messagesPerType; i++) {
                     var msg = new BusinessMessageEvent { Message = $"Test message {i} for TypeA", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) };
                     tasks.Add(publisherA.PublishEventAsync(msg, EventDirection.Down));
                     sentCountA++;
                 }
                 await Task.WhenAll(tasks);
                 Console.WriteLine($"      Published TypeA: {sentCountA}/{messagesPerType}");
            }
            else
            {
                await PublishMessagesAsync(publisherA, results, "TypeA", messagesPerType);
            }
        });
        
        var publishTaskB = Task.Run(async () =>
        {
             if (_providerName == "MassTransit")
            {
                 var tasks = new List<Task>();
                 for(int i=1; i<=messagesPerType; i++) {
                     var msg = new BusinessMessageEvent { Message = $"Test message {i} for TypeB", Timestamp = Timestamp.FromDateTime(DateTime.UtcNow) };
                     tasks.Add(publisherB.PublishEventAsync(msg, EventDirection.Down));
                     sentCountB++;
                 }
                 await Task.WhenAll(tasks);
                 Console.WriteLine($"      Published TypeB: {sentCountB}/{messagesPerType}");
            }
            else
            {
                await PublishMessagesAsync(publisherB, results, "TypeB", messagesPerType);
            }
        });
        
        await Task.WhenAll(publishTaskA, publishTaskB);
        results.PublishDurationMs = (int)sw.ElapsedMilliseconds;
        
        // Update MessagesSent for MassTransit mode
        if (_providerName == "MassTransit")
        {
            results.MessagesSent = sentCountA + sentCountB;
        }
        
        Console.WriteLine("⏳ Waiting 5s...");
        await Task.Delay(5000);
        await CollectStatisticsAsync(results);
        results.TotalDurationMs = (int)sw.ElapsedMilliseconds;
        
        return results;
    }
    
    private async Task CollectStatisticsAsync(MultiTopicResults results)
    {
        foreach (var kvp in results.AgentsByType)
        {
            var typeName = kvp.Key;
            var agents = kvp.Value;
            
            var typeStats = new TypeStatistics
            {
                TypeName = typeName,
                TotalAgents = agents.Count
            };
            
            foreach (var agentInfo in agents)
            {
                // Use RPC call to get description, then parse ProcessedCount
                var description = await agentInfo.Actor.GetDescriptionAsync();
                var processedCount = ExtractProcessedCount(description);
                
                typeStats.TotalMessagesReceived += processedCount;
                
                if (processedCount > 0)
                {
                    typeStats.AgentsReceivedMessages++;
                    typeStats.MessageDistribution.Add(processedCount);
                }
            }
            
            // Calculate distribution statistics
            if (typeStats.MessageDistribution.Any())
            {
                typeStats.MinMessages = typeStats.MessageDistribution.Min();
                typeStats.MaxMessages = typeStats.MessageDistribution.Max();
                typeStats.AvgMessages = typeStats.MessageDistribution.Average();
                typeStats.MedianMessages = CalculateMedian(typeStats.MessageDistribution);
            }
            
            results.StatisticsByType[typeName] = typeStats;
        }
    }
    
    /// <summary>
    /// Extract ProcessedCount from Agent description string
    /// </summary>
    private static int ExtractProcessedCount(string description)
    {
        // Description format: "TypedAgent [TypeA] - Processed: 5"
        var match = System.Text.RegularExpressions.Regex.Match(description, @"Processed:\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }
    
    private double CalculateMedian(List<int> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        var count = sorted.Count;
        
        if (count == 0) return 0;
        if (count % 2 == 1)
            return sorted[count / 2];
        else
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
    }
    
    public static void PrintResults(MultiTopicResults results)
    {
        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine("📊 Test Results Summary");
        Console.WriteLine("=".PadRight(60, '='));
        Console.WriteLine();
        
        Console.WriteLine($"⏱️  Timing:");
        Console.WriteLine($"   • Publish Duration: {results.PublishDurationMs}ms");
        Console.WriteLine($"   • Total Duration: {results.TotalDurationMs}ms");
        Console.WriteLine();
        
        Console.WriteLine($"📤 Messages Sent: {results.MessagesSent}");
        Console.WriteLine($"🔗 Subscribed Agents: {results.SubscribedAgents} (Type A only)");
        Console.WriteLine();
        
        Console.WriteLine("📊 Message Distribution by Type:");
        Console.WriteLine();
        
        foreach (var typeName in new[] { "TypeA", "TypeB", "TypeC", "TypeD", "TypeE" })
        {
            if (!results.StatisticsByType.TryGetValue(typeName, out var stats))
                continue;
            
            var isTargetType = typeName == "TypeA";
            var emoji = isTargetType ? "🎯" : "🚫";
            
            Console.WriteLine($"{emoji} {typeName} ({stats.TotalAgents} agents):");
            Console.WriteLine($"   • Agents Received: {stats.AgentsReceivedMessages}/{stats.TotalAgents} " +
                            $"({stats.AgentsReceivedMessages * 100.0 / stats.TotalAgents:F1}%)");
            Console.WriteLine($"   • Total Messages: {stats.TotalMessagesReceived}");
            
            if (stats.AgentsReceivedMessages > 0)
            {
                Console.WriteLine($"   • Min/Max/Avg/Median: {stats.MinMessages}/{stats.MaxMessages}/" +
                                $"{stats.AvgMessages:F1}/{stats.MedianMessages:F1}");
                
                // Show distribution histogram for subscribed type
                if (isTargetType)
                {
                    PrintDistributionHistogram(stats.MessageDistribution);
                }
            }
            else
            {
                Console.WriteLine($"   • ✅ No messages received (as expected)");
            }
            
            Console.WriteLine();
        }
        
        // Analysis
        Console.WriteLine("🔍 Analysis:");
        Console.WriteLine();
        
        var typeAStats = results.StatisticsByType["TypeA"];
        var otherTypesReceivedMessages = results.StatisticsByType
            .Where(kvp => kvp.Key != "TypeA")
            .Sum(kvp => kvp.Value.TotalMessagesReceived);
        
        if (otherTypesReceivedMessages > 0)
        {
            Console.WriteLine($"⚠️  WARNING: Other types received {otherTypesReceivedMessages} messages!");
            Console.WriteLine($"   This indicates cross-topic leakage or incorrect filtering.");
        }
        else
        {
            Console.WriteLine($"✅ Topic Isolation: Verified (only Type A received messages)");
        }
        
        Console.WriteLine();
        
        if (typeAStats.AgentsReceivedMessages == typeAStats.TotalAgents)
        {
            Console.WriteLine($"✅ All Type A agents received messages");
        }
        else
        {
            Console.WriteLine($"⚠️  Only {typeAStats.AgentsReceivedMessages}/{typeAStats.TotalAgents} Type A agents received messages");
        }
        
        Console.WriteLine();
        
        // Distribution pattern
        var expectedPerAgent = results.MessagesSent;
        var actualAvg = typeAStats.AvgMessages;
        var distributionQuality = actualAvg / expectedPerAgent;
        
        Console.WriteLine($"📈 Distribution Pattern:");
        Console.WriteLine($"   • Expected per agent: {expectedPerAgent} messages");
        Console.WriteLine($"   • Actual average: {actualAvg:F1} messages");
        Console.WriteLine($"   • Distribution quality: {distributionQuality:P1}");
        
        if (distributionQuality > 0.95)
        {
            Console.WriteLine($"   ✅ Excellent distribution (near perfect)");
        }
        else if (distributionQuality > 0.80)
        {
            Console.WriteLine($"   ⚠️  Fair distribution (some imbalance)");
        }
        else
        {
            Console.WriteLine($"   ❌ Poor distribution (significant imbalance)");
        }
        
        Console.WriteLine();
        
        // Bottleneck analysis
        Console.WriteLine($"🔬 Bottleneck Analysis:");
        var variance = CalculateVariance(typeAStats.MessageDistribution);
        Console.WriteLine($"   • Message variance: {variance:F2}");
        
        if (variance < 1.0)
        {
            Console.WriteLine($"   • Pattern: Even distribution (Orleans Stream working well)");
        }
        else if (variance < 10.0)
        {
            Console.WriteLine($"   • Pattern: Slight variance (acceptable queue behavior)");
        }
        else
        {
            Console.WriteLine($"   • Pattern: High variance (possible queue/partition limitation)");
        }
        
        Console.WriteLine();
        Console.WriteLine("=".PadRight(60, '='));
    }
    
    private static void PrintDistributionHistogram(List<int> distribution)
    {
        if (distribution.Count == 0) return;
        
        var grouped = distribution.GroupBy(x => x)
                                   .OrderBy(g => g.Key)
                                   .ToDictionary(g => g.Key, g => g.Count());
        
        Console.WriteLine($"   • Distribution histogram:");
        foreach (var kvp in grouped.Take(10)) // Show top 10 buckets
        {
            var bar = new string('█', Math.Min(kvp.Value / 2, 40));
            Console.WriteLine($"      {kvp.Key} msgs: {bar} ({kvp.Value} agents)");
        }
        
        if (grouped.Count > 10)
        {
            Console.WriteLine($"      ... and {grouped.Count - 10} more buckets");
        }
    }
    
    private static double CalculateVariance(List<int> values)
    {
        if (values.Count == 0) return 0;
        
        var avg = values.Average();
        var sumSquares = values.Sum(x => Math.Pow(x - avg, 2));
        return sumSquares / values.Count;
    }
}

public class MultiTopicResults
{
    public Dictionary<string, List<TypedAgentInfo>> AgentsByType { get; } = new();
    public Dictionary<string, TypeStatistics> StatisticsByType { get; } = new();
    public int TotalAgentsCreated { get; set; }
    public int SubscribedAgents { get; set; }
    public int MessagesSent { get; set; }
    public int PublishDurationMs { get; set; }
    public int TotalDurationMs { get; set; }
}

public class TypedAgentInfo
{
    public string AgentId { get; set; } = "";
    public IGAgentActor Actor { get; set; } = null!;
    public string TypeName { get; set; } = "";
}

public class TypeStatistics
{
    public string TypeName { get; set; } = "";
    public int TotalAgents { get; set; }
    public int AgentsReceivedMessages { get; set; }
    public int TotalMessagesReceived { get; set; }
    public List<int> MessageDistribution { get; } = new();
    public int MinMessages { get; set; }
    public int MaxMessages { get; set; }
    public double AvgMessages { get; set; }
    public double MedianMessages { get; set; }
}
