# Frontend API Migration Guide

## Overview

账户管理 API 已从 `HttpApi` 迁移到 `AuthServer`，主要变更：

1. **URI 前缀变更**：`/api/account` → `/api/app/account`
2. **新增 `appName` 字段**：用于区分不同应用（GodGPT / Lumen）
3. **请求方式不变**：全部为 `POST`，`Content-Type: application/json`

---

## API 对照表

| 功能 | 旧路径 | 新路径 | `appName` |
|------|--------|--------|-----------|
| 发送注册验证码 | `POST /api/account/send-register-code` | `POST /api/app/account/send-register-code` | **必填** |
| 验证注册码 | `POST /api/account/verify-register-code` | `POST /api/app/account/verify-register-code` | 可选 |
| 注册 | `POST /api/account/register` | `POST /api/app/account/register` | **必填** |
| 检查邮箱是否已注册 | `POST /api/account/check-email-registered` | `POST /api/app/account/check-email-registered` | 可选 |
| 发送密码重置邮件 | `POST /api/account/send-password-reset-code` | `POST /api/app/account/send-password-reset-code` | **必填** |
| 验证重置 Token | `POST /api/account/verify-password-reset-token` | `POST /api/app/account/verify-password-reset-token` | 无 |
| 重置密码 | `POST /api/account/reset-password` | `POST /api/app/account/reset-password` | 无 |
| 登出 | `POST /api/account/logout` | `POST /api/app/account/logout` | 无 |

---

## 详细请求/响应格式

### 1. 发送注册验证码

```
POST /api/app/account/send-register-code
```

**Request:**
```json
{
  "email": "user@example.com",
  "appName": "GodGPT",        // 必填: "GodGPT" | "Lumen"
  "platform": 0,              // 0=web, 1=iOS, 2=Android
  "recaptchaToken": "..."     // 可选: web端reCAPTCHA验证
}
```

**Response:**
```json
{
  "success": true,
  "message": "Verification code sent successfully"
}
```

---

### 2. 验证注册码

```
POST /api/app/account/verify-register-code
```

**Request:**
```json
{
  "email": "user@example.com",
  "code": "123456",
  "appName": "GodGPT"         // 可选
}
```

**Response:**
```json
true   // 或 false
```

---

### 3. 注册

```
POST /api/app/account/register
```

**Request:**
```json
{
  "emailAddress": "user@example.com",
  "userName": "username",     // 可选，不填则使用邮箱前缀
  "password": "Password123!",
  "code": "123456",           // 验证码
  "appName": "GodGPT"         // 必填
}
```

**Response:** `IdentityUserDto`
```json
{
  "id": "guid",
  "userName": "username",
  "email": "user@example.com",
  "emailConfirmed": true,
  ...
}
```

---

### 4. 检查邮箱是否已注册

```
POST /api/app/account/check-email-registered
```

**Request:**
```json
{
  "emailAddress": "user@example.com",
  "appName": "GodGPT"         // 可选
}
```

**Response:**
```json
true   // 已注册
false  // 未注册
```

---

### 5. 发送密码重置邮件

```
POST /api/app/account/send-password-reset-code
```

**Request:**
```json
{
  "email": "user@example.com",
  "appName": "GodGPT"         // 必填: 决定重置链接指向哪个站点
}
```

**Response:** 无内容 (204) 或异常

---

### 6. 验证重置 Token

```
POST /api/app/account/verify-password-reset-token
```

**Request:**
```json
{
  "userId": "00000000-0000-0000-0000-000000000000",
  "resetToken": "CfDJ8..."
}
```

**Response:**
```json
true   // Token有效
false  // Token无效或过期
```

---

### 7. 重置密码

```
POST /api/app/account/reset-password
```

**Request:**
```json
{
  "userId": "00000000-0000-0000-0000-000000000000",
  "resetToken": "CfDJ8...",
  "password": "NewPassword123!"
}
```

**Response:** 无内容 (204) 或异常

---

### 8. 登出

```
POST /api/app/account/logout
```

**Request:** 无需请求体

**Response:**
```json
{
  "success": true,
  "message": "Logged out successfully"
}
```

---

## 多语言支持

通过 HTTP Header 指定语言：

```
GodGPTLanguage: en       // 英文 (默认)
GodGPTLanguage: zh-cn    // 简体中文
GodGPTLanguage: zh-tw    // 繁体中文
GodGPTLanguage: es       // 西班牙语
```

影响：
- 验证码邮件内容语言
- 密码重置邮件内容语言
- 错误消息语言

---

## 不变的 API

以下 API 保持不变：

| API | 路径 | 说明 |
|-----|------|------|
| OAuth Token | `POST /connect/token` | 密码登录、Google/Apple登录、Refresh |
| Lumen 业务 | `/api/lumen/*` | 保持在 HttpApi |
| GodGPT 业务 | `/api/godgpt/*` | 保持在 HttpApi |
| Google 集成 | `/api/google-auth/*` | 日历同步等 |

---

## 前端代码修改示例

### TypeScript 常量更新

```typescript
// 旧
const ACCOUNT_API_BASE = `${GPT_API_PREFIX}/api/account`;

// 新
const ACCOUNT_API_BASE = `${AUTH_API_PREFIX}/api/app/account`;
```

### 请求示例

```typescript
// 发送验证码
async function sendRegisterCode(email: string) {
  const response = await fetch(`${ACCOUNT_API_BASE}/send-register-code`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      'GodGPTLanguage': getCurrentLanguage(), // 'en' | 'zh-cn' | 'zh-tw' | 'es'
    },
    body: JSON.stringify({
      email,
      appName: 'GodGPT',  // 新增必填字段
      platform: 0,
    }),
  });
  return response.json();
}
```

---

## 迁移检查清单

- [ ] 更新 API 前缀：`/api/account` → `/api/app/account`
- [ ] 添加 `appName` 字段到以下请求：
  - [ ] `send-register-code` (必填)
  - [ ] `verify-register-code` (可选)
  - [ ] `register` (必填)
  - [ ] `check-email-registered` (可选)
  - [ ] `send-password-reset-code` (必填)
- [ ] 确认 `GodGPTLanguage` Header 正确传递
- [ ] 测试所有账户流程：注册、登录、重置密码、登出

---

*更新日期: 2026-01-06*

