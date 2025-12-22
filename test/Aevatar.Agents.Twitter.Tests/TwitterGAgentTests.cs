using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Moq;
using Xunit;

namespace Aevatar.Agents.Twitter.Tests;

/// <summary>
/// Unit tests for TwitterGAgent
/// </summary>
public class TwitterGAgentTests
{
    private readonly Mock<ITwitterProvider> _mockTwitterProvider;

    public TwitterGAgentTests()
    {
        _mockTwitterProvider = new Mock<ITwitterProvider>();
    }

    private TwitterGAgent CreateAgent()
    {
        return new TwitterGAgent(_mockTwitterProvider.Object);
    }

    [Fact(DisplayName = "TwitterGAgent should initialize with correct state")]
    public async Task TwitterGAgent_ShouldInitializeWithCorrectState()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        await agent.ActivateAsync();

        // Assert
        agent.Id.Should().NotBeNullOrEmpty();
        var state = agent.GetState();
        state.Should().NotBeNull();
        state.AgentId.Should().Be(agent.Id.ToString());
        state.ReplyLimit.Should().Be(10); // Default value
    }

    [Fact(DisplayName = "GetDescriptionAsync should return correct description")]
    public async Task GetDescriptionAsync_ShouldReturnCorrectDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Twitter Agent");
        description.Should().Contain("Not bound");
    }

    [Fact(DisplayName = "GetDescriptionAsync should show bound status when account is bound")]
    public async Task GetDescriptionAsync_ShouldShowBoundStatusWhenAccountIsBound()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "token",
            TokenSecret = "secret"
        };

        await agent.HandleEventAsync(bindEvent.CreateEventEnvelope());

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("@testuser");
    }

    [Fact(DisplayName = "HandleBindTwitterAccountEvent should bind account")]
    public async Task HandleBindTwitterAccountEvent_ShouldBindAccount()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "oauth_token",
            TokenSecret = "oauth_secret"
        };

        // Act
        await agent.HandleEventAsync(bindEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.UserName.Should().Be("testuser");
        state.UserId.Should().Be("12345");
        state.Token.Should().Be("oauth_token");
        state.TokenSecret.Should().Be("oauth_secret");
        agent.IsAccountBound().Should().BeTrue();
        agent.GetUserName().Should().Be("testuser");
    }

    [Fact(DisplayName = "HandleUnbindTwitterAccountEvent should unbind account")]
    public async Task HandleUnbindTwitterAccountEvent_ShouldUnbindAccount()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // First bind
        var bindEvent = new BindTwitterAccountEvent
        {
            UserName = "testuser",
            UserId = "12345",
            Token = "oauth_token",
            TokenSecret = "oauth_secret"
        };

        await agent.HandleEventAsync(bindEvent.CreateEventEnvelope());

        // Then unbind
        var unbindEvent = new UnbindTwitterAccountEvent();

        // Act
        await agent.HandleEventAsync(unbindEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.UserName.Should().BeEmpty();
        state.UserId.Should().BeEmpty();
        state.Token.Should().BeEmpty();
        state.TokenSecret.Should().BeEmpty();
        agent.IsAccountBound().Should().BeFalse();
    }

    [Fact(DisplayName = "HandleTwitterConfiguration should update configuration")]
    public async Task HandleTwitterConfiguration_ShouldUpdateConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new TwitterConfiguration
        {
            ConsumerKey = "test_consumer_key",
            ConsumerSecret = "test_consumer_secret",
            BearerToken = "test_bearer_token",
            EncryptionPassword = "test_password",
            ReplyLimit = 20
        };

        // Act
        await agent.HandleEventAsync(config.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.ConsumerKey.Should().Be("test_consumer_key");
        state.ConsumerSecret.Should().Be("test_consumer_secret");
        state.BearerToken.Should().Be("test_bearer_token");
        state.EncryptionPassword.Should().Be("test_password");
        state.ReplyLimit.Should().Be(20);
    }

    [Fact(DisplayName = "ConfigureAsync should configure agent")]
    public async Task ConfigureAsync_ShouldConfigureAgent()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new TwitterConfiguration
        {
            ConsumerKey = "test_consumer_key",
            ConsumerSecret = "test_consumer_secret",
            BearerToken = "test_bearer_token",
            EncryptionPassword = "test_password",
            ReplyLimit = 15
        };

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.ConsumerKey.Should().Be("test_consumer_key");
        state.ConsumerSecret.Should().Be("test_consumer_secret");
        state.BearerToken.Should().Be("test_bearer_token");
        state.EncryptionPassword.Should().Be("test_password");
        state.ReplyLimit.Should().Be(15);
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should not create tweet when user not bound")]
    public async Task HandleCreateTweetEvent_ShouldNotCreateTweetWhenUserNotBound()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var createEvent = new CreateTweetEvent
        {
            Text = "Hello Twitter!"
        };

        // Act
        await agent.HandleEventAsync(createEvent.CreateEventEnvelope());

        // Assert
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "HandleCreateTweetEvent should not create tweet when text is empty")]
    public async Task HandleCreateTweetEvent_ShouldNotCreateTweetWhenTextIsEmpty()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var createEvent = new CreateTweetEvent
        {
            Text = string.Empty
        };

        // Act
        await agent.HandleEventAsync(createEvent.CreateEventEnvelope());

        // Assert
        _mockTwitterProvider.Verify(
            p => p.PostTwitterAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()),
            Times.Never);
    }

    [Fact(DisplayName = "HandleReceiveReplyEvent should update state")]
    public async Task HandleReceiveReplyEvent_ShouldUpdateState()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var receiveEvent = new ReceiveReplyEvent
        {
            TweetId = "tweet123",
            Text = "Received reply text"
        };

        // Act
        await agent.HandleEventAsync(receiveEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.LastUpdated.Should().NotBeNull();
    }
}
