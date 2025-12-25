# Test Common Script Usage Guide

## 概述

`test-common.sh` 是一个公共测试工具脚本，提供了所有测试脚本共享的功能：
- URL 自动检测（支持 HTTPS/HTTP 端口回退）
- 登录认证
- HTTP 请求辅助函数
- 日志输出函数

## 使用方法

### 1. 在测试脚本中引入公共脚本

```bash
#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load common test utilities
source "$SCRIPT_DIR/test-common.sh"

# 你的测试代码...
```

### 2. 配置（可选）

公共脚本提供了默认配置，可以通过环境变量覆盖：

```bash
# 在脚本中设置（在 source 之前）
export AUTH_URL="https://localhost:44320"
export API_URL="https://localhost:44345"
export CLIENT_ID="AevatarAuthServer"
export SCOPE="Aevatar openid profile"
export TEST_USERNAME="admin"
export TEST_PASSWORD="1q2w3E*"

source "$SCRIPT_DIR/test-common.sh"
```

### 3. URL 自动检测

公共脚本会自动检测可用的服务 URL：

```bash
# 自动检测（默认行为）
source "$SCRIPT_DIR/test-common.sh"
# 会尝试：
# - API: https://localhost:44345 → http://localhost:8082
# - Auth: https://localhost:44320 → http://localhost:8001
```

### 4. 登录

```bash
# 登录获取 access token
if ! login; then
    log_error "Login failed, aborting tests"
    exit 1
fi

# ACCESS_TOKEN 变量会自动设置
```

### 5. 使用 HTTP 请求辅助函数

```bash
# GET 请求
response=$(api_get "/api/godgpt/account")

# POST 请求
response=$(api_post "/api/godgpt/endpoint" '{"key": "value"}')

# PUT 请求
response=$(api_put "/api/godgpt/endpoint" '{"key": "value"}')

# DELETE 请求
response=$(api_delete "/api/godgpt/endpoint")

# Guest POST（无需认证）
response=$(api_post_guest "/api/godgpt/guest/endpoint" '{"key": "value"}')

# Guest GET（无需认证）
response=$(api_get_guest "/api/godgpt/public/endpoint")
```

### 6. 日志函数

```bash
log_info "Information message"
log_step "Step message"
log_warn "Warning message"
log_error "Error message"
log_response "$response"  # 格式化输出 JSON
```

## 完整示例

```bash
#!/bin/bash

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/test-common.sh"

test_my_endpoint() {
    log_step "Testing my endpoint..."
    
    local response=$(api_get "/api/godgpt/my-endpoint")
    log_response "$response"
    
    if echo "$response" | jq -e '.success' > /dev/null 2>&1; then
        log_info "Test passed ✓"
        return 0
    else
        log_error "Test failed"
        return 1
    fi
}

main() {
    log_info "========================================"
    log_info "My Test Script"
    log_info "========================================"
    echo ""
    
    if ! login; then
        log_error "Login failed"
        exit 1
    fi
    echo ""
    
    if test_my_endpoint; then
        log_info "All tests passed ✓"
        exit 0
    else
        log_error "Tests failed"
        exit 1
    fi
}

main "$@"
```

## 迁移现有脚本

### 步骤 1: 添加 source 语句

```bash
# 在脚本开头添加
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/test-common.sh"
```

### 步骤 2: 删除重复的函数

删除以下函数定义（公共脚本已提供）：
- `log_info()`, `log_step()`, `log_warn()`, `log_error()`, `log_response()`
- `login()`
- URL 检测相关函数

### 步骤 3: 替换 curl 调用

```bash
# 旧代码
curl -k -s -X GET "$API_URL/api/godgpt/account" \
    -H "Authorization: Bearer $ACCESS_TOKEN" \
    -H "Content-Type: application/json"

# 新代码
api_get "/api/godgpt/account"
```

### 步骤 4: 更新登录调用

```bash
# 旧代码
login
if [ -z "$ACCESS_TOKEN" ]; then
    exit 1
fi

# 新代码
if ! login; then
    log_error "Login failed"
    exit 1
fi
```

## 可用函数列表

### 日志函数
- `log_info(message)` - 信息日志（绿色）
- `log_step(message)` - 步骤日志（蓝色）
- `log_warn(message)` - 警告日志（黄色）
- `log_error(message)` - 错误日志（红色）
- `log_response(response)` - 格式化输出响应

### URL 检测
- `detect_urls()` - 自动检测 API 和 Auth URL
- `probe_url(url, path)` - 探测 URL 是否可达
- `check_services()` - 检查服务是否运行

### 认证
- `login()` - 登录并获取 access token

### HTTP 请求
- `api_get(endpoint, [extra_headers])` - GET 请求（已认证）
- `api_post(endpoint, data, [extra_headers])` - POST 请求（已认证）
- `api_put(endpoint, data, [extra_headers])` - PUT 请求（已认证）
- `api_delete(endpoint, [extra_headers])` - DELETE 请求（已认证）
- `api_post_guest(endpoint, data, [extra_headers])` - POST 请求（未认证）
- `api_get_guest(endpoint, [extra_headers])` - GET 请求（未认证）

## 环境变量

| 变量 | 默认值 | 说明 |
|------|--------|------|
| `AUTH_URL` | `https://localhost:44320` | AuthServer URL |
| `API_URL` | `https://localhost:44345` | HttpApi URL |
| `CLIENT_ID` | `AevatarAuthServer` | OAuth Client ID |
| `SCOPE` | `Aevatar openid profile` | OAuth Scope |
| `TEST_USERNAME` | `admin` | 测试用户名 |
| `TEST_PASSWORD` | `1q2w3E*` | 测试密码 |

## 注意事项

1. **URL 自动检测**：如果服务运行在 HTTP 端口，脚本会自动检测并回退
2. **ACCESS_TOKEN**：登录后会自动设置全局变量 `ACCESS_TOKEN`
3. **错误处理**：`login()` 返回 0 表示成功，非 0 表示失败
4. **JSON 解析**：响应会自动尝试用 `jq` 格式化，如果没有 `jq` 则原样输出

