# Legacy Configuration Migration Analysis

**Date:** 2026-01-05  
**Scope:** Configuration migration from `old/godgpt-api` to `apps/Aevatar.App`

---

## 📊 Executive Summary

| 配置项 | 状态 | 部署位置 | 说明 |
|--------|------|----------|------|
| `AppleAuth.RedirectUrls` | 🗑️ **已废弃** | - | 已迁移到 OpenIddict Grant Handler |
| `Account.*` | ⚠️ **部分废弃** | - | ResetPasswordUrl/CNResetPasswordUrl 不再使用 |
| `Thumbnail.*` | ⚠️ **配置格式变化** | HttpApi | 移除 `Sizes[].Name` 属性 |
| `GoogleAnalytics.*` | ✅ **继续使用** | HttpApi | 配置不变 |
| `ExternalLocalization.*` | 🗑️ **已废弃** | - | 改为硬编码字典 |
| `Settings.Abp.Mailing.*` | ✅ **继续使用** | HttpApi | 配置不变 |
| `OpenTelemetry.*` | ✅ **已集成** | All | AuthServer/Silo/HttpApi 均已添加 |
| `Authentication.Google.*` | ⚠️ **配置结构变化** | AuthServer | 实际使用 `GoogleAuth`，新代码改为 `Google` |
| `GoogleLogin.RedirectUrl` | 🗑️ **已废弃** | - | 旧代码中已是遗留未使用配置 |
| `RolePrompts` | ✅ **Silo 专用** | Silo | AI 角色提示词配置 |
| `ApplePay` | ✅ **Silo 专用** | Silo | Apple 应用内购买配置 |
| `GooglePay` | ✅ **Silo 专用** | Silo | Google Play 支付配置 |
| `Awakening` | ✅ **Silo 专用** | Silo | 唤醒系统配置 |

---

## 🗑️ 已废弃配置 (需要删除)

### 1. AppleAuth.RedirectUrls

**旧配置:**
```json
"AppleAuth": {
  "RedirectUrls": {
    "mobile": "https://godgpt-ui-dev.aelf.dev",
    "mobilecn": "https://godgpt-cn-ui-testnet.aelf.dev",
    "mobilefun": "https://godgpt-ui-dev.aelf.dev"
  }
}
```

**废弃原因:**
- 新架构使用 **OpenIddict Grant Handler** 模式替代独立 Controller
- 配置结构完全改变：`Apple.APPs.{AppName}.MobileRedirectUri`
- Apple 认证流程由 OpenIddict 统一管理

**新配置位置:**
```json
"Apple": {
  "APPs": {
    "GodGPT": {
      "MobileRedirectUri": "https://...",
      "RedirectUri": "https://..."
    }
  }
}
```

**清理操作:**
- ✅ 删除 `apps/Aevatar.App/src/Aevatar.App.Application/Options/AppleAuthOption.cs`
- ✅ 从 `appsettings.json` 移除 `AppleAuth` 配置节

---

### 2. Account.ResetPasswordUrl / CNResetPasswordUrl

**旧配置:**
```json
"Account": {
  "ResetPasswordUrl": "https://godgpt-ui-dev.aelf.dev/reset-password",
  "CNResetPasswordUrl": "https://app-staging.godgpt.cc/reset-password/",
  "TokenLifespan": 10
}
```

**废弃原因:**
- 旧代码使用 **链接重置模式** (`SendPasswordResetLinkAsync`)，需要构建完整 URL
- 新代码改为 **验证码模式** (`SendPasswordResetCodeAsync`)，用户输入验证码而非点击链接
- `TokenLifespan` 由 OpenIddict 配置管理 (`AccessTokenLifetime`)

**旧代码实现:**
```csharp
// old/godgpt-api/src/Aevatar.Application/Account/AevatarAccountEmailer.cs
var link = $"{url}?userId={user.Id}&email={inputEmail}&resetToken={resetToken}";
await _emailSender.SendAsync(user.Email, subject, emailContent);
```

**新代码实现:**
```csharp
// apps/Aevatar.App/src/Aevatar.App.Application.Contracts/Services/IAccountService.cs
Task SendPasswordResetCodeAsync(SendPasswordResetCodeDto input, GodGPTChatLanguage language);
// 发送验证码，用户在前端输入验证码重置密码
```

**清理操作:**
- ✅ 从 `appsettings.json` 移除 `Account` 配置节

---

### 3. ExternalLocalization

**旧配置:**
```json
"ExternalLocalization": {
  "BasePath": "/app/localization",
  "GodGPTSubPath": "godgpt",
  "LumenSubPath": "lumen"
}
```

**废弃原因:**
- 旧代码从磁盘文件加载翻译：`/app/localization/godgpt/en.json`
- 新代码使用硬编码字典，翻译文本直接写在代码中
- 简化部署（无需挂载文件），提升启动性能

**旧代码实现:**
```csharp
// old/godgpt-api/src/Aevatar.Application/Localization/ExternalLocalizationService.cs
var filePath = Path.Combine(_basePath, resourceName.ToLower(), $"{cultureName}.json");
var jsonContent = File.ReadAllText(filePath);
return JsonDocument.Parse(jsonContent);
```

**新代码实现:**
```csharp
// apps/Aevatar.App/src/Aevatar.App.Application/Services/LocalizationService.cs
private Dictionary<string, Dictionary<string, string>> LoadTranslations()
{
    return new Dictionary<string, Dictionary<string, string>>
    {
        ["exceptions"] = new Dictionary<string, string>
        {
            ["en.Unauthorized"] = "Unauthorized: User is not authenticated.",
            ["zh.Unauthorized"] = "未授权：用户未通过身份验证。",
            // ... 所有翻译硬编码
        }
    };
}
```

**清理操作:**
- ✅ 从 `appsettings.json` 移除 `ExternalLocalization` 配置节

---

### 4. GoogleLogin.RedirectUrl

**旧配置:**
```json
"GoogleLogin": {
  "RedirectUrl": "localhost:8001/test"
}
```

**旧代码使用情况:**
- 配置类定义在：`old/godgpt-api/src/Aevatar.Domain.Shared/Options/GoogleLoginOptions.cs`
- 配置注册位置：仅在 `Developer.Host` 模块中注册
  ```csharp
  // old/godgpt-api/src/Aevatar.Developer.Host/AevatarDeveloperHostModule.cs
  Configure<GoogleLoginOptions>(configuration.GetSection("GoogleLogin"));
  ```
- ⚠️ **未找到实际使用此配置的代码** - 虽然配置被注册，但没有任何 Service 或 Controller 注入 `IOptions<GoogleLoginOptions>`
- 这是一个**遗留废弃配置**，可能在某个版本后被弃用但未清理

**旧代码配置类:**
```csharp
// old/godgpt-api/src/Aevatar.Domain.Shared/Options/GoogleLoginOptions.cs
public class GoogleLoginOptions
{
    public string RedirectUrl { get; set; }
}
```

**废弃原因:**
- 旧代码中该配置已经未被使用
- 新代码使用 **OpenIddict Grant Handler** 模式，Google OAuth 流程由 OpenIddict 统一管理
- 重定向 URL 由 OpenIddict 应用配置中的 `RedirectUri` 管理

**清理操作:**
- ✅ 从 `appsettings.json` 移除 `GoogleLogin` 配置节
- ✅ 删除 `apps/Aevatar.App/src/Aevatar.App.Application/Options/GoogleLoginOptions.cs` (如果存在且未使用)

---

## ⚠️ 配置格式变化 (需要调整)

### Thumbnail Configuration

**配置变化:**

| 属性 | 旧格式 | 新格式 | 说明 |
|------|--------|--------|------|
| `Sizes[].Name` | ✅ `"small"`, `"medium"`, `"large"` | ❌ **已移除** | 不再需要名称字段 |
| `Sizes[].Width` | ✅ 保留 | ✅ 保留 | 不变 |
| `Sizes[].Height` | ✅ 保留 | ✅ 保留 | 不变 |
| `Sizes[].ResizeMode` | ✅ 保留 | ✅ 保留 | 不变 |

**旧配置格式:**
```json
"Thumbnail": {
  "EnableThumbnail": true,
  "Quality": 85,
  "Format": "webp",
  "Sizes": [
    { "Name": "small", "Width": 200, "Height": 200, "ResizeMode": 0 },
    { "Name": "medium", "Width": 300, "Height": 300, "ResizeMode": 0 },
    { "Name": "large", "Width": 600, "Height": 600, "ResizeMode": 0 }
  ]
}
```

**新配置格式 (移除 Name):**
```json
"Thumbnail": {
  "EnableThumbnail": true,
  "Quality": 85,
  "Format": "webp",
  "Sizes": [
    { "Width": 200, "Height": 200, "ResizeMode": 0 },
    { "Width": 300, "Height": 300, "ResizeMode": 0 },
    { "Width": 600, "Height": 600, "ResizeMode": 0 }
  ]
}
```

**原因:**
- 新代码使用 `GetSizeName()` 方法自动生成名称：`"{Width}-{Height}px"`
- 生成的文件名格式：`filename@200-200px.webp`

**迁移操作:**
- ✅ 从 `Sizes` 数组中移除所有 `Name` 字段
- ✅ 其他配置保持不变

---

### 2. Authentication.Google → Google (配置结构变化)

#### ⚠️ 重要说明：旧代码中的 Google 配置体系

**旧代码实际上有两套不同的 Google 配置：**

1. **`Authentication.Google`** - 未找到实际使用代码，可能是预留或废弃配置
2. **`GoogleAuth`** - 在 GodGPT.GAgents 项目中实际使用，用于 Google 账户绑定（Calendar/Tasks 集成）

#### 旧代码中的 GoogleAuth 配置使用

**配置注册位置:**
```csharp
// old/godgpt/src/GodGPT.GAgents/GodGPTGAgentModule.cs
Configure<GoogleAuthOptions>(configuration.GetSection("GoogleAuth"));
```

**旧代码配置类 (GoogleAuthOptions):**
```csharp
// old/godgpt/src/GodGPT.GAgents/Common/Options/GoogleAuthOptions.cs
public class GoogleAuthOptions
{
    public string TokenEndpoint { get; set; } = "https://oauth2.googleapis.com/token";
    public string CalendarApiEndpoint { get; set; } = "https://www.googleapis.com/calendar/v3";
    public string TasksApiEndpoint { get; set; } = "https://tasks.googleapis.com/tasks/v1";
    public List<string> Scopes { get; set; } = new();
    public string WebhookBaseUrl { get; set; }
    public int TokenRefreshThresholdMinutes { get; set; } = 10;
    
    // 多平台配置支持
    public Dictionary<string, GooglePlatformConfig?> PlatformConfigs { get; set; } = new();
}

public class GooglePlatformConfig
{
    public string ClientId { get; set; }
    public string ClientSecret { get; set; }
    public string RedirectUri { get; set; }
    public bool RequiresClientSecret { get; set; } = true; // iOS 不需要 ClientSecret
    public List<string>? Scopes { get; set; }
}
```

**旧代码使用场景:**
- `GoogleAuthGAgent` - Google OAuth2 账户绑定
- Google Calendar 日程同步
- Google Tasks 任务同步
- 多平台支持 (web/ios/android)

**旧代码流程:**
```csharp
// old/godgpt-api/src/Aevatar.HttpApi/Controllers/GodGPTGoogleAuthController.cs
[Route("api/godgpt/google")]
public class GodGPTGoogleAuthController : AevatarController
{
    [HttpPost("verify-code")]    // 验证授权码并绑定账户
    [HttpDelete("unbind")]       // 解绑 Google 账户
    [HttpGet("bind-status")]     // 获取绑定状态
}
```

#### 新代码配置变化

**配置对比:**

| 属性 | 旧格式 (GoogleAuth) | 新格式 (Google) | 说明 |
|------|---------------------|-----------------|------|
| 配置节名称 | `GoogleAuth` | `Google` | 简化配置路径 |
| `ClientId` | 在 PlatformConfigs 中 | ✅ 顶级属性 | 简化单应用场景 |
| `ClientSecret` | 在 PlatformConfigs 中 | ❌ **已移除** | OpenIddict 不需要 |
| `PlatformConfigs` | ✅ 多平台支持 | `AppConfigs` | 改名 |
| `TokenEndpoint` | ✅ 存在 | ❌ 不需要 | OpenIddict 内置 |
| `CalendarApiEndpoint` | ✅ 存在 | ❌ 不需要 | 新代码无此功能 |
| `IOSClientId` | ❌ 在 PlatformConfigs 中 | ✅ 顶级属性 | 简化配置 |

**新配置格式:**
```json
"Google": {
  "ClientId": "664186607150-8b7sufft3mdp77pvoa2mts0hm2t1s7ed.apps.googleusercontent.com",
  "IOSClientId": "ios-client-id-here",
  "AndroidClientId": "android-client-id-here",
  "AppConfigs": {
    "GodGPT": {
      "ClientId": "app-specific-client-id",
      "IOSClientId": "app-specific-ios-client-id",
      "AndroidClientId": "app-specific-android-client-id"
    }
  }
}
```

**配置注册位置:**
```csharp
// apps/Aevatar.App/src/Aevatar.AuthServer/AuthServerModule.cs
context.Services.Configure<GoogleOptions>(configuration.GetSection("Google"));
```

**原因:**
- 新架构使用 OpenIddict Grant Handler，Google OAuth 通过 `id_token` 验证，不需要 `ClientSecret`
- 新代码仅用于**登录认证**，不包含 Google Calendar/Tasks 集成功能
- 如需恢复日历/任务集成功能，需要单独添加 GoogleAuth 配置

**迁移操作:**
- ✅ 将 `Authentication.Google` 和 `GoogleAuth` 统一为 `Google` 配置节
- ✅ 移除 `ClientSecret` 字段（OpenIddict 使用 id_token 验证）
- ✅ 如需多应用支持，配置 `AppConfigs` 结构
- ⚠️ 如需 Google Calendar/Tasks 功能，需要额外集成

---

## ✅ 继续使用的配置 (配置不变)

### GoogleAnalytics

**配置结构:** 保持不变

```json
"GoogleAnalytics": {
  "EnableAnalytics": true,
  "MeasurementId": "G-LMQLPL5Y9D",
  "ApiSecret": "YOUR_GA4_API_SECRET",
  "ApiEndpoint": "https://www.google-analytics.com/mp/collect",
  "TimeoutSeconds": 10
}
```

**说明:** 功能完全保留，配置格式不变。注意在生产环境填入真实的 `ApiSecret`。

---

### Settings.Abp.Mailing (SMTP 配置)

**配置结构:** 保持不变

```json
"Settings": {
  "Abp.Mailing.Smtp.Host": "email-smtp.ap-northeast-1.amazonaws.com",
  "Abp.Mailing.Smtp.Port": "587",
  "Abp.Mailing.Smtp.UserName": "AKIAY4SM3WWTCRJ4XQES",
  "Abp.Mailing.Smtp.Password": "BB2GRj3FZL8NnN9gJCOAaenF91IVETH7bKMklS3GDB5f",
  "Abp.Mailing.Smtp.Domain": "",
  "Abp.Mailing.Smtp.EnableSsl": "true",
  "Abp.Mailing.Smtp.UseDefaultCredentials": "false",
  "Abp.Mailing.DefaultFromAddress": "godgpt-noreply@portkey.finance",
  "Abp.Mailing.DefaultFromDisplayName": "GodGPT"
}
```

**说明:** 
- 新代码使用 `AbpEmailingModule`，通过 `IEmailSender` 发送邮件
- 用于注册验证码、密码重置码等邮件发送功能
- 配置格式完全兼容，无需修改

**使用位置:**
- `AccountService.SendRegisterCodeAsync()` - 发送注册验证码
- `AccountService.SendPasswordResetCodeAsync()` - 发送密码重置码

---

### OpenTelemetry (配置方式变化)

**旧配置:**
```json
"OpenTelemetry": {
  "CollectorEndpoint": "http://otel-collector-testnet-collector.observability:4315",
  "ServiceName": "Aevatar.godgpt-test.Host",
  "ServiceVersion": "1.0.0"
}
```

**旧代码使用:**
- 旧代码使用 `AElf.OpenTelemetry` 模块进行可观测性集成
- 配置了 OTLP Collector 端点用于收集遥测数据
- 在 `AevatarHttpApiHostModule` 中注册了 `OpenTelemetryModule`

**新代码状态:**
- ⚠️ 当前新代码未集成 OpenTelemetry
- 项目已引用 OpenTelemetry 包 (版本 1.12.0)
- 需要在启动代码中添加配置

---

#### .NET 10 OpenTelemetry 集成指南

**.NET 10 使用标准 OpenTelemetry SDK，不再依赖 `AElf.OpenTelemetry` 模块。**

**1. 已引用的包 (Directory.Packages.props):**
```xml
<PackageVersion Include="OpenTelemetry" Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Extensions.Hosting" Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.AspNetCore" Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Http" Version="1.12.0" />
<PackageVersion Include="OpenTelemetry.Instrumentation.Runtime" Version="1.12.0" />
```

**2. 代码配置方式 (Program.cs 或 Module):**
```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource
        .AddService(
            serviceName: builder.Configuration["OpenTelemetry:ServiceName"] ?? "Aevatar.App",
            serviceVersion: builder.Configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:CollectorEndpoint"] 
                ?? "http://localhost:4317");
        }))
    .WithMetrics(metrics => metrics
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddRuntimeInstrumentation()
        .AddOtlpExporter(options =>
        {
            options.Endpoint = new Uri(
                builder.Configuration["OpenTelemetry:CollectorEndpoint"] 
                ?? "http://localhost:4317");
        }));
```

**3. 新配置格式 (appsettings.json):**
```json
"OpenTelemetry": {
  "CollectorEndpoint": "http://otel-collector:4317",
  "ServiceName": "Aevatar.App.HttpApi.Host",
  "ServiceVersion": "1.0.0"
}
```

**4. 环境变量配置 (推荐用于 K8s/Docker):**
```bash
# OpenTelemetry 标准环境变量
OTEL_EXPORTER_OTLP_ENDPOINT=http://otel-collector:4317
OTEL_SERVICE_NAME=Aevatar.App.HttpApi.Host
OTEL_SERVICE_VERSION=1.0.0

# 可选：协议选择 (grpc 或 http/protobuf)
OTEL_EXPORTER_OTLP_PROTOCOL=grpc
```

**5. Aspire 自动集成:**
如果使用 .NET Aspire (如 `Aevatar.AppHost`)，OpenTelemetry 会自动配置：
- Aspire Dashboard 自动收集 traces/metrics/logs
- 无需手动配置 exporter endpoint
- 分布式追踪自动关联

**迁移操作:**
- ⚠️ 如需可观测性，在 HttpApi.Host/Silo/AuthServer 中添加上述代码配置
- ✅ 配置格式与旧版兼容，只需更新端口 (4315 → 4317 for gRPC)
- ✅ 或使用环境变量配置，无需修改 appsettings.json

---

## 🎮 Silo 专用配置 (Agent 业务配置)

以下配置仅在 **Silo** 运行时使用，由 `GodGPTGAgentModule` 注册，用于 GAgent 业务逻辑。

### RolePrompts (AI 角色提示词)

**配置用途:** 定义不同 AI 角色的系统提示词，用于角色扮演聊天场景。

**配置注册:**
```csharp
// agents/Aevatar.Agents.GodGPT/GodGPTGAgentModule.cs
Configure<RolePromptOptions>(configuration.GetSection("RolePrompts"));
```

**使用位置:**
- `ChatManagerGAgent` - 获取角色特定提示词
- `AnonymousUserGAgent` - 匿名用户聊天时的角色提示

**配置格式:**
```json
"RolePrompts": {
  "RolePrompts": {
    "Echo·Yun": "You are a master of metaphysics, highly skilled in BaZi...",
    "Echo·Ira": "You are an astrology master...",
    "Echo·Seed": "You are an Inner Insight Oracle...",
    "Echo·KongZi": "你是 孔子·修...",
    "Echo·ZhuangZi": "你是 庄子·游...",
    "Echo·Yijing": "你是 易经·灵..."
  }
}
```

**配置类:**
```csharp
// agents/Aevatar.Agents.GodGPT/Common/Options/RolePromptOptions.cs
public class RolePromptOptions
{
    public Dictionary<string, string> RolePrompts { get; set; } = new();
}
```

---

### ApplePay (Apple 支付配置)

**配置用途:** Apple App Store Connect API 配置，用于 iOS 应用内购买验证和订阅管理。

**配置注册:**
```csharp
// agents/Aevatar.Agents.GodGPT/GodGPTGAgentModule.cs
Configure<ApplePayOptions>(configuration.GetSection("ApplePay"));

// modules/Aevatar.Payment/PaymentModule.cs
context.Services.Configure<ApplePayOptions>(configuration.GetSection(ApplePayOptions.SectionName));
```

**使用位置:**
- `ApplePayProvider` - Apple 支付验证和订阅状态查询
- 订阅管理和退款处理

**配置格式:**
```json
"ApplePay": {
  "Environment": "Production",
  "IssuerId": "6d2c681f-53e1-46df-8200-3419ce03db39",
  "BundleId": "com.gpt.god",
  "KeyId": "XPC9X932LF",
  "PrivateKey": "MIGTAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBHkwd...",
  "SharedSecret": "",
  "NotificationToken": "",
  "Products": [
    {
      "PlanType": 4,
      "ProductId": "weekly6",
      "Name": "Weekly",
      "Amount": 6,
      "Currency": "USD",
      "IsUltimate": false
    }
  ]
}
```

**关键字段说明:**

| 字段 | 说明 |
|------|------|
| `Environment` | `Production` / `Sandbox` - App Store 环境 |
| `IssuerId` | App Store Connect API Issuer ID |
| `BundleId` | iOS 应用 Bundle Identifier |
| `KeyId` | App Store Connect API Key ID |
| `PrivateKey` | P8 私钥内容 (Base64 编码) |
| `Products` | 订阅产品列表，`PlanType`: 2=月付, 3=年付, 4=周付 |
| `IsUltimate` | 是否为旗舰版订阅 |

---

### GooglePay (Google Play 支付配置)

**配置用途:** Google Play 订阅验证配置，支持 RevenueCat 和原生 Google Play API。

**配置注册:**
```csharp
// agents/Aevatar.Agents.GodGPT/GodGPTGAgentModule.cs
Configure<GooglePayOptions>(configuration.GetSection("GooglePay"));
context.Services.AddSingleton<IPostConfigureOptions<GooglePayOptions>, GooglePayOptionsPostProcessor>();
```

**使用位置:**
- `GooglePayService` - Google Play 订阅验证
- `GooglePaySecurityValidator` - Pub/Sub 签名验证

**配置格式:**
```json
"GooglePay": {
  "RevenueCatApiKey": "goog_dQUhEcmAngAEknjYjEgMBTMSwcQ",
  "RevenueCatBaseUrl": "https://api.revenuecat.com/v1",
  "PackageName": "com.ai.godgpt",
  "RsaPublicKey": "MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA2+lb...",
  "Products": [
    {
      "PlanType": 4,
      "ProductId": "godgpt_test_premium_weekly:basic",
      "Amount": 6,
      "Currency": "USD",
      "IsSubscription": true,
      "IsUltimate": false
    }
  ]
}
```

**关键字段说明:**

| 字段 | 说明 |
|------|------|
| `RevenueCatApiKey` | RevenueCat Google Play API Key (以 `goog_` 开头) |
| `PackageName` | Android 应用包名 |
| `RsaPublicKey` | Google Play Console RSA 公钥，用于 Pub/Sub 签名验证 |
| `Products` | 订阅产品列表 |
| `IsSubscription` | 是否为订阅型产品 |

---

### Awakening (唤醒系统配置)

**配置用途:** GodGPT "唤醒" 功能配置，基于用户聊天内容生成个性化启发语。

**配置注册:**
```csharp
// agents/Aevatar.Agents.GodGPT/GodGPTGAgentModule.cs
Configure<AwakeningOptions>(configuration.GetSection("Awakening"));
```

**使用位置:**
- `AwakeningGAgent` - 唤醒内容生成
- `AwakeningPromptHelper` - 构建 LLM 提示词

**配置格式:**
```json
"Awakening": {
  "EnableAwakening": true,
  "MaxRetryAttempts": 1,
  "TimeoutSeconds": 5,
  "Temperature": 0.9,
  "EnableLanguageSpecificPrompt": false,
  "PromptTemplate": "Your task is to determine the user's awakening level and awakening sentence based on their chat information..."
}
```

**关键字段说明:**

| 字段 | 说明 |
|------|------|
| `EnableAwakening` | 是否启用唤醒功能 |
| `MaxRetryAttempts` | LLM 调用失败重试次数 |
| `TimeoutSeconds` | LLM 调用超时时间 |
| `Temperature` | LLM 生成温度 (0-1)，越高越有创意 |
| `PromptTemplate` | 唤醒提示词模板，`{USER_CONTEXT}` 为占位符 |

**返回格式:**
```json
{"level": 3, "message": "Your inner light is awakening..."}
```

---

## 📋 Migration Checklist

### 需要删除的配置节
- [ ] `AppleAuth` (整个配置节)
- [ ] `Account` (整个配置节)
- [ ] `ExternalLocalization` (整个配置节)
- [ ] `GoogleLogin` (整个配置节)

### 需要调整的配置节
- [ ] `Thumbnail.Sizes[]` - 移除所有 `Name` 字段
- [ ] `GoogleAuth` / `Authentication.Google` → `Google` - 更改配置节名称，移除 `ClientSecret`
  - 注：旧代码实际使用 `GoogleAuth` 配置（在 GodGPT.GAgents 中），新代码统一为 `Google`

### 保持不变但需要验证的配置节
- [ ] `GoogleAnalytics` - 确认 `ApiSecret` 已填入真实值
- [ ] `Settings.Abp.Mailing.*` - 确认 SMTP 配置正确 (仅 HttpApi.Host 需要)

### Silo 专用配置 (需要添加到 Silo appsettings.json)
- [ ] `RolePrompts` - AI 角色提示词配置
- [ ] `ApplePay` - Apple 支付配置 (生产环境需填入真实密钥)
- [ ] `GooglePay` - Google Play 支付配置
- [ ] `Awakening` - 唤醒系统配置

### 需要实现的配置节
- [ ] `OpenTelemetry` - 配置格式兼容，需在代码中添加 `AddOpenTelemetry()` 集成
  - 端口变化：4315 (旧 HTTP) → 4317 (新 gRPC)
  - 或使用 OTEL_* 环境变量配置

### 需要删除的代码文件
- [ ] `apps/Aevatar.App/src/Aevatar.App.Application/Options/AppleAuthOption.cs`
- [ ] `apps/Aevatar.App/src/Aevatar.App.Application/Options/GoogleLoginOptions.cs` (如果存在且未使用)

---

## 🔍 验证步骤

1. **删除配置后验证:**
   ```bash
   # 搜索是否还有代码引用这些配置
   grep -r "AppleAuthOption" apps/Aevatar.App/src/
   grep -r "AccountOptions" apps/Aevatar.App/src/
   grep -r "ExternalLocalizationOptions" apps/Aevatar.App/src/
   ```

2. **测试邮件发送:**
   ```bash
   # 测试注册验证码发送
   POST /api/account/send-register-code
   
   # 测试密码重置码发送
   POST /api/account/send-password-reset-code
   ```

3. **测试缩略图生成:**
   ```bash
   # 上传图片，验证缩略图是否正常生成
   POST /api/godgpt/storage/blob
   ```

---

**Document Generated:** 2026-01-05  
**Last Updated:** 2026-01-06  
**Revisions:**
- Added: OpenTelemetry, Authentication.Google, GoogleLogin analysis
- Updated: Detailed old code usage analysis for GoogleAuth and GoogleLogin configurations
- Added: .NET 10 OpenTelemetry integration guide with code examples and environment variables
- Added: Silo 专用配置分析 (RolePrompts, ApplePay, GooglePay, Awakening)

