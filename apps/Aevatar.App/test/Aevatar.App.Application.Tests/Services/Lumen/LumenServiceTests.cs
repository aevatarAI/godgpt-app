using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Lumen.Favourite;
using Aevatar.Agents.Lumen.Feedback;
using Aevatar.Agents.Lumen.History;
using Aevatar.Agents.Lumen.Prediction;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.Agents.Lumen.UserProfile;
using Aevatar.App.Application.Contracts.BlobStorings;
using Aevatar.App.Services.Lumen;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Volo.Abp.BlobStoring;
using Xunit;

namespace Aevatar.App.Services.Lumen;

/// <summary>
/// Unit tests for LumenService
/// Tests the service layer integration with Lumen GAgents
/// </summary>
public class LumenServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<LumenService> _mockLogger;
    private readonly IBlobContainer _mockBlobContainer;
    private readonly IOptionsSnapshot<BlobStoringOptions> _mockBlobOptions;
    private readonly LumenService _lumenService;

    public LumenServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<LumenService>>();
        _mockBlobContainer = Substitute.For<IBlobContainer>();
        _mockBlobOptions = Substitute.For<IOptionsSnapshot<BlobStoringOptions>>();
        _mockBlobOptions.Value.Returns(new BlobStoringOptions());
        
        _lumenService = new LumenService(
            _mockActorFactory,
            _mockLogger,
            _mockBlobContainer,
            _mockBlobOptions);
    }

    #region Helper Methods

    private static LumenUserProfileDto CreateTestProfile(string userId = "user123")
    {
        return new LumenUserProfileDto
        {
            UserId = userId,
            FullName = "Test User",
            Gender = GenderEnum.GenderMale,
            BirthDate = new DateValue { Year = 1990, Month = 5, Day = 15 },
            BirthCity = "New York",
            LatLong = "40.7128,-74.0060",
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            ZodiacSign = "Taurus",
            ZodiacSignEnum = ZodiacSignEnum.ZodiacTaurus,
            ChineseZodiac = "Horse"
        };
    }

    private void SetupMockUserProfileAgent(string userId, LumenUserProfileDto? profile = null)
    {
        var mockActor = Substitute.For<IGAgentActor>();
        profile ??= CreateTestProfile(userId);
        
        TestHelpers.SetupRpcMock<ILumenUserProfileGAgent>(mockActor, "GetUserProfileAsync", 
            new GetUserProfileResult { Success = true, UserProfile = profile });
        
        TestHelpers.SetupRpcMock<ILumenUserProfileGAgent>(mockActor, "GetLanguageInfoAsync",
            new GetLanguageInfoResult { Success = true, CurrentLanguage = "en", RemainingChanges = 3 });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));
    }

    private void SetupMockPredictionAgent(string userId, PredictionType type = PredictionType.PredictionDaily)
    {
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenPredictionGAgent>(mockActor, "GetPredictionAsync",
            new PredictionResultDto { PredictionId = Guid.NewGuid().ToString(), Type = type });
        
        TestHelpers.SetupRpcMock<ILumenPredictionGAgent>(mockActor, "GetPredictionStatusAsync",
            new PredictionStatusDto { HasPrediction = true, IsGenerating = false });
        
        TestHelpers.SetupRpcMock<ILumenPredictionGAgent>(mockActor, "GetCalculatedValuesAsync",
            new CalculatedValuesDto());
        
        _mockActorFactory.CreateGAgentActorAsync<LumenPredictionGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));
    }

    private void SetupMockHistoryAgent(string userId)
    {
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenPredictionHistoryGAgent>(mockActor, "GetRecentPredictionsAsync",
            new GetRecentPredictionsResult { Success = true });
        
        TestHelpers.SetupRpcMock<ILumenPredictionHistoryGAgent>(mockActor, "GetPredictionByDateAsync",
            new GetPredictionByDateResult { Success = true });
        
        TestHelpers.SetupRpcMock<ILumenPredictionHistoryGAgent>(mockActor, "GetMonthlyPredictionsAsync",
            new GetMonthlyPredictionsResult { Success = true });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenPredictionHistoryGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));
    }

    private void SetupMockFeedbackAgent(string predictionId)
    {
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenFeedbackGAgent>(mockActor, "SubmitOrUpdateFeedbackAsync",
            new SubmitFeedbackResult { Success = true, FeedbackId = Guid.NewGuid().ToString() });
        
        TestHelpers.SetupRpcMock<ILumenFeedbackGAgent>(mockActor, "UpdateMethodRatingAsync",
            new UpdateMethodRatingResult { Success = true, UpdatedRating = 1 });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenFeedbackGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));
    }

    private void SetupMockFavouriteAgent(string userId)
    {
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenFavouriteGAgent>(mockActor, "ToggleFavouriteAsync",
            new ToggleFavouriteResult { Success = true, IsFavourite = true });
        
        TestHelpers.SetupRpcMock<ILumenFavouriteGAgent>(mockActor, "GetFavouritesAsync",
            new GetFavouritesResult { Success = true });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenFavouriteGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));
    }

    #endregion

    #region User Management Tests

    [Fact(DisplayName = "GetUserProfileAsync should return profile when exists")]
    public async Task GetUserProfileAsync_ShouldReturnProfile_WhenExists()
    {
        // Arrange
        var userId = "user123";
        SetupMockUserProfileAgent(userId);

        // Act
        var result = await _lumenService.GetUserProfileAsync(userId, "en");

        // Assert
        result.Success.ShouldBeTrue();
        result.UserProfile.ShouldNotBeNull();
        result.UserProfile.UserId.ShouldBe(userId);
    }

    [Fact(DisplayName = "GetUserProfileAsync should handle agent exception")]
    public async Task GetUserProfileAsync_ShouldHandleException()
    {
        // Arrange
        var userId = "user123";
        _mockActorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(Arg.Any<string>())
            .Returns<IGAgentActor>(x => throw new Exception("Test error"));

        // Act
        var result = await _lumenService.GetUserProfileAsync(userId, "en");

        // Assert
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Error");
    }

    [Fact(DisplayName = "UpdateUserProfileAsync should update profile successfully")]
    public async Task UpdateUserProfileAsync_ShouldUpdateProfileSuccessfully()
    {
        // Arrange
        var userId = "user123";
        var mockActor = Substitute.For<IGAgentActor>();
        
        var request = new UpdateUserProfileRequest
        {
            UserId = userId,
            FullName = "Updated User",
            Gender = GenderEnum.GenderMale,
            BirthDate = new DateValue { Year = 1990, Month = 5, Day = 15 }
        };
        
        TestHelpers.SetupRpcMock<ILumenUserProfileGAgent>(mockActor, "UpdateUserProfileAsync",
            new UpdateUserProfileResult { Success = true, Message = "Profile updated" });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));

        // Act
        var result = await _lumenService.UpdateUserProfileAsync(request, "en");

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "SetLanguageAsync should update language")]
    public async Task SetLanguageAsync_ShouldUpdateLanguage()
    {
        // Arrange
        var userId = "user123";
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenUserProfileGAgent>(mockActor, "SetLanguageAsync",
            new SetLanguageResult { Success = true, CurrentLanguage = "zh" });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));

        // Act
        var result = await _lumenService.SetLanguageAsync(userId, "zh");

        // Assert
        result.Success.ShouldBeTrue();
        result.CurrentLanguage.ShouldBe("zh");
    }

    #endregion

    #region Prediction Tests

    [Fact(DisplayName = "GetTodayPredictionAsync should return prediction")]
    public async Task GetTodayPredictionAsync_ShouldReturnPrediction()
    {
        // Arrange
        var userId = "user123";
        SetupMockPredictionAgent(userId);

        // Act
        var result = await _lumenService.GetTodayPredictionAsync(userId, "en");

        // Assert
        result.Success.ShouldBeTrue();
        result.Prediction.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GetPredictionStatusAsync should return status for all types")]
    public async Task GetPredictionStatusAsync_ShouldReturnStatusForAllTypes()
    {
        // Arrange
        var userId = "user123";
        SetupMockPredictionAgent(userId, PredictionType.PredictionDaily);

        // Act
        var result = await _lumenService.GetPredictionStatusAsync(userId);

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetCalculatedValuesAsync should return values")]
    public async Task GetCalculatedValuesAsync_ShouldReturnValues()
    {
        // Arrange
        var userId = "user123";
        SetupMockUserProfileAgent(userId);
        SetupMockPredictionAgent(userId);

        // Act
        var result = await _lumenService.GetCalculatedValuesAsync(userId, "en");

        // Assert
        result.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetCalculatedValuesAsync should fail when profile not found")]
    public async Task GetCalculatedValuesAsync_ShouldFail_WhenProfileNotFound()
    {
        // Arrange
        var userId = "user123";
        var mockActor = Substitute.For<IGAgentActor>();
        
        TestHelpers.SetupRpcMock<ILumenUserProfileGAgent>(mockActor, "GetUserProfileAsync",
            new GetUserProfileResult { Success = false, Message = "Profile not found" });
        
        _mockActorFactory.CreateGAgentActorAsync<LumenUserProfileGAgent>(Arg.Any<string>())
            .Returns(Task.FromResult(mockActor));

        // Act
        var result = await _lumenService.GetCalculatedValuesAsync(userId, "en");

        // Assert
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("not found");
    }

    #endregion

    #region History Tests

    [Fact(DisplayName = "GetPredictionHistoryAsync should return history")]
    public async Task GetPredictionHistoryAsync_ShouldReturnHistory()
    {
        // Arrange
        var userId = "user123";
        SetupMockHistoryAgent(userId);

        // Act
        var result = await _lumenService.GetPredictionHistoryAsync(userId);

        // Assert
        result.Success.ShouldBeTrue();
        result.Predictions.ShouldNotBeNull();
    }

    [Fact(DisplayName = "GetPredictionByDateAsync should return null when not found")]
    public async Task GetPredictionByDateAsync_ShouldReturnNull_WhenNotFound()
    {
        // Arrange
        var userId = "user123";
        SetupMockHistoryAgent(userId);

        // Act
        var result = await _lumenService.GetPredictionByDateAsync(userId, DateOnly.FromDateTime(DateTime.Today));

        // Assert
        // When prediction is null, success is false
        result.Success.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetMonthlyPredictionsAsync should return monthly predictions")]
    public async Task GetMonthlyPredictionsAsync_ShouldReturnMonthlyPredictions()
    {
        // Arrange
        var userId = "user123";
        SetupMockHistoryAgent(userId);

        // Act
        var result = await _lumenService.GetMonthlyPredictionsAsync(userId, DateOnly.FromDateTime(DateTime.Today));

        // Assert
        result.Success.ShouldBeTrue();
    }

    #endregion

    #region Feedback Tests

    [Fact(DisplayName = "SubmitFeedbackAsync should submit feedback")]
    public async Task SubmitFeedbackAsync_ShouldSubmitFeedback()
    {
        // Arrange
        var predictionId = Guid.NewGuid().ToString();
        SetupMockFeedbackAgent(predictionId);
        
        var request = new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = predictionId,
            PredictionMethod = "daily",
            Rating = 1,
            Comment = "Great prediction!"
        };

        // Act
        var result = await _lumenService.SubmitFeedbackAsync(request);

        // Assert
        result.Success.ShouldBeTrue();
        result.FeedbackId.ShouldNotBeNullOrEmpty();
    }

    [Fact(DisplayName = "UpdateMethodRatingAsync should update rating")]
    public async Task UpdateMethodRatingAsync_ShouldUpdateRating()
    {
        // Arrange
        var predictionId = Guid.NewGuid().ToString();
        SetupMockFeedbackAgent(predictionId);
        
        var request = new UpdateMethodRatingRequest
        {
            UserId = "user123",
            PredictionId = predictionId,
            PredictionMethod = "daily",
            Rating = 1
        };

        // Act
        var result = await _lumenService.UpdateMethodRatingAsync(request);

        // Assert
        result.Success.ShouldBeTrue();
    }

    #endregion

    #region Favourite Tests

    [Fact(DisplayName = "ToggleFavouriteAsync should toggle favourite")]
    public async Task ToggleFavouriteAsync_ShouldToggleFavourite()
    {
        // Arrange
        var userId = "user123";
        SetupMockFavouriteAgent(userId);
        
        var request = new ToggleFavouriteRequest
        {
            UserId = userId,
            PredictionId = Guid.NewGuid().ToString(),
            Date = new DateValue { Year = 2024, Month = 1, Day = 15 },
            IsFavourite = true
        };

        // Act
        var result = await _lumenService.ToggleFavouriteAsync(request);

        // Assert
        result.Success.ShouldBeTrue();
        result.IsFavourite.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetFavouritesAsync should return favourites")]
    public async Task GetFavouritesAsync_ShouldReturnFavourites()
    {
        // Arrange
        var userId = "user123";
        SetupMockFavouriteAgent(userId);

        // Act
        var result = await _lumenService.GetFavouritesAsync(userId);

        // Assert
        result.Success.ShouldBeTrue();
    }

    #endregion
}
