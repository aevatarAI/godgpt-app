# GodGPT Migration Plan

## 概述

将 `old/godgpt/src/GodGPT.GAgents` 和 `old/godgpt-api/src/Aevatar.HttpApi` 迁移到新框架。

---

## 📋 迁移状态总览

### Agent 迁移状态

| Agent | 代码行数 | 状态 | Proto | 备注 |
|-------|---------|------|-------|------|
| ConfigurationGAgent | ~200 | ✅ 完成 | ✅ | |
| UserStatisticsGAgent | ~300 | ✅ 完成 | ✅ | |
| InviteCodeGAgent | ~350 | ✅ 完成 | ✅ | |
| InvitationGAgent | ~400 | ✅ 完成 | ✅ | |
| AnonymousUserGAgent | ~250 | ✅ 完成 | ✅ | |
| UserFeedbackGAgent | ~200 | ✅ 完成 | ✅ | |
| FreeTrialCodeFactoryGAgent | ~300 | ✅ 完成 | ✅ | |
| UserInfoCollectionGAgent | ~250 | ✅ 完成 | ✅ | |
| UserQuotaGAgent | 799 | ✅ 完成 | ✅ | |
| GlobalJwtProviderGAgent | ~100 | ✅ 完成 | ✅ | |
| FirebaseTokenProviderGAgent | ~200 | ✅ 完成 | ✅ | |
| DailyContentGAgent | ~150 | ✅ 完成 | ✅ | |
| PushSubscriberIndexGAgent | ~150 | ✅ 完成 | ✅ | |
| AwakeningGAgent | ~300 | ✅ 完成 | ✅ | |
| **GodChatGAgent** | 2362 | ✅ 完成 | ✅ | 大型 |
| **ChatManagerGAgent** | 3163 | ✅ 完成 | ✅ | 大型 |
| **UserBillingGAgent** | 5484 | ✅ 完成 | ✅ | 大型 |
| DailyPushCoordinatorGAgent | 1128 | ⏸️ 暂停 | - | 依赖Orleans Reminders |

### API 迁移状态

| Controller | 行数 | 状态 | 依赖 Service |
|------------|------|------|--------------|
| GodGPTController | 875 | ✅ 完成 | IGodGPTService |
| GodGPTPaymentController | 249 | ✅ 完成 | IGodGPTService |
| GodGPTInvitationController | 122 | ✅ 完成 | IGodGPTService |
| GodGPTConfigController | 113 | ✅ 完成 | IGodGPTService |
| GodGPTManagementController | 175 | ✅ 完成 | IUserFeedbackService, IGodGPTService |
| DailyPushController | 164 | ✅ 完成 | IDailyPushService |
| GodGPTTwitterManagementController | 313 | ❌ 跳过 | Twitter 不再使用 |
| GodGPTGoogleAuthController | 61 | ❌ 跳过 | Google Auth 不再使用 |

### Service 迁移状态

| Service | 行数 | 状态 | 备注 |
|---------|------|------|------|
| GodGPTService | 1368 | ✅ 完成 | 核心业务服务 |
| DTOs | 31 files | ✅ 完成 | 请求/响应 DTOs |
| Service Contracts | 4 files | ✅ 完成 | IDailyPushService 等 |

---

## 🔴 Protobuf 规则

### 类型映射

| C# 类型 | Protobuf 类型 | 备注 |
|---------|---------------|------|
| `Guid` | `string` | 自动转换 |
| `DateTime` | `google.protobuf.Timestamp` | UTC |
| `T?` (nullable) | `optional T` | Proto3 语法 |
| `List<T>` | `repeated T` | 集合 |
| `Dictionary<K,V>` | `map<K,V>` | 字典 |
| 带负值枚举 | `int32` | Proto3 不支持负数枚举 |

### 命名规则

- C# `PascalCase` → Proto `snake_case`
- `[Id(N)]` → field number `N+1`

### Proto 枚举处理

```protobuf
// 定义枚举，值与 C# 枚举一致
enum PlanType {
    PLAN_TYPE_NONE = 0;
    PLAN_TYPE_DAY = 1;
    PLAN_TYPE_MONTH = 2;
    PLAN_TYPE_YEAR = 3;
}
```

```csharp
// 使用 using alias 避免冲突
using CsPlanType = Aevatar.Application.Grains.Common.Constants.PlanType;

// 直接强转
PlanType = (PlanType)initDto.PlanType,  // C# → Proto
PlanType = (CsPlanType)State.PlanType,  // Proto → C#
```

---

## 🔵 API 映射

### 方法映射

| 旧框架 | 新框架 | 说明 |
|--------|--------|------|
| `GAgentBase<TState, TEventLog>` | `GAgentBase<TState>` | 基类变更 |
| `this.GetPrimaryKey()` | `Id` | ID 获取 |
| `ConfirmEvents()` | `ConfirmEventsAsync()` | 异步确认 |
| `GAgentTransitionState` | `TransitionState` | 状态转换 |
| `GrainFactory.GetGrain<T>` | `_clusterClient.GetGrain<T>` | Grain 获取 |
| `PublishAsync(event)` | `PublishAsync(event.ToProto())` | 事件发布 |

### 配置机制

```csharp
// 旧框架
protected override async Task PerformConfigAsync(TConfig config) { }

// 新框架 - 继承 GAgentBase<TState, TConfig>
// Config 属性自动持久化，无需重写
```

---

## 📦 API 迁移计划 (NEW)

### 迁移范围

**来源**: `old/godgpt-api/src/Aevatar.HttpApi/Controllers/`
**目标**: `apps/Aevatar.App/src/Aevatar.App.HttpApi/Controllers/`

### 需要迁移的 Controller

1. **GodGPTController.cs** (875行) - 核心聊天 API
2. **GodGPTPaymentController.cs** (249行) - 支付 API
3. **GodGPTInvitationController.cs** (122行) - 邀请 API
4. **GodGPTConfigController.cs** (113行) - 配置 API
5. **GodGPTManagementController.cs** (175行) - 管理 API
6. **DailyPushController.cs** (164行) - 推送 API

### 需要迁移的 Service

**来源**: `old/godgpt-api/src/Aevatar.Application/Service/`
**目标**: `apps/Aevatar.App/src/Aevatar.App.Application/Services/`

1. **GodGPTService.cs** (1368行) - 核心业务服务

### 依赖变更

| 旧依赖 | 新依赖 | 说明 |
|--------|--------|------|
| `Aevatar.Core.Abstractions.IGAgent` | `Aevatar.Agents.Abstractions.IGAgent` | Agent 接口 |
| `GodGPT.GAgents.*` | `Aevatar.Agents.GodGPT.*` | Agent 引用 |
| `GrainFactory.GetGrain<T>` | `IGAgentFactory.CreateGAgent<T>` | 新框架 Agent |
| `GrainFactory.GetGrain<T>` | `IClusterClient.GetGrain<T>` | 未迁移 Grain |

### 迁移步骤

1. **复制 Controller 文件**
   ```bash
   cp old/godgpt-api/src/Aevatar.HttpApi/Controllers/GodGPT*.cs \
      apps/Aevatar.App/src/Aevatar.App.HttpApi/Controllers/
   cp old/godgpt-api/src/Aevatar.HttpApi/Controllers/DailyPushController.cs \
      apps/Aevatar.App/src/Aevatar.App.HttpApi/Controllers/
   ```

2. **复制 Service 文件**
   ```bash
   cp old/godgpt-api/src/Aevatar.Application/Service/GodGPTService.cs \
      apps/Aevatar.App/src/Aevatar.App.Application/Services/
   ```

3. **更新命名空间和 using**
   - 移除旧框架引用
   - 添加新框架引用 (`Aevatar.Agents.*`)
   - 更新 Agent 获取方式

4. **修改 Agent 调用**
   ```csharp
   // 旧代码 - 直接获取 Orleans Grain
   var agent = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
   
   // 新代码 - 使用 IGAgentFactory 获取新框架 Agent
   var agent = _agentFactory.CreateGAgent<ChatManagerGAgent>(userId);
   await agent.ActivateAsync();
   ```

5. **编译修复**
   - 修复类型不匹配
   - 更新方法签名

### 跳过的 Controller

- **GodGPTTwitterManagementController.cs** - Twitter 功能不再使用
- **GodGPTGoogleAuthController.cs** - Google Auth 功能不再使用

---

## 🟢 Event Sourcing 规则

### RaiseEvent 模式

```csharp
RaiseEvent(new SomeEvent { ... });  // 同步，暂存
await ConfirmEventsAsync();          // 异步，持久化
```

### TransitionState 中的枚举比较

```csharp
// Proto 字段是 int，需要显式转换
if (payment.Status == (int)PaymentStatus.Completed) { }
```

### RepeatedField 处理

```csharp
// 不能直接赋值，使用初始化器或 AddRange
state.PaymentHistory.Clear();
state.PaymentHistory.AddRange(newList);
```

---

## 🔶 大型 Agent 迁移模式

### 转换辅助类

每个大型 Agent 创建独立的 `XxxConversions.cs`:

```csharp
public static class GodChatConversions
{
    // C# → Protobuf
    public static ChatMessageProto ToProto(this ChatMessage msg) { }
    
    // Protobuf → C#
    public static ChatMessage FromProto(this ChatMessageProto proto) { }
    public static List<ChatMessage> FromProtoList(this RepeatedField<ChatMessageProto> protos) { }
}
```

### 批量替换脚本

```bash
# GrainFactory 替换
sed -i '' 's/GrainFactory\.GetGrain/_clusterClient.GetGrain/g' Agent.cs

# PublishAsync 替换
sed -i '' 's/await PublishAsync(chatMessage);/await PublishAsync(chatMessage.ToProto());/g' Agent.cs
```

### 依赖注入模式

```csharp
public MyAgent(
    IGAgentFactory agentFactory,     // 新框架 Agent
    IClusterClient clusterClient)    // 未迁移 Grain
{
    _agentFactory = agentFactory;
    _clusterClient = clusterClient;
}
```

---

## 📂 目录结构

```
agents/Aevatar.Agents.GodGPT/
├── Protos/                    # Protobuf 定义
│   ├── common.proto
│   ├── user_quota.proto
│   ├── chat_manager.proto
│   ├── god_chat.proto
│   ├── user_billing.proto
│   └── ...
├── ChatManager/
│   ├── ChatManagerGAgent.cs
│   └── ChatManagerConversions.cs
├── GodChat/
│   ├── GodChatGAgent.cs
│   └── GodChatConversions.cs
├── UserBilling/
│   ├── UserBillingGAgent.cs
│   └── UserBillingConversions.cs
└── ...

apps/Aevatar.App/src/
├── Aevatar.App.HttpApi/
│   └── Controllers/
│       ├── GodGPTController.cs      # NEW: 待迁移
│       ├── GodGPTPaymentController.cs
│       └── ...
└── Aevatar.App.Application/
    └── Services/
        └── GodGPTService.cs         # NEW: 待迁移
```

---

## ⚠️ 限制和注意事项

### Orleans Reminders 限制

新框架 `GAgentBase<T>` 不继承 `Orleans.Grain`，无法使用 `IRemindable`。

**解决方案**: `DailyPushCoordinatorGAgent` 暂不迁移，等待新框架支持。

### 数据迁移策略

**不在代码中实现迁移桥梁**。新旧系统并行运行，数据迁移由独立脚本处理。

---

## ✅ 验证清单

- [ ] 所有 Agent 编译通过
- [ ] 所有 Controller 编译通过
- [ ] Silo 启动成功
- [ ] API 端点可访问
- [ ] Event Sourcing 正常持久化
