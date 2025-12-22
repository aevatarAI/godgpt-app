using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;
using Xunit;

namespace Aevatar.Agents.Orleans.Tests.Stream;

/// <summary>
/// Unit tests for UnifiedGrainEventPublisher
/// Tests the unified event publishing mechanism for Orleans grains
/// </summary>
public class UnifiedGrainEventPublisherTests
{
    private readonly Mock<IMessageStream> _mockStream;
    private readonly Mock<IGrainFactory> _mockGrainFactory;
    private readonly ILogger _logger;
    private readonly string _testGrainId = "TestAgent:12345678-1234-1234-1234-123456789abc";

    public UnifiedGrainEventPublisherTests()
    {
        _mockStream = new Mock<IMessageStream>();
        _mockGrainFactory = new Mock<IGrainFactory>();
        _logger = NullLogger.Instance;
    }

    #region PublishEventAsync Tests

    [Fact]
    public async Task PublishEventAsync_Should_Create_EventEnvelope_And_Publish_To_Stream()
    {
        // Arrange
        EventEnvelope? capturedEnvelope = null;
        _mockStream
            .Setup(s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<EventEnvelope, CancellationToken>((e, _) => capturedEnvelope = e)
            .Returns(Task.CompletedTask);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Hello" };

        // Act
        var eventId = await publisher.PublishEventAsync(testEvent, EventDirection.Down);

        // Assert
        Assert.NotNull(eventId);
        Assert.NotNull(capturedEnvelope);
        Assert.Equal(_testGrainId, capturedEnvelope!.PublisherId);
        Assert.Equal(EventDirection.Down, capturedEnvelope.Direction);
        Assert.NotNull(capturedEnvelope.Payload);
        _mockStream.Verify(s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PublishEventAsync_Should_Set_Correct_Direction()
    {
        // Arrange
        EventEnvelope? capturedEnvelope = null;
        _mockStream
            .Setup(s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<EventEnvelope, CancellationToken>((e, _) => capturedEnvelope = e)
            .Returns(Task.CompletedTask);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Test" };

        // Act - Publish with Up direction
        await publisher.PublishEventAsync(testEvent, EventDirection.Up);

        // Assert
        Assert.NotNull(capturedEnvelope);
        Assert.Equal(EventDirection.Up, capturedEnvelope!.Direction);
    }

    [Fact]
    public async Task PublishEventAsync_Should_Generate_Unique_EventIds()
    {
        // Arrange
        _mockStream
            .Setup(s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Test" };

        // Act
        var eventId1 = await publisher.PublishEventAsync(testEvent);
        var eventId2 = await publisher.PublishEventAsync(testEvent);
        var eventId3 = await publisher.PublishEventAsync(testEvent);

        // Assert
        Assert.NotEqual(eventId1, eventId2);
        Assert.NotEqual(eventId2, eventId3);
        Assert.NotEqual(eventId1, eventId3);
    }

    [Fact]
    public async Task PublishEventAsync_Should_Handle_Null_Stream_Gracefully()
    {
        // Arrange
        var publisher = new UnifiedGrainEventPublisher(
            null,  // No stream
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Test" };

        // Act - Should not throw
        var eventId = await publisher.PublishEventAsync(testEvent);

        // Assert
        Assert.NotNull(eventId);  // Event ID should still be generated
    }

    [Fact]
    public async Task PublishEventAsync_Should_Set_Timestamp()
    {
        // Arrange
        EventEnvelope? capturedEnvelope = null;
        _mockStream
            .Setup(s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()))
            .Callback<EventEnvelope, CancellationToken>((e, _) => capturedEnvelope = e)
            .Returns(Task.CompletedTask);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var beforeTime = DateTime.UtcNow;
        var testEvent = new StringValue { Value = "Test" };

        // Act
        await publisher.PublishEventAsync(testEvent);
        var afterTime = DateTime.UtcNow;

        // Assert
        Assert.NotNull(capturedEnvelope);
        var timestamp = capturedEnvelope!.Timestamp.ToDateTime();
        Assert.True(timestamp >= beforeTime && timestamp <= afterTime);
    }

    #endregion

    #region SendToAsync Tests

    [Fact]
    public async Task SendToAsync_Should_Send_Event_Via_RPC_To_Target_Grain()
    {
        // Arrange
        var targetAgentId = Guid.NewGuid();
        byte[]? capturedBytes = null;

        var mockTargetGrain = new Mock<IGAgentGrain>();
        mockTargetGrain
            .Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        _mockGrainFactory
            .Setup(f => f.GetGrain<IGAgentGrain>(targetAgentId.ToString(), null))
            .Returns(mockTargetGrain.Object);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "P2P Message" };

        // Act
        var eventId = await publisher.SendToAsync(targetAgentId.ToString(), testEvent, EventDirection.Down);

        // Assert
        Assert.NotNull(eventId);
        Assert.NotNull(capturedBytes);
        mockTargetGrain.Verify(g => g.HandleEventAsync(It.IsAny<byte[]>()), Times.Once);

        // Verify the envelope was serialized correctly
        var deserializedEnvelope = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.Equal(_testGrainId, deserializedEnvelope.PublisherId);
        Assert.Equal(targetAgentId.ToString(), deserializedEnvelope.TargetAgentId);
    }

    [Fact]
    public async Task SendToAsync_Should_Set_OnArrivalDirection()
    {
        // Arrange
        var targetAgentId = Guid.NewGuid().ToString();
        byte[]? capturedBytes = null;

        var mockTargetGrain = new Mock<IGAgentGrain>();
        mockTargetGrain
            .Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Callback<byte[]>(bytes => capturedBytes = bytes)
            .Returns(Task.CompletedTask);

        _mockGrainFactory
            .Setup(f => f.GetGrain<IGAgentGrain>(targetAgentId, null))
            .Returns(mockTargetGrain.Object);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Test" };

        // Act
        await publisher.SendToAsync(targetAgentId, testEvent, EventDirection.Both);

        // Assert
        var deserializedEnvelope = EventEnvelope.Parser.ParseFrom(capturedBytes);
        Assert.Equal(EventDirection.Both, deserializedEnvelope.OnArrivalDirection);
    }

    [Fact]
    public async Task SendToAsync_Should_Not_Use_Stream_For_P2P()
    {
        // Arrange
        var targetAgentId = Guid.NewGuid().ToString();

        var mockTargetGrain = new Mock<IGAgentGrain>();
        mockTargetGrain
            .Setup(g => g.HandleEventAsync(It.IsAny<byte[]>()))
            .Returns(Task.CompletedTask);

        _mockGrainFactory
            .Setup(f => f.GetGrain<IGAgentGrain>(targetAgentId, null))
            .Returns(mockTargetGrain.Object);

        var publisher = new UnifiedGrainEventPublisher(
            _mockStream.Object,
            () => _testGrainId,
            _logger,
            _mockGrainFactory.Object);

        var testEvent = new StringValue { Value = "Test" };

        // Act
        await publisher.SendToAsync(targetAgentId, testEvent);

        // Assert - Stream should NOT be used for P2P communication
        _mockStream.Verify(
            s => s.ProduceAsync(It.IsAny<EventEnvelope>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #endregion
}

