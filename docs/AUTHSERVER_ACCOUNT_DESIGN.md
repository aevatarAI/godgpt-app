# AuthServer Account Module Design

## 1. Executive Summary

### 1.1 Objective
将账户管理功能（注册、验证码、密码重置、登出）从 HttpApi 迁移到 AuthServer，使其成为独立的认证服务模块，支持多个应用（GodGPT、Lumen 及未来应用）复用。

### 1.2 Design Principles
- **独立性**: AuthServer 拥有自己的 Account 模块，不依赖 HttpApi 的 Application 层
- **多应用支持**: 通过 `appName` 参数区分不同应用的配置
- **职责单一**: AuthServer 专注于身份认证相关功能

---

## 2. Architecture Overview

### 2.1 Current State (Before)

```
┌─────────────────────────────────────────────────────────────┐
│                        AuthServer                            │
│  ┌─────────────────┐  ┌─────────────────┐                   │
│  │ GoogleGrant     │  │ AppleGrant      │                   │
│  │ Handler         │  │ Handler         │                   │
│  └────────┬────────┘  └────────┬────────┘                   │
│           │                    │                             │
│           └────────┬───────────┘                             │
│                    ▼                                         │
│           OpenIddict /connect/token                          │
└─────────────────────────────────────────────────────────────┘
                              
┌─────────────────────────────────────────────────────────────┐
│                        HttpApi                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ AccountController (BROKEN - No IAccountService impl) │    │
│  │  - /api/app/account/register                             │    │
│  │  - /api/app/account/send-register-code                   │    │
│  │  - /api/app/account/verify-register-code                 │    │
│  │  - /api/app/account/reset-password                       │    │
│  │  - /api/app/account/logout (NOT EXISTS)                  │    │
│  └─────────────────────────────────────────────────────┘    │
│                         ↓                                    │
│              Aevatar.App.Application (shared)                │
└─────────────────────────────────────────────────────────────┘
```

**Problems:**
- `AccountController` 在 HttpApi 中但依赖 `IAccountService`（未实现）
- 账户管理功能分散在不同位置
- 无法支持多应用复用

### 2.2 Target State (After)

```
┌─────────────────────────────────────────────────────────────┐
│                     AuthServer (独立)                        │
│                                                              │
│  ┌──────────────── Account Module ─────────────────────┐    │
│  │                                                      │    │
│  │  AppAccountController                                │    │
│  │   POST /api/app/account/register                     │    │
│  │   POST /api/app/account/send-register-code           │    │
│  │   POST /api/app/account/verify-register-code         │    │
│  │   POST /api/app/account/send-password-reset-code     │    │
│  │   POST /api/app/account/verify-password-reset-token  │    │
│  │   POST /api/app/account/reset-password               │    │
│  │   POST /api/app/account/check-email-registered       │    │
│  │   POST /api/app/account/logout                       │    │
│  │                                                      │    │
│  │  AccountService                                      │    │
│  │   - Register logic                                   │    │
│  │   - Verification code management                     │    │
│  │   - Password reset logic                             │    │
│  │                                                      │    │
│  │  AccountEmailer                                      │    │
│  │   - Send verification code email                     │    │
│  │   - Send password reset email                        │    │
│  │                                                      │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  ┌──────────────── Grant Handlers ─────────────────────┐    │
│  │  GoogleGrantHandler                                  │    │
│  │  AppleGrantHandler                                   │    │
│  └──────────────────────────────────────────────────────┘    │
│                                                              │
│  OpenIddict /connect/token                                   │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│                        HttpApi                               │
│  ┌─────────────────────────────────────────────────────┐    │
│  │ AccountController (REMOVED or PROXY to AuthServer)   │    │
│  └─────────────────────────────────────────────────────┘    │
│                                                              │
│  Business Logic Controllers (GodGPT, Lumen, etc.)            │
└─────────────────────────────────────────────────────────────┘
```

---

## 3. Module Structure

### 3.1 AuthServer Project Structure

```
apps/Aevatar.App/src/Aevatar.AuthServer/
├── Account/
│   ├── AccountController.cs          # API endpoints (class: AppAccountController)
│   ├── AccountService.cs             # Business logic
│   ├── AccountEmailer.cs             # Email sending
│   ├── AccountOptions.cs             # Configuration
│   ├── Dtos/
│   │   ├── RegisterDto.cs
│   │   ├── SendRegisterCodeDto.cs
│   │   ├── VerifyRegisterCodeDto.cs
│   │   ├── SendPasswordResetCodeDto.cs
│   │   ├── ResetPasswordDto.cs
│   │   ├── CheckEmailRegisteredDto.cs
│   │   └── LogoutResultDto.cs
│   └── Templates/
│       ├── AccountEmailTemplates.cs
│       ├── AccountEmailTemplateDefinitionProvider.cs
│       ├── RegisterCode.tpl
│       ├── RegisterCode_zh-cn.tpl
│       ├── RegisterCode_zh-tw.tpl
│       ├── RegisterCode_es.tpl
│       ├── PasswordResetLink.tpl
│       ├── PasswordResetLink_zh-cn.tpl
│       ├── PasswordResetLink_zh-tw.tpl
│       └── PasswordResetLink_es.tpl
├── Grants/
│   ├── GoogleGrantHandler.cs         # (existing)
│   ├── AppleGrantHandler.cs          # (existing)
│   └── ...
├── AuthServerModule.cs               # Module configuration
└── ...
```

### 3.2 File Responsibilities

| File | Responsibility |
|------|---------------|
| `AccountController.cs` | HTTP endpoints (`AppAccountController` class, route: `/api/app/account/`) |
| `AccountService.cs` | Core business logic: registration, verification, password reset |
| `AccountEmailer.cs` | Email composition and sending using templates |
| `AccountOptions.cs` | Configuration options (URLs, timeouts, etc.) |
| `Templates/*.tpl` | Email templates in multiple languages |

---

## 4. API Specification

### 4.1 Route Prefix

使用 `/api/app/account/` 而非 `/api/account/` 的原因：
- **避免冲突**: ABP 框架的 `Volo.Abp.Account.HttpApi` 模块提供了 `/api/account/` 路由的管理界面功能
- **保留 ABP 原生功能**: ABP 的 `AccountController` 用于管理界面的用户管理，需要保留
- **清晰区分**: `/api/app/` 前缀表明这是应用级别的自定义 API

### 4.2 Endpoints

All endpoints support multi-app via `appName` parameter:

| Method | Endpoint | Description | Multi-App |
|--------|----------|-------------|-----------|
| POST | `/api/app/account/register` | Register with verification code | ✅ appName |
| POST | `/api/app/account/send-register-code` | Send email verification code | ✅ appName |
| POST | `/api/app/account/verify-register-code` | Verify email code | ✅ appName |
| POST | `/api/app/account/send-password-reset-code` | Send password reset link | ✅ appName |
| POST | `/api/app/account/verify-password-reset-token` | Verify reset token | ✅ appName |
| POST | `/api/app/account/reset-password` | Reset password | ✅ appName |
| POST | `/api/app/account/check-email-registered` | Check if email exists | ✅ appName |
| POST | `/api/app/account/logout` | Logout (audit + optional token revoke) | ✅ appName |

### 4.2 Request/Response DTOs

#### SendRegisterCodeDto
```csharp
public class SendRegisterCodeDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }
    
    [Required]
    public string AppName { get; set; }  // "GodGPT", "Lumen", etc.
    
    public int Platform { get; set; }    // 0: web, 1: ios, 2: android
    
    public string? RecaptchaToken { get; set; }
}
```

#### RegisterDto
```csharp
public class RegisterDto
{
    [Required]
    [EmailAddress]
    public string EmailAddress { get; set; }
    
    [Required]
    public string UserName { get; set; }
    
    [Required]
    public string Password { get; set; }
    
    [Required]
    public string Code { get; set; }  // Verification code
    
    [Required]
    public string AppName { get; set; }
}
```

#### LogoutResultDto
```csharp
public class LogoutResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; }
}
```

### 4.3 Multi-App Configuration

```json
// appsettings.json
{
  "Account": {
    "RegisterCodeDuration": 10,
    "MailSendingInterval": 1,
    "Apps": {
      "GodGPT": {
        "ResetPasswordUrl": "https://godgpt.ai/reset-password",
        "CNResetPasswordUrl": "https://godgpt.fun/reset-password",
        "EmailFromName": "GodGPT"
      },
      "Lumen": {
        "ResetPasswordUrl": "https://lumen.ai/reset-password",
        "CNResetPasswordUrl": "https://lumen.cn/reset-password",
        "EmailFromName": "Lumen"
      }
    }
  }
}
```

---

## 5. Data Model

### 5.1 Verification Code Storage (Redis Cache)

```
Key: "RegisterCode_{appName}_{email.ToLower()}"
Value: "123456" (6-digit code)
TTL: 10 minutes (configurable)
```

### 5.2 Email Rate Limiting (Redis Cache)

```
Key: "LastEmail_{appName}_{email.ToLower()}"
Value: email
TTL: 1 minute (configurable)
```

### 5.3 User Entity (ABP Identity)

Uses existing `Volo.Abp.Identity.IdentityUser` - no changes needed.

---

## 6. Implementation Plan

### Phase 1: Create Account Module in AuthServer

1. **Create directory structure**
   ```
   AuthServer/Account/
   AuthServer/Account/Dtos/
   AuthServer/Account/Templates/
   ```

2. **Create DTOs** (reuse existing from Application.Contracts where possible)
   - `SendRegisterCodeDto`
   - `RegisterDto`
   - `VerifyRegisterCodeDto`
   - `ResetPasswordDto`
   - `LogoutResultDto`

3. **Create AccountOptions**
   - Multi-app configuration support

4. **Create AccountEmailer**
   - Template-based email sending
   - Multi-language support

5. **Create AccountService**
   - Registration logic
   - Verification code management
   - Password reset logic

6. **Create AccountController**
   - All endpoints
   - Request validation
   - Localization support

### Phase 2: Configure Module

1. **Update AuthServerModule.cs**
   - Register AccountService
   - Register AccountEmailer
   - Configure AccountOptions
   - Register email templates

2. **Add email templates**
   - Copy from old project
   - Update virtual file paths

### Phase 3: Migration & Cleanup

1. **Remove AccountController from HttpApi**
   - Or convert to proxy (optional)

2. **Update frontend API prefix**
   - GodGPT: point account APIs to AuthServer
   - Lumen: same

3. **Test all endpoints**

---

## 7. Configuration

### 7.1 AuthServer appsettings.json additions

```json
{
  "Account": {
    "RegisterCodeDuration": 10,
    "MailSendingInterval": 1,
    "TokenLifespan": 1440,
    "Apps": {
      "GodGPT": {
        "ResetPasswordUrl": "https://godgpt.ai/reset-password",
        "CNResetPasswordUrl": "https://godgpt.fun/reset-password"
      },
      "Lumen": {
        "ResetPasswordUrl": "https://lumen.ai/reset-password",
        "CNResetPasswordUrl": "https://lumen.cn/reset-password"
      }
    }
  }
}
```

### 7.2 Email Configuration

Reuse existing SMTP configuration from ABP's email module.

---

## 8. Security Considerations

### 8.1 Rate Limiting
- Email sending: 1 per minute per email
- Registration code: 10 minute expiry
- reCAPTCHA validation for web platform

### 8.2 Verification Code
- 6-digit numeric code
- Case-insensitive email matching
- Stored in Redis with TTL

### 8.3 Password Reset
- Token generated via ASP.NET Identity
- URL-encoded in email link
- Single-use token

### 8.4 Logout
- Server-side audit logging
- Optional refresh token revocation (future enhancement)

---

## 9. Testing Strategy

### 9.1 Unit Tests
- AccountService methods
- AccountEmailer template rendering
- DTO validation

### 9.2 Integration Tests
- Full registration flow
- Password reset flow
- Multi-app configuration

### 9.3 Manual Tests
- Email delivery
- Template rendering in all languages
- Cross-app isolation

---

## 10. Migration Checklist

- [ ] Create Account module directory structure
- [ ] Create DTOs (or reference existing)
- [ ] Create AccountOptions with multi-app support
- [ ] Create AccountEmailer
- [ ] Create AccountService
- [ ] Create AccountController
- [ ] Copy email templates
- [ ] Update AuthServerModule configuration
- [ ] Add appsettings.json configuration
- [ ] Remove/disable HttpApi AccountController
- [ ] Test all endpoints
- [ ] Update frontend API prefix (if needed)
- [ ] Update documentation

---

## 11. Open Questions

| Question | Decision |
|----------|----------|
| Should HttpApi AccountController be removed or converted to proxy? | **Remove** - AuthServer is the single source of truth |
| Should existing DTOs from Application.Contracts be reused? | **Yes** - Copy needed DTOs, avoid dependency on HttpApi's Application layer |
| How to handle frontend transition? | **Gradual** - Frontend already has authAPIPrefix, just update endpoints |

---

## 12. Appendix

### A. Files to Migrate from old/godgpt-api

| Source | Target |
|--------|--------|
| `old/godgpt-api/.../Account/AccountService.cs` | `AuthServer/Account/AccountService.cs` |
| `old/godgpt-api/.../Account/AccountOptions.cs` | `AuthServer/Account/AccountOptions.cs` |
| `old/godgpt-api/.../Account/AevatarAccountEmailer.cs` | `AuthServer/Account/AccountEmailer.cs` |
| `old/godgpt-api/.../Account/Templates/*.cs` | `AuthServer/Account/Templates/*.cs` |
| `old/godgpt-api/.../Account/Templates/*.tpl` | `AuthServer/Account/Templates/*.tpl` |

### B. Existing DTOs in Application.Contracts

These can be referenced or copied:
- `SendRegisterCodeDto`
- `SendRegisterCodeResponseDto`
- `VerifyRegisterCodeDto`
- `AevatarRegisterDto`
- `GodGptRegisterDto`
- `CheckEmailRegisteredDto`
- `LogoutResultDto`

---

*Document Version: 1.0*
*Created: 2026-01-06*
*Author: HyperEcho*

