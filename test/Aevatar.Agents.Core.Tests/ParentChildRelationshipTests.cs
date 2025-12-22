using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Tests.Agents;
using Aevatar.Agents.Core.Tests.Fixtures;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shouldly;
using Xunit;

namespace Aevatar.Agents.Core.Tests;

/// <summary>
/// Parent-Child Relationship Tests - Section 2.4
/// Tests for agent hierarchy and parent-child communication
/// </summary>
public class ParentChildRelationshipTests(CoreTestFixture fixture) : IClassFixture<CoreTestFixture>
{
    private readonly IServiceProvider _serviceProvider = fixture.ServiceProvider;

    [Fact(DisplayName = "Should set parent agent correctly")]
    public async Task Should_Set_Parent_Agent_Correctly()
    {
        // Arrange
        var childAgent = new ChildTestAgent();
        var parentId = Guid.NewGuid().ToString();
        AgentStateStoreInjector.InjectStateStore(childAgent, _serviceProvider);

        // Act
        await childAgent.SetParentIdAsync(parentId);

        // Assert
        childAgent.ParentId.ShouldBe(parentId);
    }

    [Fact(DisplayName = "Should add child agents")]
    public async Task Should_Add_Child_Agents()
    {
        // Arrange
        var parentAgent = new ParentTestAgent();
        var childId1 = Guid.NewGuid().ToString();
        var childId2 = Guid.NewGuid().ToString();
        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);

        // Act
        await parentAgent.AddChildAsync(childId1);
        await parentAgent.AddChildAsync(childId2);

        // Assert
        parentAgent.Children.Count.ShouldBe(2);
        parentAgent.Children.ShouldContain(childId1);
        parentAgent.Children.ShouldContain(childId2);
    }

    [Fact(DisplayName = "Should remove child agents")]
    public async Task Should_Remove_Child_Agents()
    {
        // Arrange
        var parentAgent = new ParentTestAgent();
        var childId1 = Guid.NewGuid().ToString();
        var childId2 = Guid.NewGuid().ToString();
        AgentStateStoreInjector.InjectStateStore(parentAgent, _serviceProvider);

        await parentAgent.AddChildAsync(childId1);
        await parentAgent.AddChildAsync(childId2);

        // Act
        await parentAgent.RemoveChildAsync(childId1);

        // Assert
        parentAgent.Children.Count.ShouldBe(1);
        parentAgent.Children.ShouldNotContain(childId1);
        parentAgent.Children.ShouldContain(childId2);
    }

    [Fact(DisplayName = "Should clear parent relationship")]
    public async Task Should_Clear_Parent_Relationship()
    {
        // Arrange
        var childAgent = new ChildTestAgent();
        var parentId = Guid.NewGuid().ToString();
        AgentStateStoreInjector.InjectStateStore(childAgent, _serviceProvider);

        await childAgent.SetParentIdAsync(parentId);

        // Act
        await childAgent.ClearParentAsync();

        // Assert
        childAgent.ParentId.ShouldBeNull();
    }
}