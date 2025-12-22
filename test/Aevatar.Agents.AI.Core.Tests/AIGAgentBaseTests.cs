using System.ComponentModel;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Configuration;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.Abstractions.Tests.Fixtures;
using Aevatar.Agents.AI.Abstractions.Tests.LLMProvider;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.AI.Core.Tests.TestAgents;
using Aevatar.Agents.Core.Helpers;
using FluentAssertions;
using Shouldly;

namespace Aevatar.Agents.AI.Core.Tests;

/// <summary>
/// Simplified unit tests for AIGAgentBase core functionality
/// </summary>
public class AIGAgentBaseTests(AITestFixture fixture) : IClassFixture<AITestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;
    private readonly ILLMProviderFactory _factory = fixture.LLMProviderFactory;
    private readonly IGAgentFactory _agentFactory = fixture.GAgentFactory;
    private MockLLMProvider _mockProvider => GetMockProvider();

    private MockLLMProvider GetMockProvider()
    {
        return (MockLLMProvider)fixture.LLMProviderFactory.GetProvider("test-provider");
    }

    #region Initialization Tests

    [Fact]
    [DisplayName("Initialize with provider name should configure agent correctly")]
    public async Task Initialize_WithProviderName_ShouldWork()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>(agentId);
        agent.Id.ShouldBe(agentId);

        // Act
        await agent.InitializeAsync("test-provider");

        // Assert
        agent.InitializeCallCount.Should().Be(1);
        agent.ConfigureAICallCount.Should().Be(1);
        agent.IsInitialized.Should().BeTrue();

        var aiConfig = agent.GetConfig();
        aiConfig.Should().NotBeNull();
        aiConfig.Model.Should().Be("test-model");
        aiConfig.Temperature.Should().Be(0.5f);
    }

    [Fact]
    [DisplayName("Initialize should configure embedding generator when available")]
    public async Task Initialize_ShouldConfigureEmbeddingGenerator()
    {
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        await agent.InitializeAsync("openai-provider");

        agent.HasEmbeddings.Should().BeTrue();
        var embedding = await agent.GenerateEmbeddingForTestAsync("embedding-test");
        embedding.Should().NotBeNull();
        embedding!.Vector.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    [DisplayName("Initialize with custom config should override defaults")]
    public async Task Initialize_WithCustomConfig_ShouldOverride()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        var customConfig = new LLMProviderConfig
        {
            Name = "custom-provider",
            ProviderType = "custom",
            Model = "custom-model",
            ApiKey = "custom-key",
            Temperature = 0.8,
            MaxTokens = 2000
        };

        // Act
        await agent.InitializeAsync(customConfig, config =>
        {
            config.Model = "overridden-model";
            config.Temperature = 0.9f;
        });

        // Assert
        agent.InitializeCallCount.Should().Be(1);
        var aiConfig = agent.GetConfig();
        aiConfig.Model.Should().Be("overridden-model");
        aiConfig.Temperature.Should().Be(0.9f);
    }

    [Fact]
    [DisplayName("Initialize called twice should be idempotent")]
    public async Task Initialize_CalledTwice_ShouldBeIdempotent()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        // Act
        await agent.InitializeAsync("test-provider");
        await agent.InitializeAsync("another-provider");

        // Assert
        agent.InitializeCallCount.Should().Be(2); // Both calls tracked
        agent.ConfigureAICallCount.Should().Be(1); // But config only once
    }

    [Fact]
    [DisplayName("Uninitialized agent accessing LLMProvider should throw")]
    public void UninitializedAgent_AccessingProvider_ShouldThrow()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        // Act & Assert
        var action = () => agent.LLMProvider;
        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*must be initialized*");
    }

    #endregion

    #region Chat Functionality Tests

    [Fact]
    [DisplayName("ChatAsync should return valid response")]
    public async Task ChatAsync_ShouldReturnResponse()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        await agent.InitializeAsync("test-provider");

        var request = ChatRequest.Create("Hello, AI!");

        // Act
        var response = await agent.ChatAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Content.ShouldNotBeEmpty();
        response.RequestId.ShouldBe(request.RequestId);
    }

    [Fact]
    [DisplayName("ChatStreamAsync should stream tokens")]
    public async Task ChatStreamAsync_ShouldStream()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        await agent.InitializeAsync("test-provider");

        var request = ChatRequest.Create("Stream test");
        var receivedTokens = new List<string>();

        // Act
        await foreach (var token in agent.ChatStreamAsync(request))
        {
            receivedTokens.Add(token);
        }

        // Assert
        // The mock provider returns "Test response from test-provider" by default, split into words
        receivedTokens.Should().HaveCount(4); // "Test ", "response ", "from ", "test-provider"
        string.Join("", receivedTokens).Should().Be("Test response from test-provider");
    }

    [Fact]
    [DisplayName("GenerateResponseAsync should be convenience method")]
    public async Task GenerateResponseAsync_ShouldWork()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        await agent.InitializeAsync("test-provider");

        // Act
        var response = await agent.GenerateResponseAsync("Quick test");

        // Assert
        response.Should().NotBeNull();
        response.Content.ShouldNotBeEmpty();
    }

    [Fact]
    [DisplayName("ChatAsync without initialization should throw")]
    public async Task ChatAsync_WithoutInit_ShouldThrow()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        var request = ChatRequest.Create("Test");

        // Act & Assert
        var action = async () => await agent.ChatAsync(request);
        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*must be initialized*");
    }

    #endregion

    #region Configuration Tests

    [Fact]
    [DisplayName("Custom configuration should be set correctly")]
    public async Task CustomConfig_ShouldBeSet()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();

        // Act
        await agent.InitializeAsync("test-provider");

        // Assert
        var config = agent.GetCustomConfig();
        config.ShouldNotBeNull();
        config.ConfigId.ShouldBe("test-config");
        config.MaxRetries.ShouldBe(3);
        config.TimeoutSeconds.ShouldBe(30.0);
        config.EnableLogging.ShouldBeTrue();
        config.AllowedOperations.ShouldContain("read");
        config.AllowedOperations.ShouldContain("write");
        config.CustomSettings["test-key"].ShouldBe("test-value");
    }

    [Fact]
    [DisplayName("System prompt should be customizable")]
    public async Task SystemPrompt_ShouldBeCustomizable()
    {
        // Arrange
        _mockProvider.Clear();
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        agent.TestSystemPrompt = "Custom system prompt";

        await agent.InitializeAsync("test-provider");

        _mockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "Response" });

        // Act
        await agent.ChatAsync(ChatRequest.Create("Test"));

        // Assert
        _mockProvider.CapturedRequests.ShouldNotBeEmpty();
        _mockProvider.CapturedRequests[0].SystemPrompt.ShouldBe("Custom system prompt");
    }

    [Fact]
    [DisplayName("LLM settings with request overrides should use request values")]
    public async Task LLMSettings_WithOverrides_ShouldUseRequestValues()
    {
        // Arrange
        _mockProvider.Clear();
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        await agent.InitializeAsync("test-provider");

        var request = new ChatRequest
        {
            Message = "Test",
            RequestId = "test-id",
            Temperature = 0.9,
            MaxTokens = 500
        };

        _mockProvider.EnqueueResponse(new AevatarLLMResponse { Content = "Response" });

        // Act
        await agent.ChatAsync(request);

        // Assert
        _mockProvider.CapturedRequests.ShouldNotBeEmpty();
        _mockProvider.CapturedRequests[0].Settings.ShouldNotBeNull();
        _mockProvider.CapturedRequests[0].Settings.Temperature.ShouldBe(0.9);
        _mockProvider.CapturedRequests[0].Settings.MaxTokens.ShouldBe(500);
    }

    #endregion

    #region Stream Support Tests

    [Fact]
    [DisplayName("SupportsStreamingAsync should reflect provider capability")]
    public async Task SupportsStreamingAsync_ShouldReflect()
    {
        // Arrange
        var agent = _agentFactory.CreateGAgent<TestAIGAgent>();
        await agent.InitializeAsync("test-provider");

        // Act
        var supportsStreaming = await agent.SupportsStreamingAsync();

        // Assert
        supportsStreaming.Should().BeTrue();
    }

    #endregion
}