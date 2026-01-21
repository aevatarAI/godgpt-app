using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Twitter;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Twitter;
using Aevatar.Application.Grains.Twitter.Services;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Twitter;

/// <summary>
/// Unit tests for TwitterMonitorGAgent
/// Uses TestHelpers pattern with property injection
/// </summary>
public class TwitterMonitorGAgentTests
{
    private class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private TwitterMonitorGAgent CreateAgent(ITwitterApiService? twitterApiService = null)
    {
        var agent = TestHelpers.CreateAgent<TwitterMonitorGAgent>();
        
        // Setup Options
        var options = new TwitterRewardOptions
        {
            PullIntervalMinutes = 60,
            BatchFetchSize = 100,
            DataRetentionDays = 7,
            MonitorHandle = "@testhandle",
            TimeWindowHours = 4,
            ShareLinkDomain = "https://test.com",
            ShareLinkMultiplier = 1.5,
            DailyRewardLimit = 500,
            MinViewsForReward = 20,
            ExcludedAccountIds = new List<string> { "excluded1" },
            RewardTiers = new List<TwitterRewardTierOptions>
            {
                new() { MinViews = 100, MinFollowers = 0, RewardCredits = 10 }
            }
        };
        agent.Options = new TestOptionsMonitor<TwitterRewardOptions>(options);
        agent.TwitterApiService = twitterApiService ?? Substitute.For<ITwitterApiService>();
        
        return agent;
    }

    [Fact(DisplayName = "Should initialize with correct state")]
    public async Task ShouldInitializeWithCorrectState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Assert
        var state = agent.GetState();
        state.ShouldNotBeNull();
        state.IsRunning.ShouldBeFalse();
        state.StoredTweets.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Twitter Monitor Agent");
    }

    [Fact(DisplayName = "StartMonitoringAsync should start monitoring")]
    public async Task StartMonitoringAsync_ShouldStartMonitoring()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.StartMonitoringAsync();

        // Assert
        result.ShouldBeTrue();
        agent.GetState().IsRunning.ShouldBeTrue();
    }

    [Fact(DisplayName = "StopMonitoringAsync should stop monitoring")]
    public async Task StopMonitoringAsync_ShouldStopMonitoring()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        await agent.StartMonitoringAsync();

        // Act
        var result = await agent.StopMonitoringAsync();

        // Assert
        result.ShouldBeTrue();
        agent.GetState().IsRunning.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetMonitoringStatusAsync should return current status")]
    public async Task GetMonitoringStatusAsync_ShouldReturnCurrentStatus()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        await agent.StartMonitoringAsync();

        // Act
        var status = await agent.GetMonitoringStatusAsync();

        // Assert
        status.ShouldNotBeNull();
        status.IsRunning.ShouldBeTrue();
    }

    [Fact(DisplayName = "QueryTweetsByTimeRangeAsync should return empty list when no tweets")]
    public async Task QueryTweetsByTimeRangeAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        
        var timeRange = new TimeRange
        {
            StartTimeUtcSecond = DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds(),
            EndTimeUtcSecond = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        // Act
        var result = await agent.QueryTweetsByTimeRangeAsync(timeRange);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetFetchHistoryAsync should return empty list for new agent")]
    public async Task GetFetchHistoryAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetFetchHistoryAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetMonitoringConfigAsync should return config")]
    public async Task GetMonitoringConfigAsync_ShouldReturnConfig()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var config = await agent.GetMonitoringConfigAsync();

        // Assert
        config.ShouldNotBeNull();
        config.FetchIntervalMinutes.ShouldBe(60);
        config.SearchQuery.ShouldBe("@testhandle");
    }

    [Fact(DisplayName = "FetchTweetsManuallyAsync should handle API error")]
    public async Task FetchTweetsManuallyAsync_ShouldHandleApiError()
    {
        // Arrange
        var mockApi = Substitute.For<ITwitterApiService>();
        mockApi.SearchTweetsAsync(Arg.Any<SearchTweetsRequest>())
            .Returns(new TwitterApiResult<SearchTweetsResponse>
            {
                IsSuccess = false,
                ErrorMessage = "API Error"
            });
        
        var agent = CreateAgent(mockApi);
        await agent.ActivateAsync();

        // Act
        var result = await agent.FetchTweetsManuallyAsync();

        // Assert
        result.ShouldNotBeNull();
        result.ErrorMessage.ShouldBe("API Error");
    }

    [Fact(DisplayName = "CleanupExpiredTweetsAsync should return 0 when no expired tweets")]
    public async Task CleanupExpiredTweetsAsync_ShouldReturn0()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.CleanupExpiredTweetsAsync();

        // Assert
        result.ShouldBe(0);
    }
}
