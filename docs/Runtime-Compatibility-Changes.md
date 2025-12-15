# Runtime Compatibility Changes

## Overview

Branch: `fix/eventstore-statestore-and-publisherid`

Three key fixes for Orleans runtime compatibility:

1. **EventStore/StateStore Mutual Exclusion** - Avoid duplicate storage
2. **Unified `isInternalCall` Parameter** - Consistent external/internal call handling
3. **StateDocumentConverter Optimization** - Simplified caching for CQRS projection

---

## 1. EventStore/StateStore Mutual Exclusion

### Problem
EventStore and StateStore were both active, causing duplicate storage operations.

### Solution
Make them mutually exclusive:

```csharp
if (EventStore != null)
    → Use EventStore (events + snapshots)
else if (StateStore != null)
    → Use StateStore (direct state)
```

### Files Changed
- `src/Aevatar.Agents.Core/GAgentBase.TState.cs`

---

## 2. Unified `isInternalCall` Parameter

### Problem
External API calls couldn't be distinguished from internal Agent calls, causing event handling issues.

### Solution
Add `isInternalCall` parameter to `IEventPublisher`:

| Call Source | isInternalCall | PublisherId | Agent Handles? |
|-------------|----------------|-------------|----------------|
| HTTP API | `false` | `""` | ✅ Yes |
| Agent Internal | `true` | AgentId | ❌ No (self-check) |

### Files Changed
- `src/Aevatar.Agents.Abstractions/IEventPublisher.cs`
- `src/Aevatar.Agents.Core/GAgentBase.cs`
- `src/Aevatar.Agents.Core/GAgentActorBase.cs`
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentActor.cs`

---

## 3. StateDocumentConverter Optimization

### Problem
Type resolution was slow (O(n) assembly scan every time).

### Solution
Simple caching strategy:

```csharp
// 3 caches only
private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
private static readonly ConcurrentDictionary<Type, MessageParser?> _parserCache = new();
private static readonly JsonSerializerOptions _jsonOptions = new() { ... };
```

### Verified Results (CQRS Demo)
```
Basic Types     → ES native (string, int, double, bool)
Nested Objects  → JSON string
List/Map        → JSON array/object string
Timestamp       → DateTime
```

### Files Changed
- `plugins/Aevatar.Agents.Plugins.CQRS/StateDocumentConverter.cs`

---

## Architecture

```
External API                    Agent Internal
     │                               │
     ▼                               ▼
isInternalCall=false          isInternalCall=true
     │                               │
     ▼                               ▼
PublisherId=""                PublisherId=AgentId
     │                               │
     └───────────┬───────────────────┘
                 ▼
         HandleEventAsync
                 │
    ┌────────────┴────────────┐
    ▼                         ▼
EventStore?              StateStore?
    │                         │
    ▼                         ▼
ConfirmEvents            SaveState
    │                         │
    └────────────┬────────────┘
                 ▼
         OnStateChangedAsync
                 │
                 ▼
          CQRS Projection
```

---

## Commits

| Hash | Description |
|------|-------------|
| `7fc39c4` | EventStore/StateStore mutual exclusion |
| `93ba8f3` | Unified isInternalCall parameter |
| `8cce4aa` | StateDocumentConverter simplified caching |
| `c9fdfd8` | Documentation update |

---

**Last Updated**: 2025-12-15
