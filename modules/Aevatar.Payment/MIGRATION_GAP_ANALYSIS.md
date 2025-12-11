# Payment Module Migration Gap Analysis

## Overview

对比旧代码 `UserBillingGAgent.cs` (5469行) 与新 `Aevatar.Payment` 模块的功能差异。

**最后更新**: 2024-12 (已完成核心功能迁移)

---

## 1. Stripe 相关功能

### 1.1 产品获取 ✅ 已完成

| 功能 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| 数据来源 | 配置文件 `_stripeOptions.Products` | 配置文件 `_options.Products` | ✅ 一致 |
| PlanType | 配置定义 | 配置定义 | ✅ 一致 |
| IsUltimate | ✅ 有 | ✅ 有 (from metadata) | ✅ |
| DailyAvgPrice | ✅ 有 | ✅ 有 (from config) | ✅ |
| Mode | ✅ 有 | ✅ 有 (from config) | ✅ |

### 1.2 Customer 管理 ✅ 已完成

| 功能 | 旧代码位置 | 新代码 | 状态 |
|------|-----------|--------|------|
| 创建 Customer | `GetOrCreateStripeCustomerAsync` | `StripeProvider.GetOrCreateCustomerAsync` | ✅ |
| 存储 Customer ID | `State.CustomerId` | `PaymentIndexGAgent.platform_customers` | ✅ |
| 获取 Customer | `GetStripeCustomerAsync` | `PaymentService.GetCustomerSessionAsync` | ✅ |
| EphemeralKey 创建 | `EphemeralKeyService.CreateAsync()` | `StripeProvider.GetCustomerSessionAsync` | ✅ |

### 1.3 PaymentSheet 支付 ✅ 已完成

| 功能 | 旧代码位置 | 新代码 | 状态 |
|------|-----------|--------|------|
| 创建 PaymentSheet | `CreatePaymentSheetAsync` | `StripeProvider.CreatePaymentSheetAsync` | ✅ |
| PaymentIntent 创建 | `PaymentIntentService.CreateAsync()` | `StripeProvider.CreatePaymentSheetAsync` | ✅ |
| EphemeralKey | ✅ | ✅ | ✅ |

**注**: `UserPaymentGrain` 的逻辑已整合到 `PaymentRecordGAgent`。

### 1.4 Checkout Session ✅ 已完成

| 功能 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| 创建 Session | ✅ | ✅ | ✅ |
| Embedded UI Mode | ✅ `UiMode = "embedded"` | ✅ (via request param) | ✅ |
| Discount/Coupon | ✅ `Discounts = [...]` | ✅ (via request param) | ✅ |
| Trial Code 处理 | ✅ `ProcessTrialCodeIfProvidedAsync` | ⏭️ 业务层职责 | ⏭️ |

### 1.5 Webhook 处理 ✅ 核心已完成

| 事件 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| checkout.session.completed | ✅ | ✅ | ✅ |
| invoice.paid | ✅ | ✅ | ✅ |
| customer.subscription.updated | ✅ | ✅ | ✅ |
| customer.subscription.deleted | ✅ | ✅ | ✅ |
| **事件广播** | 直接调用 Agent | GAgent Stream (Down) | ✅ |
| **点对点回调** | N/A | `SendToAsync` (可选) | ✅ |

**业务层职责** (通过订阅 `PaymentCompletedEvent`/`PaymentFailedEvent`/`RefundCompletedEvent`):
- ⏭️ UserQuotaGAgent 更新
- ⏭️ 自动取消升级前的订阅
- ⏭️ Trial Code 标记
- ⏭️ 邀请奖励处理
- ⏭️ GA 上报

---

## 2. Apple Pay 相关功能

### 2.1 产品获取 ✅ 已完成

| 功能 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| 数据来源 | 配置文件 | 配置文件 | ✅ |
| DailyAvgPrice | ✅ | ✅ (可在配置中添加) | ✅ |

### 2.2 订阅创建 ✅ 已完成

| 功能 | 旧代码位置 | 新代码 | 状态 |
|------|-----------|--------|------|
| 验证交易 | `VerifyAppStoreTransactionAsync` | `ApplePayProvider.VerifyTransactionAsync` | ✅ |
| 创建订阅记录 | `CreateAppStoreSubscriptionAsync` | `PaymentService` + `PaymentRecordGAgent` | ✅ |
| 支付回调处理 | UserPaymentGrain | `PaymentRecordGAgent` | ✅ |
| **Quota 更新** | 直接调用 | 业务层订阅 `PaymentCompletedEvent` | ⏭️ |

### 2.3 Webhook 处理 ✅ 核心已完成

| 事件 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| SUBSCRIBED | ✅ | ✅ | ✅ |
| DID_RENEW | ✅ | ✅ | ✅ |
| DID_CHANGE_RENEWAL_STATUS | ✅ | ✅ | ✅ |
| EXPIRED | ✅ | ✅ | ✅ |
| REVOKE | ✅ | ✅ | ✅ |
| REFUND | ✅ | ✅ | ✅ |
| **事件广播** | 直接调用 | GAgent Stream | ✅ |

**业务层职责**: Quota 更新、GA 上报

### 2.4 JWT 签名验证 ✅ 已完成

| 功能 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| JWT 生成 | ✅ 完整实现 | ✅ 简化版 | ✅ |
| JWT 验证 | ✅ `VerifyJwtSignature` | ✅ `VerifyJwtSignature` (x5c) | ✅ |

**配置**:
- `EnableSignatureVerification`: 启用签名验证 (生产环境建议开启)
- `AppleRootCertificatePath`: Apple Root CA 证书路径

---

## 3. Google Play 相关功能

### 3.1 交易验证 ✅ 已完成

| 功能 | 旧代码位置 | 新代码 | 状态 |
|------|-----------|--------|------|
| RevenueCat 验证 | ✅ | ✅ `GooglePlayProvider` | ✅ |
| 直接 API 验证 | ✅ | ⚠️ 可选 (P3) | ⚠️ |
| Purchase Token 查询 | ✅ | ⚠️ 可选 (P3) | ⚠️ |

### 3.2 Webhook (RevenueCat) 处理 ✅ 已完成

| 事件 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| INITIAL_PURCHASE | ✅ | ✅ | ✅ |
| RENEWAL | ✅ | ✅ | ✅ |
| CANCELLATION | ✅ | ✅ | ✅ |
| EXPIRATION | ✅ | ✅ | ✅ |
| UNCANCELLATION | ✅ | ✅ (→ Completed) | ✅ |
| PRODUCT_CHANGE | ✅ | ✅ (→ Completed) | ✅ |
| BILLING_ISSUE | ✅ | ✅ (→ Failed) | ✅ |
| **事件广播** | 直接调用 | GAgent Stream | ✅ |

**重复检测**: 通过 `PaymentRecordGAgent.IsInitializedAsync()` + Agent ID 唯一性保证

**业务层职责**: Quota 更新、GA 上报

---

## 4. 业务逻辑 ⏭️ 业务层职责

> **设计原则**: Payment 模块只负责支付，业务逻辑通过事件驱动由业务层 (GodGPT) 处理。
> 业务层订阅 `PaymentCompletedEvent`、`PaymentFailedEvent`、`RefundCompletedEvent` 实现。

### 4.1 Quota/订阅管理 ⏭️

| 功能 | 实现方式 | 状态 |
|------|----------|------|
| 更新用户订阅 | 订阅 `PaymentCompletedEvent` | ⏭️ 业务层 |
| 重置速率限制 | 订阅 `PaymentCompletedEvent` | ⏭️ 业务层 |
| 获取订阅信息 | 订阅 `PaymentCompletedEvent` | ⏭️ 业务层 |
| 退款回滚 | 订阅 `RefundCompletedEvent` | ⏭️ 业务层 |
| 过期处理 | 定时任务 + `period_end` 检查 | ⏭️ 业务层 |

### 4.2 Trial Code 处理 ⏭️

| 功能 | 实现方式 | 状态 |
|------|----------|------|
| 验证 Trial Code | 业务层调用 `FreeTrialCodeFactoryGAgent` | ⏭️ 业务层 |
| 初始化 Trial Code | 业务层 | ⏭️ 业务层 |
| 标记已使用 | 订阅 `PaymentCompletedEvent` 后处理 | ⏭️ 业务层 |

### 4.3 邀请奖励 ⏭️

| 功能 | 实现方式 | 状态 |
|------|----------|------|
| 处理邀请奖励 | 订阅 `PaymentCompletedEvent` 后调用 `InviteCodeGAgent` | ⏭️ 业务层 |

### 4.4 订阅升级逻辑 ⏭️

| 功能 | 实现方式 | 状态 |
|------|----------|------|
| 验证升级路径 | 业务层在创建订阅前验证 | ⏭️ 业务层 |
| 自动取消旧订阅 | 订阅 `PaymentCompletedEvent` 后处理 | ⏭️ 业务层 |
| Ultimate 升级处理 | 订阅 `PaymentCompletedEvent` 后处理 | ⏭️ 业务层 |

### 4.5 分析上报 ⏭️

| 功能 | 实现方式 | 状态 |
|------|----------|------|
| GA 支付成功上报 | 订阅 `PaymentCompletedEvent` | ⏭️ 业务层 |
| GA Google 上报 | 订阅 `PaymentCompletedEvent` | ⏭️ 业务层 |
| GA 退款上报 | 订阅 `RefundCompletedEvent` | ⏭️ 业务层 |

---

## 5. 数据存储 ✅ 已优化

### 5.1 支付历史

| 方面 | 旧代码 | 新代码 | 状态 |
|------|--------|--------|------|
| 存储位置 | `State.PaymentHistory` (单 Agent) | `PaymentRecordGAgent` (每订阅一个) | ✅ 改进 |
| 交易记录 | 嵌套在 PaymentSummary | `TransactionProto` 列表 | ✅ 改进 |
| 索引查询 | 全量加载 | `PaymentIndexGAgent` 轻量索引 | ✅ 改进 |
| 历史查询 | Agent State | CQRS Read Model (推荐) | ✅ 设计 |

### 5.2 字段对比

| 旧字段 | 新字段 | 状态 |
|--------|--------|------|
| PaymentGrainId | `PaymentRecordGAgent.Id` | ✅ 对应 |
| MembershipLevel | `business_metadata["membership_level"]` | ✅ 扩展 |
| AppStoreEnvironment | `environment` | ✅ 对应 |
| InvoiceDetails[] | `TransactionProto.transactions` | ✅ 对应 |
| SubscriptionStartDate/EndDate | `period_start` / `period_end` | ✅ 对应 |

---

## 6. Agent 交互 ✅ 通过事件解耦

| Agent | 旧代码 | 新代码 | 状态 |
|-------|--------|--------|------|
| UserQuotaGAgent | 直接调用 | 订阅 `PaymentCompletedEvent` | ✅ 解耦 |
| UserPaymentGrain | 处理支付回调 | `PaymentRecordGAgent` | ✅ 合并 |
| FreeTrialCodeFactoryGAgent | 直接调用 | 业务层订阅事件后调用 | ⏭️ 业务层 |
| InviteCodeGAgent | 直接调用 | 业务层订阅事件后调用 | ⏭️ 业务层 |

---

## 7. 完成状态总结

### ✅ P0 - 已完成

1. ~~**产品获取** - 改为配置驱动~~ ✅
2. ~~**Customer 管理** - GetOrCreateCustomer, GetCustomerSession~~ ✅
3. ~~**事件发布** - GAgent Stream Down 广播~~ ✅
4. ~~**点对点回调** - SendToAsync (可选)~~ ✅

### ✅ P1 - 已完成

1. ~~**PaymentSheet** - 支持 App 内嵌支付~~ ✅
2. ~~**EphemeralKey** - Mobile SDK 必需~~ ✅
3. ~~**Embedded UI Mode** - Web 嵌入支付~~ ✅
4. ~~**Discount/Coupon** - 支持优惠券~~ ✅

### ⏭️ P2 - 业务层职责 (不在 Payment 模块)

1. **Trial Code 处理** - 业务层订阅事件后处理
2. **邀请奖励处理** - 业务层订阅事件后处理
3. **订阅升级逻辑** - 业务层订阅事件后处理
4. **Quota 更新** - 业务层订阅 `PaymentCompletedEvent`

### ⚠️ P3 - 可选增强

1. ~~**JWT 签名完整验证**~~ ✅ 已完成
2. ~~**更多 Google Play 事件**~~ ✅ 已完成
3. **直接 Google Play API** - 不依赖 RevenueCat (可选)
4. **GA 分析上报** - 业务层实现 (⏭️)

---

## 8. 已实现架构

```
┌─────────────────────────────────────────────────────────────────┐
│                     Payment Module (通用)                        │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ PaymentService (Orchestrator)                             │  │
│  │  - 产品获取、订阅创建、Webhook 路由                        │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Providers (Strategy Pattern)                              │  │
│  │  - StripeProvider ✅                                       │  │
│  │  - ApplePayProvider ✅                                     │  │
│  │  - GooglePlayProvider ✅                                   │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ Agents (State Management)                                 │  │
│  │  - PaymentIndexGAgent (用户级索引 + 事件广播)              │  │
│  │  - PaymentRecordGAgent (订单级生命周期 + 点对点回调)        │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ GAgent Stream (Down)
                              │ PaymentCompletedEvent
                              │ PaymentFailedEvent
                              │ RefundCompletedEvent
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     GodGPT Business Layer (待实现)               │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ UserQuotaGAgent - 订阅 PaymentCompletedEvent              │  │
│  │  → UpdateSubscriptionAsync()                              │  │
│  │  → ResetRateLimitsAsync()                                 │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ FreeTrialCodeFactoryGAgent - 订阅事件后处理               │  │
│  │  → MarkTrialCodeUsedAsync()                               │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ InviteCodeGAgent - 订阅事件后处理                         │  │
│  │  → ProcessInviteeRewardAsync()                            │  │
│  └───────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │ AnalyticsService - 订阅事件后处理                         │  │
│  │  → ReportPaymentSuccessToGA()                             │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 事件订阅方式

**方式 1: 用户级订阅 (广播)**
```csharp
// 业务 Agent 注册为 PaymentIndexGAgent 的子节点
await agentManager.LinkParentChildAsync(paymentIndexAgentId, businessAgentId);

// 在业务 Agent 中添加事件处理器
[EventHandler]
public Task HandlePaymentCompleted(PaymentCompletedEvent evt) { ... }
```

**方式 2: 订单级订阅 (点对点)**
```csharp
// 创建支付时指定回调 Agent
var request = new CreatePaymentRequest {
    CallbackAgentId = orderAgentId,  // 可选
    ...
};

// 在订单 Agent 中添加事件处理器
[EventHandler]
public Task HandlePaymentCompleted(PaymentCompletedEvent evt) { ... }
```

---

## 9. 已完成功能清单

### Provider 层 ✅

- [x] `StripeProvider.GetProductsAsync` - 配置驱动
- [x] `StripeProvider.GetOrCreateCustomerAsync` - Customer 管理
- [x] `StripeProvider.GetCustomerSessionAsync` - EphemeralKey
- [x] `StripeProvider.CreatePaymentSheetAsync` - App 内嵌支付
- [x] `StripeProvider.CreateSubscriptionAsync` - 支持 Embedded Mode, Discount
- [x] `ApplePayProvider` - Webhook 处理
- [x] `ApplePayProvider.VerifyJwtSignature` - x5c 证书链签名验证
- [x] `GooglePlayProvider` - RevenueCat Webhook 处理
- [x] `GooglePlayProvider` - 更多事件类型 (UNCANCELLATION, PRODUCT_CHANGE, BILLING_ISSUE)

### Service 层 ✅

- [x] `PaymentService` - 事件广播到 IndexAgent
- [x] 重复检测 - Agent ID 唯一性 + IsInitializedAsync

### Agent 层 ✅

- [x] `PaymentIndexGAgent` - CustomerId 存储、事件广播
- [x] `PaymentRecordGAgent` - 点对点回调
- [x] 事件定义: `PaymentCompletedEvent`, `PaymentFailedEvent`, `RefundCompletedEvent`

### 业务集成层 (GodGPT 侧) ⏭️

- [ ] 订阅 Payment 事件 (LinkParentChildAsync)
- [ ] 调用 UserQuotaGAgent 更新订阅
- [ ] 处理 Trial Code
- [ ] 处理邀请奖励
- [ ] GA 上报

---

## 10. 业务层实现详情 (GodGPT 侧)

> 以下功能需要在 GodGPT 业务层实现，通过订阅 `PaymentCompletedEvent` 触发。

### 10.1 Trial Code 处理

**背景**: 用户使用试用码 (Trial Code) 创建订阅时，支付成功后需要标记该 Trial Code 为已使用。

**触发时机**: 收到 `PaymentCompletedEvent` 且 `business_metadata["trial_code"]` 不为空

**实现步骤**:

```csharp
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    // 1. 检查是否有 trial_code
    if (!evt.Context.BusinessMetadata.TryGetValue("trial_code", out var trialCode) 
        || string.IsNullOrEmpty(trialCode))
    {
        return;
    }

    // 2. 解析 trial code 获取 batch ID
    var (codeType, batchId) = InvitationCodeHelper.ParseCodeInfo(trialCode);

    // 3. 标记 FreeTrialCodeFactoryGAgent 中的 code 为已使用
    var factoryGAgent = await GetFreeTrialCodeFactoryAgentAsync(batchId);
    await factoryGAgent.MarkCodeAsUsedAsync(trialCode, evt.Context.UserId);

    // 4. 标记 InviteCodeGAgent 中的 code 为已使用
    var inviteCodeGAgent = await GetInviteCodeAgentAsync(
        CommonHelper.StringToGuid(trialCode));
    await inviteCodeGAgent.MarkCodeAsUsedAsync();

    // 5. 获取 trial days 用于计算订阅结束日期
    var batchInfo = await factoryGAgent.GetBatchInfoAsync();
    var trialDays = batchInfo?.Config?.TrialDays ?? 0;

    _logger.LogInformation(
        "[PaymentHandler] Trial code {Code} marked as used for user {UserId}, trial days: {Days}",
        trialCode, evt.Context.UserId, trialDays);
}
```

**相关 Agent**:
- `FreeTrialCodeFactoryGAgent`: 管理 Trial Code 批次和使用状态
- `InviteCodeGAgent`: 管理单个邀请码/试用码的状态

**Metadata 传递**:
创建订阅时，业务层需要将 trial_code 放入 `CreatePaymentRequest.BusinessMetadata`:

```csharp
var request = new CreatePaymentRequest
{
    UserId = userId,
    BusinessType = "godgpt",
    BusinessMetadata = new Dictionary<string, string>
    {
        ["trial_code"] = createCheckoutSessionDto.TrialCode,
        ["trial_days"] = trialDays.ToString()
    },
    // ...
};
```

### 10.2 邀请奖励处理

**背景**: 当被邀请用户 (Invitee) 首次付费成功后，需要给邀请人 (Inviter) 发放奖励。

**触发时机**: 收到 `PaymentCompletedEvent` 且 `is_renewal = false` (首次支付)

**实现步骤**:

```csharp
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    // 1. 检查是否是首次支付 (非续订)
    if (evt.IsRenewal)
    {
        return; // 续订不触发邀请奖励
    }

    var userId = Guid.Parse(evt.Context.UserId);

    // 2. 获取用户的邀请人
    var chatManagerGAgent = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
    var inviterId = await chatManagerGAgent.GetInviterAsync();

    if (inviterId == null || inviterId == Guid.Empty)
    {
        return; // 用户没有邀请人
    }

    // 3. 获取产品配置判断 PlanType 和 IsUltimate
    var planType = GetPlanTypeFromMetadata(evt.Context.BusinessMetadata);
    var isUltimate = GetIsUltimateFromMetadata(evt.Context.BusinessMetadata);

    // 4. 处理邀请奖励
    var invitationGAgent = await GetInvitationAgentAsync((Guid)inviterId);
    await invitationGAgent.ProcessInviteeSubscriptionAsync(
        userId.ToString(),
        planType,
        isUltimate,
        evt.TransactionId);

    _logger.LogInformation(
        "[PaymentHandler] Processed invitation reward for inviter {InviterId}, invitee {InviteeId}",
        inviterId, userId);
}

private PlanType GetPlanTypeFromMetadata(IDictionary<string, string> metadata)
{
    if (metadata.TryGetValue("plan_type", out var planTypeStr) 
        && Enum.TryParse<PlanType>(planTypeStr, out var planType))
    {
        return planType;
    }
    return PlanType.Monthly; // default
}

private bool GetIsUltimateFromMetadata(IDictionary<string, string> metadata)
{
    return metadata.TryGetValue("is_ultimate", out var isUltimateStr) 
        && bool.TryParse(isUltimateStr, out var isUltimate) 
        && isUltimate;
}
```

**相关 Agent**:
- `ChatManagerGAgent`: 管理用户信息，包括邀请人 ID
- `InvitationGAgent`: 处理邀请奖励逻辑

**Metadata 传递**:
创建订阅时，业务层需要将产品信息放入 `CreatePaymentRequest.BusinessMetadata`:

```csharp
var request = new CreatePaymentRequest
{
    UserId = userId,
    BusinessType = "godgpt",
    BusinessMetadata = new Dictionary<string, string>
    {
        ["plan_type"] = productConfig.PlanType.ToString(),
        ["is_ultimate"] = productConfig.IsUltimate.ToString()
    },
    // ...
};
```

### 10.3 Quota 更新

**背景**: 支付成功后需要更新用户的订阅状态和配额。

**触发时机**: 收到 `PaymentCompletedEvent`

**实现步骤**:

```csharp
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    var userId = Guid.Parse(evt.Context.UserId);
    var userQuotaGAgent = await GetUserQuotaAgentAsync(userId);

    // 获取产品信息
    var planType = GetPlanTypeFromMetadata(evt.Context.BusinessMetadata);
    var isUltimate = GetIsUltimateFromMetadata(evt.Context.BusinessMetadata);
    var trialDays = GetTrialDaysFromMetadata(evt.Context.BusinessMetadata);

    // 计算订阅结束日期
    var periodEnd = evt.PeriodEnd?.ToDateTime() ?? CalculatePeriodEnd(planType, trialDays);

    var subscriptionInfo = new SubscriptionInfoDto
    {
        IsActive = true,
        PlanType = planType,
        StartDate = DateTime.UtcNow,
        EndDate = periodEnd,
        Status = PaymentStatus.Completed,
        SubscriptionIds = new List<string> { evt.Context.SubscriptionId }
    };

    // 更新订阅
    await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionInfo, isUltimate);

    // 首次订阅时重置速率限制
    if (!evt.IsRenewal)
    {
        await userQuotaGAgent.ResetRateLimitsAsync();
    }

    _logger.LogInformation(
        "[PaymentHandler] Updated quota for user {UserId}, plan: {PlanType}, end: {EndDate}",
        userId, planType, periodEnd);
}
```

### 10.4 退款回滚

**背景**: 退款后需要回滚用户的订阅时间。

**触发时机**: 收到 `RefundCompletedEvent`

**实现步骤**:

```csharp
[EventHandler]
public async Task HandleRefundCompleted(RefundCompletedEvent evt)
{
    var userId = Guid.Parse(evt.Context.UserId);
    var userQuotaGAgent = await GetUserQuotaAgentAsync(userId);

    var planType = GetPlanTypeFromMetadata(evt.Context.BusinessMetadata);
    var isUltimate = GetIsUltimateFromMetadata(evt.Context.BusinessMetadata);

    var subscriptionInfo = await userQuotaGAgent.GetSubscriptionAsync(isUltimate);

    // 计算需要回滚的天数
    var daysToDeduct = GetDaysForPlanType(planType);
    subscriptionInfo.EndDate = subscriptionInfo.EndDate.AddDays(-daysToDeduct);

    // 移除订阅 ID
    subscriptionInfo.SubscriptionIds?.Remove(evt.Context.SubscriptionId);

    await userQuotaGAgent.UpdateSubscriptionAsync(subscriptionInfo, isUltimate);

    _logger.LogInformation(
        "[PaymentHandler] Rolled back quota for user {UserId} due to refund",
        userId);
}
```

### 10.5 GA 分析上报

**背景**: 支付成功后上报 Google Analytics。

**触发时机**: 收到 `PaymentCompletedEvent`

**实现步骤**:

```csharp
[EventHandler]
public async Task HandlePaymentCompleted(PaymentCompletedEvent evt)
{
    var purchaseType = evt.IsRenewal 
        ? PurchaseType.Renewal 
        : (GetTrialDaysFromMetadata(evt.Context.BusinessMetadata) > 0 
            ? PurchaseType.Trial 
            : PurchaseType.Subscription);

    _ = ReportPaymentSuccessAsync(
        evt.Context.UserId,
        evt.TransactionId,
        purchaseType,
        (PaymentPlatform)evt.Context.Platform,
        evt.Context.BusinessMetadata.GetValueOrDefault("price_id", ""),
        evt.Context.BusinessMetadata.GetValueOrDefault("currency", "USD"),
        GetAmountFromMetadata(evt.Context.BusinessMetadata));
}
```

### 10.6 事件订阅注册

**注册方式**: 在应用启动时，将业务 Agent 链接到 `PaymentIndexGAgent`

```csharp
// 在 Startup 或模块初始化时
public async Task RegisterPaymentEventHandlers(IGAgentActorManager agentManager)
{
    // 为每个用户的 PaymentIndexGAgent 注册业务 Agent 作为子节点
    // 这样业务 Agent 就能收到 PaymentCompletedEvent 等事件
    
    // 方式 1: 在用户首次创建订阅时注册
    var paymentIndexAgentId = GetPaymentIndexAgentId(userId);
    var businessAgentId = GetUserBusinessAgentId(userId);
    await agentManager.LinkParentChildAsync(paymentIndexAgentId, businessAgentId);
}
```

**或者使用点对点回调**:

```csharp
// 创建订阅时指定回调 Agent
var request = new CreatePaymentRequest
{
    UserId = userId,
    CallbackAgentId = userBusinessAgentId, // 支付完成后直接回调
    // ...
};
```

