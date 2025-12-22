using System.Collections.Concurrent;

namespace Aevatar.Agents.Runtime.Orleans.EventSourcing;

/// <summary>
/// In-memory implementation of IEventRepository for development and testing.
/// Thread-safe using ConcurrentDictionary.
/// </summary>
/// <remarks>
/// This implementation is suitable for:
/// - Unit tests
/// - Integration tests
/// - Local development
/// - POC/Demo scenarios
/// 
/// NOT recommended for production use - data is lost on restart.
/// </remarks>
public class InMemoryEventRepository : IEventRepository
{
    private readonly ConcurrentDictionary<string, ConcurrentBag<AgentStateEvent>> _events = new();

    public Task<long> AppendEventsAsync(
        string agentId,
        IEnumerable<AgentStateEvent> events,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventRepository ignores agentTypeName (single in-memory store)
        var eventsList = events.ToList();
        if (!eventsList.Any()) return Task.FromResult(0L);

        var bag = _events.GetOrAdd(agentId, _ => new ConcurrentBag<AgentStateEvent>());
        foreach (var evt in eventsList)
        {
            bag.Add(evt);
        }

        return Task.FromResult(eventsList[^1].Version);
    }

    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        string agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventRepository ignores agentTypeName (single in-memory store)
        if (!_events.TryGetValue(agentId, out var events))
        {
            return Task.FromResult<IReadOnlyList<AgentStateEvent>>(Array.Empty<AgentStateEvent>());
        }

        var query = events.AsEnumerable();

        if (fromVersion.HasValue)
        {
            query = query.Where(e => e.Version >= fromVersion.Value);
        }

        if (toVersion.HasValue)
        {
            query = query.Where(e => e.Version <= toVersion.Value);
        }

        query = query.OrderBy(e => e.Version);

        if (maxCount.HasValue)
        {
            query = query.Take(maxCount.Value);
        }

        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(query.ToList());
    }

    public Task<long> GetLatestVersionAsync(
        string agentId,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventRepository ignores agentTypeName (single in-memory store)
        if (!_events.TryGetValue(agentId, out var events) || !events.Any())
        {
            return Task.FromResult(0L);
        }

        return Task.FromResult(events.Max(e => e.Version));
    }

    public Task DeleteEventsBeforeVersionAsync(
        string agentId,
        long version,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventRepository ignores agentTypeName (single in-memory store)
        if (_events.TryGetValue(agentId, out var events))
        {
            // Create a new bag without old events (ConcurrentBag doesn't support Remove)
            var newBag = new ConcurrentBag<AgentStateEvent>(
                events.Where(e => e.Version >= version)
            );
            _events.TryUpdate(agentId, newBag, events);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Clear all events for all agents (testing utility)
    /// </summary>
    public void Clear()
    {
        _events.Clear();
    }

    /// <summary>
    /// Get total event count across all agents (testing utility)
    /// </summary>
    public int GetTotalEventCount()
    {
        return _events.Values.Sum(bag => bag.Count);
    }
}

