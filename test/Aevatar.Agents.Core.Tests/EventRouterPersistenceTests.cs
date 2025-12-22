using Aevatar.Agents.Abstractions.EventRouting;
using Aevatar.Agents.Core.EventRouting;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.Core.Tests;

public class EventRouterPersistenceTests
{
    [Fact(DisplayName = "EventRouter should save hierarchy when child is added")]
    public async Task AddChildAsync_ShouldSaveHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        // Act
        await router.AddChildAsync(childId);

        // Assert
        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ChildrenIds.Should().Contain(childId);
    }

    [Fact(DisplayName = "EventRouter should save hierarchy when child is removed")]
    public async Task RemoveChildAsync_ShouldSaveHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        await router.AddChildAsync(childId);

        // Act
        await router.RemoveChildAsync(childId);

        // Assert
        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ChildrenIds.Should().NotContain(childId);
    }

    [Fact(DisplayName = "EventRouter should save hierarchy when parent is set")]
    public async Task SetParentAsync_ShouldSaveHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        // Act
        await router.SetParentAsync(parentId);

        // Assert
        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().Be(parentId);
    }

    [Fact(DisplayName = "EventRouter should save hierarchy when parent is cleared")]
    public async Task ClearParentAsync_ShouldSaveHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        await router.SetParentAsync(parentId);

        // Act
        await router.ClearParentAsync();

        // Assert
        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().BeNull();
    }

    [Fact(DisplayName = "EventRouter should load hierarchy on LoadHierarchyAsync")]
    public async Task LoadHierarchyAsync_ShouldRestoreHierarchy()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();
        var child1Id = Guid.NewGuid().ToString();
        var child2Id = Guid.NewGuid().ToString();

        // Pre-populate store
        var hierarchy = new EventRouterHierarchy
        {
            ParentId = parentId,
            ChildrenIds = new HashSet<string> { child1Id, child2Id }
        };
        await store.SaveAsync(agentId, hierarchy);

        // Create new router instance
        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        // Act
        await router.LoadHierarchyAsync();

        // Assert
        router.GetParent().Should().Be(parentId);
        router.GetChildren().Should().BeEquivalentTo(new[] { child1Id, child2Id });
    }

    [Fact(DisplayName = "EventRouter without store should work without persistence")]
    public async Task EventRouterWithoutStore_ShouldStillWork()
    {
        // Arrange
        var agentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store: null);

        // Act
        await router.AddChildAsync(childId);
        await router.LoadHierarchyAsync();

        // Assert - No exception should be thrown
        router.GetChildren().Should().Contain(childId);
    }

    [Fact(DisplayName = "Hierarchy should survive EventRouter recreation")]
    public async Task Hierarchy_ShouldSurviveRecreation()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var parentId = Guid.NewGuid().ToString();
        var childId = Guid.NewGuid().ToString();

        // Create first router instance and set up hierarchy
        var router1 = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        await router1.SetParentAsync(parentId);
        await router1.AddChildAsync(childId);

        // Act - Create new router instance and load
        var router2 = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        await router2.LoadHierarchyAsync();

        // Assert
        router2.GetParent().Should().Be(parentId);
        router2.GetChildren().Should().Contain(childId);
    }

    [Fact(DisplayName = "Complex hierarchy changes should all be persisted")]
    public async Task ComplexHierarchyChanges_ShouldAllBePersisted()
    {
        // Arrange
        var store = new InMemoryEventRouterStore();
        var agentId = Guid.NewGuid().ToString();
        var parent1 = Guid.NewGuid().ToString();
        var parent2 = Guid.NewGuid().ToString();
        var child1 = Guid.NewGuid().ToString();
        var child2 = Guid.NewGuid().ToString();
        var child3 = Guid.NewGuid().ToString();

        var router = new EventRouter(
            agentId,
            (id, env, ct) => Task.CompletedTask,
            (env, ct) => Task.CompletedTask,
            NullLogger.Instance,
            store);

        // Act - Perform multiple operations
        await router.SetParentAsync(parent1);
        await router.AddChildAsync(child1);
        await router.AddChildAsync(child2);
        await router.AddChildAsync(child3);
        await router.RemoveChildAsync(child2);
        await router.SetParentAsync(parent2);

        // Assert
        var loaded = await store.LoadAsync(agentId);
        loaded.Should().NotBeNull();
        loaded!.ParentId.Should().Be(parent2);
        loaded.ChildrenIds.Should().BeEquivalentTo(new[] { child1, child3 });
        loaded.ChildrenIds.Should().NotContain(child2);
    }
}
