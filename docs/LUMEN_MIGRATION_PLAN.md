# Lumen Module Migration Plan

> 创建日期: 2025-12-31
> 分支: feature/lumen-migration
> 基于: GodGPT Agent 迁移模式 (参考 GODGPT_ARCHITECTURE.md)

---

## 📊 迁移范围总览

```
Agent层:     10 GAgents 需迁移
State层:     8 State Classes → Protobuf
Event层:     ~30 Events → Protobuf
DTO层:       ~50 DTOs → Protobuf (部分可复用)
Service层:   1 Service
Helper层:    4 Helpers (可直接迁移)
Calculator层: 2 Calculators (可直接迁移)
```

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

### Phase 1: Protobuf 定义 (优先级: 高)

| 文件 | 状态 | 复杂度 | 备注 |
|------|------|--------|------|
| lumen_common.proto | ⏳ 待开始 | 中 | 枚举(12+), 公共消息 |
| lumen_user_profile.proto | ⏳ 待开始 | 高 | State + 8 Events + DTOs |
| lumen_prediction.proto | ⏳ 待开始 | 高 | State + 6 Events + DTOs |
| lumen_feedback.proto | ⏳ 待开始 | 中 | State + 3 Events + DTOs |
| lumen_favourite.proto | ⏳ 待开始 | 低 | State + 2 Events + DTOs |
| lumen_history.proto | ⏳ 待开始 | 中 | State + 3 Events + DTOs |

### Phase 2: Agent 迁移 (优先级: 高)

| Agent | 行数 | 状态 | 复杂度 | 依赖 |
|-------|------|------|--------|------|
| LumenUserProfileGAgent | 1712 | ⏳ 待开始 | 高 | lumen_user_profile.proto |
| LumenPredictionGAgent | 6935 | ⏳ 待开始 | 极高 | lumen_prediction.proto, AI集成 |
| LumenPredictionHistoryGAgent | 298 | ⏳ 待开始 | 中 | lumen_history.proto |
| LumenFeedbackGAgent | 375 | ⏳ 待开始 | 中 | lumen_feedback.proto |
| LumenFavouriteGAgent | 252 | ⏳ 待开始 | 低 | lumen_favourite.proto |
| LumenStatsSnapshotGAgent | 89 | ⏳ 待开始 | 低 | lumen_common.proto |
| LumenDailyYearlyHistoryGAgent | 228 | ⏳ 待开始 | 中 | lumen_history.proto |
| LumenUserGAgent | 716 | ❌ 跳过 | - | DEPRECATED |

### Phase 3: 辅助组件迁移 (优先级: 中)

| 组件 | 状态 | 备注 |
|------|------|------|
| LumenCalculator | ⏳ 待开始 | 可直接迁移 (纯计算逻辑) |
| WesternAstrologyCalculator | ⏳ 待开始 | 可直接迁移 |
| SolarTermCalculator | ⏳ 待开始 | 可直接迁移 |
| LumenTimezoneHelper | ⏳ 待开始 | 可直接迁移 |
| LuckyNumberService | ⏳ 待开始 | 可直接迁移 |
| LumenOptions | ⏳ 待开始 | 配置类保持C# |

### Phase 4: 服务层适配 (优先级: 低)

| 任务 | 状态 | 备注 |
|------|------|------|
| 创建 ILumenService | ⏳ 待开始 | 聚合接口 |
| 注册 DI | ⏳ 待开始 | - |
| API Controller 适配 | ⏳ 待开始 | 如有需要 |

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

### Week 1: 基础设施
1. ✅ 创建分支 `feature/lumen-migration`
2. ⏳ 创建项目结构 `agents/Aevatar.Agents.Lumen/`
3. ⏳ 定义 `lumen_common.proto`
4. ⏳ 配置 .csproj 文件

### Week 2: 核心 Proto 定义
1. ⏳ 定义 `lumen_user_profile.proto`
2. ⏳ 定义 `lumen_prediction.proto`
3. ⏳ 定义 `lumen_feedback.proto`
4. ⏳ 定义 `lumen_favourite.proto`
5. ⏳ 定义 `lumen_history.proto`
6. ⏳ 生成 Proto 代码并验证

### Week 3: Agent 迁移 (Part 1)
1. ⏳ 迁移 LumenFavouriteGAgent (最简单)
2. ⏳ 迁移 LumenFeedbackGAgent
3. ⏳ 迁移 LumenStatsSnapshotGAgent
4. ⏳ 编写单元测试

### Week 4: Agent 迁移 (Part 2)
1. ⏳ 迁移 LumenUserProfileGAgent
2. ⏳ 迁移 LumenPredictionHistoryGAgent
3. ⏳ 迁移 LumenDailyYearlyHistoryGAgent
4. ⏳ 编写单元测试

### Week 5-6: LumenPredictionGAgent 迁移
1. ⏳ 拆分并迁移 LumenPredictionGAgent
2. ⏳ 处理 AI 集成部分
3. ⏳ 处理 Reminder 逻辑
4. ⏳ 编写集成测试

### Week 7: 辅助组件 & 集成
1. ⏳ 迁移 Calculators
2. ⏳ 迁移 Helpers
3. ⏳ 创建服务层适配
4. ⏳ 端到端测试

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

*最后更新: 2025-12-31*
*分支: feature/lumen-migration*

