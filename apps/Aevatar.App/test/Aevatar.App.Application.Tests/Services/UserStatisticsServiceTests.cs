using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Dtos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for UserStatisticsService
/// Tests business logic for user statistics and app ratings
/// </summary>
public class UserStatisticsServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<UserStatisticsService> _mockLogger;
    private readonly UserStatisticsService _userStatisticsService;

    public UserStatisticsServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<UserStatisticsService>>();
        
        _userStatisticsService = new UserStatisticsService(
            _mockActorFactory,
            _mockLogger);
    }

    [Fact(DisplayName = "RecordAppRatingAsync should record rating successfully")]
    public async Task RecordAppRatingAsync_ShouldRecordRatingSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new RecordAppRatingInput
        {
            Platform = "iOS",
            DeviceId = "device-123"
        };

        var actor = Substitute.For<IGAgentActor, IUserStatisticsGAgent>();
        var agent = (IUserStatisticsGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(Arg.Any<Guid>())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new AppRatingRecordProto
        {
            Platform = "iOS",
            DeviceId = "device-123",
            FirstRatingTime = Timestamp.FromDateTime(DateTime.UtcNow),
            LastRatingTime = Timestamp.FromDateTime(DateTime.UtcNow),
            RatingCount = 1
        };
        
        agent.RecordAppRatingAsync(Arg.Any<RecordAppRatingRequestProto>())
            .Returns(protoResponse);

        // Act
        var result = await _userStatisticsService.RecordAppRatingAsync(userId, input);

        // Assert
        result.ShouldNotBeNull();
        result.Platform.ShouldBe("iOS");
        result.DeviceId.ShouldBe("device-123");
        result.RatingCount.ShouldBe(1);
    }

    [Fact(DisplayName = "CanUserRateAppAsync should return true when user can rate")]
    public async Task CanUserRateAppAsync_ShouldReturnTrueWhenUserCanRate()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var input = new CanUserRateAppInput
        {
            DeviceId = "device-123"
        };

        var actor = Substitute.For<IGAgentActor, IUserStatisticsGAgent>();
        var agent = (IUserStatisticsGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(Arg.Any<Guid>())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        agent.CanUserRateAppAsync(Arg.Any<string>())
            .Returns(true);

        // Act
        var result = await _userStatisticsService.CanUserRateAppAsync(userId, input);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetUserStatisticsAsync should return user statistics")]
    public async Task GetUserStatisticsAsync_ShouldReturnUserStatistics()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var actor = Substitute.For<IGAgentActor, IUserStatisticsGAgent>();
        var agent = (IUserStatisticsGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(userId)
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new UserStatisticsProto
        {
            UserId = userId.ToString()
        };
        protoResponse.AppRatings.Add(new AppRatingRecordProto
        {
            Platform = "iOS",
            DeviceId = "device-123",
            FirstRatingTime = Timestamp.FromDateTime(DateTime.UtcNow),
            LastRatingTime = Timestamp.FromDateTime(DateTime.UtcNow),
            RatingCount = 1
        });
        
        agent.GetUserStatisticsAsync()
            .Returns(protoResponse);

        // Act
        var result = await _userStatisticsService.GetUserStatisticsAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.UserId.ShouldBe(userId);
        result.AppRatings.ShouldNotBeNull();
        result.AppRatings.Count.ShouldBe(1);
    }
}

