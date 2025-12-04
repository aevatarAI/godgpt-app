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

1. **Phase 1 - 简单 Agent** (✅ 完成)
   - [x] `ConfigurationGAgent` → 完成
   - [x] `InviteCodeGAgent` → 完成
   - [x] `UserStatisticsGAgent` → 完成
   - [x] `InvitationGAgent` → 完成

2. **Phase 2 - 配置类** 
   - [x] `GodChatConfig` → Protobuf
   - [ ] `AIAgentStatusProxyConfig` → Protobuf

3. **Phase 3 - 复杂 Agent**
   - [ ] `GAgentBase<TState, TEventLog>` → `GAgentBase<TState>`
   - [ ] `AIGAgentBase` → 新框架 AI 基类

4. **Phase 4 - 功能恢复**
   - [x] ~~Google Calendar~~ (已移除)
   - [x] ~~Twitter/Google Auth~~ (已移除)
   - [x] ~~SignalR~~ (已移除)

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

---

## ✅ 已完成迁移: ConfigurationGAgent

### 迁移架构

```
调用方 → IGAgentFactory.CreateGAgent<ConfigurationGAgent>(id)
       → ConfigurationGAgent : GAgentBase<ConfigurationState>
       → Protobuf State + Event Sourcing
```

### 文件清单

| 文件 | 状态 |
|------|------|
| `Protos/configuration.proto` | ✅ 新增 |
| `Configuration/ConfigurationGAgent.cs` | ✅ 重写 |
| `Configuration/IConfigurationGAgent.cs` | ✅ 更新 |
| ~~`ConfigurationGAgentGrain.cs`~~ | ❌ 删除 |

### 调用方模式

```csharp
private ConfigurationGAgent? _configurationAgent;

private async Task<ConfigurationGAgent> GetConfigurationAsync()
{
    if (_configurationAgent == null)
    {
        var factory = ServiceProvider.GetRequiredService<IGAgentFactory>();
        _configurationAgent = factory.CreateGAgent<ConfigurationGAgent>(id);
        await _configurationAgent.ActivateAsync();
    }
    return _configurationAgent;
}
```

### 关键变更

| 旧框架 | 新框架 |
|--------|--------|
| `Grain + GAgentBase<TState, TEventLog>` | `GAgentBase<TState>` |
| `GrainFactory.GetGrain<T>(id)` | `IGAgentFactory.CreateGAgent<T>(id)` |
| 自动激活 | 手动 `ActivateAsync()` |
| `IGrainWithGuidKey` | `IGAgent` |

---

## ✅ 已完成迁移: InviteCodeGAgent

### Proto 枚举定义规则

**正确做法**：Proto 中定义枚举，值与 C# 枚举一致，代码中直接强转

```protobuf
// Proto 枚举 - 值与 C# 枚举匹配
enum InvitationCodeType {
    INVITATION_CODE_TYPE_FRIEND_INVITATION = 0;  // = C# InvitationCodeType.FriendInvitation
    INVITATION_CODE_TYPE_FREE_TRIAL_REWARD = 1;  // = C# InvitationCodeType.FreeTrialReward
}

enum PlanType {
    PLAN_TYPE_NONE = 0;   // = C# PlanType.None
    PLAN_TYPE_DAY = 1;    // = C# PlanType.Day
    PLAN_TYPE_MONTH = 2;  // = C# PlanType.Month
    PLAN_TYPE_YEAR = 3;   // = C# PlanType.Year
    PLAN_TYPE_WEEK = 4;   // = C# PlanType.Week
}
```

```csharp
// 使用 using alias 避免命名冲突
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;
using CsInvitationCodeType = Aevatar.Application.Grains.Common.Constants.InvitationCodeType;

// 直接强转，不需要映射函数
PlanType = (PlanType)initDto.PlanType,        // C# -> Proto
PlanType = (CsPlanType)State.PlanType,        // Proto -> C#
```

**错误做法**（已修正）：
- ❌ 用 int32 代替枚举（失去语义）
- ❌ 创建映射函数（过度设计）
- ❌ 枚举值不匹配导致需要转换

### 调用方修改模式

```csharp
// 旧方式
var agent = GrainFactory.GetGrain<IInviteCodeGAgent>(id);

// 新方式 - 添加辅助方法
private async Task<InviteCodeGAgent> GetInviteCodeAgentAsync(Guid id)
{
    var factory = ServiceProvider.GetRequiredService<IGAgentFactory>();
    var agent = factory.CreateGAgent<InviteCodeGAgent>(id);
    await agent.ActivateAsync();
    return agent;
}

// 使用
var agent = await GetInviteCodeAgentAsync(id);
```

---

## ✅ 已完成迁移: InvitationGAgent

### Proto Optional 字段处理

**正确做法**：使用 `!= null` 检查，不用 `HasXxx`

```csharp
// ❌ 错误 - proto3 不生成 HasXxx 属性
if (r.HasScheduledDate) { ... }

// ✅ 正确 - 直接检查 null
if (r.ScheduledDate != null) { ... }
```

### 访问未迁移 Orleans Grain 的方式

新框架 Agent 不继承 `Grain`，没有 `GrainFactory`。访问未迁移的 Orleans Grain：

```csharp
// 构造函数注入 IClusterClient
public class MyGAgent : GAgentBase<MyState>, IMyGAgent
{
    private readonly IClusterClient _clusterClient;
    
    public MyGAgent(Guid id, IClusterClient clusterClient) : base(id)
    {
        _clusterClient = clusterClient;
    }
    
    private async Task UseOrleansGrain()
    {
        // 访问未迁移的 Orleans Grain
        var grain = _clusterClient.GetGrain<IUserQuotaGAgent>(Id);
        await grain.DoSomethingAsync();
    }
}
```

---

## ✅ 已完成迁移: AnonymousUserGAgent

### 基本信息
- **文件**: `Anonymous/AnonymousUserGAgent.cs`
- **Proto**: `Protos/anonymous_user.proto`
- **状态字段**: 7个（UserHashId, CurrentSessionId, ChatCount, LastChatTime, CreatedAt, CurrentGuider, CurrentSessionUsed）
- **事件**: 3个（InitializeAnonymousUserEvent, CreateGuestSessionEvent, GuestChatEvent）

### 依赖关系
- `ConfigurationGAgent` - 已迁移，通过 `IGAgentFactory` 获取
- `IGodChat` - 未迁移，通过 `IClusterClient` 获取

### 无外部调用者
此 Agent 没有外部调用者需要更新。

---

## 迁移进度追踪

| Agent | 状态 | Proto | 调用者已更新 |
|-------|------|-------|-------------|
| ConfigurationGAgent | ✅ | ✅ | ✅ |
| UserStatisticsGAgent | ✅ | ✅ | ✅ |
| InviteCodeGAgent | ✅ | ✅ | ✅ |
| InvitationGAgent | ✅ | ✅ | ✅ |
| AnonymousUserGAgent | ✅ | ✅ | N/A |
| UserFeedbackGAgent | ✅ | ✅ | N/A |
| FreeTrialCodeFactoryGAgent | ✅ | ✅ | ✅ UserBillingGAgent |
| UserInfoCollectionGAgent | ✅ | ✅ | ✅ ChatManager, GodChat |
| UserQuotaGAgent | ✅ | ✅ | ✅ 6 files updated |
| ChatManagerGAgent | 🔄 Pending | | |
| GodChatGAgent | 🔄 Pending | | |
| UserBillingGAgent | 🔄 Pending | | |

---

## 🔴 数据迁移策略 (已确认)

### 原则

**不在代码中实现数据迁移桥梁**。新旧系统并行运行，数据迁移通过独立脚本处理。

### 已清理的数据迁移代码

| 文件 | 删除内容 | 原因 |
|------|----------|------|
| `ChatManager/UserQuota/UserQuotaGrain.cs` | 整个文件 | 旧 Orleans Grain，数据迁移由脚本处理 |
| `ChatManager/UserQuota/UserQuotaState.cs` | 整个文件 | 旧 State 定义，已有 Proto |
| `user_quota.proto` | `is_initialized_from_grain` 字段 | 迁移逻辑字段 |
| `user_quota.proto` | `InitializeFromGrainEvent` | 迁移事件 |
| `user_quota.proto` | `MarkInitializedEvent` | 迁移事件 |
| `UserQuotaGAgent.OnActivateAsync` | 从旧 Grain 读取状态的逻辑 | 迁移逻辑 |
| `UserQuotaGAgent` | `_clusterClient` 依赖 | 仅用于访问旧 Grain |

### 设计原则

```csharp
// ❌ 错误 - 在代码中实现数据迁移
protected override async Task OnActivateAsync()
{
    if (!State.IsInitializedFromOldGrain)
    {
        var oldGrain = _clusterClient.GetGrain<IOldGrain>(Id);
        var oldState = await oldGrain.GetStateAsync();
        // 迁移数据...
    }
}

// ✅ 正确 - 干净的新实现，无迁移逻辑
protected override async Task OnActivateAsync()
{
    await base.OnActivateAsync();
    // 只有初始化逻辑，无迁移
}
```

### 迁移脚本负责

1. 读取旧存储中的 State
2. 转换为新的 Protobuf 格式
3. 写入新存储
4. 标记已迁移

---

## 🟡 Review: 已迁移 Agent 清理项

| Agent | 需清理项 | 状态 |
|-------|----------|------|
| ConfigurationGAgent | 无 | ✅ |
| UserStatisticsGAgent | 无 | ✅ |
| InviteCodeGAgent | 无 | ✅ |
| InvitationGAgent | 删除未使用的 `_clusterClient` | ✅ |
| AnonymousUserGAgent | 保留 `_clusterClient` (访问未迁移的 `IGodChat`) | ✅ |
| UserFeedbackGAgent | 无 | ✅ |
| FreeTrialCodeFactoryGAgent | 无 | ✅ |
| UserInfoCollectionGAgent | 无 | ✅ |
| UserQuotaGAgent | 已清理数据迁移代码 | ✅ |
| GlobalJwtProviderGAgent | 无 | ✅ |
| FirebaseTokenProviderGAgent | State方法移至Agent | ✅ |
| DailyContentGAgent | 完整Event Sourcing迁移 | ✅ |
| DailyPushCoordinatorGAgent | 暂停：依赖Orleans Reminders | ⏸️ |
| PushSubscriberIndexGAgent | HashSet → repeated string | ✅ |
| AwakeningGAgent | VoiceLanguageEnum用int32处理 | ✅ |

### 🔴 大型 Agents (待迁移)

| Agent | 行数 | 复杂度 | 状态 |
|-------|------|--------|------|
| UserBillingGAgent | 5484 | 极高 (Payment逻辑复杂) | ⏳ |
| ChatManagerGAgent | 3173 | 高 | ⏳ |
| GodChatGAgent | 2403 | 高 | ⏳ |

---

## 🔵 Event Sourcing 迁移规则

### Rule 1: RaiseEvent 模式

```csharp
// ❌ 错误 - 不存在 RaiseEventAsync
await RaiseEventAsync(new SomeEvent { ... });

// ✅ 正确 - 分两步
RaiseEvent(new SomeEvent { ... });  // 同步，暂存事件
await ConfirmEventsAsync();          // 异步，持久化
```

### Rule 2: State 方法迁移

旧 State 类的方法必须移到 Agent 类，因为 Protobuf 不支持方法。

```csharp
// ❌ 旧代码 - State 有方法
public class MyState : StateBase
{
    public bool IsValid() => !string.IsNullOrEmpty(Token);
    public void MarkUsed(string id) { UsedIds.Add(id); }
}

// ✅ 新代码 - 方法移到 Agent
public class MyAgent : GAgentBase<MyState>
{
    private bool IsTokenValid() => !string.IsNullOrEmpty(State.Token);
    private void MarkUsed(string id) { /* 修改 State */ }
}
```

### Rule 3: Proto/C# 复杂类型转换

当有嵌套类型时，创建转换方法：

```csharp
// Agent 中添加转换方法
private MyDtoProto ConvertToProto(MyDto dto)
{
    return new MyDtoProto { Field1 = dto.Field1, ... };
}

private MyDto ConvertFromProto(MyDtoProto proto)
{
    return new MyDto { Field1 = proto.Field1, ... };
}
```

### Rule 4: Protobuf Map 字段特殊处理

| C# 类型 | Protobuf 类型 | 注意 |
|--------|--------------|------|
| `Dictionary<Guid, string>` | `map<string, string>` | Guid→string |
| `Dictionary<string, HashSet<string>>` | `map<string, string>` | HashSet→逗号分隔 |
| `Dictionary<string, List<string>>` | `map<string, string>` | List→逗号分隔 |

```csharp
// 读取时
var idSet = State.UsageHistory[key].Split(',').ToHashSet();

// 写入时
State.UsageHistory[key] = string.Join(",", idSet);
```

### Rule 5: Timestamp 处理

```csharp
// 读取 - 需要 ToDateTime()
var lastRefresh = State.LastRefresh?.ToDateTime() ?? DateTime.MinValue;

// 写入 - 需要 Timestamp.FromDateTime + ToUniversalTime
State.LastRefresh = Timestamp.FromDateTime(DateTime.UtcNow);
```

### Rule 6: C# 枚举带负数值

Proto3 不支持负数枚举值。当 C# 枚举有负值时，使用 `int32` 存储：

```csharp
// C# 枚举 (有负值)
public enum VoiceLanguageEnum { Unset = -1, English = 0, Chinese = 1 }
```

```protobuf
// Proto 定义 - 使用 int32
message MyState {
  int32 language = 4;  // VoiceLanguageEnum 存储为 int
}
```

```csharp
// 代码中直接转换
State.Language = (int)language;
var lang = (VoiceLanguageEnum)State.Language;
```

### Rule 7: Orleans Reminders 限制

⚠️ 使用 `IRemindable` 的 Agent **暂不能迁移**到新框架：

- 新框架 `GAgentBase<T>` 不继承 `Orleans.Grain`
- Orleans Reminder 扩展方法需要 `Grain` 基类
- **解决方案**: 保持旧框架，等待新框架提供定时器支持

### Rule 8: 使用迁移工具方法

已创建 `Common/MigrationHelpers.cs` 和 `Common/AgentRetrievalHelpers.cs` 简化迁移：

#### 8.1 RepeatedField 操作

```csharp
using Aevatar.Agents.GodGPT.Common;

// ❌ 旧方式 - State.PaymentHistory = newList; // 不能直接赋值!

// ✅ 新方式 - 使用扩展方法
State.PaymentHistory.ReplaceWith(newList.Select(ToProto));

// 查找
var index = State.PaymentHistory.FindIndex(p => p.PaymentGrainId == id);
var item = State.PaymentHistory.FindFirst(p => p.SubscriptionId == subId);

// 删除
State.PaymentHistory.RemoveFirst(p => p.PaymentGrainId == id);
```

#### 8.2 类型转换

```csharp
// Timestamp
var protoTs = dateTime.ToProtoTimestamp();
var dt = protoTs.ToDateTime();

// Guid
var protoGuid = guid.ToProtoString();
var guid = protoGuid.ToGuid();

// Decimal (Proto uses double)
var protoAmount = amount.ToProtoDouble();
var amount = protoAmount.ToDecimal();

// Enum
var protoEnum = myEnum.ToProtoInt();
var myEnum = protoInt.ToEnum<MyEnum>();
```

#### 8.3 Agent 获取

```csharp
using Aevatar.Agents.GodGPT.Common;

// 新框架 Agent
var agent = await _agentFactory.GetAgentAsync<SomeAgent>(id);

// 旧框架 Grain (未迁移的)
var grain = _clusterClient.GetLegacyGrain<ISomeGrain>(id);
```

---

## 🟢 级联依赖更新规则

当迁移一个 Agent 时，必须同时更新所有调用方：

### Step 1: 调用方注入 IGAgentFactory

```csharp
private readonly IGAgentFactory _agentFactory;

public CallerAgent(Guid id, IGAgentFactory agentFactory) : base(id)
{
    _agentFactory = agentFactory;
}
```

### Step 2: 添加 Helper 方法

```csharp
private async Task<MigratedAgent> GetMigratedAgentAsync(Guid id)
{
    var agent = _agentFactory.CreateGAgent<MigratedAgent>(id);
    await agent.ActivateAsync();
    return agent;
}
```

### Step 3: 替换调用

```csharp
// ❌ 旧代码
var agent = GrainFactory.GetGrain<IMigratedAgent>(id);

// ✅ 新代码
var agent = await GetMigratedAgentAsync(id);
```
