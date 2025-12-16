using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.GodGPT.Protos.UserQuota;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.UserQuota;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents;

/// <summary>
/// Unit tests for UserQuotaGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class UserQuotaGAgentTests
{
    private UserQuotaGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<UserQuotaGAgent>();
        
        // Setup CreditsOptions
        var creditsOptions = new CreditsOptions
        {
            InitialCreditsAmount = 320,
            CreditsPerConversation = 10,
            OperatorUserId = new List<string> { "test-operator-1" }
        };
        var mockCreditsOptions = new TestOptionsMonitor<CreditsOptions>(creditsOptions);
        agent.CreditsOptions = mockCreditsOptions;
        
        // Setup RateLimitOptions
        var rateLimitOptions = new RateLimitOptions
        {
            UserMaxRequests = 10,
            SubscribedUserMaxRequests = 100,
            UserTimeWindowSeconds = 60,
            SubscribedUserTimeWindowSeconds = 60
        };
        var mockRateLimitOptions = new TestOptionsMonitor<RateLimitOptions>(rateLimitOptions);
        agent.RateLimiterOptions = mockRateLimitOptions;
        
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

    [Fact(DisplayName = "SetShownCreditsToastAsync should set toast flag")]
    public async Task SetShownCreditsToastAsync_ShouldSetToastFlag()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new SetShownCreditsToastRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            HasShownInitialCreditsToast = true
        };

        // Act
        await agent.SetShownCreditsToastAsync(request);

        // Assert
        var state = agent.GetState();
        state.HasShownInitialCreditsToast.ShouldBeTrue();
    }

    [Fact(DisplayName = "UpdateCreditsAsync should update credits successfully")]
    public async Task UpdateCreditsAsync_ShouldUpdateCreditsSuccessfully()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.InitializeCreditsAsync(); // Initialize first
        
        var request = new UpdateCreditsRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            OperatorUserId = "test-operator-1",
            CreditsChange = 100
        };

        // Act
        var result = await agent.UpdateCreditsAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.Data.ShouldBe(420); // 320 initial + 100
        
        var state = agent.GetState();
        state.Credits.ShouldBe(420);
    }

    [Fact(DisplayName = "UpdateCreditsAsync should reject unauthorized users")]
    public async Task UpdateCreditsAsync_ShouldRejectUnauthorizedUsers()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new UpdateCreditsRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            OperatorUserId = "unauthorized-user",
            CreditsChange = 100
        };

        // Act
        var result = await agent.UpdateCreditsAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("Unauthorized");
    }

    [Fact(DisplayName = "CanUploadImageAsync should return true for subscribed user")]
    public async Task CanUploadImageAsync_ShouldReturnTrueForSubscribedUser()
    {
        // Arrange
        var agent = CreateAgent();
        
        // Set up subscription
        var subscriptionRequest = new UpdateSubscriptionRequestProto
        {
            UserId = Guid.NewGuid().ToString(),
            OperatorUserId = "test-operator-1",
            PlanType = (int)Aevatar.Application.Grains.Common.Constants.PlanType.Month,
            IsUltimate = false
        };
        await agent.UpdateSubscriptionAsync(subscriptionRequest);

        // Act
        var result = await agent.CanUploadImageAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.CanUpload.ShouldBeTrue();
    }

    [Fact(DisplayName = "CanUploadImageAsync should return true for new user")]
    public async Task CanUploadImageAsync_ShouldReturnTrueForNewUser()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var result = await agent.CanUploadImageAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        result.CanUpload.ShouldBeTrue(); // New user can upload once per day
    }
}

