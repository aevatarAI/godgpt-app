# Agent Communication Patterns

This document describes the communication patterns between agents in the Aevatar Agent Framework.

## Overview

The framework supports two fundamental communication modes:

| Mode | Method | Semantics | Use Case |
|------|--------|-----------|----------|
| **Broadcast** | `PublishAsync()` | One-to-many via hierarchical streams | Group coordination, event propagation |
| **Point-to-Point** | `SendToAsync()` | One-to-one direct delivery | Task assignment, private queries |

```
┌─────────────────────────────────────────────────────────────────┐
│                    Communication Topology                        │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   Broadcast Mode:              Point-to-Point Mode:             │
│                                                                  │
│       Publisher                    Sender                        │
│          │                           │                           │
│          ▼                           │                           │
│       Stream ──────────────┐         │ Direct RPC                │
│          │                 │         │                           │
│    ┌─────┼─────┐          │         ▼                           │
│    ▼     ▼     ▼          │      Target                         │
│   C1    C2    C3          │         │                           │
│   (all subscribers        │         ▼ (optional)                │
│    receive)               │      Propagate                      │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. Broadcast Mode (Group Communication)

### API

```csharp
protected Task<string> PublishAsync<TEvent>(
    TEvent evt,
    EventDirection direction = EventDirection.Down,
    CancellationToken ct = default)
```

### Event Directions

| Direction | Flow | Description |
|-----------|------|-------------|
| `Down` | Parent → Children | Broadcast to all children via parent's stream |
| `Up` | Child → Parent → Siblings | Send to parent's stream, siblings receive via subscription |
| `Both` | Bidirectional | Combines Up and Down |

### Sequence Diagram: Down Broadcast

```
    Parent                Stream                Child1              Child2
       │                    │                     │                    │
       │─── PublishAsync ──►│                     │                    │
       │    (Direction=Down)│                     │                    │
       │                    │                     │                    │
       │                    │── OnNextAsync ─────►│                    │
       │                    │                     │── HandleEvent     │
       │                    │                     │                    │
       │                    │── OnNextAsync ──────────────────────────►│
       │                    │                                          │── HandleEvent
       │                    │                                          │
```

### Sequence Diagram: Up Broadcast

```
    Child                Parent.Stream           Parent              Sibling
       │                      │                    │                    │
       │─── PublishAsync ────►│                    │                    │
       │    (Direction=Up)    │                    │                    │
       │                      │                    │                    │
       │                      │── OnNextAsync ────►│                    │
       │                      │                    │── HandleEvent     │
       │                      │                    │                    │
       │                      │── OnNextAsync ─────────────────────────►│
       │                      │                    │                    │── HandleEvent
       │                      │                    │                    │
```

### Example: Coordinator Broadcasting Task to Workers

```csharp
public class CoordinatorAgent : GAgentBase<CoordinatorState>
{
    public async Task AssignTaskToAllWorkers(string taskDescription)
    {
        var task = new TaskAssignedEvent
        {
            TaskId = Guid.NewGuid().ToString(),
            Description = taskDescription
        };
        
        // Broadcast DOWN to all children (workers)
        await PublishAsync(task, EventDirection.Down);
    }
}
```

---

## 2. Point-to-Point Mode (Direct Communication)

### API

```csharp
protected Task<string> SendToAsync<TEvent>(
    Guid targetAgentId,
    TEvent evt,
    EventDirection onArrivalDirection = EventDirection.Unspecified,
    CancellationToken ct = default)
```

### On-Arrival Directions

| Direction | Behavior After Arrival |
|-----------|----------------------|
| `Unspecified` | Pure P2P: only target processes, no propagation |
| `Down` | Target processes, then broadcasts to its children |
| `Up` | Target processes, then propagates to its parent |
| `Both` | Target processes, then propagates in both directions |

### Sequence Diagram: Pure P2P

```
    Sender                                      Target
       │                                           │
       │─────── SendToAsync ──────────────────────►│
       │        (onArrival=Unspecified)            │
       │                                           │── HandleEvent
       │                                           │
       │                                           │   (Message stops here)
       │                                           │
```

### Example: Direct Task Query

```csharp
public class ManagerAgent : GAgentBase<ManagerState>
{
    public async Task QueryWorkerStatus(Guid workerId)
    {
        var query = new StatusQueryEvent
        {
            RequestId = Guid.NewGuid().ToString(),
            QueryType = "health_check"
        };
        
        // Pure P2P: only worker receives, no broadcast
        await SendToAsync(workerId, query, EventDirection.Unspecified);
    }
}
```

---

## 3. P2P + Group Mode (Hybrid Communication)

This mode combines point-to-point delivery with group propagation. The message is first delivered directly to a specific agent, then that agent propagates it to its group.

### Use Cases

| Pattern | onArrivalDirection | Scenario |
|---------|-------------------|----------|
| P2P → Broadcast Down | `Down` | Send task to coordinator, coordinator distributes to workers |
| P2P → Propagate Up | `Up` | Send report to node, node escalates to management chain |
| P2P → Both | `Both` | Notify a node, which then informs both upstream and downstream |

### Sequence Diagram: P2P + Group Broadcast

```
    Sender              Coordinator             Worker1             Worker2
       │                     │                    │                    │
       │── SendToAsync ─────►│                    │                    │
       │   (onArrival=Down)  │                    │                    │
       │                     │── HandleEvent     │                    │
       │                     │                    │                    │
       │                     │   (Convert to broadcast)                │
       │                     │                    │                    │
       │                     │── Stream.Publish ─►│                    │
       │                     │                    │── HandleEvent     │
       │                     │                    │                    │
       │                     │── Stream.Publish ──────────────────────►│
       │                     │                                         │── HandleEvent
       │                     │                                         │
```

### Example: Cross-Group Task Assignment

```csharp
public class ExternalSystemAgent : GAgentBase<ExternalState>
{
    /// <summary>
    /// Send a task to another group's coordinator.
    /// The coordinator will distribute it to its workers.
    /// </summary>
    public async Task AssignTaskToGroup(Guid coordinatorId, string task)
    {
        var taskEvent = new TaskAssignedEvent
        {
            TaskId = Guid.NewGuid().ToString(),
            Description = task,
            AssignedBy = Id.ToString()
        };
        
        // P2P to coordinator, coordinator broadcasts to its workers
        await SendToAsync(coordinatorId, taskEvent, EventDirection.Down);
    }
}
```

### Example: Report Escalation

```csharp
public class SensorAgent : GAgentBase<SensorState>
{
    /// <summary>
    /// Send critical alert to a specific node,
    /// which then escalates up the management chain.
    /// </summary>
    public async Task ReportCriticalAlert(Guid monitorId, string alert)
    {
        var alertEvent = new CriticalAlertEvent
        {
            AlertId = Guid.NewGuid().ToString(),
            Message = alert,
            Severity = "Critical"
        };
        
        // P2P to monitor, monitor escalates to its parent chain
        await SendToAsync(monitorId, alertEvent, EventDirection.Up);
    }
}
```

---

## 4. Architecture Comparison

### Message Flow Comparison

```
┌─────────────────────────────────────────────────────────────────────────┐
│                        Message Flow Patterns                             │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. PublishAsync (Broadcast)         2. SendToAsync (P2P)               │
│                                                                          │
│     Agent A                              Agent A                         │
│        │                                    │                            │
│        ▼                                    │                            │
│     Stream ◄─── subscribers                 │ Direct RPC                 │
│        │                                    │                            │
│   ┌────┴────┐                              ▼                            │
│   ▼         ▼                           Agent B                         │
│ Agent B  Agent C                           │                            │
│                                            X (stops)                    │
│                                                                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  3. SendToAsync + onArrival=Down     4. SendToAsync + onArrival=Up      │
│                                                                          │
│     Agent A                              Agent A                         │
│        │                                    │                            │
│        │ P2P                                │ P2P                        │
│        ▼                                    ▼                            │
│     Agent B (Coordinator)                Agent B                         │
│        │                                    │                            │
│        ▼ (broadcast)                        ▼ (escalate)                │
│     Stream                               Parent                          │
│        │                                    │                            │
│   ┌────┴────┐                              ▼                            │
│   ▼         ▼                           Grandparent                      │
│ Worker1  Worker2                                                         │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### When to Use Each Pattern

| Pattern | When to Use | Example |
|---------|-------------|---------|
| **Broadcast Down** | Notify all children | Coordinator assigns tasks to all workers |
| **Broadcast Up** | Report to parent + siblings | Worker reports completion, siblings can observe |
| **Pure P2P** | Private communication | Direct query, one-on-one negotiation |
| **P2P + Down** | Cross-group task delegation | External system assigns task to another team |
| **P2P + Up** | Report escalation | Sensor reports alert, needs to reach management |

---

## 5. Implementation Details

### EventEnvelope Fields for P2P

```protobuf
message EventEnvelope {
  // ... existing fields ...
  
  // Target agent ID for P2P (only set when using SendToAsync)
  string target_agent_id = 14;
  
  // Propagation direction after P2P message arrives at target
  EventDirection on_arrival_direction = 15;
}
```

### P2P Message Handling Flow

```csharp
// In GAgentActorBase.HandleEventAsync()

if (isPointToPoint)
{
    // 1. Process event at target
    await ProcessEventAsync(envelope, ct);
    
    // 2. Check if propagation is needed
    if (envelope.OnArrivalDirection != EventDirection.Unspecified)
    {
        // Convert P2P to broadcast and propagate
        var propagationEnvelope = envelope.Clone();
        propagationEnvelope.TargetAgentId = string.Empty;
        propagationEnvelope.Direction = envelope.OnArrivalDirection;
        
        await EventRouter.RouteEventAsync(propagationEnvelope, ct);
    }
}
```

---

## 6. Best Practices

### ✅ Do

1. **Use P2P for targeted communication** - When only one agent needs to receive
2. **Use broadcast for group coordination** - When all children need to know
3. **Use P2P + Down for cross-group delegation** - When delegating to another team
4. **Keep messages idempotent** - Handlers should be safe to replay

### ❌ Don't

1. **Don't use broadcast for private data** - All subscribers will see it
2. **Don't use P2P when you need group acknowledgment** - Use broadcast instead
3. **Don't forget to handle propagation loops** - Framework handles this via Publishers list

### Performance Considerations

| Pattern | Latency | Network Load | Scalability |
|---------|---------|--------------|-------------|
| Pure P2P | Low | Minimal | Excellent |
| Broadcast | Medium | Proportional to subscribers | Good |
| P2P + Propagate | Medium | Depends on target's group size | Good |

---

## 7. Quick Reference

```csharp
// ============================================================
// BROADCAST PATTERNS
// ============================================================

// Broadcast to all children
await PublishAsync(evt, EventDirection.Down);

// Send to parent (siblings also receive via subscription)
await PublishAsync(evt, EventDirection.Up);

// Broadcast in both directions
await PublishAsync(evt, EventDirection.Both);

// ============================================================
// POINT-TO-POINT PATTERNS
// ============================================================

// Pure P2P: only target processes
await SendToAsync(targetId, evt);
await SendToAsync(targetId, evt, EventDirection.Unspecified);

// P2P + Group: target processes, then broadcasts to its children
await SendToAsync(coordinatorId, evt, EventDirection.Down);

// P2P + Escalation: target processes, then propagates up
await SendToAsync(nodeId, evt, EventDirection.Up);

// P2P + Both: target processes, then propagates in both directions
await SendToAsync(nodeId, evt, EventDirection.Both);
```

---

## 8. Visual Summary

```
┌─────────────────────────────────────────────────────────────────────────┐
│                     Agent Communication Matrix                           │
├──────────────────┬──────────────────────────────────────────────────────┤
│                  │                   Receiver                            │
│                  ├──────────────┬───────────────┬───────────────────────┤
│                  │ Single Agent │ Agent's Group │ Agent + Parent Chain  │
├──────────────────┼──────────────┼───────────────┼───────────────────────┤
│ Direct Delivery  │ SendToAsync  │ SendToAsync   │ SendToAsync           │
│                  │ (Unspecified)│ (Down)        │ (Up)                  │
├──────────────────┼──────────────┼───────────────┼───────────────────────┤
│ Via Stream       │ N/A          │ PublishAsync  │ PublishAsync          │
│                  │              │ (Down)        │ (Up)                  │
└──────────────────┴──────────────┴───────────────┴───────────────────────┘
```
