using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.EventPublisher;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Point-to-point communication tests.
/// Tests various scenarios for SendToAsync functionality.
/// </summary>
public class PointToPointCommunicationTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly TestEventPublisher _eventPublisher = fixture.EventPublisher;

    // ============================================================
    // Basic Point-to-Point Communication Tests
    // ============================================================

    [Fact(DisplayName = "Pure P2P: SendToAsync should record target AgentId")]
    public async Task SendToAsync_Should_Record_TargetAgentId()
    {
        // Arrange
        _eventPublisher.Clear();
        var senderAgent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act
        await senderAgent.SendDirectMessageAsync(targetId, "Hello Target");

        // Assert
        _eventPublisher.SentEvents.Count.ShouldBe(1);
        var sentEvent = _eventPublisher.SentEvents[0];
        sentEvent.TargetAgentId.ShouldBe(targetId);
        sentEvent.OnArrivalDirection.ShouldBe(EventDirection.Unspecified);
        sentEvent.Event.ShouldBeOfType<DirectMessage>();
        ((DirectMessage)sentEvent.Event).Content.ShouldBe("Hello Target");
    }

    [Fact(DisplayName = "Pure P2P: OnArrivalDirection should be Unspecified")]
    public async Task PureP2P_Should_Have_Unspecified_OnArrivalDirection()
    {
        // Arrange
        _eventPublisher.Clear();
        var senderAgent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act
        await senderAgent.SendDirectMessageAsync(targetId, "Private message");

        // Assert
        _eventPublisher.SentEvents.Count.ShouldBe(1);
        _eventPublisher.SentEvents[0].OnArrivalDirection.ShouldBe(EventDirection.Unspecified);
    }

    // ============================================================
    // P2P + Group Broadcast Tests
    // ============================================================

    [Fact(DisplayName = "P2P+Broadcast: SendToGroup should set OnArrivalDirection to Down")]
    public async Task SendToGroup_Should_Set_OnArrivalDirection_Down()
    {
        // Arrange
        _eventPublisher.Clear();
        var senderAgent = CreateAgent();
        var coordinatorId = Guid.NewGuid().ToString();

        // Act
        await senderAgent.SendToGroupAsync(coordinatorId, "Task for the group");

        // Assert
        _eventPublisher.SentEvents.Count.ShouldBe(1);
        var sentEvent = _eventPublisher.SentEvents[0];
        sentEvent.TargetAgentId.ShouldBe(coordinatorId);
        sentEvent.OnArrivalDirection.ShouldBe(EventDirection.Down);
    }

    [Fact(DisplayName = "P2P+Up propagation: should set OnArrivalDirection to Up")]
    public async Task SendWithUpPropagation_Should_Set_OnArrivalDirection_Up()
    {
        // Arrange
        _eventPublisher.Clear();
        var senderAgent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act
        await senderAgent.SendWithUpPropagationAsync(targetId, "Report to chain");

        // Assert
        _eventPublisher.SentEvents.Count.ShouldBe(1);
        var sentEvent = _eventPublisher.SentEvents[0];
        sentEvent.TargetAgentId.ShouldBe(targetId);
        sentEvent.OnArrivalDirection.ShouldBe(EventDirection.Up);
    }

    // ============================================================
    // P2P vs Broadcast Comparison Tests
    // ============================================================

    [Fact(DisplayName = "P2P and broadcast should use different channels")]
    public async Task P2P_And_Broadcast_Should_Use_Different_Channels()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act - Send P2P message first
        await agent.SendDirectMessageAsync(targetId, "P2P message");
        
        // Act - Then send broadcast message
        await agent.BroadcastMessageAsync("Broadcast message");

        // Assert - P2P and broadcast should be recorded separately
        _eventPublisher.SentEvents.Count.ShouldBe(1);
        _eventPublisher.PublishedEvents.Count.ShouldBe(1);

        // P2P message
        var p2pEvent = _eventPublisher.SentEvents[0];
        p2pEvent.TargetAgentId.ShouldBe(targetId);
        ((DirectMessage)p2pEvent.Event).Content.ShouldBe("P2P message");

        // Broadcast message
        var broadcastEvent = _eventPublisher.PublishedEvents[0];
        broadcastEvent.Direction.ShouldBe(EventDirection.Down);
        ((DirectMessage)broadcastEvent.Event).Content.ShouldBe("Broadcast message");
    }

    // ============================================================
    // Event Handling Tests
    // ============================================================

    [Fact(DisplayName = "Agent should handle received P2P DirectMessage")]
    public async Task Agent_Should_Handle_Received_DirectMessage()
    {
        // Arrange
        _eventPublisher.Clear();
        var receiverAgent = CreateAgent();
        var senderId = Guid.NewGuid();

        // Simulate received P2P message
        var directMessage = new DirectMessage
        {
            MessageId = "dm-001",
            Content = "Hello from sender",
            SenderId = senderId.ToString()
        };

        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            Payload = Any.Pack(directMessage),
            PublisherId = senderId.ToString(),
            TargetAgentId = receiverAgent.Id.ToString(),
            OnArrivalDirection = EventDirection.Unspecified
        };

        // Act
        await receiverAgent.HandleEventAsync(envelope);

        // Assert
        receiverAgent.ReceivedDirectMessageCount.ShouldBe(1);
        receiverAgent.LastReceivedMessageId.ShouldBe("dm-001");
        receiverAgent.LastReceivedContent.ShouldBe("Hello from sender");
        receiverAgent.LastSenderId.ShouldBe(senderId.ToString());
    }

    [Fact(DisplayName = "P2P message count should be independent from broadcast count")]
    public async Task P2P_Count_Should_Be_Independent_From_Broadcast_Count()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = CreateAgent();
        var senderId = Guid.NewGuid();

        // Send 2 P2P messages
        for (int i = 0; i < 2; i++)
        {
            var dm = new DirectMessage
            {
                MessageId = $"dm-{i}",
                Content = $"Direct message {i}",
                SenderId = senderId.ToString()
            };
            var dmEnvelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(dm),
                PublisherId = senderId.ToString(),
                TargetAgentId = agent.Id.ToString()
            };
            await agent.HandleEventAsync(dmEnvelope);
        }

        // Send 3 broadcast messages
        for (int i = 0; i < 3; i++)
        {
            var evt = new TestEvent { EventId = $"evt-{i}" };
            var evtEnvelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Any.Pack(evt),
                PublisherId = Guid.NewGuid().ToString()
            };
            await agent.HandleEventAsync(evtEnvelope);
        }

        // Assert
        agent.ReceivedDirectMessageCount.ShouldBe(2);
        agent.ReceivedBroadcastCount.ShouldBe(3);
    }

    // ============================================================
    // Edge Case Tests
    // ============================================================

    [Fact(DisplayName = "SendToAsync should return valid event ID")]
    public async Task SendToAsync_Should_Return_Valid_EventId()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act
        var eventId = await agent.SendDirectMessageAsync(targetId, "Test");

        // Assert
        eventId.ShouldNotBeNullOrEmpty();
        Guid.TryParse(eventId, out _).ShouldBeTrue();
    }

    [Fact(DisplayName = "Multiple P2P messages should all be recorded")]
    public async Task Multiple_P2P_Messages_Should_All_Be_Recorded()
    {
        // Arrange
        _eventPublisher.Clear();
        var agent = CreateAgent();
        var targets = new[] { Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), Guid.NewGuid().ToString() };

        // Act
        foreach (var targetId in targets)
        {
            await agent.SendDirectMessageAsync(targetId, $"Message to {targetId}");
        }

        // Assert
        _eventPublisher.SentEvents.Count.ShouldBe(3);
        _eventPublisher.AttemptedSendCount.ShouldBe(3);

        for (int i = 0; i < targets.Length; i++)
        {
            _eventPublisher.SentEvents[i].TargetAgentId.ShouldBe(targets[i]);
        }
    }

    [Fact(DisplayName = "EventPublisher exception should propagate correctly")]
    public async Task EventPublisher_Exception_Should_Propagate()
    {
        // Arrange
        _eventPublisher.Clear();
        _eventPublisher.ShouldThrowException = true;
        _eventPublisher.ExceptionMessage = "P2P send failed";
        
        var agent = CreateAgent();
        var targetId = Guid.NewGuid().ToString();

        // Act & Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(
            async () => await agent.SendDirectMessageAsync(targetId, "Test"));
        
        exception.Message.ShouldBe("P2P send failed");

        // Cleanup
        _eventPublisher.ShouldThrowException = false;
    }

    // ============================================================
    // Helper Methods
    // ============================================================

    private P2PTestAgent CreateAgent()
    {
        var agent = new P2PTestAgent();
        AgentStateStoreInjector.InjectStateStore(agent, _serviceProvider);
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        return agent;
    }
}
