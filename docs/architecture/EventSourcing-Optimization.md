# EventSourcing 架构优化

> 最后更新: 2025-12-17
> 代码验证: ✅

## 一、背景与目标

### 1.1 原有问题

1. **Grain 调用链过长**：
   ```
   OrleansGAgentGrain → GAgentBase → OrleansEventStore → EventStorageGrain → MongoEventRepository
   ```
   每次写事件需要额外的 Grain RPC 调用。

2. **Snapshot 存储不合理**：
   - 所有 Agent 类型的 Snapshot 存储在同一 Orleans State 集合
   - 无法按类型分表

3. **职责不清晰**：
   - `EventStorageGrain` 同时负责并发控制和 Snapshot 存储

### 1.2 优化目标

1. **简化调用链**：移除 `EventStorageGrain`，减少 RPC 开销
2. **按类型分表**：Events 和 State 都按 Agent 类型存储到不同集合
3. **保持兼容**：不影响 Local 运行时和非 EventSourcing 模式

---

## 二、最终架构

### 2.1 整体架构

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              GAgentBase<TState>                             │
│                                                                             │
│  ┌─────────────────────────────────┐    ┌─────────────────────────────────┐│
│  │   EventSourcing 模式            │    │   简单 State 模式               ││
│  │   - IEventStore (Events)        │    │   - IStateStore<TState>         ││
│  │   - IStateStore<TState> (Snap)  │    │                                 ││
│  └─────────────────────────────────┘    └─────────────────────────────────┘│
└────────────────────────┬────────────────────────────────┬───────────────────┘
                         │                                │
         ┌───────────────┴───────────────┐                │
         ↓                               ↓                ↓
┌─────────────────────┐    ┌─────────────────────┐    ┌─────────────────────┐
│   Local 运行时      │    │   Orleans 运行时    │    │   MongoDBStateStore │
│                     │    │                     │    │   <TState>          │
│ InMemoryEventStore  │    │ OrleansEventStore   │    │                     │
│                     │    │       ↓             │    │ agent_states_       │
│                     │    │ MongoEventRepository│    │ {StateType}         │
└─────────────────────┘    └─────────────────────┘    └─────────────────────┘
                                    ↓
                           ┌─────────────────────┐
                           │  agent_events_      │
                           │  {AgentType}        │
                           └─────────────────────┘
```

### 2.2 组件职责

| 组件 | 职责 | 存储位置 | 文件路径 |
|------|------|----------|----------|
| `IEventStore` | 抽象层，EventSourcing Events 接口 | - | `Abstractions/EventSourcing/` |
| `InMemoryEventStore` | Local 运行时的 Events 存储 | 内存 | `Core/EventSourcing/` |
| `OrleansEventStore` | Orleans 运行时的 Events 存储 | - | `Runtime.Orleans/EventSourcing/` |
| `IEventRepository` | Events 持久化接口 | MongoDB | `Runtime.Orleans/EventSourcing/` |
| `MongoEventRepository` | Events 持久化实现，按类型分表 | `agent_events_{ShortTypeName}` | `Runtime.Orleans.MongoDB/` |
| `IStateStore<TState>` | State/Snapshot 持久化接口 | - | `Abstractions/Persistence/` |
| `MongoDBStateStore<TState>` | State/Snapshot 统一存储 | `agent_states_{StateName}` | `Persistence.MongoDB/` |

> **命名规则**: 
> - Events 集合使用短类名: `agent_events_PaymentRecordGAgent`
> - States 集合使用 State 类型名: `agent_states_PaymentRecordStateProto`

### 2.3 调用链对比

#### 优化前 (4 层)

```
Client Request
    ↓
OrleansGAgentGrain.HandleEventAsync()
    ↓
GAgentBase.ConfirmEventsAsync()
    ↓
IEventStore.AppendEventsAsync()
    ↓
OrleansEventStore
    ↓ RPC ❌
EventStorageGrain.AppendEventsAsync()  ← 额外 Grain
    ↓
MongoEventRepository
    ↓
MongoDB
```

#### 优化后 (3 层)

```
Client Request
    ↓
OrleansGAgentGrain.HandleEventAsync()
    ↓
GAgentBase.ConfirmEventsAsync()
    ↓
IEventStore.AppendEventsAsync()
    ↓
OrleansEventStore
    ↓ 直接调用 ✅
MongoEventRepository
    ↓
MongoDB
```

---

## 三、数据存储设计

### 3.1 MongoDB 集合结构

```
AevatarBusiness Database
│
├── Events (按 Agent 类型分表)
│   ├── agent_events_PaymentIndexGAgent
│   ├── agent_events_InvitationGAgent
│   ├── agent_events_UserQuotaGAgent
│   └── agent_events_...
│
├── States/Snapshots (按 State 类型分表)
│   ├── agent_states_PaymentRecordStateProto
│   ├── agent_states_InvitationStateProto
│   ├── agent_states_CalculatorAgentState
│   └── agent_states_...
│
└── Orleans 元数据 (仅存 Grain 元数据)
    ├── OrleansAevataragentState  ← AgentId, ParentId, Children
    └── OrleansAevatarOrleansMembershipSingle
```

### 3.2 Event 文档结构

```json
{
  "_id": ObjectId,
  "AgentId": "guid",
  "Version": 1,
  "EventType": "PaymentCreatedEvent",
  "EventData": BinData(Protobuf),
  "Timestamp": ISODate,
  "EventId": "guid"
}
```

### 3.3 State/Snapshot 文档结构

```json
{
  "_id": "guid (AgentId)",
  "StateData": BinData(Protobuf),   // Protobuf 序列化的 byte[]
  "StateType": "Demo.Agents.BankAccountState",
  "Version": 100,
  "UpdatedAt": ISODate
}
```

> **序列化一致性**: State 和 Event 都使用 Protobuf byte[] 存储，保证：
> - 统一的序列化格式
> - 更小的存储空间 (~30-50%)
> - 完整的 Protobuf 类型支持 (RepeatedField, MapField, Timestamp)

---

## 四、并发控制设计

### 4.1 并发安全保证

```
┌─────────────────────────────────────────────────────────────────┐
│                    Orleans Grain 单线程保证                      │
│                                                                 │
│  OrleansGAgentGrain 保证同一 Agent 的所有操作是串行执行的         │
│  - HandleEventAsync() 串行                                      │
│  - RPC 调用串行                                                 │
│  - State 访问串行                                               │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                    MongoDB 唯一索引保护                          │
│                                                                 │
│  索引 { agentId: 1, version: -1 } 唯一约束                      │
│  - 防止重复版本插入                                              │
│  - Document ID 格式: "{agentId}_{version}"                      │
│  - 正常情况下不会触发（Grain 单线程已保证）                        │
└─────────────────────────────────────────────────────────────────┘
```

> **实现细节**: `OrleansEventStore.AppendEventsAsync` 在调用 Repository 前会显式检查版本，
> 作为防御性编程。同时 MongoDB 唯一索引 `{agentId, version}` 提供最终保证。

---

## 五、接口设计

### 5.1 IEventStore (简化版)

```csharp
public interface IEventStore
{
    // Events 操作
    Task<long> AppendEventsAsync(
        Guid agentId,
        IEnumerable<AgentStateEvent> events,
        long expectedVersion,  // 用于 GAgentBase 内部追踪，不传给 Repository
        string? agentTypeName = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(
        Guid agentId,
        long? fromVersion = null,
        long? toVersion = null,
        int? maxCount = null,
        string? agentTypeName = null,
        CancellationToken ct = default);

    Task<long> GetLatestVersionAsync(
        Guid agentId,
        string? agentTypeName = null,
        CancellationToken ct = default);

    // Note: Snapshots 由 GAgentBase + IStateStore<TState> 直接处理
}
```

### 5.2 IEventRepository (持久化层)

```csharp
public interface IEventRepository
{
    // Events 已包含 Version，无需 expectedVersion 参数
    // 并发由 Orleans Grain 单线程 + MongoDB 唯一索引保证
    Task<long> AppendEventsAsync(
        Guid agentId, 
        IEnumerable<AgentStateEvent> events,
        string? agentTypeName = null,
        CancellationToken ct = default);

    Task<IReadOnlyList<AgentStateEvent>> GetEventsAsync(...);
    Task<long> GetLatestVersionAsync(...);
    Task DeleteEventsBeforeVersionAsync(...);  // 清理旧事件
}
```

### 5.3 IStateStore<TState>

```csharp
public interface IStateStore<TState>
{
    Task<TState?> LoadAsync(Guid agentId, CancellationToken ct = default);
    Task SaveAsync(Guid agentId, TState state, CancellationToken ct = default);
    Task DeleteAsync(Guid agentId, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid agentId, CancellationToken ct = default);
}

public interface IVersionedStateStore<TState> : IStateStore<TState>
    where TState : class
{
    // 带版本保存（用于 EventSourcing Snapshot）
    Task SaveAsync(Guid agentId, TState state, long version, CancellationToken ct = default);
    Task<long> GetCurrentVersionAsync(Guid agentId, CancellationToken ct = default);
}

// MongoDBStateStore 实现要求 TState 是 Protobuf 消息
public class MongoDBStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()  // Protobuf 约束
{
    // 使用 Protobuf byte[] 序列化存储
}
```

---

## 六、实现清单

### 6.1 已删除文件

| 文件 | 原因 |
|------|------|
| `EventStorageGrain.cs` | 移除额外 Grain RPC 层 ✅ 已验证 |
| `IEventStorageGrain.cs` | 接口不再需要 ✅ 已验证 |
| `OrleansGAgentGrainGeneric.cs` | 泛型 Grain 方案废弃 |
| `ProtobufStateStore.cs` | 统一使用 MongoDBStateStore |
| `SnapshotDocument.cs` | 随 ProtobufStateStore 删除 |

### 6.2 核心文件结构

```
src/Aevatar.Agents.Runtime.Orleans/
├── EventSourcing/
│   ├── IEventRepository.cs      # Events 持久化接口
│   ├── InMemoryEventRepository.cs
│   └── OrleansEventStore.cs     # IEventStore 实现，调用 IEventRepository
├── OrleansGAgentGrain.cs        # 主 Grain 实现
└── ...

src/Aevatar.Agents.Runtime.Orleans.MongoDB/
├── MongoEventRepository.cs       # IEventRepository 的 MongoDB 实现
└── MongoEventRepositoryOptions.cs

src/Aevatar.Agents.Persistence.MongoDB/
├── MongoDBStateStore.cs          # IStateStore/IVersionedStateStore 实现
└── ...

src/Aevatar.Agents.Core/
├── GAgentBase.TState.cs          # EventSourcing + StateStore 集成
└── EventSourcing/
    └── InMemoryEventStore.cs     # Local 运行时 EventStore
```

### 6.3 关键修改点

| 文件 | 修改内容 |
|------|----------|
| `OrleansEventStore.cs` | 直接调用 `IEventRepository`，无 Grain RPC |
| `GAgentBase.TState.cs` | Snapshot 使用 `IStateStore<TState>`，支持 `IVersionedStateStore` |
| `IEventStore.cs` | 无 Snapshot 方法，只有 Events 操作 |
| `OrleansGAgentGrain.cs` | 注入 StateStore/EventStore，管理 Agent 生命周期 |
| `MongoEventRepository.cs` | 按类型分表，唯一索引保证并发安全 |

---

## 七、配置说明

### 7.1 appsettings.json

```json
{
  "EventSourcing": {
    "SnapshotFrequency": 1
  },
  "ConnectionStrings": {
    "MongoDB": "mongodb://localhost:27017/AevatarBusiness"
  }
}
```

### 7.2 DI 注册 (Orleans Silo)

```csharp
// MongoDB 基础服务
services.AddSingleton<IMongoClient>(_ => new MongoClient(connectionString));
services.AddSingleton(sp => sp.GetRequiredService<IMongoClient>().GetDatabase(databaseName));

// Event Repository
services.AddSingleton<IEventRepository>(sp => new MongoEventRepository(
    sp.GetRequiredService<IMongoClient>(),
    new MongoEventRepositoryOptions
    {
        DatabaseName = databaseName,
        CollectionName = "agent_events"
    },
    sp.GetRequiredService<ILogger<MongoEventRepository>>()));

// Event Store
services.AddSingleton<IEventStore>(sp => new OrleansEventStore(
    sp.GetRequiredService<IEventRepository>(),
    sp.GetRequiredService<ILogger<OrleansEventStore>>()));

// State Store (开放泛型注册，自动按类型分表)
services.AddSingleton(typeof(IStateStore<>), typeof(MongoDBStateStore<>));
services.AddSingleton(typeof(IVersionedStateStore<>), typeof(MongoDBStateStore<>));
```

---

## 八、性能分析

### 8.1 当前架构效率

| 操作 | 调用链 | IO 次数 | 说明 |
|------|--------|:-------:|------|
| RaiseEvent + Confirm | Grain → EventStore → Repository → MongoDB | 1 | 批量插入 |
| 创建 Snapshot | Grain → StateStore → MongoDB | 1 | Upsert |
| 重放 Events | Grain → Repository → MongoDB | 1 | 按版本范围查询 |
| 加载 Snapshot | Grain → StateStore → MongoDB | 1 | 单文档查询 |

### 8.2 优化效果

| 指标 | 优化前 | 优化后 | 提升 |
|------|:------:|:------:|:----:|
| Event 写入 RPC 次数 | 2 | 1 | 50% ↓ |
| Snapshot 存储分表 | ❌ | ✅ | - |
| Events 存储分表 | ✅ | ✅ | - |
| 代码复杂度 | 高 | 低 | - |

### 8.3 剩余优化空间

| 优化点 | 当前状态 | 可能优化 | 预期收益 |
|--------|----------|----------|----------|
| Event 批量写入 | 每次 Confirm 一次 IO | 多个 Confirm 合并 | 高并发场景 ↑ |
| Snapshot 策略 | 固定间隔 | 自适应（按数据量） | 存储 ↓ |
| 索引优化 | 基础索引 | 复合索引 + 覆盖查询 | 查询 ↑ |
| 连接池 | 默认配置 | 调优 MaxPoolSize | 并发 ↑ |

---

## 九、总结

### 架构简化成果

```
优化前:
OrleansGAgentGrain → GAgentBase → OrleansEventStore → EventStorageGrain → MongoDB
                                                            ↑
                                              (额外 Grain RPC + 状态管理)

优化后:
OrleansGAgentGrain → GAgentBase → OrleansEventStore → MongoDB
                          ↓
                    IStateStore<TState> → MongoDB (按类型分表)
```

### 关键设计决策

1. **移除 EventStorageGrain**：Orleans Grain 单线程已保证并发安全，无需额外 Grain
2. **统一 StateStore**：EventSourcing Snapshot 和普通 State 使用同一 MongoDBStateStore
3. **按类型自动分表**：`agent_events_{AgentType}` 和 `agent_states_{StateType}`
4. **保持接口抽象**：IEventStore/IStateStore 确保 Local 和 Orleans 运行时兼容
