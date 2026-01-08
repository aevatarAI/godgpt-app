# Lumen Module Migration Plan

> 创建日期: 2025-12-31
> 分支: feature/lumen-migration
> 基于: GodGPT Agent 迁移模式 (参考 GODGPT_ARCHITECTURE.md)

---

## 📊 迁移范围总览

```
Agent层:     7 GAgents 需迁移 (1个DEPRECATED跳过)
State层:     7 State Classes → Protobuf
Event层:     ~30 Events → Protobuf
DTO层:       ~50 DTOs → Protobuf (部分可复用)
Service层:   1 Service
Helper层:    4 Helpers (可直接迁移)
Calculator层: 2 Calculators (可直接迁移)
```

---

## 🎯 迁移策略

### 迁移顺序: 从小到大

```
优先级 1 → LumenStatsSnapshotGAgent       (89行)   - 最小，热身
优先级 2 → LumenDailyYearlyHistoryGAgent  (228行)  - 小型
优先级 3 → LumenFavouriteGAgent           (252行)  - 小型
优先级 4 → LumenPredictionHistoryGAgent   (298行)  - 中型
优先级 5 → LumenFeedbackGAgent            (375行)  - 中型
优先级 6 → LumenUserProfileGAgent         (1712行) - 大型 ⚠️
优先级 7 → LumenPredictionGAgent          (6935行) - 超大 🔥

跳过     → LumenUserGAgent                (716行)  - DEPRECATED
```

### 每个Agent的四步迁移法

```
┌─────────────────────────────────────────────────────────────┐
│  Step 1: Partial Class 分析                                 │
│  ├── 分析代码结构                                            │
│  ├── 识别方法分组 (Handlers/RPC/Business/Helpers)           │
│  └── 决定是否需要拆分为 partial class                        │
├─────────────────────────────────────────────────────────────┤
│  Step 2: 代码风格修改                                        │
│  ├── 移除 GAgentTransitionState()                           │
│  ├── 添加 [EventHandler] 属性                               │
│  ├── ConfirmEvents() → ConfirmEventsAsync()                 │
│  └── 更新类型引用为 Protobuf                                 │
├─────────────────────────────────────────────────────────────┤
│  Step 3: 拆分评估                                            │
│  ├── 评估是否需要拆分为多个 Agent                            │
│  ├── 考虑: 状态隔离、调用频率、故障隔离、扩展性               │
│  └── 记录决策理由                                            │
├─────────────────────────────────────────────────────────────┤
│  Step 4: 单元测试 ✅ (迁移完成的唯一标准)                     │
│  ├── 在 Aevatar.App.Application.Tests/Agents/ 创建测试文件  │
│  ├── 覆盖所有 RPC 方法                                       │
│  ├── 覆盖所有 EventHandler                                   │
│  ├── 测试状态转换                                            │
│  └── 测试通过 = 迁移完成                                     │
└─────────────────────────────────────────────────────────────┘

🔴 重要: 没有单元测试通过，迁移不算完成！
```

### 为什么这个顺序？

1. **从小到大**: 小Agent (89行) 迁移快，积累经验，建立信心
2. **复杂的放最后**: LumenPredictionGAgent (6935行) 涉及AI集成、多语言翻译、定时任务，需要充分准备
3. **依赖顺序**: UserProfile 被 Prediction 依赖，需要先稳定

---

## 🏗️ 架构对比

### 旧框架 → 新框架

| 组件 | 旧框架 | 新框架 |
|------|--------|--------|
| Agent基类 | `GAgentBase<TState, TEventLog>` | `GAgentBase<TState>` |
| Agent获取 | `GrainFactory.GetGrain<T>()` | `IGAgentActorFactory.CreateGAgentActorAsync<T>()` |
| ID获取 | `this.GetPrimaryKey()` | `this.Id` |
| 事件确认 | `ConfirmEvents()` | `await ConfirmEventsAsync()` |
| 状态定义 | C# Class + `[GenerateSerializer]` | **Protobuf Message** (必须) |
| 事件定义 | C# Class + `StateLogEventBase` | **Protobuf Message** (必须) |
| 状态转换 | `GAgentTransitionState()` override | 无需 (事件驱动自动处理) |

### 核心原则

```
🔴 关键规则：
- 所有 State 和 Event 必须使用 Protobuf 定义
- 删除 GAgentTransitionState() 方法
- 使用 RaiseEvent() + ConfirmEventsAsync() 模式
- 使用 [EventHandler] 属性替代手动状态转换
- 通过 ActorFactory + As<T>() 获取RPC代理
```

---

## 📁 源文件清单

### Lumen 模块位置
```
old/godgpt/src/GodGPT.GAgents/Lumen/
├── LumenUserGAgent.cs              # 716 行 (DEPRECATED)
├── LumenUserState.cs               # 37 行
├── LumenUserProfileGAgent.cs       # 1712 行
├── LumenUserProfileState.cs        # 57 行
├── LumenPredictionGAgent.cs        # 6935 行 ⚠️ 超大文件
├── LumenPredictionState.cs         # 71 行
├── LumenPredictionHistoryGAgent.cs # 298 行
├── LumenPredictionHistoryState.cs  # 44 行
├── LumenFavouriteGAgent.cs         # 252 行
├── LumenFavouriteState.cs          # 30 行
├── LumenFeedbackGAgent.cs          # 375 行
├── LumenFeedbackState.cs           # 32 行
├── LumenStatsSnapshotGAgent.cs     # 89 行
├── LumenStatsSnapshotState.cs      # 25 行
├── LumenDailyYearlyHistoryGAgent.cs# 228 行
├── LumenDailyYearlyHistoryState.cs # 43 行
├── LumenCalculator.cs              # 727 行
├── WesternAstrologyCalculator.cs   # 223 行
├── SEvents/
│   ├── LumenEventLog.cs            # 420 行 (所有事件)
│   └── LumenDailyYearlyHistoryEventLog.cs
├── Dtos/
│   ├── LumenDtos.cs                # 700 行
│   └── LumenUserProfileDtos.cs
├── Options/
│   └── LumenOptions.cs
├── Services/
│   └── LuckyNumberService.cs
└── Helpers/
    ├── LumenTimezoneHelper.cs
    ├── SolarTermCalculator.cs
    └── SolarTermData.cs
```

### 目标位置
```
agents/Aevatar.Agents.Lumen/
├── Protos/
│   ├── lumen_common.proto          # 枚举和公共消息
│   ├── lumen_user_profile.proto    # 用户配置
│   ├── lumen_prediction.proto      # 预测相关
│   ├── lumen_feedback.proto        # 反馈相关
│   └── lumen_favourite.proto       # 收藏相关
├── UserProfile/
│   ├── LumenUserProfileGAgent.cs
│   └── ILumenUserProfile.cs
├── Prediction/
│   ├── LumenPredictionGAgent.cs
│   ├── LumenPredictionGAgent.Daily.cs
│   ├── LumenPredictionGAgent.Yearly.cs
│   ├── LumenPredictionGAgent.Lifetime.cs
│   └── ILumenPrediction.cs
├── History/
│   └── LumenPredictionHistoryGAgent.cs
├── Feedback/
│   └── LumenFeedbackGAgent.cs
├── Favourite/
│   └── LumenFavouriteGAgent.cs
├── Calculators/
│   ├── LumenCalculator.cs
│   ├── WesternAstrologyCalculator.cs
│   └── SolarTermCalculator.cs
├── Helpers/
│   ├── LumenTimezoneHelper.cs
│   └── SolarTermData.cs
└── README.md
```

---

## 📋 迁移状态清单

### Phase 1: Protobuf 定义 (优先级: 高) ✅ 已完成

| 文件 | 状态 | 复杂度 | 备注 |
|------|------|--------|------|
| lumen_common.proto | ✅ 完成 | 中 | 枚举(12+), 公共消息 |
| lumen_stats.proto | ✅ 完成 | 低 | Stats 快照 |
| lumen_user_profile.proto | ✅ 完成 | 高 | State + 8 Events + DTOs |
| lumen_prediction.proto | ✅ 完成 | 高 | State + 6 Events + DTOs |
| lumen_feedback.proto | ✅ 完成 | 中 | State + 3 Events + DTOs |
| lumen_favourite.proto | ✅ 完成 | 低 | State + 2 Events + DTOs |
| lumen_history.proto | ✅ 完成 | 中 | State + 3 Events + DTOs |

### Phase 2: Agent 迁移 (按行数从小到大) ✅ 已完成

> **迁移策略 (每个Agent必须按此顺序执行):**
> 1. **Step 1: Partial Class 分析** - 分析代码结构，转换为 partial class
> 2. **Step 2: 代码风格修改** - 迁移到新框架写法
> 3. **Step 3: 拆分评估** - 考虑是否需要拆分成多个 Agent
> 4. **Step 4: 单元测试** - 验证功能正确

| 优先级 | Agent | 行数 | 状态 | 复杂度 | 单元测试 |
|--------|-------|------|------|--------|----------|
| 1 | LumenStatsSnapshotGAgent | 89 | ✅ 完成 | 低 | ✅ 有 |
| 2 | LumenDailyYearlyHistoryGAgent | 228 | ✅ 完成 | 中 | ✅ 有 |
| 3 | LumenFavouriteGAgent | 252 | ✅ 完成 | 低 | ✅ 有 |
| 4 | LumenPredictionHistoryGAgent | 298 | ✅ 完成 | 中 | ✅ 有 |
| 5 | LumenFeedbackGAgent | 375 | ✅ 完成 | 中 | ✅ 有 |
| **6** | **LumenUserProfileGAgent** | **1712** | ✅ 完成 | **高** | ✅ 有 (3 partial files) |
| **7** | **LumenPredictionGAgent** | **6935** | ✅ 完成 | **极高** | ✅ 有 (9 partial files) |
| - | LumenUserGAgent | 716 | ❌ 跳过 | - | DEPRECATED |

### Phase 3: 辅助组件迁移 (优先级: 中) ✅ 已完成

| 组件 | 状态 | 位置 |
|------|------|------|
| LumenCalculator | ✅ 完成 | `Calculators/LumenCalculator.cs` |
| WesternAstrologyService | ✅ 完成 | `Prediction/Services/WesternAstrologyService.cs` |
| SolarTermCalculator | ✅ 完成 | `Helpers/SolarTermCalculator.cs` |
| LumenTimezoneHelper | ✅ 完成 | `Helpers/LumenTimezoneHelper.cs` |
| LuckyNumberService | ✅ 完成 | `Services/LuckyNumberService.cs` |
| LumenOptions | ✅ 完成 | `Options/LumenOptions.cs` |
| TranslationDictionaries | ✅ 完成 | `Prediction/Dictionaries/TranslationDictionaries.cs` |
| TranslationHelpers | ✅ 完成 | `Prediction/Services/TranslationHelpers.cs` |

### Phase 4: 服务层适配 (优先级: 低) ✅ 已完成

| 任务 | 状态 | 备注 |
|------|------|------|
| ILumenService | ✅ 完成 | 聚合接口 |
| LumenService | ✅ 完成 | 服务实现 (partial class) |
| LumenController | ✅ 完成 | 3个 partial files |
| API 测试脚本 | ✅ 完成 | `scripts/test-lumen-flow.sh` |

---

## 🔄 单Agent迁移模板 (三步走)

> **每个Agent迁移必须严格按照以下三步执行**

### Step 1: Partial Class 分析 (分析阶段)

**目标**: 理解代码结构，规划拆分策略

**检查清单**:
```markdown
- [ ] 统计总行数
- [ ] 识别方法分类:
  - [ ] Event Handlers (处理事件)
  - [ ] RPC Methods (对外接口)
  - [ ] Business Logic (业务逻辑)
  - [ ] Helper Methods (辅助方法)
  - [ ] Constants/Dictionaries (常量)
- [ ] 分析依赖关系:
  - [ ] 依赖哪些其他Agent?
  - [ ] 被哪些Agent依赖?
  - [ ] 使用哪些外部服务?
- [ ] 判断是否需要partial class:
  - [ ] 行数 > 300: 建议partial
  - [ ] 行数 > 500: 必须partial
  - [ ] 行数 > 800: 必须拆分为多文件
- [ ] 规划partial class文件结构
```

**输出**: `XXXGAgent.分析报告.md`

---

### Step 2: 代码风格修改 (迁移阶段)

**目标**: 将代码从旧框架迁移到新框架

**标准迁移步骤**:

```csharp
// ============ 1. 基类变更 ============
// 旧:
public class MyAgent : GAgentBase<MyState, MyEventLog>, IMyAgent
// 新:
public class MyAgent : GAgentBase<MyStateProto>, IMyAgent

// ============ 2. 删除 GAgentTransitionState ============
// 旧:
protected sealed override void GAgentTransitionState(
    MyState state, StateLogEventBase<MyEventLog> @event)
{
    switch (@event) { ... }
}
// 新: 删除此方法，使用 [EventHandler]

// ============ 3. 添加 EventHandler ============
// 旧: 在 switch 中处理
// 新:
[EventHandler]
public Task HandleMyEvent(MyEventProto evt)
{
    State.SomeField = evt.Value;
    return Task.CompletedTask;
}

// ============ 4. 事件确认方式 ============
// 旧:
RaiseEvent(new MyEvent { ... });
await ConfirmEvents();
// 新:
RaiseEvent(new MyEventProto { ... });
await ConfirmEventsAsync();

// ============ 5. ID 获取方式 ============
// 旧: this.GetPrimaryKey() 或 this.GetPrimaryKeyString()
// 新: this.Id

// ============ 6. 构造函数 ============
// 新框架要求无参构造函数:
public MyAgent() : base() { }

// ============ 7. GetDescriptionAsync ============
// 必须实现:
public override Task<string> GetDescriptionAsync()
    => Task.FromResult($"MyAgent: {State.SomeInfo}");

// ============ 8. OnActivateAsync ============
// 状态初始化在此进行:
public override async Task OnActivateAsync(CancellationToken ct = default)
{
    await base.OnActivateAsync(ct);
    // 初始化 State 属性 (不要赋值新对象)
    State.UserId = Id.ToString("N")[..8];
}
```

**检查清单**:
```markdown
- [ ] 更新基类签名
- [ ] 删除 GAgentTransitionState
- [ ] 为每个事件添加 [EventHandler]
- [ ] 修改 ConfirmEvents → ConfirmEventsAsync
- [ ] 更新 ID 获取方式
- [ ] 添加无参构造函数
- [ ] 实现 GetDescriptionAsync
- [ ] 在 OnActivateAsync 中初始化状态
- [ ] 更新所有类型引用为 Proto 版本
- [ ] 处理 DateOnly/TimeOnly 转换
- [ ] 处理 Dictionary → map 转换
- [ ] 编译通过
- [ ] 单元测试通过
```

---

### Step 3: 拆分评估 (决策阶段)

**目标**: 评估是否需要将Agent拆分为多个独立Agent

**评估维度**:

| 维度 | 保持单一Agent | 拆分为多个Agent |
|------|---------------|-----------------|
| 状态隔离 | 所有状态紧密相关 | 不同状态独立演进 |
| 调用频率 | 各方法调用频率相近 | 部分方法调用频率高很多 |
| 失败隔离 | 可接受单点故障 | 需要故障隔离 |
| 扩展性 | 整体扩展即可 | 需要独立扩展 |
| 代码复杂度 | partial class可管理 | 逻辑差异过大 |
| 团队协作 | 单人负责 | 多人并行开发 |

**决策模板**:
```markdown
## 拆分评估报告: XXXGAgent

### 当前状态
- 总行数: xxx
- 方法数: xxx
- 事件数: xxx
- partial class 文件数: xxx

### 拆分候选
1. 候选方案A: [描述]
   - 优点: [...]
   - 缺点: [...]
   
2. 候选方案B: [描述]
   - 优点: [...]
   - 缺点: [...]

### 决策
- [ ] 保持单一Agent + partial class
- [ ] 拆分为 N 个Agent

### 理由
[详细说明决策依据]
```

**输出**: `XXXGAgent.拆分决策.md`

---

### Step 4: 单元测试 (迁移完成的唯一标准) ✅

**目标**: 编写单元测试，验证迁移后的Agent功能正确

**测试文件位置**:
```
apps/Aevatar.App/test/Aevatar.App.Application.Tests/Agents/Lumen/
├── LumenStatsSnapshotGAgentTests.cs
├── LumenDailyYearlyHistoryGAgentTests.cs
├── LumenFavouriteGAgentTests.cs
├── LumenPredictionHistoryGAgentTests.cs
├── LumenFeedbackGAgentTests.cs
├── LumenUserProfileGAgentTests.cs
└── LumenPredictionGAgentTests.cs
```

**测试模板** (参考 `UserFeedbackGAgentTests.cs`):
```csharp
using System;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Shouldly;
using Xunit;

namespace Aevatar.App.Agents.Lumen;

/// <summary>
/// Unit tests for LumenXxxGAgent
/// Uses TestHelpers pattern - no ABP framework dependency
/// </summary>
public class LumenXxxGAgentTests
{
    private LumenXxxGAgent CreateAgent()
    {
        var agent = TestHelpers.CreateAgent<LumenXxxGAgent>();
        
        // Setup any required dependencies using NSubstitute
        // var mockService = Substitute.For<IXxxService>();
        // agent.XxxService = mockService;
        
        return agent;
    }

    [Fact(DisplayName = "Method should do expected behavior")]
    public async Task Method_ShouldDoExpectedBehavior()
    {
        // Arrange
        var agent = CreateAgent();
        var request = new XxxRequestProto { /* ... */ };

        // Act
        var result = await agent.MethodAsync(request);

        // Assert
        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
        
        // Verify state changes
        var state = agent.GetState();
        state.SomeField.ShouldBe(expectedValue);
    }
}
```

**测试覆盖要求**:

| 类型 | 覆盖要求 | 示例 |
|------|----------|------|
| RPC 方法 | 100% | `ToggleFavouriteAsync`, `GetFavouritesAsync` |
| EventHandler | 100% | `HandlePredictionFavourited`, `HandlePredictionUnfavourited` |
| 状态转换 | Happy Path + Edge Cases | 空收藏、重复收藏、取消不存在的收藏 |
| 边界条件 | 至少覆盖 | 空输入、无效ID、超限 |

**检查清单**:
```markdown
- [ ] 创建测试文件: Agents/Lumen/LumenXxxGAgentTests.cs
- [ ] 实现 CreateAgent() helper
- [ ] 测试每个 RPC 方法的 Happy Path
- [ ] 测试每个 RPC 方法的 Error Path
- [ ] 测试状态初始化
- [ ] 测试 EventHandler 处理
- [ ] 运行 `dotnet test` 通过
- [ ] 代码审查通过
```

**运行测试命令**:
```bash
# 运行所有 Lumen 相关测试
dotnet test apps/Aevatar.App/test/Aevatar.App.Application.Tests/ \
  --filter "FullyQualifiedName~Lumen"

# 运行单个测试文件
dotnet test apps/Aevatar.App/test/Aevatar.App.Application.Tests/ \
  --filter "FullyQualifiedName~LumenFavouriteGAgentTests"
```

**迁移完成标准**:
```
🔴 以下条件全部满足才算迁移完成:
   ✅ 代码编译通过
   ✅ 单元测试全部通过
   ✅ 测试覆盖所有 RPC 方法
   ✅ 测试覆盖所有 EventHandler
   ✅ 无 lint 错误
```

---

## 🔄 详细迁移说明

### 1. State 类型转换示例

**旧版 C# State:**
```csharp
[GenerateSerializer]
public class LumenUserProfileState : StateBase
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    [Id(2)] public GenderEnum Gender { get; set; }
    [Id(3)] public DateOnly BirthDate { get; set; }
    [Id(4)] public TimeOnly? BirthTime { get; set; }
    // ... 28+ 字段
}
```

**新版 Protobuf:**
```protobuf
message LumenUserProfileState {
    string user_id = 1;
    string full_name = 2;
    GenderEnum gender = 3;
    DateValue birth_date = 4;  // 自定义日期类型
    optional TimeValue birth_time = 5;  // 可选
    // ...
}

// 自定义日期类型 (Protobuf 不支持 DateOnly)
message DateValue {
    int32 year = 1;
    int32 month = 2;
    int32 day = 3;
}
```

### 2. Event 类型转换示例

**旧版 C# Event:**
```csharp
[GenerateSerializer]
public class UserProfileUpdatedEvent : LumenUserProfileEventLog
{
    [Id(0)] public string UserId { get; set; } = string.Empty;
    [Id(1)] public string FullName { get; set; } = string.Empty;
    // ...
}
```

**新版 Protobuf:**
```protobuf
message UserProfileUpdatedEvent {
    string user_id = 1;
    string full_name = 2;
    GenderEnum gender = 3;
    DateValue birth_date = 4;
    // ...
}
```

### 3. Agent 迁移示例

**旧版 Agent:**
```csharp
[GAgent(nameof(LumenFavouriteGAgent))]
[Reentrant]
public class LumenFavouriteGAgent : GAgentBase<LumenFavouriteState, LumenFavouriteEventLog>,
    ILumenFavouriteGAgent
{
    protected sealed override void GAgentTransitionState(
        LumenFavouriteState state,
        StateLogEventBase<LumenFavouriteEventLog> @event)
    {
        switch (@event)
        {
            case PredictionFavouritedEvent e:
                state.Favourites[e.PredictionId] = e.FavouriteDetail;
                break;
        }
    }

    public async Task<ToggleFavouriteResult> ToggleFavouriteAsync(...)
    {
        RaiseEvent(new PredictionFavouritedEvent { ... });
        await ConfirmEvents();
        return result;
    }
}
```

**新版 Agent:**
```csharp
public class LumenFavouriteGAgent : GAgentBase<LumenFavouriteStateProto>,
    ILumenFavouriteGAgent
{
    public LumenFavouriteGAgent() : base() { }

    public override Task<string> GetDescriptionAsync()
        => Task.FromResult("Lumen favourite management");

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        // State properties 自动初始化
    }

    [EventHandler]
    public Task HandlePredictionFavourited(PredictionFavouritedEventProto evt)
    {
        State.Favourites[evt.PredictionId] = evt.FavouriteDetail;
        return Task.CompletedTask;
    }

    public async Task<ToggleFavouriteResultProto> ToggleFavouriteAsync(...)
    {
        RaiseEvent(new PredictionFavouritedEventProto { ... });
        await ConfirmEventsAsync();
        return result;
    }
}
```

### 4. 接口获取方式变更

**旧版:**
```csharp
var grain = _grainFactory.GetGrain<ILumenPredictionGAgent>(
    CommonHelper.StringToGuid(userId),
    $"{PredictionType.Daily}_{today:yyyy-MM-dd}");
await grain.TriggerTranslationAsync(userInfo, targetLanguage);
```

**新版:**
```csharp
var actor = await _actorFactory.CreateGAgentActorAsync<LumenPredictionGAgent>(
    CommonHelper.StringToGuid(userId),
    $"{PredictionType.Daily}_{today:yyyy-MM-dd}");
var agent = actor.As<ILumenPredictionGAgent>();
await agent.TriggerTranslationAsync(userInfo, targetLanguage);
```

---

## 📝 Protobuf 详细定义规划

### lumen_common.proto

```protobuf
syntax = "proto3";
package lumen;

import "google/protobuf/timestamp.proto";

// ============ 日期/时间类型 ============
message DateValue {
    int32 year = 1;
    int32 month = 2;
    int32 day = 3;
}

message TimeValue {
    int32 hour = 1;
    int32 minute = 2;
    int32 second = 3;
}

// ============ 枚举定义 ============
enum GenderEnum {
    GENDER_UNKNOWN = 0;
    GENDER_MALE = 1;
    GENDER_FEMALE = 2;
    GENDER_OTHER = 3;
}

enum MbtiTypeEnum {
    MBTI_UNKNOWN = 0;
    MBTI_INTJ = 1;
    MBTI_INTP = 2;
    MBTI_ENTJ = 3;
    MBTI_ENTP = 4;
    // ... 16 types
}

enum RelationshipStatusEnum {
    RELATIONSHIP_UNKNOWN = 0;
    RELATIONSHIP_SINGLE = 1;
    RELATIONSHIP_IN_RELATIONSHIP = 2;
    RELATIONSHIP_MARRIED = 3;
    RELATIONSHIP_SITUATIONSHIP = 4;
}

enum CalendarTypeEnum {
    CALENDAR_SOLAR = 0;
    CALENDAR_LUNAR = 1;
}

enum InterestEnum {
    INTEREST_CAREER = 0;
    INTEREST_LOVE = 1;
    INTEREST_SEX = 2;
    INTEREST_WEALTH = 3;
    INTEREST_HEALTH = 4;
    INTEREST_SOCIAL_RELATIONSHIPS = 5;
    INTEREST_LEARNING = 6;
}

enum PredictionType {
    PREDICTION_DAILY = 0;
    PREDICTION_YEARLY = 1;
    PREDICTION_LIFETIME = 2;
}

enum ZodiacSignEnum {
    ZODIAC_UNKNOWN = 0;
    ZODIAC_ARIES = 1;
    ZODIAC_TAURUS = 2;
    // ... 12 signs
}

enum ChineseZodiacEnum {
    CHINESE_ZODIAC_UNKNOWN = 0;
    CHINESE_ZODIAC_RAT = 1;
    CHINESE_ZODIAC_OX = 2;
    // ... 12 animals
}

enum TarotCardEnum {
    TAROT_UNKNOWN = 0;
    TAROT_THE_FOOL = 1;
    TAROT_THE_MAGICIAN = 2;
    // ... 78 cards
}

enum TarotOrientationEnum {
    ORIENTATION_UNKNOWN = 0;
    ORIENTATION_UPRIGHT = 1;
    ORIENTATION_REVERSED = 2;
}
```

### lumen_user_profile.proto

```protobuf
syntax = "proto3";
package lumen;

import "lumen_common.proto";
import "google/protobuf/timestamp.proto";

// ============ State ============
message LumenUserProfileState {
    string user_id = 1;
    string full_name = 2;
    GenderEnum gender = 3;
    DateValue birth_date = 4;
    optional TimeValue birth_time = 5;
    optional string birth_city = 6;
    optional string lat_long = 7;
    optional MbtiTypeEnum mbti_type = 8;
    optional RelationshipStatusEnum relationship_status = 9;
    optional string interests = 10;
    optional CalendarTypeEnum calendar_type = 11;
    repeated string actions = 12;
    optional string current_residence = 13;
    optional string email = 14;
    google.protobuf.Timestamp created_at = 15;
    google.protobuf.Timestamp updated_at = 16;
    optional string occupation = 17;
    optional string icon = 18;
    bool is_deleted = 19;
    map<string, WelcomeNoteValue> multilingual_welcome_note = 20;
    repeated google.protobuf.Timestamp update_history = 21;
    repeated google.protobuf.Timestamp icon_upload_history = 22;
    string current_language = 23;
    optional DateValue last_language_switch_date = 24;
    int32 today_language_switch_count = 25;
    optional string lat_long_inferred = 26;
    optional string inferred_from_city = 27;
    optional string current_time_zone = 28;
    repeated InterestEnum interests_list = 29;
}

message WelcomeNoteValue {
    map<string, string> values = 1;
}

// ============ Events ============
message UserProfileUpdatedEvent {
    string user_id = 1;
    string full_name = 2;
    GenderEnum gender = 3;
    DateValue birth_date = 4;
    optional TimeValue birth_time = 5;
    optional string birth_city = 6;
    optional string lat_long = 7;
    optional MbtiTypeEnum mbti_type = 8;
    optional RelationshipStatusEnum relationship_status = 9;
    optional string interests = 10;
    optional CalendarTypeEnum calendar_type = 11;
    google.protobuf.Timestamp updated_at = 12;
    optional string current_residence = 13;
    optional string email = 14;
    optional string occupation = 15;
    optional string icon = 16;
    optional string current_time_zone = 17;
    repeated InterestEnum interests_list = 18;
}

message UserProfileClearedEvent {
    google.protobuf.Timestamp cleared_at = 1;
}

message IconUpdatedEvent {
    string user_id = 1;
    optional string icon_url = 2;
    google.protobuf.Timestamp updated_at = 3;
    google.protobuf.Timestamp upload_timestamp = 4;
}

message UserProfileLanguageSwitchedEvent {
    string user_id = 1;
    string previous_language = 2;
    string new_language = 3;
    google.protobuf.Timestamp switched_at = 4;
    DateValue switch_date = 5;
    int32 today_count = 6;
}

message TimeZoneUpdatedEvent {
    string user_id = 1;
    string time_zone_id = 2;
    google.protobuf.Timestamp updated_at = 3;
}

message UserProfileLatLongInferredEvent {
    string user_id = 1;
    string lat_long_inferred = 2;
    string birth_city = 3;
    google.protobuf.Timestamp inferred_at = 4;
}

// ============ DTOs ============
message LumenUserProfileDto {
    string user_id = 1;
    string full_name = 2;
    GenderEnum gender = 3;
    DateValue birth_date = 4;
    TimeValue birth_time = 5;
    string birth_city = 6;
    string lat_long = 7;
    optional CalendarTypeEnum calendar_type = 8;
    google.protobuf.Timestamp created_at = 9;
    optional string current_residence = 10;
    google.protobuf.Timestamp updated_at = 11;
    map<string, string> welcome_note = 12;
    string zodiac_sign = 13;
    ZodiacSignEnum zodiac_sign_enum = 14;
    string chinese_zodiac = 15;
    ChineseZodiacEnum chinese_zodiac_enum = 16;
    optional string occupation = 17;
    optional MbtiTypeEnum mbti_type = 18;
    optional RelationshipStatusEnum relationship_status = 19;
    optional string interests = 20;
    repeated InterestEnum interests_list = 21;
    optional string email = 22;
    optional string icon = 23;
    optional string current_time_zone = 24;
    string current_language = 25;
    optional string lat_long_inferred = 26;
    optional string inferred_from_city = 27;
}

// Request/Response messages...
```

---

## ⚠️ 特殊处理项

### 1. LumenPredictionGAgent 拆分策略

由于 `LumenPredictionGAgent.cs` 有 6935 行，需要拆分为多个文件：

```
LumenPredictionGAgent.cs           # 主类 + 公共方法
LumenPredictionGAgent.Daily.cs     # Daily 预测逻辑 (partial)
LumenPredictionGAgent.Yearly.cs    # Yearly 预测逻辑 (partial)
LumenPredictionGAgent.Lifetime.cs  # Lifetime 预测逻辑 (partial)
LumenPredictionGAgent.Translation.cs # 翻译逻辑 (partial)
LumenPredictionGAgent.Dictionaries.cs # 翻译字典 (partial)
```

### 2. IRemindable 接口处理

`LumenPredictionGAgent` 实现了 `IRemindable` 接口用于 Orleans Reminders。
新框架暂不支持 Reminders，需要：
- 选项 A: 移除 Reminder 功能，改用外部调度器
- 选项 B: 保留但标记为 TODO，等待框架支持

### 3. DateOnly/TimeOnly 类型转换

Protobuf 不原生支持 `DateOnly`/`TimeOnly`，需要：
- 定义自定义 `DateValue`/`TimeValue` 消息
- 创建扩展方法进行转换

### 4. Dictionary 字段处理

State 中有多个 `Dictionary<string, T>` 字段：
- Protobuf 支持 `map<K, V>` 语法
- 复杂值类型需要定义为单独的 message

---

## 🚀 执行顺序

> **核心原则: 从小到大，每个Agent三步走**
> - 优先迁移小型Agent，积累经验
> - LumenUserProfileGAgent (1712行) 倒数第二
> - LumenPredictionGAgent (6935行) 最后迁移

---

### Week 1: 基础设施 ✅ 完成
1. ✅ 创建分支 `feature/lumen-migration`
2. ✅ 创建项目结构 `agents/Aevatar.Agents.Lumen/`
3. ✅ 定义 `lumen_common.proto`
4. ✅ 配置 .csproj 文件

### Week 2: 核心 Proto 定义 ✅ 完成
1. ✅ 定义 `lumen_stats.proto` (最简单)
2. ✅ 定义 `lumen_history.proto`
3. ✅ 定义 `lumen_favourite.proto`
4. ✅ 定义 `lumen_feedback.proto`
5. ✅ 定义 `lumen_user_profile.proto`
6. ✅ 定义 `lumen_prediction.proto`
7. ✅ 生成 Proto 代码并验证

---

### Week 3-4: Agent 迁移 ✅ 已完成

所有 7 个 Agent 已成功迁移，使用 partial class 模式管理大型文件：

| Agent | 文件数 | 测试状态 |
|-------|--------|----------|
| LumenStatsSnapshotGAgent | 1 | ✅ |
| LumenDailyYearlyHistoryGAgent | 1 | ✅ |
| LumenFavouriteGAgent | 1 | ✅ |
| LumenPredictionHistoryGAgent | 1 | ✅ |
| LumenFeedbackGAgent | 1 | ✅ |
| LumenUserProfileGAgent | 3 partial | ✅ |
| LumenPredictionGAgent | 9 partial | ✅ |

---

### Week 5-6: 服务层 & 集成 ✅ 已完成

1. ✅ LumenService 实现 (partial class)
2. ✅ LumenController (3 partial files)
3. ✅ API 测试脚本创建
4. ✅ 单元测试编写

---

## 📊 依赖关系图

```
lumen_common.proto (基础)
    ↓
lumen_user_profile.proto
    ↓
lumen_prediction.proto → lumen_history.proto
    ↓
lumen_feedback.proto
lumen_favourite.proto (独立)

Agent 依赖:
LumenUserProfileGAgent ← LumenPredictionGAgent
                      ← LumenFeedbackGAgent
                      ← LumenFavouriteGAgent
                      ← LumenPredictionHistoryGAgent
```

---

## 🧪 测试策略

### 单元测试
- 每个 Agent 的 EventHandler 测试
- State 转换测试
- Proto 序列化/反序列化测试

### 集成测试
- Agent 间通信测试
- 完整预测流程测试
- 语言切换测试

### 回归测试
- 对比新旧实现的输出
- 性能基准测试

---

## 📚 参考文档

- [GODGPT_ARCHITECTURE.md](./GODGPT_ARCHITECTURE.md) - GodGPT 迁移参考
- [AGENTS.md](../AGENTS.md) - Agent 框架规范
- [AEVATAR_FRAMEWORK_GUIDE.md](./AEVATAR_FRAMEWORK_GUIDE.md) - 框架指南

---

---

## 🎉 迁移完成总结

### 完成日期
- 开始: 2025-12-31
- 完成: 2026-01-08

### 最终项目结构

```
agents/Aevatar.Agents.Lumen/
├── Protos/                      # 7 proto 文件
│   ├── lumen_common.proto
│   ├── lumen_stats.proto
│   ├── lumen_user_profile.proto
│   ├── lumen_prediction.proto
│   ├── lumen_feedback.proto
│   ├── lumen_favourite.proto
│   └── lumen_history.proto
├── Stats/
│   └── LumenStatsSnapshotGAgent.cs
├── UserProfile/                 # 3 partial files
│   ├── LumenUserProfileGAgent.cs
│   ├── LumenUserProfileGAgent.Helpers.cs
│   └── LumenUserProfileGAgent.Language.cs
├── Prediction/                  # 9 partial files
│   ├── LumenPredictionGAgent.cs
│   ├── LumenPredictionGAgent.Api.cs
│   ├── LumenPredictionGAgent.Generation.cs
│   ├── LumenPredictionGAgent.LifetimeBatch.cs
│   ├── LumenPredictionGAgent.Parsing.cs
│   ├── LumenPredictionGAgent.Prompts.cs
│   ├── LumenPredictionGAgent.Prompts.Yearly.cs
│   ├── LumenPredictionGAgent.Translation.cs
│   └── LumenPredictionGAgent.Utilities.cs
├── History/
│   ├── LumenDailyYearlyHistoryGAgent.cs
│   └── LumenPredictionHistoryGAgent.cs
├── Feedback/
│   └── LumenFeedbackGAgent.cs
├── Favourite/
│   └── LumenFavouriteGAgent.cs
├── Calculators/
│   └── LumenCalculator.cs
├── Helpers/
│   ├── LumenTimezoneHelper.cs
│   ├── SolarTermCalculator.cs
│   └── SolarTermData.cs
├── Services/
│   └── LuckyNumberService.cs
└── Options/
    └── LumenOptions.cs
```

### 测试文件

```
apps/Aevatar.App/test/Aevatar.App.Application.Tests/
├── Agents/Lumen/
│   ├── LumenStatsSnapshotGAgentTests.cs
│   ├── LumenDailyYearlyHistoryGAgentTests.cs
│   ├── LumenFavouriteGAgentTests.cs
│   ├── LumenPredictionHistoryGAgentTests.cs
│   ├── LumenFeedbackGAgentTests.cs
│   ├── LumenUserProfileGAgentTests.cs
│   └── LumenPredictionGAgentTests.cs
└── Services/Lumen/
    └── LumenServiceTests.cs

scripts/
└── test-lumen-flow.sh           # API 端到端测试
```

### 运行测试命令

```bash
# 单元测试
dotnet test apps/Aevatar.App/test/Aevatar.App.Application.Tests/ \
  --filter "FullyQualifiedName~Lumen"

# API 集成测试 (需要服务运行)
./scripts/test-lumen-flow.sh
```

---

*最后更新: 2026-01-08*
*迁移状态: ✅ 已完成*

