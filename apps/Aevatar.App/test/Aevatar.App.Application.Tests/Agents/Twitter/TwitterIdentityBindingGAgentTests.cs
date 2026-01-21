using System;
using System.Threading.Tasks;
using Aevatar.Application.Grains.Twitter;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Twitter;

/// <summary>
/// Unit tests for TwitterIdentityBindingGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class TwitterIdentityBindingGAgentTests
{
    private TwitterIdentityBindingGAgent CreateAgent()
    {
        return TestHelpers.CreateAgent<TwitterIdentityBindingGAgent>();
    }

    [Fact(DisplayName = "Should initialize with empty state")]
    public async Task ShouldInitializeWithEmptyState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Assert
        var state = agent.GetState();
        state.ShouldNotBeNull();
        state.UserId.ShouldBeNullOrEmpty();
        state.TwitterUsername.ShouldBeNullOrEmpty();
    }

    [Fact(DisplayName = "GetDescriptionAsync should return description")]
    public async Task GetDescriptionAsync_ShouldReturnDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.ShouldContain("Twitter Identity Binding");
    }

    [Fact(DisplayName = "CreateOrUpdateBindingAsync should create new binding")]
    public async Task CreateOrUpdateBindingAsync_ShouldCreateNewBinding()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var userId = Guid.NewGuid();

        // Act
        var result = await agent.CreateOrUpdateBindingAsync(
            "twitter123", userId, "testuser", "https://example.com/avatar.png");

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        
        var state = agent.GetState();
        state.UserId.ShouldBe(userId.ToString());
        state.TwitterUsername.ShouldBe("testuser");
    }

    [Fact(DisplayName = "CreateOrUpdateBindingAsync should update existing binding")]
    public async Task CreateOrUpdateBindingAsync_ShouldUpdateExistingBinding()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var userId = Guid.NewGuid();
        
        await agent.CreateOrUpdateBindingAsync("twitter123", userId, "olduser", "https://old.com/avatar.png");

        // Act
        var result = await agent.CreateOrUpdateBindingAsync(
            "twitter123", userId, "newuser", "https://new.com/avatar.png");

        // Assert
        result.Success.ShouldBeTrue();
        agent.GetState().TwitterUsername.ShouldBe("newuser");
    }

    [Fact(DisplayName = "GetUserIdAsync should return null when no binding")]
    public async Task GetUserIdAsync_ShouldReturnNull_WhenNoBinding()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetUserIdAsync();

        // Assert
        result.ShouldBeNull();
    }

    [Fact(DisplayName = "GetUserIdAsync should return userId when binding exists")]
    public async Task GetUserIdAsync_ShouldReturnUserId_WhenBindingExists()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        var userId = Guid.NewGuid();
        await agent.CreateOrUpdateBindingAsync("twitter123", userId, "testuser", "https://example.com/avatar.png");

        // Act
        var result = await agent.GetUserIdAsync();

        // Assert
        result.ShouldNotBeNull();
        result.Value.ShouldBe(userId);
    }

    [Fact(DisplayName = "GetBindStatusAsync should return unbound status")]
    public async Task GetBindStatusAsync_ShouldReturnUnboundStatus()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Act
        var result = await agent.GetBindStatusAsync();

        // Assert
        result.ShouldNotBeNull();
        result.IsBound.ShouldBeFalse();
    }

    [Fact(DisplayName = "GetBindStatusAsync should return bound status")]
    public async Task GetBindStatusAsync_ShouldReturnBoundStatus()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();
        await agent.CreateOrUpdateBindingAsync("twitter123", Guid.NewGuid(), "testuser", "https://example.com/avatar.png");

        // Act
        var result = await agent.GetBindStatusAsync();

        // Assert
        result.IsBound.ShouldBeTrue();
        result.Username.ShouldBe("testuser");
    }
}
