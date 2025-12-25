using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.UserFeedback;
using Aevatar.Application.Grains.UserFeedback.Dtos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Services;

/// <summary>
/// Unit tests for UserFeedbackService
/// Tests business logic for user feedback collection
/// </summary>
public class UserFeedbackServiceTests
{
    private readonly IGAgentActorFactory _mockActorFactory;
    private readonly ILogger<UserFeedbackService> _mockLogger;
    private readonly UserFeedbackService _userFeedbackService;

    public UserFeedbackServiceTests()
    {
        _mockActorFactory = Substitute.For<IGAgentActorFactory>();
        _mockLogger = Substitute.For<ILogger<UserFeedbackService>>();
        
        _userFeedbackService = new UserFeedbackService(
            _mockActorFactory,
            _mockLogger);
    }

    [Fact(DisplayName = "SubmitFeedbackAsync should submit feedback successfully")]
    public async Task SubmitFeedbackAsync_ShouldSubmitFeedbackSuccessfully()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new SubmitFeedbackRequest
        {
            FeedbackType = "Cancel",
            Response = "Too expensive",
            ContactRequested = false,
            SkippedFeedback = false
        };
        request.Reasons.Add(Aevatar.Application.Grains.Common.Constants.FeedbackReasonEnum.TooExpensive);

        var actor = Substitute.For<IGAgentActor, IUserFeedbackGAgent>();
        var agent = (IUserFeedbackGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserFeedbackGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new SubmitFeedbackResultProto
        {
            Success = true,
            Message = "Feedback submitted successfully"
        };
        
        agent.SubmitFeedbackAsync(Arg.Any<SubmitFeedbackRequestProto>())
            .Returns(protoResponse);

        // Act
        var result = await _userFeedbackService.SubmitFeedbackAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Message.ShouldBe("Feedback submitted successfully");
    }

    [Fact(DisplayName = "CheckFeedbackEligibilityAsync should return eligibility result")]
    public async Task CheckFeedbackEligibilityAsync_ShouldReturnEligibilityResult()
    {
        // Arrange
        var userId = Guid.NewGuid();

        var actor = Substitute.For<IGAgentActor, IUserFeedbackGAgent>();
        var agent = (IUserFeedbackGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserFeedbackGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new CheckEligibilityResultProto
        {
            Eligible = true,
            Message = string.Empty
        };
        
        agent.CheckFeedbackEligibilityAsync()
            .Returns(protoResponse);

        // Act
        var result = await _userFeedbackService.CheckFeedbackEligibilityAsync(userId);

        // Assert
        result.ShouldNotBeNull();
        result.Eligible.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetFeedbackHistoryAsync should return feedback history")]
    public async Task GetFeedbackHistoryAsync_ShouldReturnFeedbackHistory()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var request = new GetFeedbackHistoryRequest
        {
            PageSize = 10,
            PageIndex = 0
        };

        var actor = Substitute.For<IGAgentActor, IUserFeedbackGAgent>();
        var agent = (IUserFeedbackGAgent)actor;
        
        _mockActorFactory.CreateGAgentActorAsync<UserFeedbackGAgent>(userId.ToString())
            .Returns(Task.FromResult((IGAgentActor)actor));
        
        var protoResponse = new GetFeedbackHistoryResultProto
        {
            TotalCount = 1,
            HasMore = false
        };
        protoResponse.Feedbacks.Add(new FeedbackHistoryItemProto
        {
            FeedbackId = Guid.NewGuid().ToString(),
            FeedbackType = "Cancel",
            Response = "Too expensive",
            SubmittedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        
        agent.GetFeedbackHistoryAsync(Arg.Any<GetFeedbackHistoryRequestProto>())
            .Returns(protoResponse);

        // Act
        var result = await _userFeedbackService.GetFeedbackHistoryAsync(userId, request);

        // Assert
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(1);
        result.Feedbacks.ShouldNotBeNull();
        result.Feedbacks.Count.ShouldBe(1);
    }
}

