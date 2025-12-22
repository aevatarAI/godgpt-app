using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Actor;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit.Abstractions;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Tests for GAgentActorBase framework functionality
/// Testing parent-child relationships, event routing, and actor lifecycle
/// </summary>
public class GAgentActorBaseTests : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ITestOutputHelper _output;

    public GAgentActorBaseTests(CoreTestFixture fixture, ITestOutputHelper output)
    {
        _serviceProvider = fixture.ServiceProvider;
        _output = output;
    }

    #region Mock Implementation

    /// <summary>
    /// Mock implementation of GAgentActorBase for testing framework logic
    /// </summary>

    /// <summary>
    /// Simple test agent for use with MockGAgentActor
    /// </summary>
    private class SimpleTestAgent : GAgentBase<TestAgentState>
    {
        public int EventHandlerCallCount { get; private set; }
        public bool IsActivated { get; private set; }

        [EventHandler]
        public async Task HandleTestEvent(TestEvent evt)
        {
            EventHandlerCallCount++;
            GetState().Counter++;
            await Task.CompletedTask;
        }

        public override string GetDescription()
        {
            return $"SimpleTestAgent: {GetState().Name}";
        }

        protected override async Task OnActivateAsync(CancellationToken ct = default)
        {
            IsActivated = true;
            await base.OnActivateAsync(ct);
        }

        protected override async Task OnDeactivateAsync(CancellationToken ct = default)
        {
            IsActivated = false;
            await base.OnDeactivateAsync(ct);
        }

        public TestAgentState GetState() => State;
    }

    #endregion

    #region Parent-Child Relationship Tests

    [Fact(DisplayName = "Should set parent correctly")]
    public async Task Should_Set_Parent_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();

        // Act
        await actor.SetParentAsync(parentId);

        // Assert
        var parent = await actor.GetParentAsync();
        parent.ShouldBe(parentId);
        actor.GetEventRouter().GetParent().ShouldBe(parentId);
    }

    [Fact(DisplayName = "Should clear parent correctly")]
    public async Task Should_Clear_Parent_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();
        await actor.SetParentAsync(parentId);

        // Act
        await actor.ClearParentAsync();

        // Assert
        var parent = await actor.GetParentAsync();
        parent.ShouldBeNull();
        actor.GetEventRouter().GetParent().ShouldBeNull();
    }

    [Fact(DisplayName = "Should add children correctly")]
    public async Task Should_Add_Children_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var childId1 = Guid.NewGuid().ToString();
        var childId2 = Guid.NewGuid().ToString();

        // Act
        await actor.AddChildAsync(childId1);
        await actor.AddChildAsync(childId2);

        // Assert
        var children = await actor.GetChildrenAsync();
        children.Count.ShouldBe(2);
        children.ShouldContain(childId1);
        children.ShouldContain(childId2);
    }

    [Fact(DisplayName = "Should remove children correctly")]
    public async Task Should_Remove_Children_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var childId1 = Guid.NewGuid().ToString();
        var childId2 = Guid.NewGuid().ToString();
        await actor.AddChildAsync(childId1);
        await actor.AddChildAsync(childId2);

        // Act
        await actor.RemoveChildAsync(childId1);

        // Assert
        var children = await actor.GetChildrenAsync();
        children.Count.ShouldBe(1);
        children.ShouldNotContain(childId1);
        children.ShouldContain(childId2);
    }

    #endregion

    #region Event Publishing Tests

    [Fact(DisplayName = "Should publish event UP to parent")]
    public async Task Should_Publish_Event_Up_To_Parent()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();
        await actor.SetParentAsync(parentId);

        var testEvent = new TestEvent { EventId = "test-up" };

        // Act - isInternalCall: true to simulate Agent internal publishing (preserves PublisherId)
        var eventId = await ((IEventPublisher)actor).PublishEventAsync(testEvent, EventDirection.Up, default, isInternalCall: true);

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        actor.SentEvents.ShouldContainKey(parentId);
        actor.SentEvents[parentId].Count.ShouldBe(1);

        var sentEnvelope = actor.SentEvents[parentId][0];
        sentEnvelope.Direction.ShouldBe(EventDirection.Up);
        sentEnvelope.PublisherId.ShouldBe(agent.Id.ToString());
    }

    [Fact(DisplayName = "Should publish event DOWN to children")]
    public async Task Should_Publish_Event_Down_To_Children()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var childId1 = Guid.NewGuid().ToString();
        var childId2 = Guid.NewGuid().ToString();
        await actor.AddChildAsync(childId1);
        await actor.AddChildAsync(childId2);

        var testEvent = new TestEvent { EventId = "test-down" };

        // Act - isInternalCall: true to simulate Agent internal publishing
        var eventId = await ((IEventPublisher)actor).PublishEventAsync(testEvent, EventDirection.Down, default, isInternalCall: true);

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        actor.SentEvents.Count.ShouldBe(2);
        actor.SentEvents.ShouldContainKey(childId1);
        actor.SentEvents.ShouldContainKey(childId2);

        foreach (var childId in new[] { childId1, childId2 })
        {
            var sentEnvelope = actor.SentEvents[childId][0];
            sentEnvelope.Direction.ShouldBe(EventDirection.Down);
            sentEnvelope.PublisherId.ShouldBe(agent.Id.ToString());
        }
    }

    [Fact(DisplayName = "Should publish event BOTH directions")]
    public async Task Should_Publish_Event_Both_Directions()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();
        await actor.SetParentAsync(parentId);
        await actor.AddChildAsync(childId);

        var testEvent = new TestEvent { EventId = "test-both" };

        // Act
        var eventId = await ((IEventPublisher)actor).PublishEventAsync(testEvent, EventDirection.Both);

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        actor.SentEvents.Count.ShouldBe(2);
        actor.SentEvents.ShouldContainKey(parentId);
        actor.SentEvents.ShouldContainKey(childId);

        // Check UP event to parent
        var upEnvelope = actor.SentEvents[parentId][0];
        upEnvelope.Direction.ShouldBe(EventDirection.Up);

        // Check DOWN event to child
        var downEnvelope = actor.SentEvents[childId][0];
        downEnvelope.Direction.ShouldBe(EventDirection.Down);
    }

    [Fact(DisplayName = "Should not send UP event when no parent")]
    public async Task Should_Not_Send_Up_Event_When_No_Parent()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        var testEvent = new TestEvent { EventId = "test-no-parent" };

        // Act
        var eventId = await ((IEventPublisher)actor).PublishEventAsync(testEvent, EventDirection.Up);

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        actor.SentEvents.Count.ShouldBe(0); // No parent to send to
    }

    [Fact(DisplayName = "Should not send DOWN event when no children")]
    public async Task Should_Not_Send_Down_Event_When_No_Children()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        var testEvent = new TestEvent { EventId = "test-no-children" };

        // Act
        var eventId = await ((IEventPublisher)actor).PublishEventAsync(testEvent, EventDirection.Down);

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        actor.SentEvents.Count.ShouldBe(0); // No children to send to
    }

    #endregion

    #region Event Handling Tests

    [Fact(DisplayName = "Should handle incoming events")]
    public async Task Should_Handle_Incoming_Events()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        var testEvent = new TestEvent { EventId = "incoming-event" };
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(testEvent),
            PublisherId = Guid.NewGuid().ToString()
        };

        // Act
        await actor.HandleEventAsync(envelope);

        // Assert
        agent.EventHandlerCallCount.ShouldBe(1);
        agent.GetState().Counter.ShouldBe(1);
    }

    [Fact(DisplayName = "Should deduplicate events")]
    public async Task Should_Deduplicate_Events()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        var testEvent = new TestEvent { EventId = "duplicate-event" };
        var envelope = new EventEnvelope
        {
            Id = "same-event-id",
            Payload = Any.Pack(testEvent),
            PublisherId = Guid.NewGuid().ToString()
        };

        // Act - Send same event twice
        await actor.HandleEventAsync(envelope);
        await actor.HandleEventAsync(envelope);

        // Assert - Should only process once
        agent.EventHandlerCallCount.ShouldBe(1);
        agent.GetState().Counter.ShouldBe(1);
    }

    #endregion

    #region Lifecycle Tests

    [Fact(DisplayName = "Should activate actor correctly")]
    public async Task Should_Activate_Actor_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        // Act
        await actor.ActivateAsync();

        // Assert
        actor.ActivateCallCount.ShouldBe(1);
        agent.IsActivated.ShouldBeTrue();
    }

    [Fact(DisplayName = "Should deactivate actor correctly")]
    public async Task Should_Deactivate_Actor_Correctly()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        await actor.ActivateAsync();

        // Act
        await actor.DeactivateAsync();

        // Assert
        actor.DeactivateCallCount.ShouldBe(1);
        agent.IsActivated.ShouldBeFalse();
    }

    [Fact(DisplayName = "Should get agent description")]
    public async Task Should_Get_Agent_Description()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        agent.GetState().Name = "TestAgent";
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);

        // Act
        var description = await actor.GetDescriptionAsync();

        // Assert
        description.ShouldBe("SimpleTestAgent: TestAgent");
    }

    #endregion

    #region Event Routing Tests

    [Fact(DisplayName = "Should route events through EventRouter")]
    public async Task Should_Route_Events_Through_EventRouter()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        await actor.SetParentAsync(parentId);
        await actor.AddChildAsync(childId);

        // Act - Get EventRouter state
        var eventRouter = actor.GetEventRouter();

        // Assert
        eventRouter.GetParent().ShouldBe(parentId);
        eventRouter.GetChildren().Count.ShouldBe(1);
        eventRouter.GetChildren().ShouldContain(childId);
    }

    [Fact(DisplayName = "Should maintain event publisher list in envelope")]
    public async Task Should_Maintain_Event_Publisher_List()
    {
        // Arrange
        var agent = new SimpleTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        var actor = new MockGAgentActor(agent);
        var parentId = Guid.NewGuid().ToString();
        await actor.SetParentAsync(parentId);

        var testEvent = new TestEvent { EventId = "publisher-tracking" };

        // Act
        await actor.PublishEventAsync(testEvent, EventDirection.Up);

        // Assert
        actor.SentEvents[parentId][0].Publishers.Count.ShouldBe(1);
        actor.SentEvents[parentId][0].Publishers.ShouldContain(agent.Id.ToString());
    }

    #endregion
}