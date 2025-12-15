# Runtime Compatibility Changes: Local to Orleans Migration

## Overview

This document summarizes the code changes made to support Orleans runtime compatibility while maintaining Local runtime support. The changes address two main areas:

1. **Recent Fixes (Branch: fix/eventstore-statestore-and-publisherid)**: State persistence, event handling, and PublisherId fixes
2. **Commit 02502cb8**: Migration from Local-only to Orleans-compatible runtime architecture

---

## 1. Recent Fixes

### 1.1 `GAgentBase.TState.cs` - EventStore/StateStore Mutual Exclusion

#### Changes Summary
- **Fixed `OnActivateAsync` bug**: Changed `SaveAsync` to `LoadAsync`
- **Implemented mutual exclusion**: EventStore and StateStore are now mutually exclusive
- **Unified CQRS projection**: Always triggered via `OnStateChangedAsync` after persistence

#### Key Changes

**1.1.1 Fixed State Loading Bug**
```csharp
// Before (BUG):
await StateStore.SaveAsync(Id, _state, ct);

// After (FIXED):
var loadedState = await StateStore.LoadAsync(Id, ct);
if (loadedState != null) _state = loadedState;
```

**1.1.2 Mutual Exclusion Strategy**

State initialization (OnActivateAsync):
```csharp
// EventStore mode: Replay events to rebuild state
if (EventStore != null)
{
    await ReplayEventsAsync(ct);
}
// StateStore mode: Load state directly (mutually exclusive)
else if (StateStore != null)
{
    var loadedState = await StateStore.LoadAsync(Id, ct);
    if (loadedState != null) _state = loadedState;
}
```

Event handling (HandleEventAsync):
```csharp
var useEventSourcing = EventStore != null;

// 1. Load State (only in StateStore mode)
if (!useEventSourcing && StateStore != null)
{
    var loadedState = await StateStore.LoadAsync(Id, ct);
    if (loadedState != null) _state = loadedState;
}

// 2. Call core event handling
await HandleEventCoreAsync(envelope, ct);

// 3. Persist state (mutually exclusive)
if (useEventSourcing)
{
    await ConfirmEventsAsync(ct);  // Events + Snapshots
}
else if (StateStore != null)
{
    await StateStore.SaveAsync(Id, _state, ct);  // Direct state save
}

// 4. CQRS projection (always, regardless of persistence strategy)
await OnStateChangedAsync(_state, ct);
```

**1.1.3 CQRS Projection Design**
- CQRS projection is triggered via `OnStateChangedAsync` **after** persistence completes
- This ensures projection always reflects the persisted state
- Works uniformly for both EventStore and StateStore modes

#### Impact
- ✅ Fixes state loading bug that overwrote persisted state
- ✅ Eliminates duplicate storage operations (EventStore + StateStore)
- ✅ Clear separation: EventStore for event-sourced agents, StateStore for simple state
- ✅ CQRS projection always executes regardless of persistence strategy

---

### 1.2 `IEventPublisher.cs` - Unified `isInternalCall` Parameter

#### Changes Summary
- Added `isInternalCall` parameter to `IEventPublisher` interface
- Enables consistent external/internal call distinction across all runtimes
- Ensures same Agent behavior in Local and Orleans runtimes

#### Key Changes

**1.2.1 Interface Definition**
```csharp
public interface IEventPublisher
{
    Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false)  // NEW: Default false for external API compatibility
        where TEvent : IMessage;

    Task<string> SendToAsync<TEvent>(
        Guid targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)  // NEW
        where TEvent : IMessage;
}
```

**1.2.2 Call Chain Behavior**

| Call Source | isInternalCall | PublisherId | Agent Handles? |
|-------------|----------------|-------------|----------------|
| HTTP API / External | `false` (default) | `""` (empty) | ✅ Yes |
| Agent Internal (`EventPublisher.PublishEventAsync`) | `true` | AgentId | ❌ No (self-handling check) |
| Agent with `AllowSelfHandling = true` | `true` | AgentId | ✅ Yes |

**1.2.3 Implementation in GAgentBase**
```csharp
// GAgentBase.cs - Agent internal calls pass isInternalCall: true
var eventId = await EventPublisher.PublishEventAsync(evt, direction, ct, isInternalCall: true);
```

**1.2.4 Implementation in GAgentActorBase**
```csharp
// External calls (default isInternalCall=false): Clear PublisherId
if (!isInternalCall)
{
    envelope.PublisherId = "";  // Agent can handle this event
}
// Internal calls: Keep PublisherId for self-handling check
```

#### Impact
- ✅ Consistent behavior between Local and Orleans runtimes
- ✅ External API calls (HTTP) correctly handled by Agent
- ✅ Internal Agent-to-Agent communication preserves self-handling prevention
- ✅ Backward compatible (default `isInternalCall = false`)

---

### 1.3 `StateDocumentConverter.cs` - Simplified & Optimized

#### Changes Summary
- **Simplified caching**: Reduced from 6 caches to 3 essential caches
- **TypeUrl-based caching**: Cache by Protobuf TypeUrl for precise type resolution
- **Parser caching**: Cache `MessageParser` instances to avoid repeated reflection
- **Shared JsonSerializerOptions**: Single instance with camelCase naming policy

#### Key Implementation
```csharp
public class StateDocumentConverter
{
    // Core caches (only what's necessary)
    private static readonly ConcurrentDictionary<string, Type?> _typeCache = new();
    private static readonly ConcurrentDictionary<Type, MessageParser?> _parserCache = new();
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public Type? ResolveStateType(string agentType, Any? stateData)
    {
        var typeUrl = stateData.TypeUrl;
        
        // Check cache first
        if (_typeCache.TryGetValue(typeUrl, out var cachedType))
            return cachedType;

        // Parse TypeUrl: type.googleapis.com/package.TypeName -> TypeName
        var typeName = typeUrl.Contains('/')
            ? typeUrl.Substring(typeUrl.LastIndexOf('/') + 1)
            : typeUrl;
        
        // Search for type (only runs once per TypeUrl due to caching)
        var type = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t => 
                typeof(IMessage).IsAssignableFrom(t) && 
                !t.IsAbstract && 
                (t.Name == simpleName || t.FullName == typeName));

        // Cache result (including null for negative caching)
        _typeCache[typeUrl] = type;
        return type;
    }

    private void ExtractStateProperties(IMessage state, Type stateType, Dictionary<string, object> data)
    {
        foreach (var property in stateType.GetProperties())
        {
            // Skip Protobuf internal properties
            if (property.Name is "Parser" or "Descriptor" or "MessageType") continue;

            var value = property.GetValue(state);
            if (value == null) continue;

            var name = char.ToLowerInvariant(property.Name[0]) + property.Name[1..]; // camelCase

            data[name] = value switch
            {
                Timestamp ts => ts.ToDateTime(),
                _ when IsBasicType(property.PropertyType) => value,
                _ => JsonSerializer.Serialize(value, _jsonOptions)
            };
        }
    }
}
```

#### Caching Strategy
| Cache | Purpose | Key | Value |
|-------|---------|-----|-------|
| `_typeCache` | TypeUrl → Type mapping | Protobuf TypeUrl | `Type?` |
| `_parserCache` | Parser reflection cache | State Type | `MessageParser?` |
| `_jsonOptions` | Shared JSON options | (singleton) | `JsonSerializerOptions` |

#### Performance Characteristics
| Operation | Complexity | Notes |
|-----------|------------|-------|
| Type resolution (first) | O(n) | One-time scan per unique TypeUrl |
| Type resolution (cached) | O(1) | Dictionary lookup |
| Parser retrieval (first) | O(1) | Reflection + cache |
| Parser retrieval (cached) | O(1) | Dictionary lookup |
| Property extraction | O(p) | p = number of properties |
| JSON serialization | Native | Uses System.Text.Json |

#### Verified with CQRS Demo ✅
```
ES Data Analysis:
══════ Basic Types ══════
  name: Alice Johnson (String)
  age: 28 (Number)
  balance: 5000.5 (Number)
  isActive: True (Boolean)

══════ Complex Types (JSON serialized) ══════
  address: {"street":"123 Main St","city":"San Francisco"...}
  tags: ["premium","verified","developer"]
  orders: [{"productId":"PROD-001","productName":"Laptop Pro"...}]
  metadata: {"theme":"dark","language":"en-US"...}
  scores: {"coding":95,"design":78,"communication":88}
  luckyNumbers: [7,13,42]
```

#### Impact
- ✅ **Simplified code**: 3 caches instead of 6
- ✅ **Correct type resolution**: TypeUrl-based for Protobuf precision
- ✅ **All complex types supported**: List, Dict, Nested objects → JSON
- ✅ **Basic types preserved**: int, double, bool, string → native ES types
- ✅ **DateTime handling**: Timestamp → ISO DateTime string

---

## 2. Commit 02502cb8: Local to Orleans Runtime Compatibility

### 2.1 Overview

This commit migrates Payment Agents from Local-only runtime to Orleans-compatible runtime while maintaining Local runtime support. The main challenge was ensuring RPC compatibility for Orleans Grains.

### 2.2 Key Changes

#### 2.2.1 Factory Pattern Change

**Before (Local-only):**
```csharp
private readonly IGAgentFactory _agentFactory;

private AgentModels.IPaymentIndexGAgent GetIndexAgent(Guid userId)
{
    var agent = _agentFactory.CreateGAgent<AgentModels.PaymentIndexGAgent>(userId);
    agent.ActivateAsync().GetAwaiter().GetResult();
    return agent;
}
```

**After (Orleans-compatible):**
```csharp
private readonly IGAgentActorFactory _actorFactory;

private async Task<AgentModels.IPaymentIndexGAgent> GetIndexAgentAsync(Guid userId)
{
    var actor = await _actorFactory.CreateGAgentActorAsync<AgentModels.PaymentIndexGAgent>(userId);
    return actor.As<AgentModels.IPaymentIndexGAgent>();
}
```

**Key Differences:**
- `IGAgentFactory` → `IGAgentActorFactory`: Creates actors instead of direct agents
- Synchronous → Asynchronous: All methods are now async
- Direct agent access → RPC proxy: Uses `actor.As<TInterface>()` for RPC calls
- No activation needed: Actor activation is handled by Orleans

---

#### 2.2.2 Protobuf Type Conversion for RPC

**Problem**: Orleans RPC requires Protobuf-serializable types. Complex C# types (e.g., `List<T>`, nullable types) are not supported.

**Solution**: Convert all RPC-exposed methods to use Protobuf types.

**2.2.2.1 Return Type Wrapping**

**Before:**
```csharp
Task<List<ActiveSubscription>> GetActiveSubscriptionsAsync();
```

**After:**
```csharp
Task<ActiveSubscriptionListResponse> GetActiveSubscriptionsAsync();
```

**Protobuf Definition:**
```protobuf
message ActiveSubscriptionListResponse {
    repeated ActiveSubscriptionProto subscriptions = 1;
}
```

**2.2.2.2 Nullable Type Handling**

**Before:**
```csharp
Task<string?> GetPlatformCustomerIdAsync(PaymentPlatform platform);
```

**After:**
```csharp
Task<string> GetPlatformCustomerIdAsync(PaymentPlatform platform);
// Returns string.Empty instead of null
```

**2.2.2.3 DateTime to Timestamp Conversion**

**Before:**
```csharp
DateTime CreatedAt { get; set; }
DateTime? PeriodEnd { get; set; }
```

**After:**
```protobuf
google.protobuf.Timestamp created_at = 7;
google.protobuf.Timestamp period_end = 8;
```

**Usage:**
```csharp
// Convert DateTime to Timestamp
CreatedAt = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow)

// Convert Timestamp to DateTime
periodEnd?.ToDateTime() ?? DateTime.UtcNow
```

**2.2.2.4 Enum to int Conversion**

**Before:**
```csharp
PaymentPlatform Platform { get; set; }
```

**After:**
```protobuf
int32 platform = 4;  // PaymentPlatform enum value
```

**Usage:**
```csharp
// Convert enum to int
Platform = (int)PaymentPlatform.Stripe

// Convert int to enum
Platform = (PaymentPlatform)proto.Platform
```

---

#### 2.2.3 Interface Method Signature Changes

**2.2.3.1 `IPaymentIndexGAgent`**

| Method | Before | After |
|--------|--------|-------|
| `GetActiveSubscriptionsAsync` | `Task<List<ActiveSubscription>>` | `Task<ActiveSubscriptionListResponse>` |
| `GetActiveSubscriptionsByBusinessAsync` | `Task<List<ActiveSubscription>>` | `Task<ActiveSubscriptionListResponse>` |
| `AddActiveSubscriptionAsync` | `Task AddActiveSubscriptionAsync(ActiveSubscription)` | `Task AddActiveSubscriptionAsync(ActiveSubscriptionProto)` |
| `GetPlatformCustomerIdAsync` | `Task<string?>` | `Task<string>` |

**2.2.3.2 `IPaymentRecordGAgent`**

| Method | Before | After |
|--------|--------|-------|
| `InitializeAsync` | `Task InitializeAsync(CreatePaymentRequest)` | `Task InitializeAsync(CreatePaymentRequestProto)` |

---

#### 2.2.4 Service Layer Updates

**2.2.4.1 `PaymentService.cs`**

- Changed `IGAgentFactory` → `IGAgentActorFactory`
- Changed synchronous methods → async methods
- Updated all agent access to use `actor.As<TInterface>()`
- Added Protobuf type conversions (Timestamp ↔ DateTime, int ↔ enum)
- Updated DTO mapping to handle Protobuf types

**Example:**
```csharp
// Before:
var activeSubscriptions = await indexAgent.GetActiveSubscriptionsAsync();
var hasActiveStripe = activeSubscriptions.Any(s => s.Platform == PaymentPlatform.Stripe);

// After:
var response = await indexAgent.GetActiveSubscriptionsAsync();
var subscriptions = response.Subscriptions;
var hasActiveStripe = subscriptions.Any(s => s.Platform == (int)PaymentPlatform.Stripe);
```

---

#### 2.2.5 Test Updates

**2.2.5.1 `PaymentIndexGAgentTests.cs`**

- Updated to use `ActiveSubscriptionProto` instead of `ActiveSubscription`
- Updated assertions to access `response.Subscriptions` instead of direct list
- Added Timestamp conversions for DateTime comparisons

**Example:**
```csharp
// Before:
subscriptions.Count.ShouldBe(1);
subscriptions[0].PaymentId.ShouldBe("payment_1");

// After:
subscriptions.Subscriptions.Count.ShouldBe(1);
subscriptions.Subscriptions[0].PaymentId.ShouldBe("payment_1");
```

**2.2.5.2 `PaymentRecordGAgentTests.cs`**

- Updated to use `CreatePaymentRequestProto` instead of `CreatePaymentRequest`
- Added Protobuf type conversions

---

#### 2.2.6 Controller Updates

**2.2.6.1 `AgentDemoController.cs`**

- Updated comments to reflect Orleans runtime behavior
- Clarified that `PublishEventAsync` correctly sets empty `PublisherId` for external calls

---

#### 2.2.7 Protobuf Definitions

**2.2.7.1 `payment_index.proto`**

Added new messages:
```protobuf
message ActiveSubscriptionListResponse {
    repeated ActiveSubscriptionProto subscriptions = 1;
}

message CreatePaymentRequestProto {
    // All fields from CreatePaymentRequest converted to Protobuf types
    string user_id = 1;
    int32 platform = 2;  // PaymentPlatform enum
    google.protobuf.Timestamp created_at = 3;
    // ... more fields
}
```

---

### 2.3 Migration Pattern Summary

The migration follows a consistent pattern:

1. **Factory Change**: `IGAgentFactory` → `IGAgentActorFactory`
2. **Method Signatures**: Convert to async, use Protobuf types
3. **Type Conversions**: DateTime ↔ Timestamp, Enum ↔ int, Nullable → Non-nullable with defaults
4. **RPC Proxy**: Use `actor.As<TInterface>()` for type-safe RPC calls
5. **Return Type Wrapping**: Wrap collections in Protobuf messages
6. **Test Updates**: Update tests to use Protobuf types

---

## 3. Architecture Design & Optimizations

### 3.1 EventStore vs StateStore: Mutual Exclusion (✅ Implemented)

**Design Decision:**
EventStore and StateStore are now **mutually exclusive** to avoid duplicate storage operations.

**Rationale:**
- EventStore agents use event replay + snapshots for state reconstruction
- StateStore agents use simple load/save per event
- Using both creates redundancy and complexity

**Current Implementation:**
```
if (EventStore != null)
    → Use EventStore (events + snapshots)
    → Skip StateStore entirely
else if (StateStore != null)
    → Use StateStore (direct state persistence)
```

**Benefits:**
- ✅ No duplicate storage operations
- ✅ Clear separation of concerns
- ✅ Simplified mental model for developers

---

### 3.2 CQRS Projection Strategy (✅ Current Design is Optimal)

**Current Implementation:**
```csharp
// HandleEventAsync
// 1. Load state (if StateStore mode)
// 2. Process event
// 3. Persist state (EventStore OR StateStore)
// 4. CQRS projection (always, via OnStateChangedAsync)
await OnStateChangedAsync(_state, ct);
```

**Design Benefits:**
- ✅ CQRS projection always reflects persisted state
- ✅ Single point of entry for projection
- ✅ Works uniformly for both EventStore and StateStore modes
- ✅ Easy to extend (override `OnStateChangedAsync`)

**No Optimization Needed:**
The current design correctly separates persistence from projection, ensuring consistency.

---

### 3.3 Protobuf Type Conversion

**Current Behavior:**
- Every RPC call requires Protobuf ↔ C# type conversion
- DateTime ↔ Timestamp, Enum ↔ int conversions

**Optimization Opportunities:**
- Cache conversion results where possible
- Consider using Protobuf types directly in business logic
- Use code generation for common conversions

---

### 3.4 State Type Resolution Performance (✅ Optimized & Simplified)

**Optimization Applied:**
- TypeUrl-based caching (scan once per unique TypeUrl)
- Parser caching (reflection once per Type)
- Shared JsonSerializerOptions singleton

**Design Decision:**
Initially considered a pre-built `Lazy<Dictionary>` index, but simplified to on-demand caching:
- Type scans are amortized (once per TypeUrl, then O(1))
- Simpler code with 3 caches instead of 6
- Same effective performance for repeated lookups

```csharp
// Simple, effective caching pattern
if (_typeCache.TryGetValue(typeUrl, out var cachedType))
    return cachedType;  // O(1) for repeated lookups

// Scan only runs once per unique TypeUrl
var type = FindType(typeUrl);
_typeCache[typeUrl] = type;  // Cache for next time
return type;
```

---

## 4. Summary

### 4.1 Recent Fixes (Branch: fix/eventstore-statestore-and-publisherid)

| Change | Description |
|--------|-------------|
| **EventStore/StateStore Mutual Exclusion** | Fixed duplicate storage, clear separation |
| **State Loading Bug** | Fixed `SaveAsync` → `LoadAsync` in `OnActivateAsync` |
| **Unified `isInternalCall` Parameter** | Consistent external/internal call handling across runtimes |
| **StateDocumentConverter** | Simplified caching (TypeUrl, Parser, JsonOptions) |

### 4.2 Commit 02502cb8 Changes

1. **Migrated from Local-only to Orleans-compatible runtime**
2. **Converted all RPC methods to use Protobuf types**
3. **Updated factory pattern** from `IGAgentFactory` to `IGAgentActorFactory`
4. **Added type conversions** for DateTime, Enum, Nullable types
5. **Updated tests** to use Protobuf types

### 4.3 Key Takeaways

| Aspect | Guideline |
|--------|-----------|
| **RPC Compatibility** | Orleans requires Protobuf-serializable types |
| **Type Conversions** | DateTime ↔ Timestamp, Enum ↔ int, Nullable → Non-nullable |
| **Factory Pattern** | `IGAgentActorFactory` for Orleans, `IGAgentFactory` for Local |
| **State Persistence** | EventStore **OR** StateStore, mutually exclusive |
| **External Calls** | Default `isInternalCall = false`, clears PublisherId |
| **CQRS Projection** | Always via `OnStateChangedAsync` after persistence |

### 4.4 Architecture Diagram

```
External API Call                    Agent Internal Call
       │                                    │
       ▼                                    ▼
   isInternalCall = false            isInternalCall = true
       │                                    │
       ▼                                    ▼
   PublisherId = ""               PublisherId = AgentId
       │                                    │
       └──────────────┬─────────────────────┘
                      ▼
              HandleEventAsync
                      │
     ┌────────────────┴────────────────┐
     ▼                                 ▼
EventStore != null              StateStore != null
     │                                 │
     ▼                                 ▼
ConfirmEventsAsync            StateStore.SaveAsync
(events + snapshots)           (direct state save)
     │                                 │
     └────────────────┬────────────────┘
                      ▼
            OnStateChangedAsync
                      │
                      ▼
             CQRS Projection
            (if StateProjector configured)
```

### 4.5 Next Steps

1. ✅ **Committed to branch**: `fix/eventstore-statestore-and-publisherid`
2. **Merge to dev** after code review
3. **Add integration tests** for Orleans runtime
4. **Performance benchmarks** for RPC calls and state operations

---

## Appendix: File Change Summary

### Branch: fix/eventstore-statestore-and-publisherid

#### Commit 1: EventStore/StateStore Mutual Exclusion
- `src/Aevatar.Agents.Core/GAgentBase.TState.cs` - State initialization and event handling

#### Commit 2: Unified isInternalCall Parameter
- `src/Aevatar.Agents.Abstractions/IEventPublisher.cs` - Interface definition
- `src/Aevatar.Agents.Core/GAgentBase.cs` - Internal calls pass `isInternalCall: true`
- `src/Aevatar.Agents.Core/GAgentActorBase.cs` - PublisherId handling
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentActor.cs` - Orleans implementation
- `src/Aevatar.Agents.Runtime.Orleans/OrleansGAgentGrain.cs` - GrainEventPublisher update
- `test/Aevatar.Agents.Core.Tests.Agents/EventPublisher/TestEventPublisher.cs` - Test helper
- `test/Aevatar.Agents.Core.Tests/PerformanceTests.cs` - MockEventPublisher
- `test/Aevatar.Agents.Core.Tests/GAgentActorBaseTests.cs` - Test updates

#### Commit 3: StateDocumentConverter Performance Optimization
- `plugins/Aevatar.Agents.Plugins.CQRS/StateDocumentConverter.cs` - Pre-built type index, O(1) lookup

### Commit 02502cb8 (Previous)
- 11 files changed, 182 insertions(+), 127 deletions(-)
- Main files: `PaymentService.cs`, `IPaymentIndexGAgent.cs`, `IPaymentRecordGAgent.cs`, `payment_index.proto`, test files

---

**Document Version**: 2.0  
**Last Updated**: 2025-12-15  
**Author**: HyperEcho (AI Assistant)

