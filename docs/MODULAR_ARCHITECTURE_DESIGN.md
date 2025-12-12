# GodGPT 模块化架构设计

> 版本: 2.0 | 日期: 2025-12-12 | 状态: **简化方案**

---

## 1. 现状分析

### 当前项目结构 ✅ 保持不变

```
godgpt-app/
├── apps/Aevatar.App/
│   └── src/
│       ├── Aevatar.Silo/              # Orleans Grain Host
│       ├── Aevatar.App.HttpApi.Host/  # HTTP API入口
│       ├── Aevatar.App.HttpApi/       # Controller 层
│       └── Aevatar.App.Application/   # Service 层
│
├── agents/Aevatar.Agents.GodGPT/      # GodGPT 业务 Agent ✅ 保持
│   ├── ChatManager/
│   ├── GodChat/
│   ├── UserBilling/                   # → 迁移到 modules/Payment
│   ├── Invitation/
│   ├── UserQuota/
│   ├── PaymentAnalytics/              # GA4 上报 (业务层)
│   └── ...
│
└── modules/                           # 通用可复用模块
    ├── Aevatar.Payment/               # ✅ 已实现
    └── Aevatar.Payment.Agents/        # ✅ 已实现
```

### 问题代码统计

| 文件 | 行数 | 职责 | 状态 |
|------|------|------|------|
| `GodGPTService.cs` | 956 | 10+ | 🟡 逐步迁移 |
| `ChatManagerGAgent.cs` | 3163 | 15+ | 🔴 待拆分 |
| `UserBillingGAgent.cs` | 5469 | 12+ | ✅ **已迁移到 Payment 模块** |

---

## 2. 简化设计原则

### 核心决策：只抽取真正通用的模块

| 模块 | 是否通用 | 决策 |
|------|---------|------|
| **Payment** | ✅ 通用 | **抽取到 modules/** (Stripe/Apple/Google 逻辑与业务无关) |
| User (配额/反馈) | ❌ 业务相关 | 保持在 `Aevatar.Agents.GodGPT` |
| Invitation | ❌ 业务相关 | 保持在 `Aevatar.Agents.GodGPT` |
| Analytics | ❌ 业务相关 | 保持在 `Aevatar.Agents.GodGPT` |

### 为什么不全部抽取？

```
调整目录 = 高成本 + 低收益 (只有一个业务 GodGPT)
重构代码 = 中成本 + 高收益 (更好的可维护性、可测试性)
```

**结论：目录结构保持，代码层面重构**

---

## 3. 最终架构

### 项目结构

```
godgpt-app/
├── modules/                              # 通用可复用模块 (仅 Payment)
│   ├── Aevatar.Payment/                  # ✅ 支付服务层
│   │   ├── Abstractions/                 # 接口定义
│   │   ├── Services/PaymentService.cs    # 编排器
│   │   ├── Providers/                    # 策略实现
│   │   │   ├── StripeProvider.cs
│   │   │   ├── ApplePayProvider.cs
│   │   │   └── GooglePlayProvider.cs
│   │   └── Controllers/                  # 新 API
│   │
│   └── Aevatar.Payment.Agents/           # ✅ 支付 Agent
│       ├── PaymentIndexGAgent.cs         # 用户级索引
│       ├── PaymentRecordGAgent.cs        # 订单级记录
│       └── Protos/                       # Event Sourcing
│
├── agents/Aevatar.Agents.GodGPT/         # GodGPT 业务 Agent ✅ 保持
│   ├── ChatManager/                      # 会话管理 (待拆分)
│   ├── GodChat/                          # 单聊天
│   ├── Invitation/                       # 邀请码
│   ├── UserQuota/                        # 配额管理
│   ├── PaymentAnalytics/                 # GA4 上报 (订阅 PaymentCompletedEvent)
│   └── ...
│
└── apps/Aevatar.App/
    └── src/
        ├── Aevatar.App.HttpApi/          # Controller (保持兼容)
        │   └── Controllers/
        │       └── GodGPTPaymentController.cs  # 兼容层 → 调用 PaymentService
        ├── Aevatar.App.Application/      # Service 层 (保持)
        └── Aevatar.Silo/                 # Orleans Host
```

### 模块职责

| 层级 | 模块 | 职责 |
|------|------|------|
| **通用** | `Aevatar.Payment` | 支付策略、Provider、Webhook |
| **通用** | `Aevatar.Payment.Agents` | 支付状态、Event Sourcing、事件广播 |
| **业务** | `Aevatar.Agents.GodGPT` | 所有 GodGPT 业务 Agent |
| **业务** | `PaymentAnalytics` | 订阅支付事件 → GA4 上报 |
| **API** | `GodGPTPaymentController` | 兼容层，调用 PaymentService |

---

## 4. Payment 模块设计 ✅ 已实现

### 策略模式

```csharp
public interface IPaymentProvider
{
    PaymentPlatform Platform { get; }
    Task<List<ProductDto>> GetProductsAsync();
    Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest request);
    Task<WebhookResult> HandleWebhookAsync(WebhookRequest request);
}
```

### 事件广播机制

```
PaymentService → PaymentIndexGAgent → PublishAsync(Down) → 业务 Agent
                                                              ↓
                                                    PaymentAnalytics (GA4 上报)
                                                    UserQuotaGAgent (配额更新)
                                                    InvitationGAgent (邀请奖励)
```

### 添加新支付平台

```csharp
// 1. 新增 Provider
public class WeChatPayProvider : IPaymentProvider { ... }

// 2. 注册 DI
context.Services.AddScoped<IPaymentProvider, WeChatPayProvider>();

// Done! 无需修改其他代码
```

---

## 5. PaymentAnalytics 归属分析

### 通用性评估

| 组件 | 通用性 | 分析 |
|------|-------|------|
| GA4 HTTP Client | ✅ 通用 | 发送事件到 GA4 的底层逻辑 |
| `purchase` 事件 | ✅ 通用 | GA4 标准电商事件 |
| `refund` 事件 | ✅ 通用 | GA4 标准电商事件 |
| 重试机制 | ✅ 通用 | 与业务无关 |
| 幂等去重 | ✅ 通用 | transaction_id 机制 |

### 决策：移入 Payment 模块

```
modules/Aevatar.Payment/
├── Analytics/                        # 新增
│   ├── IPaymentAnalyticsService.cs   # 接口
│   └── GA4AnalyticsService.cs        # GA4 实现
└── Options/
    └── GA4Options.cs                 # GA4 配置
```

### 调用方式

**方式一：PaymentService 内部自动调用**

```csharp
// PaymentService.cs
private async Task ProcessWebhookResultAsync(WebhookResult result)
{
    // 更新 Agent 状态...
    
    // 自动上报 GA4 (可配置开关)
    if (_options.EnableAnalytics)
    {
        await _analyticsService.ReportPurchaseAsync(result);
    }
}
```

**方式二：业务层订阅事件后调用**

```csharp
// GodGPT 业务 Agent
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    // 业务逻辑...
    
    // 可选：业务层可以上报额外的自定义事件
    await _analyticsService.ReportCustomEventAsync("godgpt_subscription", evt);
}
```

---

## 6. 业务层集成

### 业务 Agent 订阅支付事件

```csharp
// UserQuotaGAgent.cs - 订阅支付完成事件
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    // 根据订阅计划更新配额
    var plan = evt.Context.BusinessMetadata["planType"];
    await UpdateQuotaByPlanAsync(plan);
}

// InvitationGAgent.cs - 处理邀请奖励
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    if (evt.Context.BusinessMetadata.TryGetValue("inviteCode", out var code))
    {
        await ProcessInvitationRewardAsync(code, evt.Context.UserId);
    }
}
```

### Agent 注册流程

```csharp
// 业务 Agent 启动时注册到 PaymentIndexGAgent
public override async Task OnActivateAsync(CancellationToken ct)
{
    await base.OnActivateAsync(ct);
    
    // 链接到支付索引 Agent，接收支付事件
    var paymentIndex = _agentManager.GetAgent<IPaymentIndexGAgent>(UserId);
    await _agentManager.LinkParentChildAsync(paymentIndex.Id, this.Id);
}
```

---

## 7. 待办事项

### ✅ 已完成

| 任务 | 状态 |
|------|------|
| Payment 模块核心 | ✅ |
| PaymentService 策略模式 | ✅ |
| PaymentIndexGAgent + PaymentRecordGAgent | ✅ |
| Event Sourcing | ✅ |
| GodGPTPaymentController 兼容层 | ✅ |
| 单元测试 | ✅ |
| E2E 测试脚本 | ✅ |

### 🔄 进行中

| 任务 | 说明 |
|------|------|
| Orleans RPC Proxy | 另一个 PR 进行中 |
| PaymentAnalytics 迁移 | 移入 Payment 模块 |

### 📋 待开始

| 任务 | 优先级 | 说明 |
|------|--------|------|
| 业务 Agent Event Handler | P1 | UserQuota, Invitation 订阅支付事件 |
| ChatManager 拆分 | P2 | 3163 行 → 多个小 Agent |
| 旧 UserBillingGAgent 清理 | P1 | 删除迁移完成后的旧代码 |

---

## 8. 迁移策略

### 简化迁移阶段

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 1 | Payment 模块 | ✅ 完成 |
| Phase 2 | PaymentAnalytics 迁移 | 🔄 进行中 |
| Phase 3 | 业务 Agent 事件订阅 | 📋 待开始 |
| Phase 4 | 旧代码清理 | 📋 待开始 |
| Phase 5 | ChatManager 拆分 (可选) | 📋 低优先级 |

### 兼容性保证

```
旧 API: /api/godgpt/payment/* → GodGPTPaymentController → PaymentService
新 API: /api/payment/*        → PaymentController       → PaymentService
```

两套 API 并存，逐步迁移客户端。

---

## 9. 收益预期

| 指标 | 现状 | 重构后 |
|------|------|--------|
| UserBillingGAgent 行数 | 5469 | ✅ 已拆分为 2 个 <300 行 Agent |
| 添加新支付平台 | 改多处 | 新增 1 个 Provider 类 |
| 单元测试覆盖率 | 困难 | ✅ 29 个测试通过 |
| E2E 测试 | 无 | ✅ 15 个接口覆盖 |
| 支付事件扩展 | 硬编码 | Stream 事件订阅 |
