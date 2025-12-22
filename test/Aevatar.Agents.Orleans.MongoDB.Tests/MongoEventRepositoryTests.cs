using Aevatar.Agents.Runtime.Orleans.MongoDB;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;

namespace Aevatar.Agents.Orleans.MongoDB.Tests;

public class MongoEventRepositoryTests
{
    private readonly Mock<IMongoClient> _mockClient;
    private readonly Mock<IMongoDatabase> _mockDatabase;
    private readonly Mock<IMongoCollection<EventDocument>> _mockCollection;
    private readonly Mock<ILogger<MongoEventRepository>> _mockLogger;
    private readonly MongoEventRepository _repository;

    public MongoEventRepositoryTests()
    {
        _mockClient = new Mock<IMongoClient>();
        _mockDatabase = new Mock<IMongoDatabase>();
        _mockCollection = new Mock<IMongoCollection<EventDocument>>();
        _mockLogger = new Mock<ILogger<MongoEventRepository>>();

        _mockClient
            .Setup(c => c.GetDatabase(It.IsAny<string>(), null))
            .Returns(_mockDatabase.Object);

        _mockDatabase
            .Setup(d => d.GetCollection<EventDocument>(It.IsAny<string>(), null))
            .Returns(_mockCollection.Object);

        _repository = new MongoEventRepository(
            _mockClient.Object,
            "TestDB",
            "TestCollection",
            _mockLogger.Object);
    }

    [Fact]
    public async Task AppendEventsAsync_ShouldInsertEvents()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1),
            CreateTestEvent(agentId, 2),
            CreateTestEvent(agentId, 3)
        };

        _mockCollection
            .Setup(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<EventDocument>>(),
                It.IsAny<InsertManyOptions>(),
                default))
            .Returns(Task.CompletedTask);

        // Act
        var newVersion = await _repository.AppendEventsAsync(agentId, events);

        // Assert
        Assert.Equal(3, newVersion);
        _mockCollection.Verify(c => c.InsertManyAsync(
            It.Is<IEnumerable<EventDocument>>(docs => docs.Count() == 3),
            It.IsAny<InsertManyOptions>(),
            default), Times.Once);
    }

    [Fact]
    public async Task AppendEventsAsync_ShouldReturnLastEventVersion()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var events = new List<AgentStateEvent>
        {
            CreateTestEvent(agentId, 1),
            CreateTestEvent(agentId, 2),
            CreateTestEvent(agentId, 5)  // Non-sequential
        };

        _mockCollection
            .Setup(c => c.InsertManyAsync(
                It.IsAny<IEnumerable<EventDocument>>(),
                It.IsAny<InsertManyOptions>(),
                default))
            .Returns(Task.CompletedTask);

        // Act
        var newVersion = await _repository.AppendEventsAsync(agentId, events);

        // Assert
        Assert.Equal(5, newVersion);  // Should use last element (optimized)
    }

    [Fact]
    public async Task GetLatestVersionAsync_ShouldReturnMaxVersion()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var events = new[]
        {
            CreateEventDocument(agentId, 1),
            CreateEventDocument(agentId, 5),
            CreateEventDocument(agentId, 10)
        };

        var mockCursor = new Mock<IAsyncCursor<EventDocument>>();
        mockCursor
            .SetupSequence(c => c.MoveNextAsync(default))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        mockCursor
            .Setup(c => c.Current)
            .Returns(new[] { events[2] }); // Return highest version

        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<EventDocument>>(),
                It.IsAny<FindOptions<EventDocument, EventDocument>>(),
                default))
            .ReturnsAsync(mockCursor.Object);

        // Act
        var latestVersion = await _repository.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(10, latestVersion);
    }

    [Fact]
    public async Task GetLatestVersionAsync_ShouldReturn0_WhenNoEvents()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();

        var mockCursor = new Mock<IAsyncCursor<EventDocument>>();
        mockCursor
            .Setup(c => c.MoveNextAsync(default))
            .ReturnsAsync(false);
        mockCursor
            .Setup(c => c.Current)
            .Returns(new List<EventDocument>());

        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<EventDocument>>(),
                It.IsAny<FindOptions<EventDocument, EventDocument>>(),
                default))
            .ReturnsAsync(mockCursor.Object);

        // Act
        var latestVersion = await _repository.GetLatestVersionAsync(agentId);

        // Assert
        Assert.Equal(0, latestVersion);
    }

    [Fact]
    public async Task GetEventsAsync_ShouldReturnAllEvents_WhenNoFilters()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var eventDocs = new[]
        {
            CreateEventDocument(agentId, 1),
            CreateEventDocument(agentId, 2),
            CreateEventDocument(agentId, 3)
        };

        var mockCursor = new Mock<IAsyncCursor<EventDocument>>();
        mockCursor
            .SetupSequence(c => c.MoveNextAsync(default))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        mockCursor
            .Setup(c => c.Current)
            .Returns(eventDocs);

        _mockCollection
            .Setup(c => c.FindAsync(
                It.IsAny<FilterDefinition<EventDocument>>(),
                It.IsAny<FindOptions<EventDocument, EventDocument>>(),
                default))
            .ReturnsAsync(mockCursor.Object);

        // Act
        var events = await _repository.GetEventsAsync(agentId);

        // Assert
        Assert.Equal(3, events.Count);
    }

    [Fact]
    public async Task DeleteEventsBeforeVersionAsync_ShouldCallDeleteMany()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var version = 5L;

        _mockCollection
            .Setup(c => c.DeleteManyAsync(
                It.IsAny<FilterDefinition<EventDocument>>(),
                default))
            .ReturnsAsync(new DeleteResult.Acknowledged(3));

        // Act
        await _repository.DeleteEventsBeforeVersionAsync(agentId, version);

        // Assert
        _mockCollection.Verify(c => c.DeleteManyAsync(
            It.IsAny<FilterDefinition<EventDocument>>(),
            default), Times.Once);
    }

    [Fact]
    public void Constructor_WithOptions_ShouldInitialize()
    {
        // Arrange
        var options = new MongoEventRepositoryOptions
        {
            DatabaseName = "TestDB",
            CollectionName = "TestCollection",
            MaxConnectionPoolSize = 50,
            EnableDetailedLogging = true
        };

        // Act
        var repository = new MongoEventRepository(
            _mockClient.Object,
            options,
            _mockLogger.Object);

        // Assert
        Assert.NotNull(repository);
    }

    [Fact]
    public async Task AppendEventsAsync_ShouldReturnZero_WhenNoEvents()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var emptyEvents = new List<AgentStateEvent>();

        // Act
        var newVersion = await _repository.AppendEventsAsync(agentId, emptyEvents);

        // Assert
        Assert.Equal(0, newVersion);
        _mockCollection.Verify(c => c.InsertManyAsync(
            It.IsAny<IEnumerable<EventDocument>>(),
            It.IsAny<InsertManyOptions>(),
            default), Times.Never);
    }

    private AgentStateEvent CreateTestEvent(string agentId, long version)
    {
        return new AgentStateEvent
        {
            AgentId = agentId,
            Version = version,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            EventId = Guid.NewGuid().ToString(),
            EventData = Any.Pack(new Empty())
        };
    }

    private EventDocument CreateEventDocument(string agentId, long version)
    {
        var evt = CreateTestEvent(agentId, version);
        return new EventDocument
        {
            Id = $"{agentId}_{version}",
            AgentId = agentId,
            Version = version,
            EventData = evt.ToByteArray(),
            Timestamp = evt.Timestamp.ToDateTime(),
            EventType = evt.EventData.TypeUrl
        };
    }
}
