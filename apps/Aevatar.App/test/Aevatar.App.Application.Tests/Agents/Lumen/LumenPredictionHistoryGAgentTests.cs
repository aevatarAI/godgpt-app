using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.History;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenPredictionHistoryGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenPredictionHistoryGAgentTests
{
    private LumenPredictionHistoryGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenPredictionHistoryGAgent>();
    }

    private PredictionResultDto CreatePredictionDto(
        string userId,
        string predictionId,
        DateValue date,
        PredictionType type = PredictionType.PredictionDaily)
    {
        var dto = new PredictionResultDto
        {
            PredictionId = predictionId,
            UserId = userId,
            PredictionDate = date,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Type = type,
            RequestedLanguage = "en",
            ReturnedLanguage = "en",
            FromCache = false,
            AllLanguagesGenerated = true,
            IsFallback = false
        };
        dto.AvailableLanguages.Add("en");
        dto.Results["fortune"] = "Good luck!";
        return dto;
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
        state.RecentPredictions.ShouldNotBeNull();
        state.RecentPredictions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen prediction history");
    }

    [Fact(DisplayName = "AddPredictionAsync should add prediction to history")]
    public async Task AddPredictionAsync_ShouldAddPredictionToHistory()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var prediction = CreatePredictionDto("user123", "pred-001", date);

        // Act
        await agent.AddPredictionAsync(prediction);

        // Assert
        var state = agent.GetState();
        state.UserId.ShouldBe("user123");
        state.RecentPredictions.Count.ShouldBe(1);
        state.RecentPredictions[0].PredictionId.ShouldBe("pred-001");
    }

    [Fact(DisplayName = "GetPredictionByDateAsync should return prediction for date")]
    public async Task GetPredictionByDateAsync_ShouldReturnPredictionForDate()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var prediction = CreatePredictionDto("user123", "pred-001", date);
        await agent.AddPredictionAsync(prediction);

        // Act
        var result = await agent.GetPredictionByDateAsync(date);

        // Assert
        result.ShouldNotBeNull();
        result!.PredictionId.ShouldBe("pred-001");
    }

    [Fact(DisplayName = "GetPredictionByDateAsync should return null when date does not exist")]
    public async Task GetPredictionByDateAsync_ShouldReturnNull_WhenDateDoesNotExist()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 12, Day = 31 };

        // Act
        var result = await agent.GetPredictionByDateAsync(date);

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "GetRecentPredictionsAsync should return recent predictions")]
    public async Task GetRecentPredictionsAsync_ShouldReturnRecentPredictions()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        
        // Add 3 predictions
        for (int i = 0; i < 3; i++)
        {
            var dayOffset = today.AddDays(-i);
            var date = new DateValue { Year = dayOffset.Year, Month = dayOffset.Month, Day = dayOffset.Day };
            var prediction = CreatePredictionDto("user123", $"pred-{i:D3}", date);
            await agent.AddPredictionAsync(prediction);
        }

        // Act
        var result = await agent.GetRecentPredictionsAsync(10);

        // Assert
        result.Count.ShouldBe(3);
    }

    [Fact(DisplayName = "GetMonthlyPredictionsAsync should return predictions for month")]
    public async Task GetMonthlyPredictionsAsync_ShouldReturnPredictionsForMonth()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var now = DateOnly.FromDateTime(DateTime.UtcNow);
        var thisMonth = new DateValue { Year = now.Year, Month = now.Month, Day = 15 };
        
        await agent.AddPredictionAsync(CreatePredictionDto("user123", "pred-1", thisMonth));

        var day20 = new DateValue { Year = now.Year, Month = now.Month, Day = Math.Min(20, DateTime.DaysInMonth(now.Year, now.Month)) };
        await agent.AddPredictionAsync(CreatePredictionDto("user123", "pred-2", day20));

        // Act
        var result = await agent.GetMonthlyPredictionsAsync(now.Year, now.Month);

        // Assert
        result.Count.ShouldBe(2);
    }

    [Fact(DisplayName = "ClearHistoryAsync should remove all predictions")]
    public async Task ClearHistoryAsync_ShouldRemoveAllPredictions()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var prediction = CreatePredictionDto("user123", "pred-001", date);
        await agent.AddPredictionAsync(prediction);

        // Act
        await agent.ClearHistoryAsync();

        // Assert
        var state = agent.GetState();
        state.RecentPredictions.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "AddPredictionAsync should store prediction metadata correctly")]
    public async Task AddPredictionAsync_ShouldStorePredictionMetadata_Correctly()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var date = new DateValue { Year = 2025, Month = 1, Day = 2 };
        var prediction = new PredictionResultDto
        {
            PredictionId = "pred-001",
            UserId = "user123",
            PredictionDate = date,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Type = PredictionType.PredictionYearly,
            RequestedLanguage = "zh",
            ReturnedLanguage = "en",
            FromCache = true,
            AllLanguagesGenerated = false,
            IsFallback = true
        };
        prediction.AvailableLanguages.Add("en");
        prediction.AvailableLanguages.Add("zh");
        prediction.Results["fortune"] = "Lucky year!";

        // Act
        await agent.AddPredictionAsync(prediction);

        // Assert
        var state = agent.GetState();
        var record = state.RecentPredictions[0];

        record.Type.ShouldBe(PredictionType.PredictionYearly);
        record.RequestedLanguage.ShouldBe("zh");
        record.ReturnedLanguage.ShouldBe("en");
        record.FromCache.ShouldBeTrue();
        record.AllLanguagesGenerated.ShouldBeFalse();
        record.IsFallback.ShouldBeTrue();
        record.AvailableLanguages.ShouldContain("en");
        record.AvailableLanguages.ShouldContain("zh");
        record.Results.Count.ShouldBe(1);
    }

    [Fact(DisplayName = "History should maintain newest first order")]
    public async Task History_ShouldMaintainNewestFirstOrder()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Add predictions in sequence (older first)
        for (int i = 4; i >= 0; i--)
        {
            var dayOffset = today.AddDays(-i);
            var date = new DateValue { Year = dayOffset.Year, Month = dayOffset.Month, Day = dayOffset.Day };
            var prediction = CreatePredictionDto("user123", $"pred-day-{i}", date);
            await agent.AddPredictionAsync(prediction);
        }

        // Act
        var result = await agent.GetRecentPredictionsAsync(5);

        // Assert - newest (day 0 = today) should be first
        result[0].PredictionId.ShouldBe("pred-day-0");
    }
}

