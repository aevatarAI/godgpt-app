using System;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Prediction;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenPredictionGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenPredictionGAgentTests
{
    private LumenPredictionGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenPredictionGAgent>();
    }

    private LumenUserDto CreateTestUser(string userId = "user123")
    {
        return new LumenUserDto
        {
            UserId = userId,
            FullName = "John Doe",
            Gender = GenderEnum.GenderMale,
            BirthDate = new DateValue { Year = 1990, Month = 5, Day = 15 }
        };
    }

    #region Initialization Tests

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
        state.PredictionId.ShouldBeNullOrEmpty();
        state.UserId.ShouldBeNullOrEmpty();
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen prediction");
    }

    #endregion

    #region GetOrGeneratePredictionAsync Tests

    [Fact(DisplayName = "GetOrGeneratePredictionAsync should generate prediction for new user")]
    public async Task GetOrGeneratePredictionAsync_ShouldGeneratePrediction_ForNewUser()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();

        // Act
        var result = await agent.GetOrGeneratePredictionAsync(
            user,
            PredictionType.PredictionDaily,
            "en");

        // Assert
        result.Success.ShouldBeTrue();
        result.Prediction.ShouldNotBeNull();
        result.Prediction.PredictionId.ShouldNotBeNullOrEmpty();
        result.Prediction.Type.ShouldBe(PredictionType.PredictionDaily);
        result.IsGenerating.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetOrGeneratePredictionAsync should return existing prediction")]
    public async Task GetOrGeneratePredictionAsync_ShouldReturnExistingPrediction()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        
        // First call - generate
        var firstResult = await agent.GetOrGeneratePredictionAsync(
            user,
            PredictionType.PredictionDaily,
            "en");

        // Act - Second call should return cached
        var secondResult = await agent.GetOrGeneratePredictionAsync(
            user,
            PredictionType.PredictionDaily,
            "en");

        // Assert
        secondResult.Success.ShouldBeTrue();
        secondResult.Prediction.PredictionId.ShouldBe(firstResult.Prediction.PredictionId);
    }

    [Fact(DisplayName = "GetOrGeneratePredictionAsync should generate for different type")]
    public async Task GetOrGeneratePredictionAsync_ShouldGenerateForDifferentType()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        
        // Generate daily
        await agent.GetOrGeneratePredictionAsync(
            user,
            PredictionType.PredictionDaily,
            "en");

        // Act - Generate yearly (different type)
        var yearlyResult = await agent.GetOrGeneratePredictionAsync(
            user,
            PredictionType.PredictionYearly,
            "en");

        // Assert
        yearlyResult.Success.ShouldBeTrue();
        yearlyResult.Prediction.Type.ShouldBe(PredictionType.PredictionYearly);
    }

    #endregion

    #region GetPredictionAsync Tests

    [Fact(DisplayName = "GetPredictionAsync should return null when no prediction")]
    public async Task GetPredictionAsync_ShouldReturnNull_WhenNoPrediction()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetPredictionAsync();

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "GetPredictionAsync should return prediction when exists")]
    public async Task GetPredictionAsync_ShouldReturnPrediction_WhenExists()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        var result = await agent.GetPredictionAsync();

        // Assert
        result.ShouldNotBeNull();
        result.PredictionId.ShouldNotBeNullOrEmpty();
    }

    #endregion

    #region GetPredictionStatusAsync Tests

    [Fact(DisplayName = "GetPredictionStatusAsync should return no prediction status when empty")]
    public async Task GetPredictionStatusAsync_ShouldReturnNoPredictionStatus_WhenEmpty()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetPredictionStatusAsync();

        // Assert
        result.ShouldNotBeNull();
        result.HasPrediction.ShouldBeFalse();
        result.IsGenerating.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetPredictionStatusAsync should return prediction status when exists")]
    public async Task GetPredictionStatusAsync_ShouldReturnPredictionStatus_WhenExists()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        var result = await agent.GetPredictionStatusAsync();

        // Assert
        result.ShouldNotBeNull();
        result.HasPrediction.ShouldBeTrue();
        result.IsGenerating.ShouldBeFalse();
        result.AvailableLanguages.ShouldContain("en");
    }

    #endregion

    #region ClearCurrentPredictionAsync Tests

    [Fact(DisplayName = "ClearCurrentPredictionAsync should clear prediction")]
    public async Task ClearCurrentPredictionAsync_ShouldClearPrediction()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        await agent.ClearCurrentPredictionAsync();

        // Assert
        var state = agent.GetState();
        state.PredictionId.ShouldBeNullOrEmpty();
        state.Results.Count.ShouldBe(0);
    }

    #endregion

    #region GetCalculatedValuesAsync Tests

    [Fact(DisplayName = "GetCalculatedValuesAsync should return zodiac values")]
    public async Task GetCalculatedValuesAsync_ShouldReturnZodiacValues()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        // May 15 is Taurus
        user.BirthDate = new DateValue { Year = 1990, Month = 5, Day = 15 };

        // Act
        var result = await agent.GetCalculatedValuesAsync(user);

        // Assert
        result.Values.ShouldContainKey("zodiacSign");
        result.Values["zodiacSign"].ShouldBe("Taurus");
    }

    [Fact(DisplayName = "GetCalculatedValuesAsync should return Chinese zodiac")]
    public async Task GetCalculatedValuesAsync_ShouldReturnChineseZodiac()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        // 1990 is Year of the Horse
        user.BirthDate = new DateValue { Year = 1990, Month = 5, Day = 15 };

        // Act
        var result = await agent.GetCalculatedValuesAsync(user);

        // Assert
        result.Values.ShouldContainKey("chineseZodiac");
        result.Values["chineseZodiac"].ShouldBe("Horse");
    }

    #endregion

    #region TriggerTranslationAsync Tests

    [Fact(DisplayName = "TriggerTranslationAsync should return already available for existing language")]
    public async Task TriggerTranslationAsync_ShouldReturnAlreadyAvailable_ForExistingLanguage()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        var result = await agent.TriggerTranslationAsync(user, "en");

        // Assert
        result.Success.ShouldBeTrue();
        result.Message.ShouldContain("already available");
    }

    [Fact(DisplayName = "TriggerTranslationAsync should trigger translation for new language")]
    public async Task TriggerTranslationAsync_ShouldTriggerTranslation_ForNewLanguage()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        var result = await agent.TriggerTranslationAsync(user, "zh");

        // Assert
        result.Success.ShouldBeTrue();
    }

    #endregion

    #region UpdateUserActivityAsync Tests

    [Fact(DisplayName = "UpdateUserActivityAsync should update activity timestamp")]
    public async Task UpdateUserActivityAsync_ShouldUpdateActivityTimestamp()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        await agent.UpdateUserActivityAsync("America/New_York");

        // Assert
        var state = agent.GetState();
        state.LastActiveDate.ShouldNotBeNull();
    }

    #endregion

    #region Event Handler Tests

    [Fact(DisplayName = "HandlePredictionGenerated should update state correctly")]
    public async Task HandlePredictionGenerated_ShouldUpdateStateCorrectly()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();

        // Act
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Assert
        var state = agent.GetState();
        state.PredictionId.ShouldNotBeNullOrEmpty();
        state.UserId.ShouldBe("user123");
        state.Type.ShouldBe(PredictionType.PredictionDaily);
        state.GeneratedLanguages.ShouldContain("en");
        state.Results.Count.ShouldBeGreaterThan(0);
    }

    [Fact(DisplayName = "HandlePredictionCleared should clear all state")]
    public async Task HandlePredictionCleared_ShouldClearAllState()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        await agent.GetOrGeneratePredictionAsync(user, PredictionType.PredictionDaily, "en");

        // Act
        await agent.ClearCurrentPredictionAsync();

        // Assert
        var state = agent.GetState();
        state.PredictionId.ShouldBeNullOrEmpty();
        state.Results.Count.ShouldBe(0);
        state.GeneratedLanguages.Count.ShouldBe(0);
        state.GenerationLocks.Count.ShouldBe(0);
    }

    #endregion

    #region Zodiac Calculation Tests

    [Theory(DisplayName = "Should calculate correct zodiac signs")]
    [InlineData(3, 21, "Aries")]
    [InlineData(4, 19, "Aries")]
    [InlineData(4, 20, "Taurus")]
    [InlineData(5, 20, "Taurus")]
    [InlineData(5, 21, "Gemini")]
    [InlineData(6, 20, "Gemini")]
    [InlineData(6, 21, "Cancer")]
    [InlineData(7, 22, "Cancer")]
    [InlineData(7, 23, "Leo")]
    [InlineData(8, 22, "Leo")]
    [InlineData(8, 23, "Virgo")]
    [InlineData(9, 22, "Virgo")]
    [InlineData(9, 23, "Libra")]
    [InlineData(10, 22, "Libra")]
    [InlineData(10, 23, "Scorpio")]
    [InlineData(11, 21, "Scorpio")]
    [InlineData(11, 22, "Sagittarius")]
    [InlineData(12, 21, "Sagittarius")]
    [InlineData(12, 22, "Capricorn")]
    [InlineData(1, 19, "Capricorn")]
    [InlineData(1, 20, "Aquarius")]
    [InlineData(2, 18, "Aquarius")]
    [InlineData(2, 19, "Pisces")]
    [InlineData(3, 20, "Pisces")]
    public async Task ShouldCalculateCorrectZodiacSigns(int month, int day, string expectedZodiac)
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        user.BirthDate = new DateValue { Year = 1990, Month = month, Day = day };

        // Act
        var result = await agent.GetCalculatedValuesAsync(user);

        // Assert
        result.Values["zodiacSign"].ShouldBe(expectedZodiac);
    }

    [Theory(DisplayName = "Should calculate correct Chinese zodiac")]
    [InlineData(1984, "Rat")]
    [InlineData(1985, "Ox")]
    [InlineData(1986, "Tiger")]
    [InlineData(1987, "Rabbit")]
    [InlineData(1988, "Dragon")]
    [InlineData(1989, "Snake")]
    [InlineData(1990, "Horse")]
    [InlineData(1991, "Goat")]
    [InlineData(1992, "Monkey")]
    [InlineData(1993, "Rooster")]
    [InlineData(1994, "Dog")]
    [InlineData(1995, "Pig")]
    public async Task ShouldCalculateCorrectChineseZodiac(int year, string expectedZodiac)
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var user = CreateTestUser();
        user.BirthDate = new DateValue { Year = year, Month = 5, Day = 15 };

        // Act
        var result = await agent.GetCalculatedValuesAsync(user);

        // Assert
        result.Values["chineseZodiac"].ShouldBe(expectedZodiac);
    }

    #endregion
}

