using Aevatar.Agents.Runtime.Orleans.EventSourcing;
using Aevatar.Agents.TestBase;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.EventSourcing;

/// <summary>
/// OrleansEventStore integration tests
/// Tests the Orleans-based EventStore implementation using TestCluster
/// Note: Snapshot tests removed - snapshots are now handled by IStateStore<TState> in GAgentBase
/// </summary>
public class OrleansEventStoreTests : AevatarAgentsTestBase
{
    private readonly IEventRepository _eventRepository;
    private readonly ILogger<OrleansEventStore> _logger;

    public OrleansEventStoreTests(ClusterFixture fixture) : base(fixture)
    {
        // Get the shared IEventRepository instance used by both Silo and Client
        // This ensures OrleansEventStore (in Silo) and tests use the same repository instance
        _eventRepository = Fixture.GetSharedEventRepository();
        _logger = ServiceProvider.GetRequiredService<ILogger<OrleansEventStore>>();
    }

    private OrleansEventStore CreateEventStore() => new OrleansEventStore(_eventRepository, _logger);

    private AgentStateEvent CreateTestEvent(string agentId, long version, string eventType)
    {
        return new AgentStateEvent
        {
            EventId = Guid.NewGuid().ToString(),
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            Version = version,
            EventType = eventType,
            EventData = Google.Protobuf.WellKnownTypes.Any.Pack(new ChildAddedEvent { ChildId = $"child-{version}" }),
            AgentId = agentId,
            CorrelationId = Guid.NewGuid().ToString(),
            Metadata = { { "testKey", $"testValue-{version}" } }
        };
    }

    [Fact(DisplayName = "OrleansEventStore should append events successfully")]
    public async Task AppendEventsAsync_ShouldAppendEvents()
    {
        // Arrange
        var eventStore = CreateEventStore();
        var agentId = Guid.NewGuid().ToString();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2")
        };

        // Act
        var newVersion = await eventStore.AppendEventsAsync(agentId, events, 0);

        // Assert
        Assert.Equal(2, newVersion);
        var storedEvents = await eventStore.GetEventsAsync(agentId);
        Assert.Equal(2, storedEvents.Count);
        Assert.Equal(events.First().EventId, storedEvents.First().EventId);
    }

    [Fact(DisplayName = "OrleansEventStore should enforce optimistic concurrency")]
    public async Task AppendEventsAsync_ShouldEnforceOptimisticConcurrency()
    {
        // Arrange
        var eventStore = CreateEventStore();
        var agentId = Guid.NewGuid().ToString();
        // First append: version 0 -> 1
        var firstVersion = await eventStore.AppendEventsAsync(agentId, new[] { CreateTestEvent(agentId, 1, "Event1") }, 0);
        Assert.Equal(1, firstVersion);

        // Act & Assert
        // Second append: expected version 0, but current version is 1, should throw
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            eventStore.AppendEventsAsync(agentId, new[] { CreateTestEvent(agentId, 2, "Event2") }, 0));

        Assert.Contains("Concurrency conflict", exception.Message);
    }

    [Fact(DisplayName = "OrleansEventStore should support range queries")]
    public async Task GetEventsAsync_ShouldSupportRangeQuery()
    {
        // Arrange
        var eventStore = CreateEventStore();
        var agentId = Guid.NewGuid().ToString();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2"),
            CreateTestEvent(agentId, 3, "Event3"),
            CreateTestEvent(agentId, 4, "Event4")
        };
        await eventStore.AppendEventsAsync(agentId, events, 0);

        // Act
        var rangeEvents = await eventStore.GetEventsAsync(agentId, fromVersion: 2, toVersion: 3);

        // Assert
        Assert.Equal(2, rangeEvents.Count);
        Assert.Equal(2L, rangeEvents.First().Version);
        Assert.Equal(3L, rangeEvents.Last().Version);
    }

    [Fact(DisplayName = "OrleansEventStore should get latest version")]
    public async Task GetLatestVersionAsync_ShouldReturnLatestVersion()
    {
        // Arrange
        var eventStore = CreateEventStore();
        var agentId = Guid.NewGuid().ToString();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1, "Event1"),
            CreateTestEvent(agentId, 2, "Event2"),
            CreateTestEvent(agentId, 3, "Event3")
        };
        await eventStore.AppendEventsAsync(agentId, events, 0);

        // Act
        var latestVersion = await eventStore.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(3, latestVersion);
    }

    // Note: Snapshot tests removed - snapshots are now handled by IStateStore<TState> in GAgentBase

    [Fact(DisplayName = "OrleansEventStore should return 0 for non-existent agent")]
    public async Task GetLatestVersionAsync_ShouldReturn0ForNonExistentAgent()
    {
        // Arrange
        var eventStore = CreateEventStore();
        var agentId = Guid.NewGuid().ToString();

        // Act
        var version = await eventStore.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(0, version);
    }
}