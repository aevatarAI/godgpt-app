using Aevatar.Agents.Core.EventSourcing;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests;

public class InMemoryEventStoreTests
{
    private static AgentStateEvent CreateTestEvent(Guid agentId, long version, string eventType)
    {
        return new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Version = version,
            EventType = eventType,
            EventData = Any.Pack(new ParentChangedEvent { NewParent = "test" }),
            AgentId = agentId.ToString()
        };
    }

    [Fact(DisplayName = "AppendEventsAsync should append events successfully")]
    public async Task AppendEventsAsync_ShouldAppendEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2")
        };

        // Act
        var newVersion = await store.AppendEventsAsync(agentId, events, 0);

        // Assert
        newVersion.Should().Be(2);
        var retrievedEvents = await store.GetEventsAsync(agentId);
        retrievedEvents.Should().HaveCount(2);
        retrievedEvents[0].Version.Should().Be(1);
        retrievedEvents[1].Version.Should().Be(2);
    }

    [Fact(DisplayName = "AppendEventsAsync should enforce optimistic concurrency")]
    public async Task AppendEventsAsync_ShouldEnforceOptimisticConcurrency()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var event1 = CreateTestEvent(agentId, 0, "Event1");

        await store.AppendEventsAsync(agentId, new[] { event1 }, 0);

        // Act & Assert
        var event2 = CreateTestEvent(agentId, 0, "Event2");
        var act = () => store.AppendEventsAsync(agentId, new[] { event2 }, 0);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Concurrency conflict*");
    }

    [Fact(DisplayName = "GetEventsAsync should return all events for agent")]
    public async Task GetEventsAsync_ShouldReturnAllEvents()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        await store.AppendEventsAsync(agentId, events, 0);

        // Act
        var retrievedEvents = await store.GetEventsAsync(agentId);

        // Assert
        retrievedEvents.Should().HaveCount(3);
        retrievedEvents[0].EventType.Should().Be("Event1");
        retrievedEvents[1].EventType.Should().Be("Event2");
        retrievedEvents[2].EventType.Should().Be("Event3");
    }

    [Fact(DisplayName = "GetEventsAsync should support range query (fromVersion)")]
    public async Task GetEventsAsync_ShouldSupportRangeQueryFromVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        await store.AppendEventsAsync(agentId, events, 0);

        // Act
        var retrievedEvents = await store.GetEventsAsync(agentId, fromVersion: 2);

        // Assert
        retrievedEvents.Should().HaveCount(2);
        retrievedEvents[0].Version.Should().Be(2);
        retrievedEvents[1].Version.Should().Be(3);
    }

    [Fact(DisplayName = "GetEventsAsync should support range query (toVersion)")]
    public async Task GetEventsAsync_ShouldSupportRangeQueryToVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        await store.AppendEventsAsync(agentId, events, 0);

        // Act
        var retrievedEvents = await store.GetEventsAsync(agentId, toVersion: 2);

        // Assert
        retrievedEvents.Should().HaveCount(2);
        retrievedEvents[0].Version.Should().Be(1);
        retrievedEvents[1].Version.Should().Be(2);
    }

    [Fact(DisplayName = "GetEventsAsync should support pagination (maxCount)")]
    public async Task GetEventsAsync_ShouldSupportPagination()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        await store.AppendEventsAsync(agentId, events, 0);

        // Act
        var retrievedEvents = await store.GetEventsAsync(agentId, maxCount: 2);

        // Assert
        retrievedEvents.Should().HaveCount(2);
        retrievedEvents[0].Version.Should().Be(1);
        retrievedEvents[1].Version.Should().Be(2);
    }

    [Fact(DisplayName = "GetLatestVersionAsync should return latest version")]
    public async Task GetLatestVersionAsync_ShouldReturnLatestVersion()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        await store.AppendEventsAsync(agentId, events, 0);

        // Act
        var latestVersion = await store.GetLatestVersionAsync(agentId);

        // Assert
        latestVersion.Should().Be(3);
    }

    [Fact(DisplayName = "GetLatestVersionAsync should return 0 for non-existent agent")]
    public async Task GetLatestVersionAsync_ShouldReturn0ForNonExistentAgent()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();

        // Act
        var latestVersion = await store.GetLatestVersionAsync(agentId);

        // Assert
        latestVersion.Should().Be(0);
    }

    // Note: Snapshot tests removed - snapshots are now handled by IStateStore<TState> in GAgentBase

    [Fact(DisplayName = "Multiple agents should be isolated")]
    public async Task MultipleAgents_ShouldBeIsolated()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agent1Id = Guid.NewGuid();
        var agent2Id = Guid.NewGuid();

        var agent1Events = new[] { CreateTestEvent(agent1Id, 0, "Agent1Event") };
        var agent2Events = new[] { CreateTestEvent(agent2Id, 0, "Agent2Event") };

        // Act
        await store.AppendEventsAsync(agent1Id, agent1Events, 0);
        await store.AppendEventsAsync(agent2Id, agent2Events, 0);

        var agent1Retrieved = await store.GetEventsAsync(agent1Id);
        var agent2Retrieved = await store.GetEventsAsync(agent2Id);

        // Assert
        agent1Retrieved.Should().HaveCount(1);
        agent2Retrieved.Should().HaveCount(1);
        agent1Retrieved[0].EventType.Should().Be("Agent1Event");
        agent2Retrieved[0].EventType.Should().Be("Agent2Event");
    }

    [Fact(DisplayName = "Batch append should be atomic")]
    public async Task BatchAppend_ShouldBeAtomic()
    {
        // Arrange
        var store = new InMemoryEventStore();
        var agentId = Guid.NewGuid();
        var events = new[]
        {
            CreateTestEvent(agentId, 0, "Event1"),
            CreateTestEvent(agentId, 0, "Event2"),
            CreateTestEvent(agentId, 0, "Event3")
        };

        // Act
        var newVersion = await store.AppendEventsAsync(agentId, events, 0);

        // Assert
        newVersion.Should().Be(3);
        var retrievedEvents = await store.GetEventsAsync(agentId);
        retrievedEvents.Should().HaveCount(3);

        // All events should have consecutive versions
        for (var i = 0; i < retrievedEvents.Count; i++)
        {
            retrievedEvents[i].Version.Should().Be(i + 1);
        }
    }
}