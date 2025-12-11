# Aevatar Test Scripts

测试脚本用于本地开发和端到端测试。

## 前置条件

1. **MongoDB** - 需要在本地运行 MongoDB (端口 27017)
   ```bash
   # 使用 Docker
   docker run -d -p 27017:27017 --name mongodb mongo:latest
   
   # 或使用 Homebrew (macOS)
   brew services start mongodb-community
   ```

2. **依赖工具**
   ```bash
   # 安装 jq (用于解析 JSON)
   brew install jq
   ```

3. **Stripe 配置** - 确保 `appsettings.Development.json` 中已配置 Stripe

## 脚本说明

### 1. 启动服务 (`start-services.sh`)

启动所有必要的服务：Silo (Orleans), AuthServer, HttpApi

```bash
./scripts/start-services.sh
```

服务启动后：
- **Silo**: 运行在端口 11111 (Gateway: 30000)
- **AuthServer**: https://localhost:44320
- **HttpApi**: https://localhost:44345

日志文件保存在 `scripts/.pids/` 目录下。

### 2. 停止服务 (`stop-services.sh`)

停止所有运行中的服务：

```bash
./scripts/stop-services.sh

# 清理日志文件
./scripts/stop-services.sh --clean
```

### 3. 测试支付流程 (`test-payment-flow.sh`)

测试 Stripe 支付流程的各个环节：

```bash
# 运行所有测试
./scripts/test-payment-flow.sh

# 运行特定测试
./scripts/test-payment-flow.sh products    # 测试获取产品
./scripts/test-payment-flow.sh customer    # 测试获取客户信息
./scripts/test-payment-flow.sh checkout    # 测试创建 Checkout Session
./scripts/test-payment-flow.sh status      # 测试订阅状态
./scripts/test-payment-flow.sh history     # 测试支付历史
./scripts/test-payment-flow.sh new-api     # 测试新的 Payment API
```

## 测试流程

### 完整的端到端测试

```bash
# 1. 启动服务
./scripts/start-services.sh

# 2. 等待服务就绪（约 30 秒）
sleep 30

# 3. 运行测试
./scripts/test-payment-flow.sh

# 4. 停止服务
./scripts/stop-services.sh
```

### 使用 Stripe CLI 测试 Webhook

```bash
# 1. 安装 Stripe CLI
brew install stripe/stripe-cli/stripe

# 2. 登录 Stripe
stripe login

# 3. 转发 webhook 到本地
stripe listen --forward-to https://localhost:44345/api/payment/webhook/stripe

# 4. 触发测试事件
stripe trigger checkout.session.completed
stripe trigger invoice.paid
stripe trigger customer.subscription.updated
```

## API 端点

### 旧 API (兼容层) - `/api/godgpt/payment`

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/products` | 获取 Stripe 产品列表 |
| GET | `/iap-products` | 获取 Apple IAP 产品列表 |
| POST | `/customer` | 获取/创建 Stripe 客户 |
| POST | `/create-checkout-session` | 创建 Checkout Session |
| POST | `/create-subscription` | 创建订阅 (App 端) |
| GET | `/list` | 获取支付历史 |
| GET | `/has-active-subscription` | 获取订阅状态 |
| POST | `/cancel-subscription` | 取消订阅 |
| POST | `/verify-receipt` | 验证 Apple 收据 |
| POST | `/google-play/verify-transaction` | 验证 Google Play 交易 |

### 新 API - `/api/payment`

| 方法 | 端点 | 说明 |
|------|------|------|
| GET | `/products/{platform}` | 获取产品列表 |
| POST | `/subscribe` | 创建订阅 |
| POST | `/verify` | 验证交易 |
| GET | `/subscription-status` | 获取订阅状态 |
| GET | `/history` | 获取支付历史 |
| POST | `/cancel` | 取消订阅 |

### Webhook 端点

| 方法 | 端点 | 说明 |
|------|------|------|
| POST | `/api/payment/webhook/stripe` | Stripe Webhook |
| POST | `/api/payment/webhook/apple` | Apple App Store Webhook |
| POST | `/api/payment/webhook/google` | Google Play Webhook |

## 故障排除

### 服务启动失败

1. 检查 MongoDB 是否运行：
   ```bash
   mongosh --eval "db.adminCommand('ping')"
   ```

2. 检查端口是否被占用：
   ```bash
   lsof -i :44320  # AuthServer
   lsof -i :44345  # HttpApi
   lsof -i :11111  # Silo
   ```

3. 查看日志：
   ```bash
   tail -f scripts/.pids/silo.log
   tail -f scripts/.pids/auth.log
   tail -f scripts/.pids/httpapi.log
   ```

### Token 获取失败

1. 确保 AuthServer 正在运行
2. 检查 OpenIddict 配置
3. 尝试使用测试用户登录：
   ```bash
   curl -k -X POST "https://localhost:44320/connect/token" \
     -d "grant_type=password&client_id=BusinessServer_App&client_secret=1q2w3e*&username=admin&password=1q2w3E*&scope=BusinessServer openid profile"
   ```

### Stripe 配置问题

确保 `appsettings.Development.json` 包含：

```json
{
  "Stripe": {
    "SecretKey": "sk_test_xxx",
    "PublishableKey": "pk_test_xxx",
    "WebhookSecret": "whsec_xxx",
    "Products": [
      {
        "PriceId": "price_xxx",
        "ProductId": "prod_xxx",
        "Name": "Monthly Plan",
        "Amount": 999,
        "Currency": "USD",
        "PlanType": 1,
        "BillingCycle": 1,
        "IsUltimate": false,
        "DailyAvgPrice": 33,
        "Mode": "subscription"
      }
    ]
  }
}
```

