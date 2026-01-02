using System;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Feedback;
using Aevatar.Agents.Lumen.Protos;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenFeedbackGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenFeedbackGAgentTests
{
    private LumenFeedbackGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<LumenFeedbackGAgent>();
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
        state.MethodFeedbacks.ShouldNotBeNull();
        state.MethodFeedbacks.Count.ShouldBe(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Lumen feedback");
    }

    [Fact(DisplayName = "SubmitOrUpdateFeedbackAsync should add new feedback")]
    public async Task SubmitOrUpdateFeedbackAsync_ShouldAddNewFeedback()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var request = new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1,
            Comment = "Great prediction!",
            Email = "test@example.com",
            AgreeToContact = true
        };
        request.FeedbackTypes.Add("accurate");

        // Act
        var result = await agent.SubmitOrUpdateFeedbackAsync(request);

        // Assert
        result.Success.ShouldBeTrue();
        result.FeedbackId.ShouldNotBeNullOrEmpty();
        var state = agent.GetState();
        state.UserId.ShouldBe("user123");
        state.PredictionId.ShouldBe("pred-001");
        state.MethodFeedbacks.ContainsKey("daily").ShouldBeTrue();
    }

    [Fact(DisplayName = "SubmitOrUpdateFeedbackAsync should update existing feedback")]
    public async Task SubmitOrUpdateFeedbackAsync_ShouldUpdateExistingFeedback()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var request1 = new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1,
            Comment = "Initial comment"
        };

        var request2 = new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 0,
            Comment = "Updated comment"
        };

        // Act
        await agent.SubmitOrUpdateFeedbackAsync(request1);
        var result = await agent.SubmitOrUpdateFeedbackAsync(request2);

        // Assert
        result.Success.ShouldBeTrue();
        var state = agent.GetState();
        state.MethodFeedbacks["daily"].Rating.ShouldBe(0);
        state.MethodFeedbacks["daily"].Comment.ShouldBe("Updated comment");
    }

    [Fact(DisplayName = "GetFeedbackAsync should return feedback for prediction")]
    public async Task GetFeedbackAsync_ShouldReturnFeedbackForPrediction()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var request = new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1,
            Comment = "Great!"
        };
        await agent.SubmitOrUpdateFeedbackAsync(request);

        // Act
        var result = await agent.GetFeedbackAsync();

        // Assert
        result.ShouldNotBeNull();
        result!.UserId.ShouldBe("user123");
        result.MethodFeedbacks.ContainsKey("daily").ShouldBeTrue();
    }

    [Fact(DisplayName = "GetFeedbackAsync with method filter should return specific method feedback")]
    public async Task GetFeedbackAsync_WithMethodFilter_ShouldReturnSpecificMethodFeedback()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1
        });
        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "yearly",
            Rating = 0
        });

        // Act
        var result = await agent.GetFeedbackAsync("daily");

        // Assert
        result.ShouldNotBeNull();
        result!.MethodFeedbacks.Count.ShouldBe(1);
        result.MethodFeedbacks.ContainsKey("daily").ShouldBeTrue();
    }

    [Fact(DisplayName = "UpdateMethodRatingAsync should update rating for method")]
    public async Task UpdateMethodRatingAsync_ShouldUpdateRatingForMethod()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1
        });

        // Act
        var result = await agent.UpdateMethodRatingAsync(new UpdateMethodRatingRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 0
        });

        // Assert
        result.Success.ShouldBeTrue();
        result.UpdatedRating.ShouldBe(0);
        var state = agent.GetState();
        state.MethodFeedbacks["daily"].Rating.ShouldBe(0);
    }

    [Fact(DisplayName = "Multiple method feedbacks should be stored independently")]
    public async Task MultipleMethodFeedbacks_ShouldBeStoredIndependently()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "daily",
            Rating = 1
        });
        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "yearly",
            Rating = 0
        });
        await agent.SubmitOrUpdateFeedbackAsync(new SubmitFeedbackRequest
        {
            UserId = "user123",
            PredictionId = "pred-001",
            PredictionMethod = "tarot",
            Rating = 1
        });

        // Assert
        var state = agent.GetState();
        state.MethodFeedbacks.Count.ShouldBe(3);
        state.MethodFeedbacks["daily"].Rating.ShouldBe(1);
        state.MethodFeedbacks["yearly"].Rating.ShouldBe(0);
        state.MethodFeedbacks["tarot"].Rating.ShouldBe(1);
    }
}

