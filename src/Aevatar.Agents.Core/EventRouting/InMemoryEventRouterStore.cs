using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.EventRouting;

namespace Aevatar.Agents.Core.EventRouting;

/// <summary>
/// In-memory EventRouter hierarchy store implementation
/// Stores hierarchies in a concurrent dictionary for thread safety
/// </summary>
public class InMemoryEventRouterStore : IEventRouterStore
{
    private readonly ConcurrentDictionary<string, EventRouterHierarchy> _hierarchies = new();

    /// <summary>
    /// Load hierarchy from memory
    /// </summary>
    public Task<EventRouterHierarchy?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        _hierarchies.TryGetValue(agentId, out var hierarchy);
        return Task.FromResult(hierarchy);
    }

    /// <summary>
    /// Save hierarchy to memory
    /// </summary>
    public Task SaveAsync(string agentId, EventRouterHierarchy hierarchy, CancellationToken ct = default)
    {
        _hierarchies[agentId] = hierarchy;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete hierarchy from memory
    /// </summary>
    public Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        _hierarchies.TryRemove(agentId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if hierarchy exists in memory
    /// </summary>
    public Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        return Task.FromResult(_hierarchies.ContainsKey(agentId));
    }

    /// <summary>
    /// Get all stored hierarchies (for testing/debugging)
    /// </summary>
    public IReadOnlyDictionary<string, EventRouterHierarchy> GetAllHierarchies()
    {
        return _hierarchies;
    }

    /// <summary>
    /// Clear all hierarchies (for testing)
    /// </summary>
    public void Clear()
    {
        _hierarchies.Clear();
    }
}
