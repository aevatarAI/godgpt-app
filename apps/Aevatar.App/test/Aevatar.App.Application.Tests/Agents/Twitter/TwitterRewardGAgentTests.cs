using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
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
/// Unit tests for TwitterRewardGAgent
/// Uses TestHelpers pattern with property injection
/// </summary>
public class TwitterRewardGAgentTests
{
    private class TestOptionsMonitor<T> : IOptionsMonitor<T> where T : class
    {
        private readonly T _value;
        public TestOptionsMonitor(T value) => _value = value;
        public T CurrentValue => _value;
        public T Get(string? name) => _value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private TwitterRewardGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<TwitterRewardGAgent>();
        
        // Setup Options
        var options = new TwitterRewardOptions
        {
            PullIntervalMinutes = 60,
            ShareLinkDomain = "https://test.com",
            ShareLinkMultiplier = 1.5,
            DailyRewardLimit = 500,
            MinViewsForReward = 20,
            TimeWindowMinutes = 240,
            RewardTiers = new List<TwitterRewardTierOptions>
            {
                new() { MinViews = 100, MinFollowers = 0, RewardCredits = 10 },
                new() { MinViews = 500, MinFollowers = 100, RewardCredits = 20 }
            }
        };
        agent.Options = new TestOptionsMonitor<TwitterRewardOptions>(options);
        agent.TwitterApiService = Substitute.For<ITwitterApiService>();
        agent.AgentFactory = Substitute.For<IGAgentActorFactory>();
        
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
        state.TotalUsersRewarded.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Twitter Reward Agent");
    }

    [Fact(DisplayName = "StartRewardCalculationAsync should start calculation")]
    public async Task StartRewardCalculationAsync_ShouldStartCalculation()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.StartRewardCalculationAsync();

        // Assert
        result.ShouldBeTrue();
        agent.GetState().IsRunning.ShouldBeTrue();
    }

    [Fact(DisplayName = "StopRewardCalculationAsync should stop calculation")]
    public async Task StopRewardCalculationAsync_ShouldStopCalculation()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        await agent.StartRewardCalculationAsync();

        // Act
        var result = await agent.StopRewardCalculationAsync();

        // Assert
        result.ShouldBeTrue();
        agent.GetState().IsRunning.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetRewardCalculationStatusAsync should return current status")]
    public async Task GetRewardCalculationStatusAsync_ShouldReturnStatus()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        await agent.StartRewardCalculationAsync();

        // Act
        var status = await agent.GetRewardCalculationStatusAsync();

        // Assert
        status.ShouldNotBeNull();
        status.IsRunning.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetRewardCalculationHistoryAsync should return empty list")]
    public async Task GetRewardCalculationHistoryAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetRewardCalculationHistoryAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetUserRewardRecordsAsync should return empty list")]
    public async Task GetUserRewardRecordsAsync_ShouldReturnEmptyList()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetUserRewardRecordsAsync("user123");

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "HasUserReceivedDailyRewardAsync should return false")]
    public async Task HasUserReceivedDailyRewardAsync_ShouldReturnFalse()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.HasUserReceivedDailyRewardAsync("user123", DateTime.UtcNow);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetRewardConfigAsync should return config")]
    public async Task GetRewardConfigAsync_ShouldReturnConfig()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var config = await agent.GetRewardConfigAsync();

        // Assert
        config.ShouldNotBeNull();
        config.ShareLinkMultiplier.ShouldBe(1.5);
        config.MaxDailyCreditsPerUser.ShouldBe(500);
    }

    [Fact(DisplayName = "UpdateRewardConfigAsync should update config")]
    public async Task UpdateRewardConfigAsync_ShouldUpdateConfig()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var newConfig = new RewardConfig
        {
            ShareLinkMultiplier = 3.0,
            MaxDailyCreditsPerUser = 2000,
            MinViewsForReward = 50,
            EnableRewardCalculation = true
        };

        // Act
        var result = await agent.UpdateRewardConfigAsync(newConfig);

        // Assert
        result.ShouldBeTrue();
        agent.GetState().Config.ShareLinkMultiplier.ShouldBe(3.0);
    }

    [Fact(DisplayName = "GetTimeControlStatusAsync should return status")]
    public async Task GetTimeControlStatusAsync_ShouldReturnStatus()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var status = await agent.GetTimeControlStatusAsync();

        // Assert
        status.ShouldNotBeNull();
        status.CurrentUtcTime.ShouldNotBeNull();
    }

    [Fact(DisplayName = "TriggerRewardCalculationAsync should return false when disabled")]
    public async Task TriggerRewardCalculationAsync_ShouldReturnFalse_WhenDisabled()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        
        await agent.UpdateRewardConfigAsync(new RewardConfig { EnableRewardCalculation = false });

        // Act
        var result = await agent.TriggerRewardCalculationAsync(DateTime.UtcNow.AddDays(-1));

        // Assert
        result.ShouldBeFalse();
    }
}
