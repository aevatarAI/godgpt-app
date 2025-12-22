# Agent ID 使用指南

## 概述

Aevatar Agent Framework 使用 **字符串 ID** 来标识 Agent。系统自动将用户传入的简单 ID 转换为包含类型信息的完整格式，确保跨类型唯一性。

## ID 格式

| 场景 | 格式 | 示例 |
|------|------|------|
| 用户传入 | 简单 Guid 字符串 | `"abc-123-def"` |
| 系统存储 (actor.Id) | `AgentType:Guid` | `"CalculatorAgent:abc-123-def"` |

## 统一规则

**所有 Manager 操作都使用 `actor.Id`**（完整格式）：

```
┌─────────────────────────────────────────────────────────────────┐
│  创建时：传入简单 Guid                                           │
│  后续操作：使用 actor.Id（完整格式）                              │
└─────────────────────────────────────────────────────────────────┘
```

## 完整使用示例

```csharp
// ====== 1. 创建 Agent（传入简单 Guid）======
var rawId = Guid.NewGuid().ToString();  // "abc-123"
var actor = await manager.CreateAndRegisterAsync<CalculatorAgent>(rawId);
// actor.Id = "CalculatorAgent:abc-123" (自动组装)

// ====== 2. 后续操作（使用 actor.Id）======

// 查找
var found = await manager.GetActorAsync(actor.Id);  // ✅

// 层级关系
await manager.LinkParentChildAsync(parentActor.Id, childActor.Id);  // ✅

// 点对点通信
await sender.SendToAsync(receiver.Id, message);  // ✅

// 停用
await manager.DeactivateAndUnregisterAsync(actor.Id);  // ✅

// 检查存在
await manager.ExistsAsync(actor.Id);  // ✅
```

## API 速查表

| API | 输入 | 备注 |
|-----|------|------|
| `CreateAndRegisterAsync<T>(id)` | 简单 Guid | 返回的 `actor.Id` 是完整格式 |
| `GetActorAsync(id)` | `actor.Id` | 使用完整格式查找 |
| `LinkParentChildAsync(p, c)` | `actor.Id` | 双方都用完整格式 |
| `SendToAsync(targetId, evt)` | `actor.Id` | 目标 Agent 的完整 ID |
| `DeactivateAndUnregisterAsync(id)` | `actor.Id` | 使用完整格式 |
| `ExistsAsync(id)` | `actor.Id` | 使用完整格式 |

## 类型安全

相同 Guid + 不同类型 = 不同 Agent（不冲突）：

```csharp
var sameGuid = "xyz-789";

var calc = await manager.CreateAndRegisterAsync<CalculatorAgent>(sameGuid);
// calc.Id = "CalculatorAgent:xyz-789"

var weather = await manager.CreateAndRegisterAsync<WeatherAgent>(sameGuid);
// weather.Id = "WeatherAgent:xyz-789"

// 两个完全不同的 Agent！不会互相覆盖。
// Manager 内部分别存储为：
// - _actors["CalculatorAgent:xyz-789"] = calc
// - _actors["WeatherAgent:xyz-789"] = weather
```

## 推荐模式

```csharp
public class AgentService
{
    private readonly IGAgentActorManager _manager;
    
    // 保存 actor.Id（不是原始 Guid）
    private string? _calculatorId;
    
    public async Task<string> CreateCalculatorAsync()
    {
        var rawId = Guid.NewGuid().ToString();
        var actor = await _manager.CreateAndRegisterAsync<CalculatorAgent>(rawId);
        
        // 保存完整的 actor.Id
        _calculatorId = actor.Id;
        
        return actor.Id;
    }
    
    public async Task<IGAgentActor?> GetCalculatorAsync()
    {
        if (_calculatorId == null) return null;
        return await _manager.GetActorAsync(_calculatorId);  // 用 actor.Id
    }
}
```

## 常见问题

### Q: 为什么需要完整格式？

A: 确保类型安全。如果只用简单 Guid，相同 Guid 不同类型的 Agent 会互相覆盖。

### Q: actor.Id 的格式是什么？

A: `"AgentTypeName:RawGuid"`，例如 `"CalculatorAgent:abc-123-def"`

### Q: 如何从 actor.Id 提取原始 Guid？

A: 

```csharp
var actorId = "CalculatorAgent:abc-123";
var colonIndex = actorId.IndexOf(':');
var rawGuid = actorId[(colonIndex + 1)..];  // "abc-123"
```

### Q: 存储到数据库应该存什么？

A: 存储 `actor.Id`（完整格式），这样可以唯一定位 Agent：

```csharp
db.Save(new Record { AgentId = actor.Id });  // "CalculatorAgent:abc-123"
```

## 内部原理

```
用户传入 "abc-123" + 泛型 <CalculatorAgent>
        ↓
┌─────────────────────────────────────┐
│       Factory 创建 Actor            │
│  组装 GrainKey: "CalculatorAgent:abc-123"
│  actor.Id = GrainKey                │
└─────────────────────────────────────┘
        ↓
┌─────────────────────────────────────┐
│       Manager 存储                   │
│  _actors["CalculatorAgent:abc-123"] = actor
└─────────────────────────────────────┘
        ↓
┌─────────────────────────────────────┐
│       Orleans Grain                  │
│  GetPrimaryKeyString()               │
│  = "CalculatorAgent:abc-123"        │
└─────────────────────────────────────┘
```
