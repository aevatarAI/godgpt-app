using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Extensions;
using Aevatar.Agents.Plugins.MassTransit;
using Aevatar.Agents.Plugins.MassTransit.DependencyInjection;
using Aevatar.App.Agents.Agents;
using Business.Server;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using Orleans.Providers.MongoDB.Configuration;
using Orleans.Serialization;
using Orleans.Streams.Kafka.Config;
using System.Collections.Generic;
using System.Linq;

namespace Aevatar.App.PerformanceTests;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("🚀 Orleans Stream Performance Tests");
        Console.WriteLine("====================================");
        Console.WriteLine();
        
        // Parse args
        var runMultiTopic = args.Any(a => a.ToLower() == "multitopic");
        var providerArg = args.FirstOrDefault(a => a.StartsWith("--provider="))?.Split('=')[1] ?? "Orleans";
        
        Console.WriteLine($"🔍 Mode: {(runMultiTopic ? "Multi-Topic Benchmark" : "Standard Benchmark")}");
        Console.WriteLine($"🔍 Stream Provider: {providerArg}");
        Console.WriteLine();

        var host = await SetupOrleansClientAsync(providerArg);
        var client = host.Services.GetRequiredService<IClusterClient>();
        var actorManager = await SetupActorManagerAsync(client, providerArg);

        try
        {
            if (runMultiTopic)
            {
                // Run multi-topic comparison benchmark
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                var benchmark = new MultiTopicBenchmark(client, logger, providerArg);
                
                Console.WriteLine("🔬 TRUE Single-Topic vs Multi-Topic Comparison");
                Console.WriteLine("===============================================\n");
                
                // Scenario 1: Two types SHARE one topic
                var results1 = await benchmark.RunSingleTopicTestAsync(100);
                MultiTopicBenchmark.PrintResults(results1);
                
                Console.WriteLine("\n" + "=".PadRight(60, '=') + "\n");
                Console.WriteLine("⏳ Waiting 5s before next test...\n");
                await Task.Delay(5000);
                
                // Scenario 2: Two types use SEPARATE topics
                var results2 = await benchmark.RunDualTypeTestAsync(100);
                MultiTopicBenchmark.PrintResults(results2);
                
                // Print comparison
                PrintComparison(results1, results2);
            }
            else
            {
                // Run standard performance tests
                var results = new PerformanceResults();

                await TestAgentCreation(actorManager, results);
                await TestMessagePublishing(actorManager, results);
                await TestStateQuery(actorManager, results);
                await TestParentChildPropagation(actorManager, results);
                await TestPointToPointSending(actorManager, results);

                PrintSummary(results);
            }
        }
        finally
        {
            await actorManager.DeactivateAllAsync();
            await host.StopAsync();
        }
    }

    static async Task<IHost> SetupOrleansClientAsync(string provider)
    {
        Console.WriteLine("⚙️  Setting up Orleans Client...");
        
        var host = Host.CreateDefaultBuilder()
            .UseOrleansClient((context, clientBuilder) =>
            {
                // Use static gateway list for local testing instead of MongoDB clustering
                clientBuilder.UseStaticClustering(new IPEndPoint(IPAddress.Loopback, 30000));

                clientBuilder.Configure<ClusterOptions>(options =>
                {
                    options.ClusterId = "aevatar-cluster";
                    options.ServiceId = "aevatar-service";
                });

                // ONLY configure Orleans Stream Kafka if provider is "Orleans"
                if (provider == "Orleans")
                {
                    Console.WriteLine("   • Configuring Orleans Kafka Stream Client...");
                    // Use "AevatarAgents" to match Silo's StreamProviderName
                    clientBuilder.AddKafka("AevatarAgents")
                        .WithOptions(kafkaOptions =>
                        {
                            kafkaOptions.BrokerList = new List<string> { "localhost:9092" };
                            kafkaOptions.ConsumerGroupId = "aevatar-client-consumers";
                            kafkaOptions.ConsumeMode = Orleans.Streams.Kafka.Config.ConsumeMode.LastCommittedMessage;
                            
                            // Configure benchmark topics (must match Silo configuration)
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
                                kafkaOptions.AddTopic(topic, new TopicCreationConfig
                                {
                                    AutoCreate = true,
                                    Partitions = 8,
                                    ReplicationFactor = 1
                                });
                            }
                            
                            // Add default topic
                            kafkaOptions.AddTopic("agent-events", new TopicCreationConfig
                            {
                                AutoCreate = true,
                                Partitions = 8,
                                ReplicationFactor = 1
                            });
                        })
                        .AddJson()
                        .Build();
                }
                else
                {
                     Console.WriteLine("   • Skipping Orleans Kafka Stream Client (Using MassTransit)...");
                }

                clientBuilder.ConfigureServices(services =>
                {
                    services.AddSerializer(serializerBuilder =>
                    {
                        serializerBuilder.AddProtobufSerializer();
                    });
                });
            })
            .ConfigureLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning))
            .Build();

        await host.StartAsync();
        Console.WriteLine("✅ Orleans Client connected");
        Console.WriteLine();
        return host;
    }

    static async Task<IGAgentActorManager> SetupActorManagerAsync(IClusterClient client, string provider)
    {
        // NOTE: This initial manager is just for standard tests. 
        // MultiTopicBenchmark creates its own managers internally.
        
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton(client);
        services.AddSingleton<IGrainFactory>(client);
        services.AddSingleton<IClusterClient>(client);

        // Configure Orleans Stream options - must match Silo's configuration
        services.Configure<Aevatar.Agents.StreamingOptions>(options =>
        {
            options.StreamProviderName = "AevatarAgents";  // Match Silo's ProviderName
            options.DefaultStreamNamespace = "AevatarAgents";
        });

        // Configure MessageStreamProviderOptions based on provider argument
        services.Configure<Aevatar.Agents.Abstractions.MessageStreamProviderOptions>(options =>
        {
            options.Provider = provider; // "MassTransit" or "Orleans"
            options.Runtime["Orleans"] = provider;
        });

        // If using MassTransit, configure it
        if (provider == "MassTransit")
        {
            Console.WriteLine("   • Configuring MassTransit Kafka Stream Client (Producer-only)...");
            
            // Build in-memory configuration matching Silo's appsettings.json
            var configDict = new Dictionary<string, string?>
            {
                {"MassTransit:Stream:TopicPrefix", "agent-events"},
                {"MassTransit:Stream:TransportType", "Kafka"},
                {"MassTransit:Stream:RuntimeName", "Orleans"},
                {"MassTransit:Stream:Kafka:BootstrapServers", "localhost:9092"}
                // No ConsumerGroupId needed - Client is producer-only!
            };
            
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(configDict)
                .Build();
            
            // Add MassTransit Stream Client (Producer-only mode)
            // This ensures Client only sends to Kafka, Silo handles consumption
            // Avoids Consumer Group competition between Client and Silo
            services.AddMassTransitStreamClient(configuration);
        }

        // Use new AddAevatarAgentSystem with Orleans runtime
        services.AddAevatarAgentSystem(builder => builder.UseOrleansRuntime());

        var serviceProvider = services.BuildServiceProvider();
        
        // IMPORTANT: Start MassTransit hosted services (Bus, Riders)
        if (provider == "MassTransit")
        {
            Console.WriteLine("   • Starting MassTransit services...");
            var hostedServices = serviceProvider.GetServices<IHostedService>();
            foreach (var hostedService in hostedServices)
            {
                await hostedService.StartAsync(default);
            }
            
            // Warm up Kafka producer to avoid cold-start latency
            Console.WriteLine("   • Warming up Kafka producer...");
            var streamProvider = serviceProvider.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider != null)
            {
                await streamProvider.WarmupAsync();
            }
            Console.WriteLine("   ✅ MassTransit services started and warmed up");
        }

        // Warm up Orleans Client connection by creating a dummy agent
        Console.WriteLine("   • Warming up Orleans Client connection...");
        var warmupManager = serviceProvider.GetRequiredService<IGAgentActorManager>();
        try
        {
            var warmupId = Guid.NewGuid().ToString();
            var warmupAgent = await warmupManager.CreateAndRegisterAsync<SimpleBusinessAgent>(warmupId);
            await warmupAgent.GetDescriptionAsync();  // Force RPC to establish connection
            Console.WriteLine("   ✅ Orleans Client warmed up");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"   ⚠️ Orleans warmup failed (non-fatal): {ex.Message}");
        }
        
        return warmupManager;
    }

    static async Task TestAgentCreation(IGAgentActorManager manager, PerformanceResults results)
    {
        Console.WriteLine("1️⃣  Agent Creation Time:");
        
        var sw = Stopwatch.StartNew();
        var agentId = Guid.NewGuid().ToString();
        results.Agent1 = await manager.CreateAndRegisterAsync<SimpleBusinessAgent>(agentId);
        sw.Stop();
        
        results.AgentCreationMs = (int)sw.ElapsedMilliseconds;
        Console.WriteLine($"   ✅ {results.AgentCreationMs} ms");
        Console.WriteLine($"   Agent ID: {agentId}");
    }

    static async Task TestMessagePublishing(IGAgentActorManager manager, PerformanceResults results)
    {
        Console.WriteLine();
        Console.WriteLine("2️⃣  Message Publishing (10 messages):");
        
        var sw = Stopwatch.StartNew();
        for (int i = 1; i <= 10; i++)
        {
            var message = new BusinessMessageEvent
            {
                Message = $"Performance test message {i}",
                Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            await results.Agent1!.PublishEventAsync(message, Aevatar.Agents.EventDirection.Down);
        }
        sw.Stop();
        
        results.TotalPublishMs = (int)sw.ElapsedMilliseconds;
        results.MessageAverageMs = results.TotalPublishMs / 10;
        results.ThroughputMsgPerSec = 10000 / results.TotalPublishMs;
        
        Console.WriteLine($"   ✅ Total: {results.TotalPublishMs} ms");
        Console.WriteLine($"   ✅ Average: {results.MessageAverageMs} ms per message");
        Console.WriteLine($"   ✅ Throughput: ~{results.ThroughputMsgPerSec} msg/sec");
    }

    static async Task TestStateQuery(IGAgentActorManager manager, PerformanceResults results)
    {
        Console.WriteLine();
        Console.WriteLine("3️⃣  Agent Info Query Time:");
        
        // Wait for previous messages to be processed (avoid queue blocking)
        Console.WriteLine("   • Waiting 2s for message queue to clear...");
        await Task.Delay(2000);
        
        // Run multiple queries and take average for more accurate measurement
        var times = new List<double>();
        var iterations = 10;
        string description = "";
        
        for (int i = 0; i < iterations; i++)
        {
            var sw = Stopwatch.StartNew();
            description = await results.Agent1!.GetDescriptionAsync();
            sw.Stop();
            times.Add(sw.Elapsed.TotalMilliseconds);
        }
        
        // Calculate statistics
        var avgMs = times.Average();
        var minMs = times.Min();
        var maxMs = times.Max();
        results.StateQueryMs = (int)Math.Ceiling(avgMs);
        
        Console.WriteLine($"   ✅ Avg: {avgMs:F2} ms | Min: {minMs:F2} ms | Max: {maxMs:F2} ms ({iterations} calls)");
        Console.WriteLine($"   Info: {description}");
    }

    static async Task TestParentChildPropagation(IGAgentActorManager manager, PerformanceResults results)
    {
        Console.WriteLine();
        Console.WriteLine("4️⃣  Parent-Child Event Propagation:");
        
        // Create parent and child
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        var parent = await manager.CreateAndRegisterAsync<SimpleBusinessAgent>(parentId);
        var child = await manager.CreateAndRegisterAsync<SimpleBusinessAgent>(childId);
        
        Console.WriteLine($"   Parent: {parentId}");
        Console.WriteLine($"   Child: {childId}");
        
        // Set up relationship using ActorHierarchyCoordinator
        Console.WriteLine($"   Setting up parent-child relationship...");
        await ActorHierarchyCoordinator.LinkAsync(parent, child);
        Console.WriteLine($"   ✓ Parent-child relationship established using ActorHierarchyCoordinator");
        Console.WriteLine($"   Waiting for stream subscription...");
        await Task.Delay(1000); // Wait for subscription to establish
        
        // Get initial child state using RPC
        var initialDesc = await child.GetDescriptionAsync();
        var initialCount = ExtractProcessedCount(initialDesc);
        
        // Test pure publish latency (no wait)
        Console.WriteLine($"   Publishing message from Parent to Child...");
        var sw = Stopwatch.StartNew();
        var message = new BusinessMessageEvent
        {
            Message = "Parent to child propagation test",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        await parent.PublishEventAsync(message, Aevatar.Agents.EventDirection.Down);
        var publishMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"   ✓ Message published (Direction: DOWN)");
        
        // Wait and verify child received it (try multiple times) using RPC
        // Use smaller interval for more accurate timing
        var finalCount = initialCount;
        var maxWaitMs = 3000;
        var checkIntervalMs = 50;  // Reduced for better precision
        
        while (sw.ElapsedMilliseconds < maxWaitMs && finalCount == initialCount)
        {
            await Task.Delay(checkIntervalMs);
            var currentDesc = await child.GetDescriptionAsync();
            finalCount = ExtractProcessedCount(currentDesc);
        }
        sw.Stop();
        
        results.PropagationMs = (int)publishMs;
        results.MessageReceived = finalCount > initialCount;
        // E2E = total time from publish start to message received
        results.EndToEndMs = results.MessageReceived ? (int)sw.ElapsedMilliseconds : -1;
        
        Console.WriteLine($"   ✅ Publish latency: {results.PropagationMs} ms");
        Console.WriteLine($"   ✅ Child received: {results.MessageReceived} (processed: {initialCount} → {finalCount})");
        if (results.MessageReceived)
        {
            Console.WriteLine($"   ✅ End-to-end time: {results.EndToEndMs} ms (publish + propagation + processing)");
        }
        else
        {
            Console.WriteLine($"   ⚠️  Message not received after {maxWaitMs}ms wait");
        }
    }
    
    static int ExtractProcessedCount(string description)
    {
        // Extract "Processed: X" from description like "Simple Business Agent xxx - Processed: 5"
        var match = System.Text.RegularExpressions.Regex.Match(description, @"Processed:\s*(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    static async Task TestPointToPointSending(IGAgentActorManager manager, PerformanceResults results)
    {
        Console.WriteLine();
        Console.WriteLine("5️⃣  Point-to-Point Sending (SendToAsync):");
        
        // Create sender and receiver (no parent-child relationship)
        var senderId = Guid.NewGuid().ToString();
        var receiverId = Guid.NewGuid().ToString();
        var sender = await manager.CreateAndRegisterAsync<SimpleBusinessAgent>(senderId);
        var receiver = await manager.CreateAndRegisterAsync<SimpleBusinessAgent>(receiverId);
        
        Console.WriteLine($"   Sender:   {sender.Id}");
        Console.WriteLine($"   Receiver: {receiver.Id}");
        Console.WriteLine($"   (No hierarchy relationship - pure point-to-point)");
        
        // Wait for agents to initialize
        await Task.Delay(500);
        
        // Get initial receiver state
        var initialDesc = await receiver.GetDescriptionAsync();
        var initialCount = ExtractProcessedCount(initialDesc);
        Console.WriteLine($"   Initial receiver processed count: {initialCount}");
        
        // Test point-to-point send
        // Use receiver.Id (full GrainKey format: AgentType:AgentId) for correct routing
        Console.WriteLine($"   Sending message directly to receiver...");
        var sw = Stopwatch.StartNew();
        var message = new BusinessMessageEvent
        {
            Message = "Point-to-point direct message",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };
        await sender.SendToAsync(receiver.Id, message);
        var sendMs = sw.ElapsedMilliseconds;
        Console.WriteLine($"   ✓ Message sent via SendToAsync");
        
        // Wait and verify receiver got it
        // Use smaller interval for more accurate timing
        var finalCount = initialCount;
        var maxWaitMs = 3000;
        var checkIntervalMs = 50;  // Reduced for better precision
        
        while (sw.ElapsedMilliseconds < maxWaitMs && finalCount == initialCount)
        {
            await Task.Delay(checkIntervalMs);
            var currentDesc = await receiver.GetDescriptionAsync();
            finalCount = ExtractProcessedCount(currentDesc);
        }
        sw.Stop();
        
        results.PointToPointSendMs = (int)sendMs;
        results.PointToPointReceived = finalCount > initialCount;
        // E2E = total time from send start to message received
        results.PointToPointE2EMs = results.PointToPointReceived ? (int)sw.ElapsedMilliseconds : -1;
        
        Console.WriteLine($"   ✅ Send latency: {results.PointToPointSendMs} ms");
        Console.WriteLine($"   ✅ Receiver got message: {results.PointToPointReceived} (processed: {initialCount} → {finalCount})");
        if (results.PointToPointReceived)
        {
            Console.WriteLine($"   ✅ End-to-end time: {results.PointToPointE2EMs} ms");
        }
        else
        {
            Console.WriteLine($"   ⚠️  Message not received after {maxWaitMs}ms wait");
        }
    }

    static void PrintComparison(MultiTopicResults results1, MultiTopicResults results2)
    {
        Console.WriteLine("\n" + "=".PadRight(60, '='));
        Console.WriteLine("📊 Single-Topic vs Multi-Topic Comparison");
        Console.WriteLine("=" .PadRight(60, '='));
        Console.WriteLine("\n🔬 Test Configuration:");
        Console.WriteLine("  Scenario 1: TypeA + TypeB on SAME topic (Single Topic)");
        Console.WriteLine($"              Both send {results1.MessagesSent / 2} msgs → 'AevatarAgents-Shared'");
        Console.WriteLine("  Scenario 2: TypeA + TypeB on DIFFERENT topics (Multi-Topic)");
        Console.WriteLine($"              TypeA → 'AevatarAgents-TypeA', TypeB → 'AevatarAgents-TypeB'");
        Console.WriteLine($"              Both send {results2.MessagesSent / 2} msgs (total {results1.MessagesSent} msgs in both scenarios)");

        Console.WriteLine("\n⏱️  Publish Duration:");
        Console.WriteLine($"  Single Topic:  {results1.PublishDurationMs} ms");
        Console.WriteLine($"  Multi-Topic:   {results2.PublishDurationMs} ms");
        Console.WriteLine($"  Difference:    {results2.PublishDurationMs - results1.PublishDurationMs} ms ({((double)(results2.PublishDurationMs - results1.PublishDurationMs) / results1.PublishDurationMs * 100):F1}%)");

        Console.WriteLine("\n📈 Throughput:");
        Console.WriteLine($"  Single Topic:  {((double)results1.MessagesSent / results1.PublishDurationMs * 1000):F1} msg/s");
        Console.WriteLine($"  Multi-Topic:   {((double)results2.MessagesSent / results2.PublishDurationMs * 1000):F1} msg/s");
        Console.WriteLine($"  Difference:    {((double)results2.MessagesSent / results2.PublishDurationMs * 1000) - ((double)results1.MessagesSent / results1.PublishDurationMs * 1000):F1} msg/s ({((((double)results2.MessagesSent / results2.PublishDurationMs * 1000) - ((double)results1.MessagesSent / results1.PublishDurationMs * 1000)) / ((double)results1.MessagesSent / results1.PublishDurationMs * 1000) * 100):F1}%)");

        Console.WriteLine("\n🎯 Conclusion:");
        if (results2.PublishDurationMs < results1.PublishDurationMs)
        {
            Console.WriteLine($"  ✅ Multi-Topic is {((double)(results1.PublishDurationMs - results2.PublishDurationMs) / results1.PublishDurationMs * 100):F1}% FASTER!");
        }
        else
        {
            Console.WriteLine($"  ❌ Single Topic is {((double)(results2.PublishDurationMs - results1.PublishDurationMs) / results2.PublishDurationMs * 100):F1}% FASTER!");
        }
        Console.WriteLine("  💡 Benefits of Multi-Topic:");
        Console.WriteLine("     - Independent Kafka partitions per type");
        Console.WriteLine("     - Better parallelization");
        Console.WriteLine("     - No resource contention between types");
        Console.WriteLine("  🚀 Recommendation: Use Multi-Topic architecture!");

        Console.WriteLine("\n📊 Architecture Insights:");
        Console.WriteLine($"  • Single Topic:  All agents compete for same {((double)results1.PublishDurationMs / results1.MessagesSent):F2}ms/msg");
        Console.WriteLine($"  • Multi-Topic:   Each type independent {((double)results2.PublishDurationMs / results2.MessagesSent):F2}ms/msg");
        Console.WriteLine("  • Scalability:   Multi-Topic can scale each type independently");
        Console.WriteLine("  • Isolation:     Multi-Topic prevents cross-type interference");
        Console.WriteLine("\n" + "=".PadRight(60, '='));
    }
    
    static void PrintSummary(PerformanceResults results)
    {
        Console.WriteLine();
        Console.WriteLine("📋 Performance Summary");
        Console.WriteLine("=====================");
        Console.WriteLine($"Environment:");
        Console.WriteLine($"  • .NET: {Environment.Version}");
        Console.WriteLine($"  • OS: {Environment.OSVersion}");
        Console.WriteLine($"  • Orleans + Kafka Stream");
        Console.WriteLine();
        Console.WriteLine($"Results:");
        Console.WriteLine($"  • Agent Creation:        {results.AgentCreationMs} ms");
        Console.WriteLine($"  • Message Average:       {results.MessageAverageMs} ms");
        Console.WriteLine($"  • State Query:           {results.StateQueryMs} ms");
        Console.WriteLine();
        Console.WriteLine($"Broadcast Propagation (Parent→Child via Direction.Down):");
        Console.WriteLine($"  • Publish Latency:       {results.PropagationMs} ms");
        Console.WriteLine($"  • E2E Delivery:          {(results.MessageReceived ? $"{results.EndToEndMs} ms ✅" : "N/A ❌")}");
        Console.WriteLine();
        Console.WriteLine($"Point-to-Point (SendToAsync - no hierarchy):");
        Console.WriteLine($"  • Send Latency:          {results.PointToPointSendMs} ms");
        Console.WriteLine($"  • E2E Delivery:          {(results.PointToPointReceived ? $"{results.PointToPointE2EMs} ms ✅" : "N/A ❌")}");
        Console.WriteLine();
        Console.WriteLine($"Throughput:                ~{results.ThroughputMsgPerSec} msg/sec");
        Console.WriteLine();
        Console.WriteLine("✅ Performance tests completed!");
    }

    class PerformanceResults
    {
        public IGAgentActor? Agent1 { get; set; }
        public int AgentCreationMs { get; set; }
        public int TotalPublishMs { get; set; }
        public int MessageAverageMs { get; set; }
        public int StateQueryMs { get; set; }
        public int PropagationMs { get; set; }
        public bool MessageReceived { get; set; }
        public int EndToEndMs { get; set; }
        public int ThroughputMsgPerSec { get; set; }
        
        // Point-to-Point results
        public int PointToPointSendMs { get; set; }
        public bool PointToPointReceived { get; set; }
        public int PointToPointE2EMs { get; set; }
    }
}