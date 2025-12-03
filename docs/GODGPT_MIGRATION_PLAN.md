# GodGPT Agents Migration Plan

## Overview

Migrate `old/godgpt/src/GodGPT.GAgents` to `agents/Aevatar.Agents.GodGPT/` with **minimal changes**.

## Migration Strategy: Compile-Driven Development

**Core Principle**: Copy the entire directory, update project references to new framework, then fix compilation errors one by one.

---

## 🔴 Protobuf Migration Rules

### Rule 1: Type Mapping

| C# Type | Protobuf Type | Notes |
|---------|---------------|-------|
| `string` | `string` | Same |
| `int` | `int32` | Same |
| `long` | `int64` | Same |
| `bool` | `bool` | Same |
| `double` | `double` | Same |
| `float` | `float` | Same |
| `Guid` | `string` | Proto 无原生 Guid，序列化时自动转换 |
| `DateTime` | `google.protobuf.Timestamp` | UTC 时间，Protobuf 标准 |
| `List<T>` | `repeated T` | Same |
| `Dictionary<K,V>` | `map<K,V>` | Same |
| `HashSet<T>` | `repeated T` | Proto 无原生 set |
| `T?` (nullable) | `optional T` | Proto3 语法，新框架已使用 |

### Rule 2: Field Number Mapping

**Keep `[Id(N)]` → field number `N+1`** (proto field numbers start at 1, not 0)

```csharp
// Old C#
[Id(0)] public string UserId { get; set; }
[Id(1)] public int Credits { get; set; }
```

```protobuf
// New Proto
string user_id = 1;   // [Id(0)] -> field 1
int32 credits = 2;    // [Id(1)] -> field 2
```

### Rule 3: Naming Convention

- C# `PascalCase` → Proto `snake_case`
- Proto compiler generates C# `PascalCase` automatically

### Rule 4: Namespace

```protobuf
option csharp_namespace = "Aevatar.Agents.GodGPT.Protos";
```

---

## AI Base Class Fields (Confirmed)

### 新框架 AI Agent 结构

```
AIGAgentBase (基础 AI Agent)
  └── State: AevatarAIAgentState
        ├── history: repeated AevatarChatMessage  ← 聊天历史
        ├── context: map<string, string>
        ├── total_token_used: int64
        ├── last_activity: Timestamp
        └── custom_state: Any  ← 业务自定义状态

AIGAgentBase<TCustomState> (带自定义状态)
  └── CustomState: TCustomState  ← 从 State.custom_state 解包

AIGAgentBase<TCustomState, TCustomConfig> (带自定义状态和配置)
  └── CustomConfig: TCustomConfig  ← 从 Config.custom_config 解包
```

### 字段映射

| 旧字段 | 新框架位置 | 说明 |
|--------|-----------|------|
| `ChatHistory` | `State.History` | ✅ 新框架已有 |
| `MaxHistoryCount` | `Config.MaxHistory` | ✅ 新框架已有 |

### Agent 迁移方案

| Agent | 旧基类 | 新基类 | Proto 定义 |
|-------|--------|--------|-----------|
| `ChatManagerGAgent` | `GAgentBase<TState, TEventLog>` | `GAgentBase<TState>` | 只定义业务字段 |
| `GodChatGAgent` | `ChatGAgentState` | `AIGAgentBase<TCustomState>` | 只定义业务字段（CustomState） |

### GodChat 迁移示例

**旧代码**:
```csharp
public class GodChatGAgent : GAgentBase<GodChatState, GodChatEventLog>
{
    State.ChatHistory.Add(...)  // 基类字段
    State.UserProfile           // 业务字段
}
```

**新代码**:
```csharp
public class GodChatGAgent : AIGAgentBase<GodChatCustomState>
{
    State.History.Add(...)      // 新框架 AI 基类字段
    CustomState.UserProfile     // 业务字段在 CustomState 中
}
```

**GodChatCustomState proto** (只需业务字段):
```protobuf
message GodChatCustomState {
    UserProfile user_profile = 1;
    string title = 2;
    string chat_manager_id = 3;
    repeated string ai_agent_ids = 4;
    map<string, string> region_proxies = 5;
    google.protobuf.Timestamp first_chat_time = 6;
    google.protobuf.Timestamp last_chat_time = 7;
    repeated ChatMessageMeta chat_message_metas = 8;
    map<string, ProxyInitStatus> proxy_init_statuses = 9;
    // 不需要 chat_history 和 max_history_count - 新框架已有！
}
```

---

## 🔵 API Migration Mapping (旧→新)

### Method Mapping

| 旧框架方法 | 新框架方法 | 说明 |
|-----------|-----------|------|
| `RegisterAsync(child)` | `IGAgentActorManager.LinkParentChildAsync(parentId, childId)` | 建立父子关系 |
| `PublishAsync(event)` | `PublishAsync<TEvent>(evt, direction)` | 默认 `Down`，事件必须是 Protobuf `IMessage` |
| `Version` (属性) | `GetCurrentVersion()` | 返回 `long`，用于判断是否有历史事件 |
| `ConfirmEvents()` | `ConfirmEventsAsync()` | 异步确认事件 |
| `GAgentTransitionState(state, event)` | `TransitionState(TState state, IMessage evt)` | 状态转换方法 |
| `this.GetPrimaryKey()` | `Id` | 获取 Agent ID |
| `GrainFactory.GetGrain<T>(id)` | 保持不变（Orleans 环境） | 获取其他 Grain |
| `PerformConfigAsync(TConfig)` | `HandleConfigAsync(TConfig)` + `[EventHandler]` | 配置处理（见下方详解） |

### Configuration 配置机制

**旧框架**：虚方法重写
```csharp
protected override async Task PerformConfigAsync(AIAgentStatusProxyConfig configuration)
{
    // 手动处理配置
    RaiseEvent(new SetConfigLogEvent { ... });
}
```

**新框架**：事件驱动 + 自动持久化
```csharp
// 1. 配置类型必须是 Protobuf
message AIAgentStatusProxyConfig {
    int64 recovery_delay_ticks = 1;
    string parent_id = 2;
}

// 2. 继承 GAgentBase<TState, TConfig>
public class MyAgent : GAgentBase<MyState, MyConfig>
{
    // 3. 基类自动提供 ConfigAsync + HandleConfigAsync
    // Config 属性自动持久化
    
    // 4. 如需额外处理，添加 EventHandler
    [EventHandler]
    public async Task OnConfigChanged(MyConfig config)
    {
        // Config 已由基类设置
        // 这里添加业务逻辑
    }
}
```

### 已迁移配置：GodChatConfig

`GodChatGAgent` 配置已迁移到 Protobuf：

```protobuf
// agents/Aevatar.Agents.GodGPT/Protos/agent_configs.proto
message GodChatConfig {
    optional string system_prompt = 1;
    int32 max_history_count = 2;
    bool enable_streaming = 3;
    optional string instructions = 5;
    bool streaming_mode_enabled = 6;
    
    // Flattened LLM config fields
    optional string llm_system_llm = 14;
    
    // Flattened streaming config fields  
    int32 streaming_buffering_size = 21;
}
```

调用方式：
```csharp
// ChatManagerGAgent / AnonymousUserGAgent
var godChatConfig = new GodChatConfig()
{
    Instructions = sysMessage, 
    MaxHistoryCount = 32,
    LlmSystemLlm = await configuration.GetSystemLLM(),
    StreamingModeEnabled = true, 
    StreamingBufferingSize = 32
};
await godChat.ConfigAsync(godChatConfig);
```

## 兼容层分析

### 分类总结

| 类别 | 类型 | 状态 | 迁移方案 |
|------|------|------|----------|
| **核心基类** | `GAgentBase<TState, TEventLog>` | 待迁移 | → 新框架 `GAgentBase<TState>` |
| **AI基类** | `AIGAgentBase<TState, TEventLog, TConfig, TEvent>` | 待迁移 | → 新框架 `AIGAgentBase` |
| **状态基类** | `StateBase` | 待迁移 | → Protobuf `IMessage` |
| **事件基类** | `StateLogEventBase<T>`, `EventBase` | 待迁移 | → Protobuf Event |
| **配置** | `GodChatConfig` | ✅ 已迁移 | Protobuf |
| **配置** | `AIAgentStatusProxyConfig` | 待迁移 | → Protobuf |
| **属性** | `[GAgent]`, `[EventHandler]` | ✅ 可用 | 新框架已有同名 |

### 详细分析

#### 1. LegacyFrameworkTypes.cs - 核心框架类型

| 类型 | 新框架对应 | 迁移优先级 | 备注 |
|------|------------|------------|------|
| `StateBase` | `IMessage<T>` (Protobuf) | 高 | State 需转 Protobuf |
| `StateLogEventBase<T>` | `IMessage` (Protobuf) | 高 | EventLog 需转 Protobuf |
| `EventBase` | `IMessage` (Protobuf) | 高 | Event 需转 Protobuf |
| `IGAgent` | `Aevatar.Agents.Abstractions.IGAgent` | 低 | 接口兼容 |
| `GAgentAttribute` | `Aevatar.Agents.Abstractions.Attributes.GAgentAttribute` | 低 | 已有对应 |
| `EventHandlerAttribute` | `Aevatar.Agents.Abstractions.Attributes.EventHandlerAttribute` | 低 | 已有对应 |
| `GAgentBase<TState, TEventLog>` | `Aevatar.Agents.Core.GAgentBase<TState>` | 高 | 核心迁移 |
| `AIGAgentBase<...>` | `Aevatar.Agents.AI.Core.AIGAgentBase` | 高 | AI 核心迁移 |
| `RegisterAsync(child)` | `IGAgentActorManager.LinkParentChildAsync` | 中 | 父子关系 |
| `PublishAsync<T>()` | `IEventPublisher.PublishEventAsync<T>()` | 中 | 事件发布 |
| `Version` | `GetCurrentVersion()` | ✅ 已迁移 | 版本号 |
| `StreamProvider`, `AevatarOptions` | 新框架 Stream | 中 | 客户端推送用 |

#### 2. AICompatibilityTypes.cs - AI 相关类型

| 类型 | 新框架对应 | 迁移方案 |
|------|------------|----------|
| `ChatMessage` | `AevatarChatMessage` (Protobuf) | 字段映射 |
| `ChatRole` | `AevatarChatRole` (Protobuf) | 枚举映射 |
| `AIExceptionEnum` | 待定 | 保留或新建 |
| `AIStreamChatContent` | 待定 | 检查新框架 AI |
| `ExecutionPromptSettings` | `AevatarAIAgentConfig` | 参数映射 |
| `StreamingConfig` | 新框架有 | 检查字段 |
| `AIChatContextDto` | `AevatarAIContext` | 字段映射 |
| `LLMConfigDto` | `AevatarAIAgentConfiguration` | 字段映射 |
| `InitializeDto` | 配置事件 | → `ConfigAsync` |
| `ConfigurationBase` | `IMessage` | → Protobuf |

#### 3. MoreCompatibilityTypes.cs - 其他类型

| 类型 | 状态 | 迁移方案 |
|------|------|----------|
| `IStreamSyncWorker` | 待定 | 检查新框架 Stream |
| `IAIGAgent` | 低优先级 | 接口兼容 |
| `AIStreamingErrorResponseGEvent` | 待定 | → Protobuf Event |
| `IChatGAgent` | 低优先级 | 接口兼容 |
| `GoogleCalendar*Dto` | ✅ 已删除 | 不再使用 |
| `ChatConfigDto` | ✅ 已迁移 | → `GodChatConfig` |
| `EventWrapper<T>` | 待定 | 检查新框架 |

### 新框架 AI 类型映射

```
旧框架                          新框架 (Protobuf)
─────────────────────────────────────────────────────
ChatMessage                  →  AevatarChatMessage
  .Role                      →  .role (AevatarChatRole)
  .Content                   →  .content
  .Timestamp                 →  .timestamp

AIGAgentStateBase            →  AevatarAIAgentState
  .History                   →  .history
  .Context                   →  .context (map)
  .TotalTokenUsed            →  .total_token_used
  .LastActivity              →  .last_activity

ExecutionPromptSettings      →  AevatarAIAgentConfig
  .Temperature               →  .temperature
  .MaxTokens                 →  .max_output_tokens
  .TopP                      →  .top_p

LLMConfigDto                 →  AevatarAIAgentConfiguration
  .ModelId                   →  .model
  .Temperature               →  .temperature
  .MaxTokens                 →  .max_tokens
```

### 迁移顺序建议

1. **Phase 1 - 配置** (✅ 部分完成)
   - [x] `GodChatConfig` → Protobuf
   - [ ] `AIAgentStatusProxyConfig` → Protobuf
   - [ ] 其他配置类型

2. **Phase 2 - State/Event**
   - [ ] 定义 Protobuf State (保持字段名兼容)
   - [ ] 定义 Protobuf Event  
   - [ ] 数据迁移脚本

3. **Phase 3 - 基类迁移**
   - [ ] `GAgentBase<TState, TEventLog>` → `GAgentBase<TState>`
   - [ ] `AIGAgentBase` → 新框架 AI 基类

4. **Phase 4 - 功能恢复**
   - [x] ~~Google Calendar~~ (已移除，不再使用)
   - [x] ~~Twitter/Google Auth~~ (已移除，不再使用)
   - [x] ~~SignalR~~ (已移除，不再使用)

### PublishAsync 详解

```csharp
// 旧框架 - 无方向概念
await PublishAsync(responseEvent);

// 新框架 - 支持方向
await PublishAsync(responseEvent);                    // 默认 Down (发给子节点)
await PublishAsync(responseEvent, EventDirection.Up);  // 发给父节点和兄弟节点
await PublishAsync(responseEvent, EventDirection.Both); // 双向发送
```

### Version 使用

```csharp
// 旧框架
var isFirstAccess = Version == 0;

// 新框架
var isFirstAccess = GetCurrentVersion() == 0;
```

### RegisterAsync 替代

```csharp
// 旧框架 - 在 GAgent 内部注册子节点
IGodChat godChat = GrainFactory.GetGrain<IGodChat>(sessionId);
await RegisterAsync(godChat);

// 新框架 - 通过 ActorManager 建立父子关系
// 方式 1: 使用 ActorManager (推荐)
await ActorManager.LinkParentChildAsync(this.Id, childId);

// 方式 2: Orleans 环境中直接省略（Grain 自动管理）
// 如果不需要层级广播，可以省略
```

---

## Code Migration Rules

### Rule 5: Base Class Change

```csharp
// Old
public class MyAgent : GAgentBase<TState, TEventLog>

// New
public class MyAgent : GAgentBase<TState>
```

### Rule 6: Event Sourcing Pattern

```csharp
// Old
RaiseEvent(new SomeEventLog { ... });
await ConfirmEvents();
protected override void GAgentTransitionState(TState state, TEventLog @event)

// New
RaiseEvent(new SomeEvent { ... });  // Must be IMessage (Protobuf)
await ConfirmEventsAsync();
protected override void TransitionState(TState state, IMessage @event)
```

### Rule 7: Agent ID Access

```csharp
// Old
this.GetPrimaryKey()

// New
Id
```

### Rule 8: Get Other Agents

```csharp
// Old
GrainFactory.GetGrain<T>(id)

// New - TBD based on runtime
// Orleans: Still use GrainFactory if running in Orleans Grain
// Or: ActorManager.GetActorAsync<T>(id)
```

---

## Directory Structure

```
agents/Aevatar.Agents.GodGPT/
├── Protos/                    # NEW: Proto definitions
│   ├── common.proto           # Shared enums
│   ├── user_quota.proto
│   ├── chat_manager.proto
│   ├── god_chat.proto
│   ├── invitation.proto
│   └── ...
├── ChatManager/               # Keep as-is
├── GodChat/                   # Keep as-is
├── UserQuota/                 # Keep as-is
├── ...
└── Aevatar.Agents.GodGPT.csproj
```

## Files to Exclude

- `GoogleAuth/` - Not needed
- `Twitter/` - Not needed
- `TwitterInteraction/` - Not needed

---

## Migration Steps

### Phase 1: Copy & Setup
1. [ ] Copy `GodGPT.GAgents` to `agents/Aevatar.Agents.GodGPT/`
2. [ ] Remove `bin/`, `obj/`, excluded directories
3. [ ] Update project file with new framework references
4. [ ] Add to solution file

### Phase 2: Create Protos
5. [ ] Create `Protos/` directory
6. [ ] Define proto files for all State types
7. [ ] Define proto files for all Event types
8. [ ] Build to generate C# code

### Phase 3: Compile & Fix
9. [ ] Run `dotnet build`
10. [ ] Fix compilation errors iteratively
11. [ ] Update State/Event references to use generated Protobuf types

### Phase 4: Integration
12. [ ] Update Silo to reference the new project
13. [ ] Update HTTP API to reference the new project
14. [ ] Test basic functionality

---

## Project File Template

```xml
<Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup>
        <TargetFramework>net9.0</TargetFramework>
        <RootNamespace>Aevatar.Application.Grains</RootNamespace>
    </PropertyGroup>

    <!-- New Framework -->
    <ItemGroup>
        <ProjectReference Include="../../src/Aevatar.Agents.Abstractions/..." />
        <ProjectReference Include="../../src/Aevatar.Agents.Core/..." />
        <ProjectReference Include="../../src/Aevatar.Agents.AI.Core/..." />
        <ProjectReference Include="../../src/Aevatar.Agents.Runtime.Orleans/..." />
    </ItemGroup>

    <!-- Protobuf -->
    <ItemGroup>
        <Protobuf Include="Protos/**/*.proto" GrpcServices="None" />
    </ItemGroup>

    <!-- External Services (keep as-is) -->
    <ItemGroup>
        <PackageReference Include="Stripe.net" />
        <PackageReference Include="Google.Apis.AndroidPublisher.v3" />
        <PackageReference Include="StackExchange.Redis" />
        <PackageReference Include="FirebaseAdmin" />
        <!-- ... -->
    </ItemGroup>
</Project>
```

---

## Success Criteria

1. Project compiles without errors
2. All agents can be instantiated
3. Event sourcing persists correctly
4. Silo starts successfully
