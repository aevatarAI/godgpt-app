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
/// Unit tests for WorkerTestGAgent
/// </summary>
public class WorkerTestGAgentTests
{
    private readonly TestEventPublisher _eventPublisher = new();

    private WorkerTestGAgent CreateAgent()
    {
        var agent = new WorkerTestGAgent();
        AgentEventPublisherInjector.InjectEventPublisher(agent, _eventPublisher);
        return agent;
    }

    [Fact(DisplayName = "WorkerTestGAgent should initialize with correct state")]
    public async Task WorkerTestGAgent_ShouldInitializeWithCorrectState()
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
        state.FailureSummary.Should().BeEmpty();
    }

    [Fact(DisplayName = "ChatAsync should return configured input when no failure summary")]
    public async Task ChatAsync_ShouldReturnConfiguredInputWhenNoFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var setInputEvent = new SetInputEvent
        {
            Input = "Hello World",
            Reason = "Test"
        };

        await agent.HandleEventAsync(setInputEvent.CreateEventEnvelope());

        // Act
        var result = await agent.ChatAsync();

        // Assert
        result.Should().Be("Hello World");
    }

    [Fact(DisplayName = "ChatAsync should throw exception when failure summary is set")]
    public async Task ChatAsync_ShouldThrowExceptionWhenFailureSummaryIsSet()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var failureEvent = new SetFailureSummaryEvent
        {
            FailureSummary = "Test failure"
        };

        await agent.HandleEventAsync(failureEvent.CreateEventEnvelope());

        // Act & Assert
        var act = async () => await agent.ChatAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test failure");
    }

    [Fact(DisplayName = "HandleSetFailureSummaryEvent should update failure summary")]
    public async Task HandleSetFailureSummaryEvent_ShouldUpdateFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var failureEvent = new SetFailureSummaryEvent
        {
            FailureSummary = "Test failure summary"
        };

        // Act
        await agent.HandleEventAsync(failureEvent.CreateEventEnvelope());

        // Assert
        var state = agent.GetState();
        state.FailureSummary.Should().Be("Test failure summary");
        agent.GetFailureSummary().Should().Be("Test failure summary");
    }

    [Fact(DisplayName = "ConfigureAsync should set failure summary from configuration")]
    public async Task ConfigureAsync_ShouldSetFailureSummaryFromConfiguration()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            FailureSummary = "Configuration failure"
        };

        // Act
        await agent.ConfigureAsync(config);

        // Assert
        var state = agent.GetState();
        state.FailureSummary.Should().Be("Configuration failure");
        agent.GetFailureSummary().Should().Be("Configuration failure");
    }

    [Fact(DisplayName = "ChatAsync should throw exception after configuring with failure summary")]
    public async Task ChatAsync_ShouldThrowExceptionAfterConfiguringWithFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            FailureSummary = "Test failure from config"
        };

        await agent.ConfigureAsync(config);

        // Act & Assert
        var act = async () => await agent.ChatAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Test failure from config");
    }

    [Fact(DisplayName = "ChatAsync should return member name format when member name is set")]
    public async Task ChatAsync_ShouldReturnMemberNameFormatWhenMemberNameIsSet()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        var config = new WorkerTestConfiguration
        {
            Input = "Test Input",
            MemberName = "TestMember"
        };

        await agent.ConfigureAsync(config);

        // Act
        var response = await agent.ChatAsync();

        // Assert
        response.Should().Be("TestMember Send the message");
    }

    [Fact(DisplayName = "GetFailureSummary should return current failure summary")]
    public async Task GetFailureSummary_ShouldReturnCurrentFailureSummary()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Set failure summary through event handling
        var failureEvent = new SetFailureSummaryEvent { FailureSummary = "Test failure" };
        await agent.HandleEventAsync(failureEvent.CreateEventEnvelope());

        // Act
        var result = agent.GetFailureSummary();

        // Assert
        result.Should().Be("Test failure");
    }

    [Fact(DisplayName = "GetMemberName should return current member name")]
    public async Task GetMemberName_ShouldReturnCurrentMemberName()
    {
        // Arrange
        var agent = CreateAgent();
        await agent.ActivateAsync();

        // Set member name through configuration
        var config = new WorkerTestConfiguration { Input = "Test", MemberName = "TestMember" };
        await agent.ConfigureAsync(config);

        // Act
        var result = agent.GetMemberName();

        // Assert
        result.Should().Be("TestMember");
    }
}
