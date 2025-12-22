# GodGPT Architecture & Migration Status

> 更新日期: 2024-12-22
> 说明: 综合架构文档，整合迁移计划、进度和技术规范

---

## 📊 迁移进度总览

```
Agent层:  17/18 完成 (94%) ✅
API层:    6/18 验证通过 (33%) ⚠️
Service层: 已清理冲突 ✅
编译状态: 0 Errors ✅
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
| 状态定义 | C# Class | **Protobuf Message** (必须) |
| 事件定义 | C# Class | **Protobuf Message** (必须) |

### 核心原则

```
🔴 关键规则：
- 所有 State 和 Event 必须使用 Protobuf 定义
- Service层统一使用 IGAgentActorFactory
- Agent通过 Actor.GetAgent() 获取接口
```

---

## 📁 目录结构

```
agents/Aevatar.Agents.GodGPT/
├── Protos/                      # Protobuf 定义
│   ├── common.proto
│   ├── user_quota.proto
│   ├── chat_manager.proto
│   └── ...
├── ChatManager/                 # 会话管理
├── GodChat/                     # 聊天功能
├── UserBilling/                 # 计费相关
└── ...

apps/Aevatar.App/src/
├── Aevatar.App.HttpApi/
│   └── Controllers/             # API Controllers
├── Aevatar.App.Application/
│   └── Services/                # Service实现
│       ├── Admin/               # 新结构（按域分文件夹）
│       ├── Session/
│       ├── User/
│       └── InvitationService.cs # 旧结构（根目录）
└── Aevatar.App.Application.Contracts/
    └── Services/                # Service接口
        ├── Admin/               # 新结构（按域分文件夹）
        ├── Session/
        └── IUserQuotaService.cs # 旧结构（根目录）

modules/Aevatar.Payment/         # 独立支付模块
```

---

## ✅ 迁移状态详情

### Agent层 (17/18 完成)

| Agent | 状态 | Proto |
|-------|------|-------|
| ChatManagerGAgent (3163行) | ✅ 完成 | ✅ |
| GodChatGAgent (2362行) | ✅ 完成 | ✅ |
| UserBillingGAgent (5484行) | ✅ 完成 | ✅ |
| UserQuotaGAgent (799行) | ✅ 完成 | ✅ |
| 其他13个Agent | ✅ 完成 | ✅ |
| DailyPushCoordinatorGAgent | ⏸️ 暂停 | - |

> DailyPush暂停原因：依赖Orleans Reminders，新框架暂不支持

### API层 (6/18 验证通过)

#### ✅✅ 已验证 (6个)

| Controller | 测试脚本 | Service依赖 |
|------------|---------|------------|
| GodGPTConfigController | test-config-flow.sh | IGodGPTConfigService |
| GodGPTManagementController | test-management-flow.sh | IGodGPTAdminService |
| GodGPTInvitationController | test-invitation-flow.sh | IInvitationService |
| GodGPTPaymentController | test-payment-flow.sh | IPaymentService (modules) |
| GodGPTUserQuotaController | test-user-quota-flow.sh | IUserQuotaService |
| GodGPTUserStatisticsController | test-user-statistics-flow.sh | IUserStatisticsService |

#### ⚠️ 待验证 (9个)

| Controller | Service依赖 | 优先级 |
|------------|------------|--------|
| GodGPTSessionController | IGodGPTSessionService | 🔴 高 |
| GodGPTAccountController | IGodGPTUserService | 🟡 中 |
| GodGPTStorageController | IGodGPTUserService | 🟡 中 |
| GodGPTAnalyticsController | IGoogleAnalyticsService | 🟡 中 |
| GodGPTShareController | IGodGPTShareService | 🟡 中 |
| GodGPTGuestController | IGodGPTGuestService | 🟡 中 |
| GodGPTContentController | IGodGPTAwakeningService | 🟢 低 |
| GodGPTWebhookController | IPaymentService | 🟡 中 |
| GodGPTAppConfigController | IOptions | 🟢 低 |

#### ❌ 不再使用 (3个)

- DailyPushController - 已删除（新业务不需要）
- GodGPTController - 已注释（被拆分）
- GodGPTTwitterManagementController - 跳过

### Service层

#### 新结构Service (按域分文件夹)

| Service | 状态 |
|---------|------|
| IGodGPTUserService | ✅ |
| IGodGPTSessionService | ✅ |
| IGodGPTShareService | ✅ |
| IGodGPTGuestService | ✅ |
| IGodGPTConfigService | ✅ |
| IGodGPTAwakeningService | ✅ |
| IGodGPTAdminService | ✅ |
| IGodGPTSubscriptionService | ✅ |

#### 旧结构Service (根目录)

| Service | 状态 |
|---------|------|
| IInvitationService | ✅ 保留 |
| IUserQuotaService | ✅ 保留 |
| IUserStatisticsService | ✅ 保留 |
| IGodGPTService | ⚠️ 待清理 |

#### 已删除 (冲突/未使用)

- IGodGPTPaymentService - 被modules替代
- IGodGPTInvitationService - 与旧版冲突
- IGodGPTStatisticsService - 与旧版冲突

---

## 🔧 核心改动模式

### 1. Agent获取方式

```csharp
// ❌ 旧方式
var agent = _agentFactory.CreateGAgent<SomeAgent>(userId);
await agent.ActivateAsync();

// ✅ 新方式
var actor = await _actorFactory.CreateGAgentActorAsync<SomeAgent>(userId);
var agent = (ISomeAgent)actor.GetAgent();
```

### 2. 状态定义 (Protobuf)

```protobuf
// user_quota.proto
message UserQuotaState {
    string user_id = 1;
    int32 credits = 2;
    google.protobuf.Timestamp updated_at = 3;
}
```

### 3. 事件发布

```csharp
// ❌ 旧方式
RaiseEvent(new SomeEvent { ... });
await ConfirmEvents();

// ✅ 新方式
RaiseEvent(new SomeEventProto { ... });
await ConfirmEventsAsync();
```

### 4. ID获取

```csharp
// ❌ 旧方式
var id = this.GetPrimaryKey();

// ✅ 新方式
var id = this.Id;
```

---

## 📋 待完成事项

### 高优先级

- [ ] 创建 test-session-flow.sh 验证 GodGPTSessionController
- [ ] 验证 GodGPTAccountController（已修改）
- [ ] 验证 GodGPTStorageController（已修改）
- [ ] 验证 GodGPTAnalyticsController（已修改）

### 中优先级

- [ ] 创建其他Controller测试脚本
- [ ] 运行时集成测试

### 低优先级

- [ ] 清理 GodGPTService.cs（已无Controller依赖）
- [ ] 清理 GodGPTController.cs（已注释）

---

## 📝 Protobuf 规范

### 类型映射

| C# | Protobuf |
|----|----------|
| Guid | string |
| DateTime | google.protobuf.Timestamp |
| T? | optional T |
| List\<T\> | repeated T |
| Dictionary\<K,V\> | map\<K,V\> |

### 命名规则

- C# PascalCase → Proto snake_case
- `[Id(N)]` → field number `N+1`

---

## 🔗 相关文档

已合并的原文档：
- ~~CODE_STRUCTURE_UNIFICATION.md~~
- ~~GODGPT_MIGRATION_PLAN.md~~
- ~~MIGRATION_ANALYSIS_REPORT.md~~
- ~~MIGRATION_VERIFICATION_STATUS.md~~
- ~~SERVICE_LAYER_ANALYSIS.md~~

---

*最后更新: 2024-12-22*

