using System.Collections.Concurrent;
using System.Diagnostics;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Performance tests for event handling, concurrency, and memory usage
/// </summary>
public class PerformanceTests(CoreTestFixture fixture, ITestOutputHelper output) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly ITestOutputHelper _output = output;
    
    #region Event Processing Performance Tests
    
    [Fact(DisplayName = "Should handle high volume of events")]
    public async Task Should_Handle_High_Volume_Of_Events()
    {
        // Arrange
        const int eventCount = 10000;
        var agent = new BasicTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        
        var stopwatch = Stopwatch.StartNew();
        
        // Act - Send many events
        var tasks = new List<Task>();
        for (int i = 0; i < eventCount; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = $"event-{i}",
                Payload = Any.Pack(new TestEvent 
                { 
                    EventId = $"perf-test-{i}",
                    EventType = "performance"
                }),
                PublisherId = "perf-tester"
            };
            
            tasks.Add(agent.HandleEventAsync(envelope));
            
            // Process in batches to avoid overwhelming the system
            if (tasks.Count >= 100)
            {
                await Task.WhenAll(tasks);
                tasks.Clear();
            }
        }
        
        // Wait for remaining tasks
        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
        
        stopwatch.Stop();
        
        // Assert
        agent.GetState().Counter.ShouldBe(eventCount);
        
        // Performance metrics
        var eventsPerSecond = eventCount / stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine($"Processed {eventCount} events in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Events per second: {eventsPerSecond:F2}");
        _output.WriteLine($"Average latency: {stopwatch.ElapsedMilliseconds / (double)eventCount:F2}ms");
        
        // Baseline performance expectation: Should process at least 1000 events per second
        eventsPerSecond.ShouldBeGreaterThan(1000);
    }
    
    [Fact(DisplayName = "Should handle concurrent events")]
    public async Task Should_Handle_Concurrent_Events()
    {
        // Arrange
        const int concurrentAgents = 10;
        const int eventsPerAgent = 100;
        var agents = new List<BasicTestAgent>();
        
        for (int i = 0; i < concurrentAgents; i++)
        {
            var agent = new BasicTestAgent();
            AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
            agents.Add(agent);
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        // Act - Send events to all agents concurrently
        var allTasks = new List<Task>();
        
        for (int agentIndex = 0; agentIndex < concurrentAgents; agentIndex++)
        {
            var agent = agents[agentIndex];
            var agentId = agentIndex;
            
            var agentTask = Task.Run(async () =>
            {
                for (int i = 0; i < eventsPerAgent; i++)
                {
                    var envelope = new EventEnvelope
                    {
                        Id = $"event-{agentId}-{i}",
                        Payload = Any.Pack(new TestEvent 
                        { 
                            EventId = $"concurrent-{agentId}-{i}"
                        }),
                        PublisherId = $"agent-{agentId}"
                    };
                    
                    await agent.HandleEventAsync(envelope);
                }
            });
            
            allTasks.Add(agentTask);
        }
        
        await Task.WhenAll(allTasks);
        stopwatch.Stop();
        
        // Assert - Each agent should have processed its events
        foreach (var agent in agents)
        {
            agent.GetState().Counter.ShouldBe(eventsPerAgent);
        }
        
        // Performance metrics
        var totalEvents = concurrentAgents * eventsPerAgent;
        var eventsPerSecond = totalEvents / stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine($"Processed {totalEvents} events across {concurrentAgents} agents in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Events per second: {eventsPerSecond:F2}");
        _output.WriteLine($"Average latency per agent: {stopwatch.ElapsedMilliseconds / (double)eventsPerAgent:F2}ms");
        
        // Should maintain good performance under concurrent load
        eventsPerSecond.ShouldBeGreaterThan(500);
    }
    
    #endregion
    
    #region Memory Usage Tests
    
    [Fact(DisplayName = "Should maintain reasonable memory usage")]
    public async Task Should_Maintain_Reasonable_Memory_Usage()
    {
        // Arrange
        var agent = new ComplexAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        
        // Get initial memory
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var initialMemory = GC.GetTotalMemory(false);
        
        // Act - Process events with complex state
        const int iterations = 1000;
        for (int i = 0; i < iterations; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = $"memory-test-{i}",
                Payload = Any.Pack(new ComplexTestEvent 
                { 
                    Id = $"complex-{i}",
                    Data = { [$"key-{i}"] = $"value-{i}" },
                    NestedEvents = 
                    { 
                        new ComplexTestEvent.Types.NestedEvent 
                        { 
                            NestedId = $"nested-{i}",
                            NestedValue = i 
                        } 
                    }
                }),
                PublisherId = "memory-tester"
            };
            
            await agent.HandleComplexEvent(envelope);
            
            // Periodically clean up
            if (i % 100 == 0)
            {
                GC.Collect(0, GCCollectionMode.Optimized);
            }
        }
        
        // Force garbage collection
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        
        var finalMemory = GC.GetTotalMemory(false);
        var memoryIncrease = finalMemory - initialMemory;
        var memoryIncreaseMB = memoryIncrease / (1024.0 * 1024.0);
        
        // Assert
        _output.WriteLine($"Initial memory: {initialMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Final memory: {finalMemory / 1024.0 / 1024.0:F2} MB");
        _output.WriteLine($"Memory increase: {memoryIncreaseMB:F2} MB");
        _output.WriteLine($"Average memory per event: {memoryIncrease / (double)iterations:F2} bytes");
        
        // Memory increase should be reasonable (less than 50MB for 1000 complex events)
        memoryIncreaseMB.ShouldBeLessThan(50);
    }
    
    #endregion
    
    #region Handler Priority Performance Tests
    
    [Fact(DisplayName = "Should maintain performance with multiple priority handlers")]
    public async Task Should_Maintain_Performance_With_Multiple_Priority_Handlers()
    {
        // Arrange
        var agent = new PriorityTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        
        const int eventCount = 1000;
        var stopwatch = Stopwatch.StartNew();
        
        // Act - Process events through multiple priority handlers
        var tasks = new Task[eventCount];
        for (int i = 0; i < eventCount; i++)
        {
            var envelope = new EventEnvelope
            {
                Id = $"priority-{i}",
                Payload = Any.Pack(new TestEvent { EventId = $"priority-test-{i}" }),
                PublisherId = "priority-tester"
            };
            
            tasks[i] = agent.HandleEventAsync(envelope);
        }
        
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert - All handlers should have executed in order
        agent.ExecutionOrder.Count.ShouldBe(eventCount * 4); // 4 handlers per event
        
        // Performance metrics
        var eventsPerSecond = eventCount / stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine($"Processed {eventCount} events through 4 priority handlers in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Events per second: {eventsPerSecond:F2}");
        _output.WriteLine($"Average time per handler chain: {stopwatch.ElapsedMilliseconds / (double)eventCount:F2}ms");
        
        // Should maintain good performance even with multiple handlers
        eventsPerSecond.ShouldBeGreaterThan(200);
    }
    
    #endregion
    
    #region State Persistence Performance Tests
    
    [Fact(DisplayName = "Should efficiently save and load state")]
    public async Task Should_Efficiently_Save_And_Load_State()
    {
        // Arrange
        var agent = new ComplexAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        
        // Build complex state
        await agent.ActivateAsync();
        var state = agent.GetState();
        
        // Add lots of nested data
        for (int i = 0; i < 100; i++)
        {
            state.NestedList.Add(new ComplexAgentState.Types.NestedState
            {
                SubId = $"sub-{i}",
                SubCounter = i,
                SubTimestamp = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            
            state.DataMap[$"key-{i}"] = $"value-{i}";
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        // Act - Save and load state multiple times
        const int iterations = 100;
        for (int i = 0; i < iterations; i++)
        {
            // Simulate state persistence (serialization/deserialization)
            var bytes = state.ToByteArray();
            var loadedState = ComplexAgentState.Parser.ParseFrom(bytes);
            
            // Verify loaded state
            loadedState.NestedList.Count.ShouldBe(100);
            loadedState.DataMap.Count.ShouldBe(100);
        }
        
        stopwatch.Stop();
        
        // Assert and metrics
        var averageTimeMs = stopwatch.ElapsedMilliseconds / (double)iterations;
        _output.WriteLine($"Serialized and deserialized complex state {iterations} times in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Average time per operation: {averageTimeMs:F2}ms");
        _output.WriteLine($"State size: {state.ToByteArray().Length} bytes");
        
        // Should be fast even with complex state
        averageTimeMs.ShouldBeLessThan(10);
    }
    
    #endregion
    
    #region Event Routing Performance Tests
    
    [Fact(DisplayName = "Should efficiently route events by direction")]
    public async Task Should_Efficiently_Route_Events_By_Direction()
    {
        // Arrange
        var upCounter = 0;
        var downCounter = 0;
        var bothCounter = 0;
        
        var agent = new PublishingTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        
        // Set up mock publisher to count events
        var mockPublisher = new MockEventPublisher(
            onPublish: (evt, direction) =>
            {
                switch (direction)
                {
                    case EventDirection.Up:
                        Interlocked.Increment(ref upCounter);
                        break;
                    case EventDirection.Down:
                        Interlocked.Increment(ref downCounter);
                        break;
                    case EventDirection.Both:
                        Interlocked.Increment(ref bothCounter);
                        break;
                }
                return Task.CompletedTask;
            });
        
        AgentEventPublisherInjector.InjectEventPublisher(agent, mockPublisher);
        
        const int eventsPerDirection = 333;
        var stopwatch = Stopwatch.StartNew();
        
        // Act - Publish events in different directions
        var tasks = new List<Task>();
        
        for (int i = 0; i < eventsPerDirection; i++)
        {
            tasks.Add(agent.PublishTestEventUpAsync(new TestEvent { EventId = $"up-{i}" }));
            tasks.Add(agent.PublishTestEventDownAsync(new TestEvent { EventId = $"down-{i}" }));
            tasks.Add(agent.PublishTestEventBothAsync(new TestEvent { EventId = $"both-{i}" }));
        }
        
        await Task.WhenAll(tasks);
        stopwatch.Stop();
        
        // Assert
        upCounter.ShouldBe(eventsPerDirection);
        downCounter.ShouldBe(eventsPerDirection);
        bothCounter.ShouldBe(eventsPerDirection);
        
        // Performance metrics
        var totalEvents = eventsPerDirection * 3;
        var eventsPerSecond = totalEvents / stopwatch.Elapsed.TotalSeconds;
        _output.WriteLine($"Routed {totalEvents} events in {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"Events per second: {eventsPerSecond:F2}");
        _output.WriteLine($"UP: {upCounter}, DOWN: {downCounter}, BOTH: {bothCounter}");
        
        // Should route events efficiently
        eventsPerSecond.ShouldBeGreaterThan(1000);
    }
    
    #endregion
    
    /// <summary>
    /// Mock event publisher for performance testing
    /// </summary>
    private class MockEventPublisher : IEventPublisher
    {
        private readonly Func<IMessage, EventDirection, Task> _onPublish;
        
        public MockEventPublisher(Func<IMessage, EventDirection, Task> onPublish)
        {
            _onPublish = onPublish;
        }
        
        public async Task<string> PublishEventAsync<TEvent>(
            TEvent evt,
            EventDirection direction = EventDirection.Down,
            CancellationToken ct = default,
            bool isInternalCall = false)
            where TEvent : IMessage
        {
            await _onPublish(evt, direction);
            return Guid.NewGuid().ToString();
        }

        public async Task<string> SendToAsync<TEvent>(
            Guid targetAgentId,
            TEvent evt,
            EventDirection onArrivalDirection = EventDirection.Unspecified,
            CancellationToken ct = default,
            bool isInternalCall = false)
            where TEvent : IMessage
        {
            await _onPublish(evt, onArrivalDirection);
            return Guid.NewGuid().ToString();
        }
    }
}
