using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.EventPublisher;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Xunit;

namespace Aevatar.Agents.Workflow.Tests;

/// <summary>
/// Unit tests for InputGAgent
/// </summary>
public class InputGAgentTests
{
    private readonly TestEventPublisher _eventPublisher = new();

    private InputGAgent CreateAgent()
    {
        var agent = new InputGAgent();
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        return agent;
    }

    [Fact(DisplayName = "InputGAgent should initialize with correct state")]
    public async Task InputGAgent_ShouldInitializeWithCorrectState()
    {
        // Arrange & Act
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Assert
        agent.Id.Should().NotBeNullOrEmpty();
        var state = agent.GetState();
        state.Should().NotBeNull();
        state.AgentId.Should().Be(agent.Id.ToString());
        state.Input.Should().BeEmpty();
        state.UpdateCount.Should().Be(0);
    }

    [Fact(DisplayName = "GetDescriptionAsync should return correct description")]
    public async Task GetDescriptionAsync_ShouldReturnCorrectDescription()
    {
        // Arrange
        var agent = CreateAgent();

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Input Agent");
        description.Should().Contain(agent.Id.ToString());
    }

    [Fact(DisplayName = "GetDescriptionAsync should show current input")]
    public async Task GetDescriptionAsync_ShouldShowCurrentInput()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var setInputEvent = new SetInputEvent
        {
            Input = "Test Input",
            Reason = "Test"
        };

        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Act
        var description = await agent.GetDescriptionAsync();

        // Assert
        description.Should().Contain("Test Input");
    }

    [Fact(DisplayName = "HandleSetInputEvent should update input value")]
    public async Task HandleSetInputEvent_ShouldUpdateInputValue()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var setInputEvent = new SetInputEvent
        {
            Input = "Hello World",
            Reason = "Test update"
        };

        // Act
        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Hello World");
        state.UpdateCount.Should().Be(1);
        state.LastUpdated.Should().NotBeNull();
        agent.GetInput().Should().Be("Hello World");
        agent.GetUpdateCount().Should().Be(1);
    }

    [Fact(DisplayName = "HandleSetInputEvent should increment update count")]
    public async Task HandleSetInputEvent_ShouldIncrementUpdateCount()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var setInputEvent1 = new SetInputEvent { Input = "First", Reason = "Test 1" };
        var setInputEvent2 = new SetInputEvent { Input = "Second", Reason = "Test 2" };
        var setInputEvent3 = new SetInputEvent { Input = "Third", Reason = "Test 3" };

        // Act
        await agent.HandleEventAsync(setInputEvent1.CreateEventEnvelope());
        await agent.HandleEventAsync(setInputEvent2.CreateEventEnvelope());
        await agent.HandleEventAsync(setInputEvent3.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.UpdateCount.Should().Be(3);
        state.Input.Should().Be("Third"); // Last update
        agent.GetUpdateCount().Should().Be(3);
    }

    [Fact(DisplayName = "HandleSetInputEvent should update last updated timestamp")]
    public async Task HandleSetInputEvent_ShouldUpdateLastUpdatedTimestamp()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var initialTimestamp = agent.GetState().LastUpdated;

        // Wait a bit to ensure timestamp difference
        await Task.Delay(50);

        var setInputEvent = new SetInputEvent
        {
            Input = "Updated",
            Reason = "Test"
        };

        // Act
        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.LastUpdated.Should().NotBeNull();
        state.LastUpdated.Should().NotBe(initialTimestamp);
    }

    [Fact(DisplayName = "ConfigureAsync should set input from configuration")]
    public async Task ConfigureAsync_ShouldSetInputFromConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new InputConfiguration
        {
            Input = "Configured Input",
            Description = "Configuration description"
        };

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Configured Input");
        state.UpdateCount.Should().Be(1);
        agent.GetInput().Should().Be("Configured Input");
    }

    [Fact(DisplayName = "GetInput should return current input value")]
    public async Task GetInput_ShouldReturnCurrentInputValue()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Set input through event handling
        var setInputEvent = new SetInputEvent { Input = "Current Input", Reason = "Test" };
        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Act
        var result = agent.GetInput();

        // Assert
        result.Should().Be("Current Input");
    }

    [Fact(DisplayName = "GetUpdateCount should return current update count")]
    public async Task GetUpdateCount_ShouldReturnCurrentUpdateCount()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Send 5 events to update count
        for (int i = 0; i < 5; i++)
        {
            var setInputEvent = new SetInputEvent { Input = $"Input {i}", Reason = $"Test {i}" };
            await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());
        }

        // Act
        var result = agent.GetUpdateCount();

        // Assert
        result.Should().Be(5);
    }

    [Fact(DisplayName = "HandleSetInputEvent should handle empty input")]
    public async Task HandleSetInputEvent_ShouldHandleEmptyInput()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var setInputEvent = new SetInputEvent
        {
            Input = string.Empty,
            Reason = "Clear input"
        };

        // Act
        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.Input.Should().BeEmpty();
        state.UpdateCount.Should().Be(1);
    }

    [Fact(DisplayName = "Multiple configurations should update input multiple times")]
    public async Task MultipleConfigurations_ShouldUpdateInputMultipleTimes()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config1 = new InputConfiguration { Input = "First", Description = "First config" };
        var config2 = new InputConfiguration { Input = "Second", Description = "Second config" };
        var config3 = new InputConfiguration { Input = "Third", Description = "Third config" };

        // Act
        await agent.ConfigureAsync(config1);
        await agent.ConfigureAsync(config2);
        await agent.ConfigureAsync(config3);

        // Assert
        var state = agent.GetState();
        state.Input.Should().Be("Third");
        state.UpdateCount.Should().Be(3);
        agent.GetInput().Should().Be("Third");
        agent.GetUpdateCount().Should().Be(3);
    }
}
