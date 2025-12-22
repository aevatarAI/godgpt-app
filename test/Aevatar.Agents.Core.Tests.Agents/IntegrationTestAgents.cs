using System.Reflection;
using Aevatar.Agents.Abstractions.Attributes;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Core.Tests.Agents;

#region Lifecycle Test Agent

/// <summary>
/// Agent for testing complete lifecycle
/// </summary>
public class LifecycleTestAgent : GAgentBase<LifecycleAgentState>
{
    public bool IsActivated { get; private set; }
    public bool IsDeactivated { get; private set; }
    public DateTime? ActivationTime { get; private set; }
    public DateTime? DeactivationTime { get; private set; }
    public int ProcessedEventCount { get; private set; }
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        IsActivated = true;
        ActivationTime = DateTime.UtcNow;
        State.Status = "active";
        State.ActivatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnActivateAsync(ct);
    }
    
    protected override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        IsDeactivated = true;
        DeactivationTime = DateTime.UtcNow;
        State.Status = "inactive";
        State.DeactivatedAt = Timestamp.FromDateTime(DateTime.UtcNow);
        return base.OnDeactivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        ProcessedEventCount++;
        State.EventHistory.Add(evt.EventId);
        State.LastProcessedTime = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"LifecycleAgent: {State.Status} (Processed: {ProcessedEventCount})";
    }
}

#endregion

#region Collaboration Test Agents

/// <summary>
/// Coordinator agent that assigns tasks
/// </summary>
public class CoordinatorAgent : GAgentBase<CoordinatorState>
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.CoordinatorId = Id.ToString();
        State.IsActive = true;
        return base.OnActivateAsync(ct);
    }
    
    public async Task AssignTask(TaskAssignedEvent task)
    {
        State.AssignedTasks.Add(task.TaskId);
        await PublishAsync(task, EventDirection.Down);
    }
    
    public override string GetDescription()
    {
        return $"Coordinator: {State.CoordinatorId} (Tasks: {State.AssignedTasks.Count})";
    }
}

/// <summary>
/// Worker agent that processes tasks
/// </summary>
public class WorkerAgent : GAgentBase<WorkerState>
{
    public string WorkerId { get; set; } = string.Empty;
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.WorkerId = WorkerId;
        State.IsAvailable = true;
        return base.OnActivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTaskAssignment(TaskAssignedEvent evt)
    {
        if (evt.AssignedTo == WorkerId)
        {
            State.ReceivedTasks.Add(evt.TaskId);
            State.IsAvailable = false;
            await Task.CompletedTask;
        }
    }
    
    public async Task CompleteTask(TaskCompletedEvent completion)
    {
        State.CompletedTasks.Add(completion.TaskId);
        State.IsAvailable = true;
        await PublishAsync(completion, EventDirection.Up);
    }
    
    public override string GetDescription()
    {
        return $"Worker: {WorkerId} (Completed: {State.CompletedTasks.Count})";
    }
}

/// <summary>
/// Aggregator agent that collects results
/// </summary>
public class AggregatorAgent : GAgentBase<AggregatorState>
{
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        State.AggregatorId = Id.ToString();
        return base.OnActivateAsync(ct);
    }
    
    [EventHandler]
    public async Task HandleTaskCompleted(TaskCompletedEvent evt)
    {
        State.CollectedResults.Add($"{evt.TaskId}:{evt.Result}");
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"Aggregator: {State.AggregatorId} (Results: {State.CollectedResults.Count})";
    }
}

#endregion

#region Tree Structure Test Agents

/// <summary>
/// Tree node agent for hierarchical event propagation
/// Demonstrates proper event-driven state management
/// </summary>
public class TreeNodeAgent : GAgentBase<TreeNodeState>
{
    public string NodeName { get; set; } = string.Empty;
    
    protected override Task OnActivateAsync(CancellationToken ct = default)
    {
        // State initialization is allowed in OnActivateAsync
        // because ActivateAsync wraps it in InitializationScope
        State.NodeName = NodeName;
        State.Level = 0; // Will be set based on tree position
        return base.OnActivateAsync(ct);
    }
    
    // Test helper method to setup tree structure
    // This uses reflection to invoke the handler with proper context
    public async Task SetupTreeNodeForTesting(string? parentName = null, params string[] childNames)
    {
        if (parentName != null)
        {
            var parentEvt = new SetParentEvent { ParentName = parentName };
            var parentHandler = GetType().GetMethod(nameof(HandleSetParent), BindingFlags.Public | BindingFlags.Instance);
            if (parentHandler == null)
            {
                throw new InvalidOperationException($"Could not find method {nameof(HandleSetParent)}");
            }
            await InvokeHandler(parentHandler, parentEvt, CancellationToken.None);
        }
        
        foreach (var childName in childNames)
        {
            var childEvt = new AddChildEvent { ChildName = childName };
            var childHandler = GetType().GetMethod(nameof(HandleAddChild), BindingFlags.Public | BindingFlags.Instance);
            if (childHandler == null)
            {
                throw new InvalidOperationException($"Could not find method {nameof(HandleAddChild)}");
            }
            await InvokeHandler(childHandler, childEvt, CancellationToken.None);
        }
    }
    
    public async Task BroadcastDown(TreeBroadcastEvent evt)
    {
        // Only publish the broadcast - no direct state modification
        await PublishAsync(evt, EventDirection.Down);
    }
    
    public async Task BroadcastUp(TreeBroadcastEvent evt)
    {
        // Only publish the broadcast - no direct state modification
        await PublishAsync(evt, EventDirection.Up);
    }
    
    // Event handlers that modify State - proper way to change state
    [EventHandler(AllowSelfHandling = true)] // Allow handling our own events
    public async Task HandleSetParent(SetParentEvent evt)
    {
        State.ParentNode = evt.ParentName;
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)] // Allow handling our own events
    public async Task HandleAddChild(AddChildEvent evt)
    {
        if (!State.ChildNodes.Contains(evt.ChildName))
        {
            State.ChildNodes.Add(evt.ChildName);
        }
        await Task.CompletedTask;
    }
    
    [EventHandler(AllowSelfHandling = true)] // Track our own broadcasts too
    public async Task HandleBroadcast(TreeBroadcastEvent evt)
    {
        State.ReceivedBroadcasts.Add($"{evt.OriginNode}:{evt.Message}");
        
        // Propagate if needed
        if (evt.Direction == "down" && State.ChildNodes.Count > 0)
        {
            var propagatedEvent = new TreeBroadcastEvent
            {
                Message = evt.Message,
                OriginNode = NodeName,
                Direction = "down"
            };
            await BroadcastDown(propagatedEvent);
        }
        
        await Task.CompletedTask;
    }
    
    public override string GetDescription()
    {
        return $"TreeNode: {NodeName} (Children: {State.ChildNodes.Count})";
    }
}

#endregion

#region Stateful Test Agent

/// <summary>
/// Stateful agent for state recovery testing
/// </summary>
public class StatefulAgent(string agentId) : GAgentBase<TestAgentState>(agentId)
{
    [EventHandler]
    public async Task HandleTestEvent(TestEvent evt)
    {
        State.Counter++;
        State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        await Task.CompletedTask;
    }
    
    // Test helper method to setup state (invokes during OnActivateAsync for proper context)
    public void SetupStateForTesting(string name, int counter, 
        IEnumerable<string>? items = null, 
        Dictionary<string, string>? metadata = null)
    {
        _setupName = name;
        _setupCounter = counter;
        _setupItems = items?.ToList();
        _setupMetadata = metadata;
    }
    
    private string? _setupName;
    private int? _setupCounter;
    private List<string>? _setupItems;
    private Dictionary<string, string>? _setupMetadata;
    private TestAgentState? _restoreState;
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Apply setup values if provided (for testing)
        if (_setupName != null)
        {
            State.Name = _setupName;
            State.Counter = _setupCounter ?? 0;
            if (_setupItems != null)
            {
                State.Items.AddRange(_setupItems);
            }
            if (_setupMetadata != null)
            {
                foreach (var kvp in _setupMetadata)
                {
                    State.Metadata[kvp.Key] = kvp.Value;
                }
            }
            State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }
        // Or restore from saved state (for state recovery test)
        else if (_restoreState != null)
        {
            State.Name = _restoreState.Name;
            State.Counter = _restoreState.Counter;
            State.Items.Clear();
            State.Items.AddRange(_restoreState.Items);
            State.Metadata.Clear();
            foreach (var kvp in _restoreState.Metadata)
            {
                State.Metadata[kvp.Key] = kvp.Value;
            }
            State.LastUpdated = _restoreState.LastUpdated;
        }
        else if (string.IsNullOrEmpty(State.Name))
        {
            State.Name = "NewStatefulAgent";
            State.Counter = 0;
            State.LastUpdated = Timestamp.FromDateTime(DateTime.UtcNow);
        }
        
        await base.OnActivateAsync(ct);
    }
    
    public void RestoreState(TestAgentState savedState)
    {
        // Save the state to be restored during OnActivateAsync
        _restoreState = savedState;
    }

    public override string GetDescription()
    {
        return $"StatefulAgent: {State.Name} (Counter: {State.Counter})";
    }
}

#endregion
