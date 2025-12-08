# Agent 通信模式

本文档描述 Aevatar Agent Framework 中 Agent 之间的通信模式。

## 概述

框架支持两种基础通信模式：

| 模式 | 方法 | 语义 | 使用场景 |
|------|------|------|----------|
| **广播模式** | `PublishAsync()` | 一对多，通过层级 Stream | 组协调、事件传播 |
| **点对点模式** | `SendToAsync()` | 一对一，直接投递 | 任务分配、私密查询 |

```
┌─────────────────────────────────────────────────────────────────┐
│                        通信拓扑结构                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   广播模式:                    点对点模式:                       │
│                                                                  │
│       发布者                       发送者                        │
│          │                           │                           │
│          ▼                           │                           │
│       Stream ──────────────┐         │ 直接 RPC                  │
│          │                 │         │                           │
│    ┌─────┼─────┐          │         ▼                           │
│    ▼     ▼     ▼          │       目标                          │
│   C1    C2    C3          │         │                           │
│   (所有订阅者              │         ▼ (可选)                    │
│    都收到)                 │       继续传播                      │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 1. 广播模式（组通信）

### API

```csharp
protected Task<string> PublishAsync<TEvent>(
    TEvent evt,
    EventDirection direction = EventDirection.Down,
    CancellationToken ct = default)
```

### 事件方向

| 方向 | 流向 | 说明 |
|------|------|------|
| `Down` | 父 → 子 | 通过父节点的 Stream 广播给所有子节点 |
| `Up` | 子 → 父 → 兄弟 | 发送到父节点的 Stream，兄弟节点通过订阅接收 |
| `Both` | 双向 | 同时向上和向下传播 |

### 时序图：向下广播

```
    父节点                Stream                子节点1              子节点2
       │                    │                     │                    │
       │─── PublishAsync ──►│                     │                    │
       │    (Direction=Down)│                     │                    │
       │                    │                     │                    │
       │                    │── OnNextAsync ─────►│                    │
       │                    │                     │── 处理事件         │
       │                    │                     │                    │
       │                    │── OnNextAsync ──────────────────────────►│
       │                    │                                          │── 处理事件
       │                    │                                          │
```

### 时序图：向上广播

```
    子节点              父节点.Stream            父节点               兄弟节点
       │                      │                    │                    │
       │─── PublishAsync ────►│                    │                    │
       │    (Direction=Up)    │                    │                    │
       │                      │                    │                    │
       │                      │── OnNextAsync ────►│                    │
       │                      │                    │── 处理事件         │
       │                      │                    │                    │
       │                      │── OnNextAsync ─────────────────────────►│
       │                      │                    │                    │── 处理事件
       │                      │                    │                    │
```

### 示例：协调者向所有 Worker 广播任务

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
        
        // 向下广播给所有子节点（workers）
        await PublishAsync(task, EventDirection.Down);
    }
}
```

---

## 2. 点对点模式（直接通信）

### API

```csharp
protected Task<string> SendToAsync<TEvent>(
    Guid targetAgentId,
    TEvent evt,
    EventDirection onArrivalDirection = EventDirection.Unspecified,
    CancellationToken ct = default)
```

### 到达后行为

| 方向 | 到达后行为 |
|------|-----------|
| `Unspecified` | 纯点对点：只有目标处理，不传播 |
| `Down` | 目标处理后，广播给它的所有子节点 |
| `Up` | 目标处理后，向上传播给它的父节点 |
| `Both` | 目标处理后，双向传播 |

### 时序图：纯点对点

```
    发送者                                       目标
       │                                           │
       │─────── SendToAsync ──────────────────────►│
       │        (onArrival=Unspecified)            │
       │                                           │── 处理事件
       │                                           │
       │                                           │   (消息到此终止)
       │                                           │
```

### 示例：直接查询任务状态

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
        
        // 纯点对点：只有 worker 收到，不广播
        await SendToAsync(workerId, query, EventDirection.Unspecified);
    }
}
```

---

## 3. 点对点 + 组播模式（混合通信）

这种模式结合了点对点投递和组传播。消息首先直接投递给特定 Agent，然后该 Agent 将其传播给它的组。

### 使用场景

| 模式 | onArrivalDirection | 场景 |
|------|-------------------|------|
| P2P → 向下广播 | `Down` | 发任务给协调者，协调者分发给 workers |
| P2P → 向上传播 | `Up` | 发报告给某节点，节点向上级汇报 |
| P2P → 双向 | `Both` | 通知某节点，节点同时通知上下游 |

### 时序图：P2P + 组广播

```
    发送者              协调者                  Worker1             Worker2
       │                  │                      │                    │
       │── SendToAsync ──►│                      │                    │
       │   (onArrival=Down)                      │                    │
       │                  │── 处理事件           │                    │
       │                  │                      │                    │
       │                  │   (转换为广播)       │                    │
       │                  │                      │                    │
       │                  │── Stream.Publish ───►│                    │
       │                  │                      │── 处理事件         │
       │                  │                      │                    │
       │                  │── Stream.Publish ────────────────────────►│
       │                  │                                           │── 处理事件
       │                  │                                           │
```

### 示例：跨组任务分配

```csharp
public class ExternalSystemAgent : GAgentBase<ExternalState>
{
    /// <summary>
    /// 发送任务给另一个组的协调者
    /// 协调者会将任务分发给它的 workers
    /// </summary>
    public async Task AssignTaskToGroup(Guid coordinatorId, string task)
    {
        var taskEvent = new TaskAssignedEvent
        {
            TaskId = Guid.NewGuid().ToString(),
            Description = task,
            AssignedBy = Id.ToString()
        };
        
        // P2P 到协调者，协调者广播给它的 workers
        await SendToAsync(coordinatorId, taskEvent, EventDirection.Down);
    }
}
```

### 示例：报告上报

```csharp
public class SensorAgent : GAgentBase<SensorState>
{
    /// <summary>
    /// 发送紧急警报给特定节点
    /// 该节点会向上级链逐级上报
    /// </summary>
    public async Task ReportCriticalAlert(Guid monitorId, string alert)
    {
        var alertEvent = new CriticalAlertEvent
        {
            AlertId = Guid.NewGuid().ToString(),
            Message = alert,
            Severity = "Critical"
        };
        
        // P2P 到监控节点，监控节点向上级链上报
        await SendToAsync(monitorId, alertEvent, EventDirection.Up);
    }
}
```

---

## 4. 架构对比

### 消息流对比

```
┌─────────────────────────────────────────────────────────────────────────┐
│                          消息流模式对比                                  │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  1. PublishAsync (广播)              2. SendToAsync (点对点)             │
│                                                                          │
│     Agent A                              Agent A                         │
│        │                                    │                            │
│        ▼                                    │                            │
│     Stream ◄─── 订阅者                      │ 直接 RPC                   │
│        │                                    │                            │
│   ┌────┴────┐                              ▼                            │
│   ▼         ▼                           Agent B                         │
│ Agent B  Agent C                           │                            │
│                                            X (终止)                     │
│                                                                          │
├─────────────────────────────────────────────────────────────────────────┤
│                                                                          │
│  3. SendToAsync + onArrival=Down     4. SendToAsync + onArrival=Up      │
│                                                                          │
│     Agent A                              Agent A                         │
│        │                                    │                            │
│        │ P2P                                │ P2P                        │
│        ▼                                    ▼                            │
│     Agent B (协调者)                     Agent B                         │
│        │                                    │                            │
│        ▼ (广播)                             ▼ (上报)                     │
│     Stream                               父节点                          │
│        │                                    │                            │
│   ┌────┴────┐                              ▼                            │
│   ▼         ▼                           祖父节点                         │
│ Worker1  Worker2                                                         │
│                                                                          │
└─────────────────────────────────────────────────────────────────────────┘
```

### 何时使用哪种模式

| 模式 | 使用场景 | 示例 |
|------|----------|------|
| **向下广播** | 通知所有子节点 | 协调者分配任务给所有 workers |
| **向上广播** | 上报给父节点+兄弟可观察 | Worker 报告完成，兄弟可以看到 |
| **纯点对点** | 私密通信 | 直接查询、一对一协商 |
| **P2P + Down** | 跨组任务委派 | 外部系统给另一个团队分配任务 |
| **P2P + Up** | 报告上报 | 传感器报告警报，需要到达管理层 |

---

## 5. 实现细节

### EventEnvelope 的 P2P 字段

```protobuf
message EventEnvelope {
  // ... 现有字段 ...
  
  // P2P 目标 Agent ID（仅在使用 SendToAsync 时设置）
  string target_agent_id = 14;
  
  // P2P 消息到达目标后的传播方向
  EventDirection on_arrival_direction = 15;
}
```

### P2P 消息处理流程

```csharp
// 在 GAgentActorBase.HandleEventAsync() 中

if (isPointToPoint)
{
    // 1. 在目标处理事件
    await ProcessEventAsync(envelope, ct);
    
    // 2. 检查是否需要继续传播
    if (envelope.OnArrivalDirection != EventDirection.Unspecified)
    {
        // 将 P2P 转换为广播并传播
        var propagationEnvelope = envelope.Clone();
        propagationEnvelope.TargetAgentId = string.Empty;
        propagationEnvelope.Direction = envelope.OnArrivalDirection;
        
        await EventRouter.RouteEventAsync(propagationEnvelope, ct);
    }
}
```

---

## 6. 最佳实践

### ✅ 推荐做法

1. **使用 P2P 进行定向通信** - 当只有一个 Agent 需要接收时
2. **使用广播进行组协调** - 当所有子节点都需要知道时
3. **使用 P2P + Down 进行跨组委派** - 当委派任务给另一个团队时
4. **保持消息幂等** - 处理器应该可以安全地重放

### ❌ 避免做法

1. **不要用广播发送私密数据** - 所有订阅者都会看到
2. **当需要组确认时不要用 P2P** - 应该使用广播
3. **不要忘记处理传播循环** - 框架通过 Publishers 列表处理

### 性能考虑

| 模式 | 延迟 | 网络负载 | 可扩展性 |
|------|------|----------|----------|
| 纯 P2P | 低 | 最小 | 优秀 |
| 广播 | 中 | 与订阅者数量成正比 | 良好 |
| P2P + 传播 | 中 | 取决于目标的组大小 | 良好 |

---

## 7. 快速参考

```csharp
// ============================================================
// 广播模式
// ============================================================

// 广播给所有子节点
await PublishAsync(evt, EventDirection.Down);

// 发送给父节点（兄弟节点也通过订阅接收）
await PublishAsync(evt, EventDirection.Up);

// 双向广播
await PublishAsync(evt, EventDirection.Both);

// ============================================================
// 点对点模式
// ============================================================

// 纯 P2P：只有目标处理
await SendToAsync(targetId, evt);
await SendToAsync(targetId, evt, EventDirection.Unspecified);

// P2P + 组播：目标处理后，广播给它的子节点
await SendToAsync(coordinatorId, evt, EventDirection.Down);

// P2P + 上报：目标处理后，向上传播
await SendToAsync(nodeId, evt, EventDirection.Up);

// P2P + 双向：目标处理后，双向传播
await SendToAsync(nodeId, evt, EventDirection.Both);
```

---

## 8. 总结图示

```
┌─────────────────────────────────────────────────────────────────────────┐
│                       Agent 通信矩阵                                     │
├──────────────────┬──────────────────────────────────────────────────────┤
│                  │                    接收者                             │
│                  ├──────────────┬───────────────┬───────────────────────┤
│                  │ 单个 Agent   │ Agent 的组    │ Agent + 父级链        │
├──────────────────┼──────────────┼───────────────┼───────────────────────┤
│ 直接投递         │ SendToAsync  │ SendToAsync   │ SendToAsync           │
│                  │ (Unspecified)│ (Down)        │ (Up)                  │
├──────────────────┼──────────────┼───────────────┼───────────────────────┤
│ 通过 Stream      │ N/A          │ PublishAsync  │ PublishAsync          │
│                  │              │ (Down)        │ (Up)                  │
└──────────────────┴──────────────┴───────────────┴───────────────────────┘
```

---

## 9. 核心概念速记

| 概念 | 英文 | 说明 |
|------|------|------|
| 广播 | Broadcast | 通过 Stream 发送给所有订阅者 |
| 点对点 | Point-to-Point (P2P) | 直接发送给指定 Agent |
| 向下传播 | Down Propagation | 从父节点传播到子节点 |
| 向上传播 | Up Propagation | 从子节点传播到父节点 |
| 到达后行为 | On-Arrival Direction | P2P 消息到达目标后是否继续传播 |
| 组播 | Group Broadcast | P2P 到达后触发的广播 |
