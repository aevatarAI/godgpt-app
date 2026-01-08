using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.Agents.Lumen.Stats;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenStatsSnapshotGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenStatsSnapshotGAgentTests
{
    private LumenStatsSnapshotGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenStatsSnapshotGAgent>();
    }

    [Fact(DisplayName = "Should initialize with empty state")]
    public async Task ShouldInitializeWithEmptyState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Assert
        agent.Id.ShouldNotBeNullOrEmpty();
        var state = agent.GetState();
        state.ShouldNotBeNull();
        state.GlobalStats.ShouldNotBeNull();
        state.GlobalStats.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen stats snapshot");
    }

    [Fact(DisplayName = "SnapshotAsync should store global stats")]
    public async Task SnapshotAsync_ShouldStoreGlobalStats()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var globalStats = new Dictionary<string, MethodStatsValue>
        {
            ["daily"] = new MethodStatsValue { TotalRating = 100, Count = 95, PositiveCount = 80 },
            ["yearly"] = new MethodStatsValue { TotalRating = 50, Count = 48, PositiveCount = 40 }
        };
        var userStats = new Dictionary<string, UserMethodStats>();

        // Act
        await agent.SnapshotAsync(globalStats, userStats);

        // Assert
        var state = agent.GetState();
        state.GlobalStats.Count.ShouldBe(2);
        state.GlobalStats["daily"].TotalRating.ShouldBe(100);
        state.GlobalStats["daily"].Count.ShouldBe(95);
    }

    [Fact(DisplayName = "SnapshotAsync should store user stats")]
    public async Task SnapshotAsync_ShouldStoreUserStats()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var globalStats = new Dictionary<string, MethodStatsValue>();
        var userStats = new Dictionary<string, UserMethodStats>
        {
            ["user123"] = new UserMethodStats
            {
                MethodStats =
                {
                    ["daily"] = new MethodStatsValue { TotalRating = 10, Count = 9, PositiveCount = 8 }
                }
            }
        };

        // Act
        await agent.SnapshotAsync(globalStats, userStats);

        // Assert
        var state = agent.GetState();
        state.UserStats.Count.ShouldBe(1);
        state.UserStats["user123"].MethodStats["daily"].TotalRating.ShouldBe(10);
    }

    [Fact(DisplayName = "GetSnapshotAsync should return current snapshot")]
    public async Task GetSnapshotAsync_ShouldReturnCurrentSnapshot()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var globalStats = new Dictionary<string, MethodStatsValue>
        {
            ["daily"] = new MethodStatsValue { TotalRating = 100, Count = 95 }
        };
        await agent.SnapshotAsync(globalStats, new Dictionary<string, UserMethodStats>());

        // Act
        var result = await agent.GetSnapshotAsync();

        // Assert
        result.Success.ShouldBeTrue();
        result.GlobalStats.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "Multiple snapshots should overwrite previous data")]
    public async Task MultipleSnapshots_ShouldOverwritePreviousData()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var globalStats1 = new Dictionary<string, MethodStatsValue>
        {
            ["daily"] = new MethodStatsValue { TotalRating = 100 }
        };
        var globalStats2 = new Dictionary<string, MethodStatsValue>
        {
            ["daily"] = new MethodStatsValue { TotalRating = 200 }
        };
        var userStats = new Dictionary<string, UserMethodStats>();

        // Act
        await agent.SnapshotAsync(globalStats1, userStats);
        await agent.SnapshotAsync(globalStats2, userStats);

        // Assert
        var state = agent.GetState();
        state.GlobalStats["daily"].TotalRating.ShouldBe(200);
    }
}

