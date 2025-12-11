# Payment Module 通用架构设计

## 1. 设计目标

- **业务无关** - Payment模块只负责支付流程，不绑定特定业务逻辑
- **多业务支持** - 同一用户可有多个业务的支付记录
- **灵活扩展** - 通过元数据支持业务特定信息
- **事件驱动** - 支付状态变更通过事件通知业务层
- **高吞吐** - 合理拆分Agent，支持Orleans并行处理

## 1.1 Agent 拆分策略 (Orleans优化)

### 为什么要拆分？

| 问题 | 单Agent | 拆分后 |
|-----|---------|--------|
| 状态大小 | 用户所有支付历史，可能很大 | 每个订单独立，状态小 |
| 并发处理 | 单线程，续费排队 | 多个订单可并行处理 |
| 激活延迟 | 加载全部状态慢 | 按需激活，快速响应 |
| 热点问题 | 高频用户成瓶颈 | 负载分散到多个Grain |

### 拆分方案

```
┌─────────────────────────────────────────────────────────────────┐
│                    PaymentIndexGAgent                            │
│                    (用户级别 - 轻量索引)                          │
│  ID: user_{userId}                                               │
│  ├── platform_customers: {stripe: "cus_xxx", apple: "xxx"}      │
│  ├── active_payments: ["pay_1", "pay_2"]  # 活跃订阅ID列表      │
│  └── payment_index: [{id, business_type, status, created_at}]   │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ 引用
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    PaymentRecordGAgent                           │
│                    (订单级别 - 完整数据)                          │
│  ID: payment_{paymentId}                                         │
│  ├── payment_info: { 完整订单信息 }                              │
│  └── transactions: [ { 交易记录1 }, { 交易记录2 }, ... ]         │
└─────────────────────────────────────────────────────────────────┘
```

### Agent职责划分

| Agent | 职责 | 数据 | 访问频率 |
|-------|-----|------|---------|
| **PaymentIndexGAgent** | 用户支付索引、活跃订阅查询、平台客户ID管理 | 轻量索引数据 | 高频 |
| **PaymentRecordGAgent** | 单个订单的完整生命周期管理 | 完整订单+交易记录 | 中频 |

### 调用流程示例

```
查询活跃订阅：
  Client → PaymentIndexGAgent.GetActivePaymentsAsync()
  返回活跃订阅列表（仅索引信息，快速）

查询订单详情：
  Client → PaymentIndexGAgent.GetPaymentRecordAgentIdAsync(paymentId)
  Client → PaymentRecordGAgent.GetPaymentDetailsAsync()

续费处理：
  Webhook → PaymentRecordGAgent.ProcessRenewalAsync()
  → 更新自身状态
  → 发布事件
  → PaymentIndexGAgent 订阅事件更新索引
```

### 并发优势

```
用户A有3个活跃订阅：
┌──────────────────┐
│ PaymentIndexGAgent │ ← 轻量，快速响应查询
└────────┬─────────┘
         │
    ┌────┴────┬─────────────┐
    ▼         ▼             ▼
┌───────┐ ┌───────┐    ┌───────┐
│ Pay_1 │ │ Pay_2 │    │ Pay_3 │  ← 可并行处理续费
└───────┘ └───────┘    └───────┘
```

## 2. 支持的业务场景

| 场景 | PaymentMode | 示例 |
|-----|-------------|------|
| 订阅服务 | Subscription | 会员订阅、SaaS服务 |
| 一次性购买 | OneTime | 功能解锁、终身会员 |
| 消耗品 | Consumable | Token包、API调用额度 |
| 内容付费 | OneTime | 课程、电子书 |

## 3. 核心概念

### 3.1 业务标识 (Business Context)

```
business_type: 业务类型标识 (如 "godgpt", "course", "api_quota")
business_id:   业务实体ID (如 用户ID、课程ID)
```

每个PaymentRecord关联到特定业务，同一用户可以有多个业务的支付记录。

### 3.2 支付模式 (Payment Mode)

```csharp
public enum PaymentMode
{
    Subscription = 0,   // 订阅 (周期性)
    OneTime = 1,        // 一次性购买
    Consumable = 2      // 消耗品 (可重复购买)
}
```

### 3.3 元数据 (Metadata)

业务特定信息通过 `metadata` 字段存储，Payment模块不解析：

```json
{
  "membership_level": "premium",
  "quota_amount": "10000",
  "course_id": "xxx",
  "feature_key": "advanced_analytics"
}
```

## 4. 数据模型设计

### 4.1 拆分后的层次结构

```
PaymentIndexGAgent (用户级别 - 极度轻量)
├── user_id
├── platform_customers{}           # 各平台客户ID（固定大小）
├── active_subscriptions[]         # 仅活跃订阅（通常1-3个）
└── counters                       # 简单计数器

PaymentRecordGAgent (订单级别 - 完整数据)
├── payment_id
├── business_context
├── product_info
├── payment_info
└── transactions[]

CQRS 读模型 (数据库)
└── 完整的支付历史查询           # 通过StateProjector投影
```

### 4.2 为什么Index不存储完整索引？

| 方案 | 状态大小 | 问题 |
|-----|---------|------|
| Index存完整列表 | 随历史增长 | ❌ 变成大Agent |
| Index只存活跃订阅 | 固定小 | ✅ 保持轻量 |

**历史查询走CQRS读模型**：
- PaymentRecordGAgent 状态变更时，通过 StateProjector 投影到数据库
- 查询支付历史直接查数据库，不经过Agent
- Agent只负责写入和实时状态，数据库负责复杂查询

### 4.3 PaymentIndexGAgent State Proto (极简版)

```protobuf
message PaymentIndexStateProto {
  // 用户标识
  string user_id = 1;
  
  // 各平台客户ID (固定大小，最多5-6个平台)
  map<string, string> platform_customers = 2;
  
  // 活跃订阅列表 (通常1-3个，最多不超过10个)
  repeated ActiveSubscriptionProto active_subscriptions = 3;
  
  // 简单计数器 (固定大小)
  int32 total_payments = 4;
  int32 active_count = 5;
  
  // 元数据
  google.protobuf.Timestamp last_updated = 6;
}

// 活跃订阅信息 (只保留必要字段)
message ActiveSubscriptionProto {
  string payment_id = 1;              // Agent ID
  string business_type = 2;           // 业务类型
  int32 platform = 3;                 // 平台
  string product_name = 4;            // 显示用
  google.protobuf.Timestamp period_end = 5;  // 到期时间
}
```

**状态大小估算**：
- platform_customers: ~200 bytes (5个平台)
- active_subscriptions: ~500 bytes (5个活跃订阅)
- counters: ~16 bytes
- **总计: < 1KB** (恒定大小，不随历史增长)

### 4.3 PaymentRecordGAgent State Proto
```

### 4.3 PaymentRecordGAgent State Proto

```protobuf
message PaymentRecordStateProto {
  // ========== 标识 ==========
  string payment_id = 1;              // 内部支付ID (Guid)
  string user_id = 2;                 // 用户ID (冗余，便于独立查询)
  string external_order_id = 3;       // 外部订单号 (业务层生成)
  string subscription_id = 4;         // 平台订阅ID (订阅模式专用)
  
  // ========== 业务上下文 ==========
  string business_type = 5;           // 业务类型 (godgpt/course/api)
  string business_id = 6;             // 业务实体ID
  map<string, string> business_metadata = 7;  // 业务元数据
  
  // ========== 平台信息 ==========
  int32 platform = 8;                 // PaymentPlatform enum
  string environment = 9;             // Production/Sandbox
  string customer_id = 10;            // 平台客户ID
  
  // ========== 产品信息 ==========
  string product_id = 11;             // 平台产品ID
  string price_id = 12;               // 平台价格ID
  string product_name = 13;           // 产品名称 (显示用)
  int32 payment_mode = 14;            // PaymentMode enum
  
  // ========== 周期信息 (订阅模式) ==========
  int32 billing_cycle = 15;           // BillingCycle enum (Monthly/Yearly/etc)
  google.protobuf.Timestamp period_start = 16;
  google.protobuf.Timestamp period_end = 17;
  
  // ========== 金额信息 ==========
  int64 amount = 18;                  // 金额 (最小单位，如分)
  string currency = 19;               // 货币代码
  optional int64 net_amount = 20;     // 扣除折扣后净额
  
  // ========== 状态信息 ==========
  int32 status = 21;                  // PaymentStatus enum
  google.protobuf.Timestamp created_at = 22;
  optional google.protobuf.Timestamp completed_at = 23;
  
  // ========== 交易记录 ==========
  repeated TransactionProto transactions = 24;
  
  // ========== 元数据 ==========
  google.protobuf.Timestamp last_updated = 25;
}
```

### 4.4 Transaction Proto (原InvoiceDetail，更通用的命名)

```protobuf
message TransactionProto {
  // ========== 交易标识 ==========
  string transaction_id = 1;          // 内部交易ID
  string external_transaction_id = 2; // 平台交易ID
  string invoice_id = 3;              // 发票ID (如有)
  string purchase_token = 4;          // Google Play购买Token
  
  // ========== 类型和状态 ==========
  int32 transaction_type = 5;         // TransactionType enum
  int32 status = 6;                   // PaymentStatus enum
  
  // ========== 金额 ==========
  int64 amount = 7;                   // 金额 (最小单位)
  string currency = 8;
  optional int64 net_amount = 9;
  
  // ========== 周期 (订阅续费) ==========
  optional google.protobuf.Timestamp period_start = 10;
  optional google.protobuf.Timestamp period_end = 11;
  
  // ========== 时间 ==========
  google.protobuf.Timestamp created_at = 12;
  optional google.protobuf.Timestamp completed_at = 13;
  
  // ========== 促销信息 ==========
  repeated PromotionProto promotions = 14;
  bool is_trial = 15;
  string trial_code = 16;
  
  // ========== 元数据 ==========
  map<string, string> metadata = 17;
}
```

### 4.5 Promotion Proto (原Discount，更通用)

```protobuf
message PromotionProto {
  string promotion_id = 1;
  string promotion_type = 2;          // coupon/promo_code/trial/etc
  string code = 3;                    // 促销码
  string name = 4;                    // 显示名称
  optional int64 amount_off = 5;      // 减免金额 (最小单位)
  optional int32 percent_off = 6;     // 折扣百分比 (0-100)
  map<string, string> metadata = 7;
}
```

## 5. 枚举定义

```csharp
// 支付平台
public enum PaymentPlatform
{
    Stripe = 0,
    AppStore = 1,
    GooglePlay = 2,
    // 未来扩展
    PayPal = 3,
    Alipay = 4,
    WechatPay = 5
}

// 支付状态
public enum PaymentStatus
{
    Pending = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3,
    Refunded = 4,
    PartialRefunded = 5,
    Cancelled = 6,
    Expired = 7
}

// 支付模式
public enum PaymentMode
{
    Subscription = 0,    // 订阅
    OneTime = 1,         // 一次性
    Consumable = 2       // 消耗品
}

// 账单周期
public enum BillingCycle
{
    None = 0,            // 非订阅
    Weekly = 1,
    Monthly = 2,
    Quarterly = 3,
    Yearly = 4,
    Lifetime = 5
}

// 交易类型
public enum TransactionType
{
    Initial = 0,         // 首次购买
    Renewal = 1,         // 自动续费
    Upgrade = 2,         // 升级
    Downgrade = 3,       // 降级
    Resubscribe = 4,     // 重新订阅
    Refund = 5,          // 退款
    PartialRefund = 6,   // 部分退款
    Adjustment = 7       // 调整
}
```

## 6. Agent 接口设计

### 6.1 PaymentIndexGAgent (用户级别 - 极简接口)

```csharp
/// <summary>
/// 用户支付索引Agent - 只管理活跃订阅和平台客户ID
/// ID格式: user_{userId}
/// 设计原则：保持极度轻量，不存储历史数据
/// </summary>
public interface IPaymentIndexGAgent : IGAgent
{
    // ========== 平台客户ID ==========
    Task<string?> GetPlatformCustomerIdAsync(PaymentPlatform platform);
    Task SetPlatformCustomerIdAsync(PaymentPlatform platform, string customerId);
    
    // ========== 活跃订阅管理 ==========
    Task AddActiveSubscriptionAsync(ActiveSubscription subscription);
    Task UpdateSubscriptionPeriodEndAsync(string paymentId, DateTime periodEnd);
    Task RemoveActiveSubscriptionAsync(string paymentId);
    
    // ========== 查询 (仅活跃订阅，快速) ==========
    Task<List<ActiveSubscription>> GetActiveSubscriptionsAsync();
    Task<List<ActiveSubscription>> GetActiveSubscriptionsByBusinessAsync(string businessType);
    Task<bool> HasActiveSubscriptionAsync(string? businessType = null);
    
    // ========== 简单统计 ==========
    Task<int> GetTotalPaymentCountAsync();
    Task IncrementPaymentCountAsync();
    
    // ========== 管理 ==========
    Task ClearAllAsync();
}

// 注意：历史支付查询走CQRS读模型（数据库），不经过Agent
```

### 6.2 PaymentRecordGAgent (订单级别)

```csharp
/// <summary>
/// 支付记录Agent - 管理单个支付订单的完整生命周期
/// ID格式: payment_{paymentId}
/// </summary>
public interface IPaymentRecordGAgent : IGAgent
{
    // ========== 初始化 ==========
    Task InitializeAsync(CreatePaymentRequest request);
    
    // ========== 查询 ==========
    Task<PaymentRecord> GetPaymentRecordAsync();
    Task<PaymentStatus> GetStatusAsync();
    Task<List<Transaction>> GetTransactionsAsync();
    Task<Transaction?> GetTransactionAsync(string transactionId);
    
    // ========== 状态更新 ==========
    Task UpdateStatusAsync(PaymentStatus status, string? reason = null);
    Task UpdatePeriodAsync(DateTime periodStart, DateTime periodEnd);
    Task CompleteAsync();
    Task CancelAsync(string? reason = null);
    
    // ========== 交易记录 ==========
    Task<string> AddTransactionAsync(Transaction transaction);
    Task UpdateTransactionStatusAsync(string transactionId, PaymentStatus status);
    
    // ========== 续费处理 ==========
    Task ProcessRenewalAsync(RenewalInfo renewal);
    
    // ========== 退款处理 ==========
    Task ProcessRefundAsync(RefundInfo refund);
    Task ProcessPartialRefundAsync(string transactionId, long refundAmount, string reason);
}
```

### 6.3 协调服务与CQRS读模型

```csharp
/// <summary>
/// 支付协调服务 - 跨Agent操作的协调器
/// 在应用层实现，不是Agent
/// </summary>
public interface IPaymentCoordinatorService
{
    // ========== 写操作（通过Agent）==========
    Task<string> CreatePaymentAsync(Guid userId, CreatePaymentRequest request);
    Task ProcessWebhookAsync(WebhookEvent webhookEvent);
    
    // ========== 实时查询（通过Agent）==========
    Task<List<ActiveSubscription>> GetActiveSubscriptionsAsync(Guid userId);
    Task<PaymentRecord?> GetPaymentDetailsAsync(string paymentId);
    
    // ========== 历史查询（通过数据库）==========
    // 注意：历史查询不经过Agent，直接查CQRS读模型
    Task<PagedResult<PaymentRecordDto>> GetPaymentHistoryAsync(Guid userId, PaymentHistoryQuery query);
    Task<PaymentStatisticsDto> GetPaymentStatisticsAsync(Guid userId);
}

/// <summary>
/// CQRS 读模型仓储 - 查询历史数据
/// 数据通过 StateProjector 从 PaymentRecordGAgent 投影而来
/// </summary>
public interface IPaymentReadRepository
{
    Task<PagedResult<PaymentRecordDto>> GetByUserIdAsync(Guid userId, int page, int pageSize);
    Task<PaymentRecordDto?> GetByIdAsync(string paymentId);
    Task<List<PaymentRecordDto>> GetByBusinessAsync(Guid userId, string businessType);
    Task<PaymentStatisticsDto> GetStatisticsAsync(Guid userId);
}
```

## 7. Webhook 路由策略

### 7.1 各平台Webhook携带的标识

| 平台 | Webhook携带的标识 | 是否有user_id |
|-----|------------------|--------------|
| Stripe | `subscription_id`, `customer_id`, `metadata` | ✅ 可通过metadata透传 |
| App Store | `original_transaction_id`, `transaction_id` | ❌ |
| Google Play | `purchase_token`, `subscription_id`, `order_id` | ❌ |

### 7.2 路由方案

**核心思路**：利用平台提供的唯一标识（如 `subscription_id`）作为Agent定位依据。

#### 方案A：PaymentRecordGAgent ID 使用平台订阅ID（推荐）

```
PaymentRecordGAgent ID 策略：
- Stripe:     payment_stripe_{subscription_id}
- App Store:  payment_appstore_{original_transaction_id}
- Google:     payment_google_{purchase_token_hash}  // token较长，可用hash
```

**优点**：
- Webhook可直接定位Agent，无需全局查找
- 简单高效，无热点

**流程**：
```
Stripe Webhook (invoice.paid):
  subscription_id = "sub_xxx"
  → PaymentRecordGAgent("payment_stripe_sub_xxx").ProcessRenewalAsync()

App Store Webhook (DID_RENEW):
  original_transaction_id = "1000000xxx"
  → PaymentRecordGAgent("payment_appstore_1000000xxx").ProcessRenewalAsync()
```

#### 方案B：全局查找表（备选）

如果需要保持Agent ID为内部GUID，则需要全局映射：

```
PaymentLookupGAgent (全局单例或分片)
├── subscription_id_map: { "sub_xxx" -> ("user_123", "pay_456") }
├── transaction_id_map: { "1000000xxx" -> ("user_123", "pay_789") }
└── purchase_token_map: { "token_hash" -> ("user_123", "pay_012") }
```

**流程**：
```
App Store Webhook:
  original_transaction_id = "1000000xxx"
  → PaymentLookupGAgent.LookupAsync("1000000xxx")
  → 返回 (userId, paymentId)
  → PaymentRecordGAgent(paymentId).ProcessRenewalAsync()
```

**缺点**：全局Lookup可能成为热点，需要分片处理。

### 7.3 推荐策略

**采用方案A**，Agent ID直接使用平台订阅标识：

```csharp
// Agent ID生成策略
public static string GetPaymentRecordAgentId(PaymentPlatform platform, string platformSubscriptionId)
{
    return platform switch
    {
        PaymentPlatform.Stripe => $"payment_stripe_{platformSubscriptionId}",
        PaymentPlatform.AppStore => $"payment_appstore_{platformSubscriptionId}",
        PaymentPlatform.GooglePlay => $"payment_google_{ComputeHash(platformSubscriptionId)}",
        _ => $"payment_{Guid.NewGuid()}"
    };
}
```

这样 `PaymentIndexGAgent` 中的反向索引可以简化，只需存储 `payment_id` 列表即可，无需维护复杂的映射关系。

## 8. Event Sourcing 事件

### 7.1 PaymentIndexGAgent 事件

```protobuf
// 平台客户ID
message PlatformCustomerUpdatedEvent {
  int32 platform = 1;
  string customer_id = 2;
}

// 支付索引
message PaymentIndexRegisteredEvent {
  PaymentIndexEntryProto entry = 1;
}

message PaymentIndexStatusUpdatedEvent {
  string payment_id = 1;
  int32 new_status = 2;
}

message PaymentIndexPeriodUpdatedEvent {
  string payment_id = 1;
  google.protobuf.Timestamp new_period_end = 2;
}

message PaymentIndexRemovedEvent {
  string payment_id = 1;
}

message IndexClearedEvent {
  string business_type = 1;  // 为空表示全部清理
  google.protobuf.Timestamp cleared_at = 2;
}
```

### 7.2 PaymentRecordGAgent 事件

```protobuf
// 初始化
message PaymentRecordInitializedEvent {
  PaymentRecordStateProto record = 1;
}

// 状态变更
message RecordStatusChangedEvent {
  int32 old_status = 1;
  int32 new_status = 2;
  google.protobuf.Timestamp changed_at = 3;
  string reason = 4;
}

// 周期变更
message RecordPeriodUpdatedEvent {
  google.protobuf.Timestamp new_period_start = 1;
  google.protobuf.Timestamp new_period_end = 2;
}

// 交易记录
message TransactionAddedEvent {
  TransactionProto transaction = 1;
}

message TransactionStatusChangedEvent {
  string transaction_id = 1;
  int32 new_status = 2;
}

// 续费
message RenewalProcessedEvent {
  TransactionProto renewal_transaction = 1;
  google.protobuf.Timestamp new_period_end = 2;
}

// 退款
message RefundProcessedEvent {
  string transaction_id = 1;
  int64 refund_amount = 2;
  string reason = 3;
}
```

## 8. 业务集成示例

### 8.1 会员订阅业务 (GodGPT)

```csharp
// 创建支付记录
await agent.CreatePaymentRecordAsync(new CreatePaymentRequest
{
    BusinessType = "godgpt",
    BusinessId = userId.ToString(),
    BusinessMetadata = new Dictionary<string, string>
    {
        ["membership_level"] = "premium",
        ["plan_name"] = "Monthly Premium"
    },
    Platform = PaymentPlatform.Stripe,
    ProductId = "prod_xxx",
    PriceId = "price_xxx",
    PaymentMode = PaymentMode.Subscription,
    BillingCycle = BillingCycle.Monthly,
    Amount = 999,  // $9.99 in cents
    Currency = "USD"
});

// 查询活跃订阅
var status = await agent.GetSubscriptionStatusAsync("godgpt", userId.ToString());
```

### 8.2 API配额购买

```csharp
await agent.CreatePaymentRecordAsync(new CreatePaymentRequest
{
    BusinessType = "api_quota",
    BusinessId = userId.ToString(),
    BusinessMetadata = new Dictionary<string, string>
    {
        ["quota_type"] = "tokens",
        ["quota_amount"] = "100000"
    },
    Platform = PaymentPlatform.Stripe,
    ProductId = "prod_tokens_100k",
    PaymentMode = PaymentMode.Consumable,
    Amount = 1999,
    Currency = "USD"
});
```

### 8.3 课程购买

```csharp
await agent.CreatePaymentRecordAsync(new CreatePaymentRequest
{
    BusinessType = "course",
    BusinessId = courseId.ToString(),
    BusinessMetadata = new Dictionary<string, string>
    {
        ["course_name"] = "Advanced AI",
        ["buyer_id"] = userId.ToString()
    },
    Platform = PaymentPlatform.AppStore,
    PaymentMode = PaymentMode.OneTime,
    Amount = 4999,
    Currency = "USD"
});
```

## 9. 业务层响应支付事件

Payment模块通过发布事件通知业务层：

```csharp
// 业务层订阅支付事件
[EventHandler]
public async Task HandlePaymentCompleted(PaymentStatusChangedEvent evt)
{
    if (evt.NewStatus != PaymentStatus.Completed) return;
    
    var payment = await _paymentAgent.GetPaymentByIdAsync(evt.PaymentId);
    
    switch (payment.BusinessType)
    {
        case "godgpt":
            await _membershipService.ActivateMembershipAsync(payment);
            break;
        case "api_quota":
            await _quotaService.AddQuotaAsync(payment);
            break;
        case "course":
            await _courseService.GrantAccessAsync(payment);
            break;
    }
}
```

## 10. 扩展点

1. **新支付平台** - 扩展PaymentPlatform枚举 + 实现IPaymentProvider
2. **新业务类型** - 只需定义business_type字符串，无需修改Payment模块
3. **新支付模式** - 扩展PaymentMode枚举
4. **新交易类型** - 扩展TransactionType枚举
5. **业务元数据** - 通过metadata字段存储任意业务信息
6. **自定义促销** - 通过Promotion.metadata扩展

## 11. 金额处理约定

- 所有金额使用**最小货币单位**存储（如美元用分）
- 使用`int64`类型避免浮点精度问题
- 业务层负责金额的显示格式化

```csharp
// 存储: 999 (分)
// 显示: $9.99
public static string FormatAmount(long amount, string currency)
{
    return currency switch
    {
        "USD" => $"${amount / 100m:F2}",
        "JPY" => $"¥{amount}",  // 日元无小数
        _ => $"{amount / 100m:F2} {currency}"
    };
}
```

## 12. 迁移策略

从旧GodGPT支付系统迁移：

```csharp
foreach (var oldPayment in oldData.PaymentHistory)
{
    var newRecord = new CreatePaymentRequest
    {
        BusinessType = "godgpt",
        BusinessId = oldPayment.UserId.ToString(),
        BusinessMetadata = new Dictionary<string, string>
        {
            ["membership_level"] = oldPayment.MembershipLevel,
            ["migrated_from"] = "legacy_user_billing"
        },
        // ... 映射其他字段
    };
    
    await newAgent.CreatePaymentRecordAsync(newRecord);
    
    foreach (var oldInvoice in oldPayment.InvoiceDetails)
    {
        await newAgent.AddTransactionAsync(paymentId, MapToTransaction(oldInvoice));
    }
}
```
