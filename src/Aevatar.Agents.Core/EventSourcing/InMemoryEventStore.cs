using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;

namespace Aevatar.Agents.Core.EventSourcing;

/// <summary>
/// In-memory event store implementation (for testing and Local runtime)
/// Thread-safe with optimistic concurrency control
/// Note: Snapshots are handled by IStateStore<TState> in GAgentBase
/// </summary>
public class InMemoryEventStore : IEventStore, IDisposable
{
    private readonly ConcurrentDictionary<Guid, List<AgentStateEvent>> _events = new();
    private readonly Channel<EventStoreOperation> _operationChannel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();

    public InMemoryEventStore()
    {
        // Create an unbounded channel for operations
        _operationChannel = Channel.CreateUnbounded<EventStoreOperation>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        // Start processing loop
        _processingTask = Task.Run(ProcessOperationsAsync);
    }

    // ========== Event Operations ==========

    public async Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventStore ignores agentTypeName (single in-memory store)
        var tcs = new TaskCompletionSource<long>(TaskCreationOptions.RunContinuationsAsynchronously);

        var operation = new AppendEventsOperation(
            agentId,
            events.ToList(),
            expectedVersion,
            tcs
        );

        await _operationChannel.Writer.WriteAsync(operation, ct);

        return await tcs.Task;
    }

    public Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventStore ignores agentTypeName (single in-memory store)
        // Read operations are safe to run concurrently with ConcurrentDictionary
        // as long as we accept that we might miss events being currently appended
        if (!_events.TryGetValue(agentId, out var eventList))
        {
            return Task.FromResult<IReadOnlyList<AgentStateEvent>>(Array.Empty<AgentStateEvent>());
        }

        // Create a snapshot of the list for querying to avoid modification during iteration
        // Note: In a high-concurrency scenario with frequent writes, this might need more robust handling
        // but for InMemory/Test purposes, this is generally sufficient.
        // For strict consistency, we could also route reads through the channel, but that would serialize reads.
        List<AgentStateEvent> snapshot;
        lock (eventList) // Minimal lock just to copy the reference/list
        {
            snapshot = eventList.ToList();
        }

        var query = snapshot.AsEnumerable();

        // Range query
        if (fromVersion.HasValue)
            query = query.Where(e => e.Version >= fromVersion.Value);

        if (toVersion.HasValue)
            query = query.Where(e => e.Version <= toVersion.Value);

        // Order by version
        query = query.OrderBy(e => e.Version);

        // Pagination
        if (maxCount.HasValue)
            query = query.Take(maxCount.Value);

        return Task.FromResult<IReadOnlyList<AgentStateEvent>>(query.ToList());
    }

    public Task<long> GetLatestVersionAsync(
        Guid agentId, 
        string? agentTypeName = null,
        CancellationToken ct = default)
    {
        // Note: InMemoryEventStore ignores agentTypeName (single in-memory store)
        if (!_events.TryGetValue(agentId, out var eventList))
        {
            return Task.FromResult(0L);
        }

        lock (eventList)
        {
            if (!eventList.Any())
                return Task.FromResult(0L);

            return Task.FromResult(eventList.Max(e => e.Version));
        }
    }

    // Note: Snapshots are handled by IStateStore<TState> in GAgentBase

    // ========== Background Processing ==========

    private async Task ProcessOperationsAsync()
    {
        try
        {
            await foreach (var operation in _operationChannel.Reader.ReadAllAsync(_cts.Token))
            {
                try
                {
                    if (operation is AppendEventsOperation appendOp)
                    {
                        ProcessAppend(appendOp);
                    }
                }
                catch (Exception ex)
                {
                    // Should not happen if individual handlers catch their exceptions, 
                    // but we must ensure the loop continues or logs critical failure.
                    Console.WriteLine($"Error processing event store operation: {ex}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
    }

    private void ProcessAppend(AppendEventsOperation op)
    {
        try
        {
            var eventList = _events.GetOrAdd(op.AgentId, _ => new List<AgentStateEvent>());

            // We lock the individual list to ensure atomic read-then-write for this specific agent
            // This is much more granular than the previous global lock
            lock (eventList)
            {
                // Optimistic concurrency check
                var currentVersion = eventList.Any() ? eventList.Max(e => e.Version) : 0;
                if (currentVersion != op.ExpectedVersion)
                {
                    op.Tcs.SetException(new InvalidOperationException(
                        $"Concurrency conflict: expected version {op.ExpectedVersion}, got {currentVersion}"));
                    return;
                }

                // Append events with incremented versions
                var newVersion = currentVersion;
                foreach (var evt in op.Events)
                {
                    evt.Version = ++newVersion;
                    eventList.Add(evt);
                }

                op.Tcs.SetResult(newVersion);
            }
        }
        catch (Exception ex)
        {
            op.Tcs.SetException(ex);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }

    // ========== Inner Types ==========

    private abstract record EventStoreOperation;

    private record AppendEventsOperation(
        Guid AgentId,
        List<AgentStateEvent> Events,
        long ExpectedVersion,
        TaskCompletionSource<long> Tcs
    ) : EventStoreOperation;
}