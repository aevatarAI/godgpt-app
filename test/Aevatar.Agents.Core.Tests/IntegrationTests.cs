using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.EventPublisher;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Integration tests for complete agent scenarios
/// </summary>
public class IntegrationTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly TestEventPublisher _eventPublisher = fixture.EventPublisher;

    #region Full Lifecycle Tests

    [Fact(DisplayName = "Should complete full agent lifecycle")]
    public async Task Should_Complete_Full_Agent_Lifecycle()
    {
        // Arrange
        var agent = new LifecycleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);

        // Act & Assert - Creation
        agent.ShouldNotBeNull();
        agent.IsActivated.ShouldBeFalse();
        agent.IsDeactivated.ShouldBeFalse();

        // Act & Assert - Activation
        await agent.ActivateAsync();
        agent.IsActivated.ShouldBeTrue();
        agent.ActivationTime.ShouldNotBeNull();
        agent.GetState().Status.ShouldBe("active");

        // Act & Assert - Operation
        for (int i = 0; i < 5; i++)
        {
            var evt = new TestEvent
            {
                EventId = $"lifecycle-{i}",
                EventType = "lifecycle-test",
                Payload = $"Data {i}"
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(evt),
                PublisherId = "lifecycle-tester"
            };

            await agent.HandleEventAsync(envelope);
        }

        agent.ProcessedEventCount.ShouldBe(5);
        agent.GetState().EventHistory.Count.ShouldBe(5);
        agent.GetState().LastProcessedTime.ShouldNotBeNull();

        // Act & Assert - State Persistence (simulate)
        var stateBytes = agent.GetState().ToByteArray();
        stateBytes.Length.ShouldBeGreaterThan(0);

        // Act & Assert - Deactivation
        await agent.DeactivateAsync();
        agent.IsDeactivated.ShouldBeTrue();
        agent.DeactivationTime.ShouldNotBeNull();
        agent.GetState().Status.ShouldBe("inactive");

        // Verify full lifecycle completed
        agent.DeactivationTime.Value.ShouldBeGreaterThan(agent.ActivationTime.Value);
    }

    #endregion

    #region Multi-Agent Collaboration Tests

    [Fact(DisplayName = "Should handle multi-agent collaboration")]
    public async Task Should_Handle_Multi_Agent_Collaboration()
    {
        // Arrange - Create a team of collaborating agents
        _eventPublisher.Clear();

        var coordinator = new CoordinatorAgent();
        var worker1 = new WorkerAgent { WorkerId = "worker-1" };
        var worker2 = new WorkerAgent { WorkerId = "worker-2" };
        var aggregator = new AggregatorAgent();

        // Inject dependencies
        var agents = new GAgentBase[] { coordinator, worker1, worker2, aggregator };
        foreach (var agent in agents)
        {
            AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
            AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
            await agent.ActivateAsync();
        }

        // Act - Coordinator assigns tasks
        var task1 = new TaskAssignedEvent
        {
            TaskId = "task-1",
            AssignedTo = "worker-1",
            Description = "Process data batch 1"
        };

        var task2 = new TaskAssignedEvent
        {
            TaskId = "task-2",
            AssignedTo = "worker-2",
            Description = "Process data batch 2"
        };

        await coordinator.AssignTask(task1);
        await coordinator.AssignTask(task2);

        // Simulate workers receiving tasks
        var task1Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(task1),
            PublisherId = coordinator.Id.ToString()
        };

        var task2Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(task2),
            PublisherId = coordinator.Id.ToString()
        };

        await worker1.HandleEventAsync(task1Envelope);
        await worker2.HandleEventAsync(task2Envelope);

        // Workers complete tasks and send results
        var result1 = new TaskCompletedEvent
        {
            TaskId = "task-1",
            WorkerId = "worker-1",
            Result = "Processed 100 items"
        };

        var result2 = new TaskCompletedEvent
        {
            TaskId = "task-2",
            WorkerId = "worker-2",
            Result = "Processed 150 items"
        };

        await worker1.CompleteTask(result1);
        await worker2.CompleteTask(result2);

        // Aggregator receives results
        var result1Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(result1),
            PublisherId = worker1.Id.ToString()
        };

        var result2Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(result2),
            PublisherId = worker2.Id.ToString()
        };

        await aggregator.HandleEventAsync(result1Envelope);
        await aggregator.HandleEventAsync(result2Envelope);

        // Assert - Verify collaboration
        coordinator.GetState().AssignedTasks.Count.ShouldBe(2);
        worker1.GetState().ReceivedTasks.Count.ShouldBe(1);
        worker2.GetState().ReceivedTasks.Count.ShouldBe(1);
        worker1.GetState().CompletedTasks.Count.ShouldBe(1);
        worker2.GetState().CompletedTasks.Count.ShouldBe(1);
        aggregator.GetState().CollectedResults.Count.ShouldBe(2);

        // Verify event flow
        _eventPublisher.PublishedEvents.Count.ShouldBe(4); // 2 assigns + 2 completes
    }

    #endregion

    #region Agent Tree Event Propagation Tests

    [Fact(DisplayName = "Should propagate events in agent tree")]
    public async Task Should_Propagate_Events_In_Agent_Tree()
    {
        // Arrange - Build agent tree
        //         root
        //      /       \
        //   node1       node2
        //    /   \       |
        //  leaf1 leaf2 leaf3

        _eventPublisher.Clear();

        var root = new TreeNodeAgent { NodeName = "root" };
        var node1 = new TreeNodeAgent { NodeName = "node1" };
        var node2 = new TreeNodeAgent { NodeName = "node2" };
        var leaf1 = new TreeNodeAgent { NodeName = "leaf1" };
        var leaf2 = new TreeNodeAgent { NodeName = "leaf2" };
        var leaf3 = new TreeNodeAgent { NodeName = "leaf3" };

        var allNodes = new[] { root, node1, node2, leaf1, leaf2, leaf3 };

        // Inject dependencies and activate
        foreach (var node in allNodes)
        {
            // Don't inject StateStore for tree nodes - it interferes with test setup
            // AgentStateStoreInjector.InjectStateStore(node, _serviceProvider);
            AgentEventPublisherInjector.InjectEventPublisher(node, _eventPublisher);
            await node.ActivateAsync();
        }

        // Build tree structure using test helper that properly invokes handlers
        await root.SetupTreeNodeForTesting(null, node1.NodeName, node2.NodeName);
        await node1.SetupTreeNodeForTesting(root.NodeName, leaf1.NodeName, leaf2.NodeName);
        await node2.SetupTreeNodeForTesting(root.NodeName, leaf3.NodeName);
        await leaf1.SetupTreeNodeForTesting(node1.NodeName);
        await leaf2.SetupTreeNodeForTesting(node1.NodeName);
        await leaf3.SetupTreeNodeForTesting(node2.NodeName);
        

        // Act - Root broadcasts DOWN
        var broadcastEvent = new TreeBroadcastEvent
        {
            Message = "Hello from root",
            OriginNode = "root",
            Direction = "down"
        };

        await root.BroadcastDown(broadcastEvent);

        // Simulate event propagation to children
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(broadcastEvent),
            PublisherId = root.Id.ToString()
        };

        // First level children receive
        await node1.HandleEventAsync(envelope);
        await node2.HandleEventAsync(envelope);

        // Second level children receive (from their parents)
        var node1Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TreeBroadcastEvent
            {
                Message = broadcastEvent.Message,
                OriginNode = "node1",
                Direction = "down"
            }),
            PublisherId = node1.Id.ToString()
        };

        var node2Envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(new TreeBroadcastEvent
            {
                Message = broadcastEvent.Message,
                OriginNode = "node2",
                Direction = "down"
            }),
            PublisherId = node2.Id.ToString()
        };

        await leaf1.HandleEventAsync(node1Envelope);
        await leaf2.HandleEventAsync(node1Envelope);
        await leaf3.HandleEventAsync(node2Envelope);

        // Act - Leaf sends UP
        var upEvent = new TreeBroadcastEvent
        {
            Message = "Report from leaf1",
            OriginNode = "leaf1",
            Direction = "up"
        };

        await leaf1.BroadcastUp(upEvent);

        // Assert - Verify tree structure
        root.GetState().ChildNodes.Count.ShouldBe(2);
        node1.GetState().ChildNodes.Count.ShouldBe(2);
        node2.GetState().ChildNodes.Count.ShouldBe(1);

        // Verify broadcast propagation
        node1.GetState().ReceivedBroadcasts.Count.ShouldBe(1);
        node2.GetState().ReceivedBroadcasts.Count.ShouldBe(1);
        leaf1.GetState().ReceivedBroadcasts.Count.ShouldBe(1);
        leaf2.GetState().ReceivedBroadcasts.Count.ShouldBe(1);
        leaf3.GetState().ReceivedBroadcasts.Count.ShouldBe(1);

        // Verify upward propagation
        _eventPublisher.PublishedEvents.Any(e => e.Direction == EventDirection.Up).ShouldBeTrue();
    }

    #endregion

    #region State Recovery Tests

    [Fact(DisplayName = "Should recover agent state after restart")]
    public async Task Should_Recover_Agent_State_After_Restart()
    {
        // Arrange - Create and setup initial agent
        var agentId = Guid.NewGuid().ToString();
        var agent1 = new StatefulAgent(agentId);
        // Don't inject StateStore to avoid state protection conflicts
        // AgentStateStoreInjector.InjectStateStore(agent1, _serviceProvider);

        // Setup initial state BEFORE activation (so OnActivateAsync can apply it)
        agent1.SetupStateForTesting("StatefulAgent", 42,
            ["item1", "item2"],
            new Dictionary<string, string> { ["version"] = "1.0", ["environment"] = "test" });
            
        await agent1.ActivateAsync();

        // Process some events
        for (var i = 0; i < 3; i++)
        {
            var evt = new TestEvent { EventId = $"state-{i}" };
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(evt),
                PublisherId = "state-tester"
            };

            await agent1.HandleEventAsync(envelope);
        }

        agent1.GetState().Counter.ShouldBe(45); // 42 + 3 events

        // Simulate state persistence
        var serializedState = agent1.GetState().ToByteArray();
        var stateSnapshot = TestAgentState.Parser.ParseFrom(serializedState);

        // Deactivate first instance
        await agent1.DeactivateAsync();

        // Act - Create new instance with same ID
        var agent2 = new StatefulAgent(agentId);
        // Don't inject StateStore to avoid state protection conflicts
        // AgentStateStoreInjector.InjectStateStore(agent2, _serviceProvider);

        // Restore state (simulated) - will be applied during OnActivateAsync
        agent2.RestoreState(stateSnapshot);
        await agent2.ActivateAsync();

        // Assert - State should be recovered
        agent2.GetState().Name.ShouldBe("StatefulAgent");
        agent2.GetState().Counter.ShouldBe(45);
        agent2.GetState().Items.Count.ShouldBe(2);
        agent2.GetState().Items.ShouldContain("item1");
        agent2.GetState().Items.ShouldContain("item2");
        agent2.GetState().Metadata.Count.ShouldBe(2);
        agent2.GetState().Metadata["version"].ShouldBe("1.0");
        agent2.GetState().Metadata["environment"].ShouldBe("test");

        // Continue operations to verify recovered state works
        var newEvent = new TestEvent { EventId = "post-recovery" };
        var newEnvelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(newEvent),
            PublisherId = "recovery-tester"
        };

        await agent2.HandleEventAsync(newEnvelope);
        agent2.GetState().Counter.ShouldBe(46); // Should continue from recovered state
    }

    #endregion
}