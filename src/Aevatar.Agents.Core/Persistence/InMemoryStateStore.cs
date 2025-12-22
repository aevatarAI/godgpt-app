using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions.Persistence;

namespace Aevatar.Agents.Core.Persistence;

/// <summary>
/// In-memory state store implementation
/// Stores states in a concurrent dictionary for thread safety
/// </summary>
/// <typeparam name="TState">State type</typeparam>
public class InMemoryStateStore<TState> : IStateStore<TState>
    where TState : class
{
    private readonly ConcurrentDictionary<string, TState> _states = new();

    /// <summary>
    /// Load state from memory
    /// </summary>
    public Task<TState?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        _states.TryGetValue(agentId, out var state);
        return Task.FromResult(state);
    }

    /// <summary>
    /// Save state to memory
    /// </summary>
    public Task SaveAsync(string agentId, TState state, CancellationToken ct = default)
    {
        _states[agentId] = state;
        return Task.CompletedTask;
    }

    /// <summary>
    /// Delete state from memory
    /// </summary>
    public Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        _states.TryRemove(agentId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Check if state exists in memory
    /// </summary>
    public Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        return Task.FromResult(_states.ContainsKey(agentId));
    }

    /// <summary>
    /// Get all stored states (for testing/debugging)
    /// </summary>
    public IReadOnlyDictionary<string, TState> GetAllStates()
    {
        return _states;
    }

    /// <summary>
    /// Clear all states (for testing)
    /// </summary>
    public void Clear()
    {
        _states.Clear();
    }
}
