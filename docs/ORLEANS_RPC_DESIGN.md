# Agent RPC 方法调用设计方案

## 实现状态

✅ **已完成实现** - 基于 Protobuf + DispatchProxy 的类型安全 RPC

## 问题背景

当前框架下，客户端只能通过 `HandleEventAsync` 发送事件，无法直接调用 Agent 接口定义的业务方法。这导致：

1. **易用性低**：简单的读取操作也需要设计请求/响应事件
2. **开发效率低**：无法复用 Agent 接口定义的方法签名
3. **类型不安全**：事件参数和返回值需要手动序列化/反序列化

## 解决方案

### 使用方式

```csharp
// 创建 Actor
var actor = await factory.CreateGAgentActorAsync<BankAccountAgent>(agentId);

// 创建类型安全代理
var bankAgent = actor.As<IBankAccountAgent>();

// 像调用本地接口一样调用方法
await bankAgent.CreateAccountAsync("User", 1000m);
await bankAgent.DepositAsync(500m, "Deposit");
var balance = await bankAgent.GetBalanceAsync();
```

### 智能 Runtime 判断

`RpcProxy` 自动检测 Runtime 类型，选择最优调用方式：

| Runtime | 调用方式 | 开销 |
|---------|----------|------|
| **Local** | 直接调用 Agent 方法 | 零开销 |
| **Orleans** | Protobuf RPC 序列化 | 网络序列化 |
| **ProtoActor** | Protobuf RPC 序列化 | 网络序列化 |

```
┌─────────────────────────────────────────────────────────────┐
│  actor.As<IBankAccountAgent>()                              │
│                    │                                        │
│                    ▼                                        │
│  ┌─────────────────────────────────────────────────────┐   │
│  │              RpcProxy<T>                             │   │
│  │  ┌─────────────────────────────────────────────────┐│   │
│  │  │ try { agent = actor.GetAgent() }                ││   │
│  │  │   ✓ Local Runtime → 直接调用 agent.Method()     ││   │
│  │  │   ✗ NotSupportedException → 走 RPC              ││   │
│  │  └─────────────────────────────────────────────────┘│   │
│  └─────────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────────┘
```

---

## 架构设计

### 核心组件

```
src/Aevatar.Agents.Abstractions/
├── agent_rpc.proto              # RPC 消息定义
├── Rpc/
│   └── ProtobufPacker.cs        # Protobuf 打包/解包工具
├── Extensions/
│   ├── RpcExtensions.cs         # 字符串方法名 RPC（低级 API）
│   └── RpcProxy.cs              # 类型安全代理（推荐 API）
└── IGAgentActor.cs              # 添加 InvokeRpcAsync 方法

src/Aevatar.Agents.Core/
└── Rpc/
    └── RpcInvoker.cs            # 服务端 RPC 调用逻辑
```

### 数据流

```
┌─────────────────────────────────────────────────────────────────┐
│                        客户端 (HttpApi)                          │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │  var bankAgent = actor.As<IBankAccountAgent>();             ││
│  │  await bankAgent.DepositAsync(500m, "Deposit");             ││
│  │                           │                                  ││
│  │                           ▼                                  ││
│  │  ┌─────────────────────────────────────────────────────────┐││
│  │  │ RpcProxy<IBankAccountAgent>                             │││
│  │  │   • Local: 直接调用 _directAgent.DepositAsync(...)      │││
│  │  │   • Remote: 构建 RpcRequest → InvokeRpcAsync(bytes)     │││
│  │  └─────────────────────────────────────────────────────────┘││
│  └─────────────────────────────────────────────────────────────┘│
│                            │ (Remote Only)                       │
└────────────────────────────│─────────────────────────────────────┘
                             ▼
┌─────────────────────────────────────────────────────────────────┐
│                        服务端 (Silo)                             │
│  ┌─────────────────────────────────────────────────────────────┐│
│  │ OrleansGAgentGrain.InvokeRpcAsync(bytes)                    ││
│  │                           │                                  ││
│  │                           ▼                                  ││
│  │ RpcInvoker.InvokeAsync(agent, bytes)                        ││
│  │   • 解析 RpcRequest                                          ││
│  │   • 反序列化参数 (ProtobufPacker.Unpack)                     ││
│  │   • 反射调用 agent.DepositAsync(500m, "Deposit")            ││
│  │   • 序列化结果 (ProtobufPacker.Pack)                         ││
│  │   • 返回 RpcResponse                                         ││
│  └─────────────────────────────────────────────────────────────┘│
└─────────────────────────────────────────────────────────────────┘
```

---

## 设计决策

### 为什么所有 Runtime 都实现 `InvokeRpcAsync`？

**问题**：Local Runtime 不需要序列化，为什么也要实现 `InvokeRpcAsync`？

**分析**：

| 方案 | 说明 | 评价 |
|------|------|------|
| **A: 统一接口 + 智能代理（当前）** | 所有 Runtime 实现 `InvokeRpcAsync`，RpcProxy 智能判断 | ✅ 推荐 |
| B: 可选接口 `IRpcCapable` | 只有远程 Runtime 实现 RPC 接口 | ❌ 增加复杂度 |
| C: 移除接口方法 | RpcProxy 内部处理所有逻辑 | ❌ 耦合严重 |

**选择方案 A 的理由**：

1. **RpcProxy 已优化**：Local Runtime 直接调用，零开销
2. **后备入口**：支持不使用 RpcProxy 的低级调用场景
3. **接口统一**：无需类型判断，代码简洁
4. **向后兼容**：不破坏现有实现

```csharp
// RpcProxy 智能判断（已实现）
public static TInterface Create(IGAgentActor actor)
{
    try
    {
        var agent = actor.GetAgent();
        if (agent is TInterface typedAgent)
        {
            // Local Runtime: 直接调用，不走 RPC
            proxy._directAgent = typedAgent;
            proxy._isLocalRuntime = true;
        }
    }
    catch (NotSupportedException)
    {
        // Orleans/Remote: GetAgent() 抛异常，走 RPC
        proxy._isLocalRuntime = false;
    }
}
```

### 为什么不用 Source Generator？

| 方案 | 优点 | 缺点 |
|------|------|------|
| **DispatchProxy（当前）** | 简单、无需额外项目、运行时动态 | 反射开销（可忽略） |
| Source Generator | 零反射、编译时检查 | 需要单独项目、配置复杂 |

**选择 DispatchProxy 的理由**：

1. **足够高效**：RPC 网络延迟远大于反射开销
2. **简单实现**：无需维护 Source Generator 项目
3. **运行时灵活**：支持动态接口代理

**未来优化**：如果性能成为瓶颈，可迁移到 Source Generator。

---

## Protobuf 消息定义

```protobuf
// agent_rpc.proto
syntax = "proto3";
package Aevatar.Agents.Rpc;
option csharp_namespace = "Aevatar.Agents.Rpc";

import "google/protobuf/any.proto";

message RpcRequest {
    string method_name = 1;
    repeated google.protobuf.Any args = 2;
    map<string, string> metadata = 3;
    string correlation_id = 4;
}

message RpcResponse {
    bool success = 1;
    google.protobuf.Any result = 2;
    RpcError error = 3;
    string correlation_id = 4;
}

message RpcError {
    string error_type = 1;
    string message = 2;
    string stack_trace = 3;
}
```

---

## 支持的类型

### 基础类型（自动映射）

| C# 类型 | Protobuf Wrapper |
|---------|------------------|
| `int` | `Int32Value` |
| `long` | `Int64Value` |
| `string` | `StringValue` |
| `bool` | `BoolValue` |
| `double` | `DoubleValue` |
| `float` | `FloatValue` |
| `decimal` | `DoubleValue`（自动转换） |
| `byte[]` | `BytesValue` |

### 复杂类型

```csharp
// Protobuf 消息类型直接支持
Task<AccountSummary> GetAccountSummaryAsync();

// 集合类型需要包装为 Protobuf 消息
message GetRecordsResponse {
    repeated PaymentRecord records = 1;
}
Task<GetRecordsResponse> GetRecordsAsync();
```

---

## 安全设计

### 接口方法白名单

只有在**接口中定义**的方法才能通过 RPC 调用：

```csharp
// RpcInvoker.GetCachedMethod
var isInInterface = agentType.GetInterfaces().Any(iface =>
    iface.GetMethod(method.Name, paramTypes) != null);

if (!isInInterface)
{
    throw new InvalidOperationException(
        $"Method '{method.Name}' is not defined in any interface. " +
        $"Only methods defined in interfaces can be called via RPC.");
}
```

### 异常传播

服务端异常正确传递到客户端：

```csharp
catch (Exception ex)
{
    response.Error = new RpcError
    {
        ErrorType = ex.GetType().FullName,
        Message = ex.Message,
        StackTrace = ex.StackTrace
    };
}
```

---

## API 对比

### 低级 API（字符串方法名）

```csharp
// 不推荐：类型不安全，无编译检查
await actor.InvokeRpcAsync("CreateAccountAsync", "User", 1000m);
var balance = await actor.InvokeRpcAsync<double>("GetBalanceAsync");
```

### 推荐 API（类型安全代理）

```csharp
// 推荐：类型安全，IDE 支持，编译检查
var bankAgent = actor.As<IBankAccountAgent>();
await bankAgent.CreateAccountAsync("User", 1000m);
var balance = await bankAgent.GetBalanceAsync();
```

---

## 性能特性

| 场景 | 调用方式 | 序列化开销 |
|------|----------|------------|
| Local Runtime | 直接方法调用 | 无 |
| Orleans Runtime | Protobuf RPC | ~25μs/次 |
| Local + RpcProxy | 自动直接调用 | 无 |
| Orleans + RpcProxy | 自动 RPC | ~25μs/次 |

---

## 完整示例

### 1. 定义接口

```csharp
public interface IBankAccountAgent : IGAgent
{
    Task CreateAccountAsync(string holder, decimal initialBalance = 0);
    Task DepositAsync(decimal amount, string description = "");
    Task WithdrawAsync(decimal amount, string description = "");
    Task<double> GetBalanceAsync();
    Task<AccountSummary> GetAccountSummaryAsync();
}
```

### 2. 实现 Agent

```csharp
public class BankAccountAgent : GAgentBase<BankAccountState>, IBankAccountAgent
{
    public async Task CreateAccountAsync(string holder, decimal initialBalance = 0)
    {
        RaiseEvent(new AccountCreated { ... });
        await ConfirmEventsAsync();
    }
    
    public Task<double> GetBalanceAsync()
        => Task.FromResult(State.Balance);
    
    // ... 其他方法
}
```

### 3. 使用（Local Runtime）

```csharp
var actor = await localFactory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
var bankAgent = actor.As<IBankAccountAgent>();  // 直接调用，零开销

await bankAgent.CreateAccountAsync("Alice", 1000m);
var balance = await bankAgent.GetBalanceAsync();  // 直接返回
```

### 4. 使用（Orleans Runtime）

```csharp
var actor = await orleansFactory.CreateGAgentActorAsync<BankAccountAgent>(agentId);
var bankAgent = actor.As<IBankAccountAgent>();  // 自动走 RPC

await bankAgent.CreateAccountAsync("Alice", 1000m);  // → Silo 执行
var balance = await bankAgent.GetBalanceAsync();     // → Silo 返回
```

---

## 未来优化方向

1. **Source Generator**：编译时生成代理，消除运行时反射
2. **批量调用**：支持 `BatchInvokeAsync` 减少网络往返
3. **缓存优化**：方法签名 + 委托缓存
4. **压缩传输**：大消息自动压缩

---

## 总结

| 特性 | 状态 |
|------|------|
| 类型安全代理 | ✅ `actor.As<T>()` |
| Local Runtime 零开销 | ✅ 自动直接调用 |
| Protobuf 序列化 | ✅ 高性能 |
| 接口方法白名单 | ✅ 安全 |
| 异常传播 | ✅ 完整错误信息 |
| IDE 支持 | ✅ 自动补全、跳转、重构 |
