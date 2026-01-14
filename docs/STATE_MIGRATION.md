# State Migration 完整文档

## 概述

State Migration 是一个 ABP BackgroundJob，用于从旧系统迁移 Agent State 数据到新系统。支持通过 API 手动触发或配置为定时任务。

## 核心特性

✅ **通过 API 获取数据**：使用旧系统的 Export API，无需 Orleans 反序列化  
✅ **版本统一**：使用 net10.0，无兼容性问题  
✅ **复用基础设施**：使用现有的 MongoDB、日志、配置  
✅ **支持异步执行**：大数据集使用 Hangfire 异步处理  
✅ **支持增量迁移**：可以指定特定集合类型  
✅ **统一集合命名**：使用 `agent_states_{StateTypeName}` 格式，与 `MongoDBStateStore` 一致  
✅ **统一文档结构**：使用 `AgentStateDocument` 格式，确保 Agent 能正常加载

## 架构设计

### 数据流程

```
旧系统 API (JSON)
    ↓
StateConverter (JSON → Protobuf)
    ↓
MongoDB (agent_states_{StateTypeName})
    ↓
MongoDBStateStore (Agent 运行时读取)
```

### 集合命名规则

- **旧系统**：`Stream{AgentTypeName}` (例如：`StreamUserStatisticsGAgent`)
- **新系统**：`agent_states_{StateTypeName}` (例如：`agent_states_UserStatisticsState`)

### 文档结构

**AgentStateDocument 格式**：
```json
{
  "AgentId": "UserStatisticsGAgent:guid",
  "StateData": <Protobuf binary>,
  "StateType": "UserStatisticsState",
  "Version": 1,
  "UpdatedAt": "2026-01-14T12:00:00Z"
}
```

### Agent ID 转换

- **旧格式**：`Aevatar.Application.Grains.UserStatistics.UserStatisticsGAgent/2e08d2bc...`
- **新格式**：`UserStatisticsGAgent:2e08d2bc...`

转换逻辑：
```csharp
// 提取短类型名
var shortTypeName = "UserStatisticsGAgent"; // 从完整类型名提取
// 提取 GUID
var guid = "2e08d2bc..."; // 从旧 ID 提取
// 组合新 ID
var newAgentId = $"{shortTypeName}:{guid}";
```

## 配置

在 `appsettings.json` 中添加配置：

```json
{
  "StateMigration": {
    "IsEnabled": true,
    "OldSystemApiBaseUrl": "http://localhost:8001",
    "TokenEndpoint": "https://godgpt-app-auth-test.godgpt.fun/connect/token",
    "Username": "admin",
    "Password": "1q2w3E*",
    "ClientId": "AevatarAuthServer",
    "Scope": "Aevatar",
    "BatchSize": 1000,
    "BatchDelayMs": 100
  },
  "Storage": {
    "Provider": "MongoDB",
    "DatabaseName": "AevatarBusiness"
  }
}
```

### Token 获取方式

系统会自动从 `TokenEndpoint` 获取 Token：
- 使用 `Username` 和 `Password` 进行 OAuth2 密码模式认证
- Token 会被自动缓存，避免重复请求
- 每次执行迁移前会自动确保 Token 有效

## API 使用

### 1. 同步执行（小数据集）

```bash
POST /api/admin/migration/execute
Content-Type: application/json

{
  "collectionTypes": ["UserStatistics"]  // 可选，为空则迁移所有
}
```

**响应**：
```json
{
  "isEnabled": true,
  "startedAt": "2026-01-14T12:00:00Z",
  "completedAt": "2026-01-14T12:00:45Z",
  "duration": "00:00:45",
  "totalRecords": 10,
  "successCount": 10,
  "failedCount": 0,
  "collectionResults": [
    {
      "collectionName": "Streamgodgpt...",
      "typeName": "UserStatisticsGAgent",
      "totalRecords": 10,
      "successCount": 10,
      "failedCount": 0
    }
  ]
}
```

### 2. 异步执行（大数据集）

```bash
POST /api/admin/migration/execute-async
Content-Type: application/json

{
  "collectionTypes": ["UserStatistics", "GodChat"]
}
```

**响应**：
```json
{
  "jobId": "abc123",
  "message": "Migration job queued"
}
```

### 3. 查询任务状态

```bash
GET /api/admin/migration/status/{jobId}
```

## 测试 API

### 1. 导入 Mock 数据并测试

**端点**: `POST /api/admin/migration/test/import-mock`

**请求体**:
```json
{
  "jsonFilePath": "../../../src/mock_state_export.json"  // 可选，默认使用此路径
}
```

**响应示例**:
```json
{
  "total": 5,
  "writeSuccess": 1,
  "readSuccess": 1,
  "verified": 1,
  "failed": 4,
  "results": [
    {
      "id": "Aevatar.Application.Grains.UserStatistics.UserStatisticsGAgent/2e08d2bc...",
      "type": "UserStatisticsGAgent",
      "writeSuccess": true,
      "readSuccess": true,
      "verified": true,
      "error": null
    }
  ]
}
```

### 2. 读取单个 State 验证

**端点**: `GET /api/admin/migration/test/read/{agentId}`

**示例**:
```bash
GET /api/admin/migration/test/read/UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6
```

### 3. 测试 Agent 加载

**端点**: `POST /api/admin/migration/test/test-load`

**请求体**:
```json
{
  "deviceIdAgentId": "UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6"
}
```

**响应**:
```json
{
  "summary": {
    "totalTests": 2,
    "successCount": 2,
    "failedCount": 0
  },
  "results": [
    {
      "testName": "Load Agent with deviceId agentId",
      "agentId": "UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6",
      "success": true,
      "stateData": {
        "userId": "...",
        "appRatingsCount": 0,
        "appRatings": []
      }
    }
  ]
}
```

## 多 Agent 迁移实现

### 设计原则

1. **统一接口**：所有 Converter 实现 `IStateConverter` 接口
2. **自动发现**：根据 Agent 类型自动选择对应的 Converter
3. **类型映射**：维护旧 Agent 类型名到新 State 类型的映射
4. **扩展性**：添加新 Agent 类型只需实现对应的 Converter

### 实现步骤

#### 1. 定义 State Converter 接口

```csharp
public interface IStateConverter
{
    IMessage? Convert(Dictionary<string, object>? oldState);
}
```

#### 2. 实现特定 Agent 的 Converter

```csharp
public class UserStatisticsStateConverter : IStateConverter
{
    public IMessage? Convert(Dictionary<string, object>? oldState)
    {
        if (oldState == null)
            return new UserStatisticsState();

        var newState = new UserStatisticsState();
        
        // 转换逻辑
        if (oldState.TryGetValue("UserId", out var userIdObj))
        {
            newState.UserId = ConvertToString(userIdObj);
        }
        
        // ... 其他字段转换
        
        return newState;
    }
}
```

#### 3. 注册 Converter 映射

在 `StateMigrationJob` 中维护类型映射：

```csharp
private static readonly Dictionary<string, IStateConverter> Converters = new()
{
    { "UserStatisticsGAgent", new UserStatisticsStateConverter() },
    { "GodChatGAgent", new GodChatStateConverter() },
    { "PaymentRecordGAgent", new PaymentRecordStateConverter() },
    // ... 添加更多 Agent 类型
};
```

#### 4. 自动选择 Converter

```csharp
private IMessage? ConvertState(Dictionary<string, object>? oldState, string agentTypeName)
{
    if (!Converters.TryGetValue(agentTypeName, out var converter))
    {
        _logger.LogWarning("[StateMigration] No converter found for type: {Type}", agentTypeName);
        return null;
    }
    
    return converter.Convert(oldState);
}
```

### 添加新 Agent 类型的步骤

1. **创建 Protobuf State 定义**（如果还没有）
   ```protobuf
   message NewAgentState {
       string field1 = 1;
       int32 field2 = 2;
   }
   ```

2. **实现 Converter**
   ```csharp
   public class NewAgentStateConverter : IStateConverter
   {
       public IMessage? Convert(Dictionary<string, object>? oldState)
       {
           // 转换逻辑
       }
   }
   ```

3. **注册 Converter**
   ```csharp
   Converters.Add("NewAgentGAgent", new NewAgentStateConverter());
   ```

4. **测试**
   ```bash
   # 使用测试 API 验证
   curl -X POST "http://localhost:8082/api/admin/migration/test/import-mock" \
     -d '{"jsonFilePath": "path/to/mock/new_agent_data.json"}'
   ```

### 当前支持的 Agent 类型

| Agent 类型 | State 类型 | Converter | 状态 |
|-----------|-----------|-----------|------|
| UserStatisticsGAgent | UserStatisticsState | UserStatisticsStateConverter | ✅ 已实现 |
| GodChatGAgent | GodChatStateProto | - | ⏳ 待实现 |
| ChatManagerGAgent | ChatManagerStateProto | - | ⏳ 待实现 |
| PaymentRecordGAgent | PaymentRecordStateProto | - | ⏳ 待实现 |
| InvitationGAgent | InvitationState | - | ⏳ 待实现 |
| UserQuotaGAgent | UserQuotaStateProto | - | ⏳ 待实现 |
| AnonymousUserGAgent | AnonymousUserState | - | ⏳ 待实现 |
| AIAgentStatusProxy | AIAgentStatusProxyStateProto | - | ⏳ 待实现 |

### Converter 注册方式

当前实现中，Converter 是在 `StateMigrationJob` 中通过 `GetConverter` 方法动态创建的：

```csharp
private IStateConverter? GetConverter(string agentTypeName)
{
    return agentTypeName switch
    {
        "UserStatisticsGAgent" => new UserStatisticsStateConverter(),
        // 添加更多类型...
        _ => null
    };
}
```

**建议改进**：使用依赖注入注册 Converter，提高可扩展性：

```csharp
// 在 Startup 中注册
services.AddSingleton<IStateConverter, UserStatisticsStateConverter>();
services.AddSingleton<IStateConverter, GodChatStateConverter>();
// ...

// 使用工厂模式
services.AddSingleton<IStateConverterFactory, StateConverterFactory>();
```

这样添加新 Agent 类型时，只需：
1. 实现 `IStateConverter`
2. 在 DI 中注册
3. 无需修改 `StateMigrationJob` 代码

## 定时任务配置（可选）

如果需要定时执行，可以在 `HangfireExtensions.cs` 中添加：

```csharp
// 在 UseHangfireWithJobs 方法中添加
recurringJobManager.AddOrUpdate<StateMigrationJob>(
    "state-migration",
    job => job.ExecuteAsync(null, default),
    "0 2 * * *", // 每天凌晨 2 点执行
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.Utc
    });
```

## 验证清单

### 迁移前检查

- [ ] 旧系统 Export API 可访问
- [ ] Token 获取配置正确
- [ ] MongoDB 连接正常
- [ ] 目标数据库名称正确

### 迁移后验证

- [ ] 数据成功写入 MongoDB
- [ ] 集合名称正确（`agent_states_{StateTypeName}`）
- [ ] 文档结构正确（`AgentStateDocument` 格式）
- [ ] Agent 能够正常加载数据
- [ ] 所有业务字段都能正常读取
- [ ] Agent 方法调用成功

### 测试命令

```bash
# 1. 导入 Mock 数据
curl -X POST "http://localhost:8082/api/admin/migration/test/import-mock" \
  -H "Content-Type: application/json" \
  -d '{}'

# 2. 测试 Agent 加载
curl -X POST "http://localhost:8082/api/admin/migration/test/test-load" \
  -H "Content-Type: application/json" \
  -d '{"deviceIdAgentId": "UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6"}'

# 3. 读取 State 验证
curl "http://localhost:8082/api/admin/migration/test/read/UserStatisticsGAgent:2e08d2bc65ad4c579afee776ba45d0d6"
```

## 注意事项

1. **集合名称**：必须使用 `agent_states_{StateTypeName}` 格式，与 `MongoDBStateStore` 一致
2. **文档结构**：必须使用 `AgentStateDocument` 格式（`AgentId`, `StateData`, `StateType`, `Version`, `UpdatedAt`）
3. **Agent ID 格式**：必须转换为新格式 `TypeName:guid`
4. **Mock 数据格式**：Mock 数据中的 `AppRatings` 可能是数组，实际 API 返回的是 Dictionary，Converter 需要处理两种格式
5. **StateBase 字段**：旧数据中包含 `Children`, `Parent`, `GAgentCreator` 等字段，这些是 StateBase 的字段，在转换时会被忽略

## 已知限制和后续工作

### Agent 拆分情况

⚠️ **已知问题**：部分 Agent 在新系统中可能被拆分为多个 Agent，迁移时需要特殊处理：

- **场景**：旧系统的一个 Agent 可能对应新系统的多个 Agent
- **影响**：需要将旧 State 数据拆分并写入多个新 Agent
- **处理方式**：
  1. 识别被拆分的 Agent 类型
  2. 实现拆分逻辑（在 Converter 中或单独的拆分器）
  3. 为每个新 Agent 创建对应的 State
  4. 维护旧 Agent ID 到新 Agent ID 的映射关系

**示例**：
```
旧系统：
  - ComplexAgent (包含 A、B、C 三个功能)

新系统：
  - AgentA (只处理 A 功能)
  - AgentB (只处理 B 功能)
  - AgentC (只处理 C 功能)

迁移时需要：
  1. 读取 ComplexAgent State
  2. 提取 A、B、C 相关数据
  3. 分别创建 AgentA、AgentB、AgentC 的 State
  4. 记录映射关系（ComplexAgent/guid → [AgentA:guid, AgentB:guid, AgentC:guid]）
```

### 当前实现状态

✅ **已完成**：
- UserStatisticsGAgent 迁移（1:1 映射）
- 统一集合命名和文档结构
- 测试 API 和验证流程

⏳ **待实现**：
- 其他 Agent 类型的 Converter
- Agent 拆分场景的处理逻辑
- 拆分 Agent 的映射关系维护

### 后续优化方向

1. **Converter 工厂模式**：使用 DI 注册 Converter，提高扩展性
2. **拆分器模式**：为拆分的 Agent 实现专门的拆分逻辑
3. **映射关系存储**：维护旧 Agent ID 到新 Agent ID 的映射表
4. **增量迁移**：支持只迁移特定时间范围的数据
5. **回滚机制**：支持迁移失败时的回滚操作

## 相关文件

- `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/BackgroundJobs/StateMigrationJob.cs` - Job 实现
- `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/Controllers/StateMigrationController.cs` - API 控制器
- `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/Controllers/StateMigrationTestController.cs` - 测试控制器
- `apps/Aevatar.App/src/Aevatar.App.HttpApi.Host/BackgroundJobs/StateMigrationOptions.cs` - 配置选项

## 优势对比

| 特性 | 单独迁移工具 | ABP BackgroundJob ✅ |
|------|------------|---------------------|
| 版本兼容性 | 需要版本隔离 | 统一 net10.0 |
| 基础设施 | 需要单独配置 | 复用现有 |
| 部署 | 需要单独部署 | 集成到现有服务 |
| 监控 | 需要单独实现 | Hangfire Dashboard |
| 定时任务 | 需要单独实现 | 内置支持 |
| 集合命名 | 可能不一致 | 统一规范 ✅ |
| 文档结构 | 可能不一致 | 统一规范 ✅ |
