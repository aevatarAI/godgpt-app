using System;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.UserFeedback;
using Aevatar.Application.Grains.Common.Service;
using Aevatar.Application.Grains.UserFeedback;
using Aevatar.Application.Grains.UserFeedback.Options;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

/// <summary>
/// Unit tests for UserFeedbackGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class UserFeedbackGAgentTests
{
    private UserFeedbackGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<UserFeedbackGAgent>();
        
        // Setup UserFeedbackOptions
        var feedbackOptions = new UserFeedbackOptions
        {
            FeedbackFrequencyDays = 14,
            MaxResponseLength = 512
        };
        var mockFeedbackOptions = new TestOptionsMonitor<UserFeedbackOptions>(feedbackOptions);
        agent.FeedbackOptions = mockFeedbackOptions;
        
        // Setup LocalizationService
        var mockLocalizationService = Substitute.For<ILocalizationService>();
        agent.LocalizationService = mockLocalizationService;
        
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

    [Fact(DisplayName = "SubmitFeedbackAsync should submit feedback successfully")]
    public async Task SubmitFeedbackAsync_ShouldSubmitFeedbackSuccessfully()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new SubmitFeedbackRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            FeedbackType = "Cancel",
            Response = "Too expensive",
            ContactRequested = false,
            SkippedFeedback = false
        };
        request.Reasons.Add(1); // FEEDBACK_REASON_TOO_EXPENSIVE = 1

        // Act
        var result = await agent.SubmitFeedbackAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        
        var state = agent.GetState();
        state.CurrentFeedback.ShouldNotBeNull();
        state.CurrentFeedback.FeedbackType.ShouldBe("Cancel");
    }

    [Fact(DisplayName = "SubmitFeedbackAsync should handle skipped feedback")]
    public async Task SubmitFeedbackAsync_ShouldHandleSkippedFeedback()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new SubmitFeedbackRequestProto
        {
            FeedbackType = "Cancel",
            SkippedFeedback = true
        };

        // Act
        var result = await agent.SubmitFeedbackAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact(DisplayName = "CheckFeedbackEligibilityAsync should return true for new user")]
    public async Task CheckFeedbackEligibilityAsync_ShouldReturnTrueForNewUser()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var result = await agent.CheckFeedbackEligibilityAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Eligible.ShouldBeTrue();
    }

    [Fact(DisplayName = "GetFeedbackHistoryAsync should return empty history for new user")]
    public async Task GetFeedbackHistoryAsync_ShouldReturnEmptyHistoryForNewUser()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new GetFeedbackHistoryRequestProto
        {
            PageSize = 10,
            PageIndex = 0
        };

        // Act
        var result = await agent.GetFeedbackHistoryAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.TotalCount.ShouldBe(0);
        result.Feedbacks.Count.ShouldBe(0);
    }
}

