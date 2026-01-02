using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.History;
using Aevatar.Agents.Lumen.Protos;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenDailyYearlyHistoryGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenDailyYearlyHistoryGAgentTests
{
    private LumenDailyYearlyHistoryGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenDailyYearlyHistoryGAgent>();
    }

    private static LanguageResults CreateLanguageResults(params (string key, string value)[] pairs)
    {
        var langResults = new LanguageResults();
        foreach (var (key, value) in pairs)
        {
            langResults.Values[key] = value;
        }
        return langResults;
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
        state.Predictions.ShouldNotBeNull();
        state.Predictions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen daily prediction yearly history");
    }

    [Fact(DisplayName = "AddOrUpdateDailyPredictionAsync should add new prediction")]
    public async Task AddOrUpdateDailyPredictionAsync_ShouldAddNewPrediction()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var multilingualResults = new Dictionary<string, LanguageResults>
        {
            ["en"] = CreateLanguageResults(("fortune", "Good luck!"))
        };

        // Act
        await agent.AddOrUpdateDailyPredictionAsync(
            "user123",
            "pred-001",
            date,
            multilingualResults,
            new[] { "en" });

        // Assert
        var state = agent.GetState();
        state.UserId.ShouldBe("user123");
        state.Year.ShouldBe(2025);
        state.Predictions.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "GetDailyPredictionAsync should return prediction for date")]
    public async Task GetDailyPredictionAsync_ShouldReturnPredictionForDate()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var results = new Dictionary<string, LanguageResults>
        {
            ["en"] = CreateLanguageResults(("fortune", "Lucky day!"))
        };
        await agent.AddOrUpdateDailyPredictionAsync("user123", "pred-001", date, results, new[] { "en" });

        // Act
        var result = await agent.GetDailyPredictionAsync(date);

        // Assert
        result.ShouldNotBeNull();
        result!.PredictionId.ShouldBe("pred-001");
    }

    [Fact(DisplayName = "GetDailyPredictionAsync should return null when date does not exist")]
    public async Task GetDailyPredictionAsync_ShouldReturnNull_WhenDateDoesNotExist()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 12, Day = 31 };

        // Act
        var result = await agent.GetDailyPredictionAsync(date);

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "GetAllDailyPredictionsAsync should return all predictions")]
    public async Task GetAllDailyPredictionsAsync_ShouldReturnAllPredictions()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        for (int i = 1; i <= 5; i++)
        {
            var date = new DateValue { Year = 2025, Month = 1, Day = i };
            var results = new Dictionary<string, LanguageResults>
            {
                ["en"] = CreateLanguageResults(("fortune", $"Fortune for day {i}"))
            };
            await agent.AddOrUpdateDailyPredictionAsync("user123", $"pred-{i:D3}", date, results, new[] { "en" });
        }

        // Act
        var result = await agent.GetAllDailyPredictionsAsync();

        // Assert
        result.Count.ShouldBe(5);
    }

    [Fact(DisplayName = "GetDailyPredictionsByRangeAsync should return predictions within range")]
    public async Task GetDailyPredictionsByRangeAsync_ShouldReturnPredictionsWithinRange()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        for (int i = 1; i <= 10; i++)
        {
            var date = new DateValue { Year = 2025, Month = 1, Day = i };
            var results = new Dictionary<string, LanguageResults>
            {
                ["en"] = CreateLanguageResults(("fortune", $"Fortune for day {i}"))
            };
            await agent.AddOrUpdateDailyPredictionAsync("user123", $"pred-{i:D3}", date, results, new[] { "en" });
        }

        var startDate = new DateValue { Year = 2025, Month = 1, Day = 3 };
        var endDate = new DateValue { Year = 2025, Month = 1, Day = 7 };

        // Act
        var result = await agent.GetDailyPredictionsByRangeAsync(startDate, endDate);

        // Assert
        result.Count.ShouldBe(5); // Days 3, 4, 5, 6, 7
    }

    [Fact(DisplayName = "ClearYearlyHistoryAsync should remove all predictions")]
    public async Task ClearYearlyHistoryAsync_ShouldRemoveAllPredictions()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var results = new Dictionary<string, LanguageResults>
        {
            ["en"] = CreateLanguageResults(("fortune", "Lucky!"))
        };
        await agent.AddOrUpdateDailyPredictionAsync("user123", "pred-001", date, results, new[] { "en" });

        // Act
        await agent.ClearYearlyHistoryAsync();

        // Assert
        var state = agent.GetState();
        state.Predictions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "Multilingual results should be stored correctly")]
    public async Task MultilingualResults_ShouldBeStoredCorrectly()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var results = new Dictionary<string, LanguageResults>
        {
            ["en"] = CreateLanguageResults(("fortune", "Good luck!")),
            ["zh"] = CreateLanguageResults(("fortune", "好运!")),
            ["ja"] = CreateLanguageResults(("fortune", "幸運!"))
        };

        // Act
        await agent.AddOrUpdateDailyPredictionAsync("user123", "pred-001", date, results, new[] { "en", "zh", "ja" });

        // Assert
        var state = agent.GetState();
        var prediction = state.Predictions["2025-01-02"];
        prediction.AvailableLanguages.Count.ShouldBe(3);
        prediction.MultilingualResults.Count.ShouldBe(3);
    }
}

