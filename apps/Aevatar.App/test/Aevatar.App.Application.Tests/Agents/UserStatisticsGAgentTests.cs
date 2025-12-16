using System;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserStatistics;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

/// <summary>
/// Unit tests for UserStatisticsGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class UserStatisticsGAgentTests
{
    private UserStatisticsGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<UserStatisticsGAgent>();
        
        // Setup UserStatisticsOptions
        var userStatisticsOptions = new UserStatisticsOptions
        {
            RatingIntervalMinutes = 60
        };
        var mockOptions = new TestOptionsMonitor<UserStatisticsOptions>(userStatisticsOptions);
        agent.UserStatisticsOptions = mockOptions;
        
        return agent;
    }
    
    private class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    [Fact(DisplayName = "RecordAppRatingAsync should record rating successfully")]
    public async Task RecordAppRatingAsync_ShouldRecordRatingSuccessfully()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new RecordAppRatingRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            Platform = "iOS",
            DeviceId = "device-123"
        };

        // Act
        var result = await agent.RecordAppRatingAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Platform.ShouldBe("iOS");
        result.DeviceId.ShouldBe("device-123");
        result.RatingCount.ShouldBe(1);
        
        var state = agent.GetState();
        state.AppRatings.ShouldContainKey("device-123");
    }

    [Fact(DisplayName = "RecordAppRatingAsync should increment rating count for same device")]
    public async Task RecordAppRatingAsync_ShouldIncrementRatingCountForSameDevice()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new RecordAppRatingRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            Platform = "iOS",
            DeviceId = "device-123"
        };

        // Act - Record first rating
        await agent.RecordAppRatingAsync(request);
        
        // Act - Record second rating
        var result = await agent.RecordAppRatingAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.RatingCount.ShouldBe(2);
        
        var state = agent.GetState();
        state.AppRatings["device-123"].RatingCount.ShouldBe(2);
    }

    [Fact(DisplayName = "CanUserRateAppAsync should return true for new device")]
    public async Task CanUserRateAppAsync_ShouldReturnTrueForNewDevice()
    {
        // Arrange
        var agent = CreateAgent();
        var deviceId = "device-123";

        // Act
        var result = await agent.CanUserRateAppAsync(deviceId);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact(DisplayName = "CanUserRateAppAsync should return false within interval")]
    public async Task CanUserRateAppAsync_ShouldReturnFalseWithinInterval()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new RecordAppRatingRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            Platform = "iOS",
            DeviceId = "device-123"
        };
        
        // Record a rating
        await agent.RecordAppRatingAsync(request);

        // Act - Try to rate again immediately
        var result = await agent.CanUserRateAppAsync("device-123");

        // Assert
        result.ShouldBeFalse(); // Within 60 minute interval
    }

    [Fact(DisplayName = "GetUserStatisticsAsync should return user statistics")]
    public async Task GetUserStatisticsAsync_ShouldReturnUserStatistics()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new RecordAppRatingRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            Platform = "iOS",
            DeviceId = "device-123"
        };
        
        await agent.RecordAppRatingAsync(request);

        // Act
        var result = await agent.GetUserStatisticsAsync();

        // Assert
        result.ShouldNotBeNull();
        result.AppRatings.ShouldNotBeNull();
        result.AppRatings.Count.ShouldBe(1);
    }
}

