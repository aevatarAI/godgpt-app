# GodGPT 模块化架构设计

> 版本: 1.0 | 日期: 2025-12-08

---

## 1. 现状与问题

### 项目结构

```
godgpt-app/
├── apps/Aevatar.App/
│   └── src/
│       ├── Aevatar.Silo/              # Orleans Grain Host
│       ├── Aevatar.App.HttpApi.Host/  # HTTP API入口
│       └── Aevatar.App.Application/   # 服务层
│
└── agents/Aevatar.Agents.GodGPT/      # Agent实现
    ├── ChatManager/
    ├── UserBilling/
    └── Invitation/
```

### 问题代码统计

| 文件 | 行数 | 职责 | 问题 |
|------|------|------|------|
| `GodGPTService.cs` | 956 | 10+ | 🔴 God Service |
| `ChatManagerGAgent.cs` | 3163 | 15+ | 🔴 Super Agent |
| `UserBillingGAgent.cs` | 5469 | 12+ | 🔴 Super Agent |

### 核心问题：职责混乱

`GodGPTService` 违反单一职责，混合了多个业务域:

```csharp
public interface IGodGPTService
{
    // 会话管理 → 应属于 Chat 模块
    Task<Guid> CreateSessionAsync(...);
    Task<List<SessionInfoDto>> GetSessionListAsync(...);
    
    // 支付功能 → 应属于 Payment 模块
    Task<List<StripeProductDto>> GetStripeProductsAsync(...);
    Task<string> CreateCheckoutSessionAsync(...);
    Task<AppStoreSubscriptionResponseDto> VerifyAppStoreReceiptAsync(...);
    
    // 邀请功能 → 应属于 Invitation 模块
    Task<GetInvitationInfoResponse> GetInvitationInfoAsync(...);
    Task<RedeemInviteCodeResponse> RedeemInviteCodeAsync(...);
    
    // 用户管理 → 应属于 User 模块
    Task<UserProfileDto> GetUserProfileAsync(...);
    Task<Guid> DeleteAccountAsync(...);
}
```

**影响**: 代码耦合高、测试困难、扩展困难、团队协作冲突

---

## 2. 设计目标

| 目标 | 说明 |
|------|------|
| 单一职责 | 每个模块只负责一个业务域 |
| 可插拔性 | 新业务可快速集成 |
| 运行时无关 | 同一代码支持Local和Orleans |

### 模块分类

**通用模块** (可复用，放 `modules/`):

| 模块 | 职责 | Agent |
|------|------|-------|
| **Payment** | 支付、订阅 | UserBillingGAgent |
| **User** | 资料、配额、反馈、配置 | UserQuotaGAgent, UserFeedbackGAgent, ConfigurationGAgent |
| **Invitation** | 邀请码、奖励 | InvitationGAgent, InviteCodeGAgent |
| **Identity** | 匿名用户 | AnonymousUserGAgent |
| **Analytics** | 统计 | UserStatisticsGAgent |

**业务模块** (GodGPT专属，放 `apps/.../GodGPT/`):

| 模块 | 职责 | Agent |
|------|------|-------|
| **Chat** | 会话、消息、分享 | ChatManagerGAgent, GodChatGAgent |
| **Awakening** | 每日觉醒内容 (依赖LLM) | AwakeningGAgent |
| **Push** | 每日推送 | DailyPushCoordinatorGAgent |
| **Speech** | 语音服务 | SpeechService |

---

## 3. 模块化架构

### 每模块2个项目

```
modules/
├── Aevatar.Payment/                # 主模块
│   ├── Services/
│   │   ├── IPaymentService.cs
│   │   └── PaymentService.cs
│   ├── Providers/
│   │   ├── IPaymentProvider.cs     # 策略接口
│   │   ├── StripeProvider.cs
│   │   └── ApplePayProvider.cs
│   ├── Controllers/
│   │   └── PaymentController.cs
│   ├── Models/
│   └── PaymentModule.cs
│
└── Aevatar.Payment.Agents/         # Agent模块 (Silo专用)
    ├── UserBillingGAgent.cs
    ├── Protos/
    │   └── user_billing.proto
    └── Aevatar.Payment.Agents.csproj
```

### 为什么分离Agent?

```
Silo:           只需 *.Agents 用于Grain托管
HttpApi.Host:   需要主模块 (自动传递Agent引用)
```

### 完整项目结构

```
godgpt-app/
├── modules/                              # 通用可复用模块
│   ├── Aevatar.Payment/                  # 支付
│   ├── Aevatar.Payment.Agents/
│   ├── Aevatar.User/                     # 用户 (配额+反馈+配置)
│   ├── Aevatar.User.Agents/
│   ├── Aevatar.Invitation/               # 邀请
│   ├── Aevatar.Invitation.Agents/
│   ├── Aevatar.Identity/                 # 身份
│   ├── Aevatar.Identity.Agents/
│   ├── Aevatar.Analytics/                # 分析
│   └── Aevatar.Analytics.Agents/
│
└── apps/Aevatar.App/
    └── src/
        ├── Aevatar.GodGPT/               # GodGPT业务模块
        │   ├── Services/
        │   └── Controllers/
        ├── Aevatar.GodGPT.Agents/        # GodGPT业务Agent
        │   ├── Chat/                     # ChatManager, GodChat
        │   ├── Awakening/                # 觉醒系统
        │   ├── Push/                     # 每日推送
        │   └── Speech/                   # 语音服务
        ├── Aevatar.Silo/
        └── Aevatar.App.HttpApi.Host/

通用模块: 10个项目 (5模块 × 2)
业务模块: 2个项目 (GodGPT + GodGPT.Agents)
```

---

## 4. 集成方案

### 运行模式

| 模式 | Silo | HttpApi.Host | 适用场景 |
|------|------|--------------|---------|
| Orleans | 独立进程托管Grain | Orleans Client | 生产 |
| Local | - | 内存托管Agent | 开发 |

### 项目引用配置

**Silo.csproj** - 引用所有Agent:
```xml
<ItemGroup>
  <!-- 通用模块Agent -->
  <ProjectReference Include="modules/Aevatar.Payment.Agents/..." />
  <ProjectReference Include="modules/Aevatar.User.Agents/..." />
  <!-- 业务模块Agent -->
  <ProjectReference Include="src/Aevatar.GodGPT.Agents/..." />
</ItemGroup>
```

**HttpApi.Host.csproj** - 引用主模块:
```xml
<ItemGroup>
  <!-- 通用模块 -->
  <ProjectReference Include="modules/Aevatar.Payment/..." />
  <ProjectReference Include="modules/Aevatar.User/..." />
  <!-- 业务模块 -->
  <ProjectReference Include="src/Aevatar.GodGPT/..." />
</ItemGroup>
```

### ABP Module配置

```csharp
[DependsOn(typeof(AbpAspNetCoreMvcModule))]
public class PaymentModule : AbpModule
{
    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        context.Services.AddScoped<IPaymentProvider, StripeProvider>();
        context.Services.AddScoped<IPaymentProvider, ApplePayProvider>();
        context.Services.AddScoped<IPaymentService, PaymentService>();
    }
}
```

---

## 5. Payment模块设计 (策略模式)

### 策略接口

```csharp
public interface IPaymentProvider
{
    PaymentPlatform Platform { get; }
    Task<List<ProductDto>> GetProductsAsync();
    Task<SubscriptionResult> CreateSubscriptionAsync(SubscriptionRequest request);
    Task<bool> HandleWebhookAsync(string payload, string signature);
}
```

### 服务调度器

```csharp
public class PaymentService : IPaymentService
{
    private readonly IEnumerable<IPaymentProvider> _providers;
    private readonly IGAgentFactory _agentFactory;
    
    public async Task<SubscriptionResult> CreateSubscriptionAsync(
        Guid userId, PaymentPlatform platform, SubscriptionRequest request)
    {
        var provider = _providers.First(p => p.Platform == platform);
        var result = await provider.CreateSubscriptionAsync(request);
        
        if (result.Success)
        {
            var agent = _agentFactory.CreateGAgent<UserBillingGAgent>(userId);
            await agent.RecordSubscriptionAsync(result);
        }
        return result;
    }
}
```

### 添加新支付平台 (如微信)

```csharp
// Step 1: 新增Provider
public class WeChatPayProvider : IPaymentProvider
{
    public PaymentPlatform Platform => PaymentPlatform.WeChat;
    // ... 实现接口方法
}

// Step 2: 注册DI
context.Services.AddScoped<IPaymentProvider, WeChatPayProvider>();

// Done! 无需修改其他代码
```

---

## 6. 迁移策略

### 迁移阶段

| 阶段 | 时间 | 内容 |
|------|------|------|
| Phase 1 | Week 1-2 | 创建目录结构，配置引用 |
| Phase 2 | Week 3-5 | **通用**: Payment模块 (策略模式) |
| Phase 3 | Week 6-7 | **通用**: User + Invitation模块 |
| Phase 4 | Week 8-10 | **业务**: GodGPT.Chat (拆分ChatManager) |
| Phase 5 | Week 11-12 | **业务**: GodGPT.Awakening + Push |
| Phase 6 | Week 13-14 | 废弃GodGPTService，清理 |

### 数据迁移：代理模式 + 后台同步

**核心思路**: 新模块作为代理层，未同步数据转发老服务

```
请求 → 检查数据是否已同步?
        ├── 是 → 返回新Agent数据
        └── 否 → 转发老服务 + 触发后台同步
```

**代码示例**:

```csharp
public class ChatService : IChatService
{
    private readonly ILegacyGodGPTClient _legacyClient;
    private readonly IDataSyncStatusService _syncStatus;
    
    public async Task<List<ChatMessageDto>> GetSessionMessagesAsync(Guid userId, Guid sessionId)
    {
        if (await _syncStatus.IsUserSyncedAsync(userId))
        {
            // 已同步，走新Agent
            var agent = _agentFactory.CreateGAgent<SessionGAgent>(sessionId);
            return await agent.GetMessagesAsync();
        }
        
        // 未同步，转发老服务 + 异步触发同步
        var data = await _legacyClient.GetSessionMessagesAsync(userId, sessionId);
        await _jobManager.EnqueueAsync<ChatDataSyncJob>(userId);
        return data;
    }
}
```

### 同步策略

| 策略 | 触发条件 | 优先级 |
|------|---------|--------|
| 按需同步 | 用户访问时 | 高 |
| 批量同步 | 定时任务 | 中 |
| 活跃用户优先 | 最近7天活跃 | 高 |

### 兼容性: Facade模式

迁移期间保留 `GodGPTService` 作为门面:

```csharp
public class GodGPTService : IGodGPTService
{
    private readonly IPaymentService _paymentService;  // 新模块
    
    public Task<List<StripeProductDto>> GetStripeProductsAsync(Guid userId)
        => _paymentService.GetProductsAsync(PaymentPlatform.Stripe);
}
```

---

## 7. FAQ

**Q1: 为什么2个项目而不是4个?**  
A: 避免项目数量爆炸。Agent必须独立供Silo引用，其他合并即可。

**Q2: 模块间如何通信?**  
A: 两种方式:
- DI注入其他模块Service
- Agent事件系统 (`PublishAsync` / `[EventHandler]`)

**Q3: 如何添加新模块?**  
A: 3步:
1. 创建 `Aevatar.NewModule/` + `Aevatar.NewModule.Agents/`
2. Silo.csproj 引用Agent
3. HttpApiHostModule 添加依赖

**Q4: 如果数据不一致?**  
A: 迁移期间新数据双写，提供数据校验工具，发现不一致时重新同步。

---

## 8. 风险与回滚

### 主要风险

| 风险 | 缓解措施 |
|------|---------|
| 数据同步失败 | 保留老服务作为备份 |
| 性能下降 | 代理层延迟监控告警 |
| 新模块Bug | 灰度发布 |

### 回滚配置

```json
{
  "Migration": {
    "ForceUseLegacy": false,       // 紧急回滚开关
    "EnabledModules": ["Payment"]  // 已启用模块
  }
}
```

```csharp
if (_config.GetValue<bool>("Migration:ForceUseLegacy"))
    return await _legacyClient.GetData();
```

### 灰度策略

```
1% 内部测试 → 5% 小范围 → 20% → 50% → 100%
```

---

## 收益预期

| 指标 | 现状 | 重构后 |
|------|------|--------|
| 最大文件行数 | 5469 | <500 |
| 添加新支付平台 | 改5000行 | 新增1个类 |
| 单元测试覆盖率 | 困难 | 80%+ |
| 新功能集成时间 | 1-2周 | 2-3天 |
