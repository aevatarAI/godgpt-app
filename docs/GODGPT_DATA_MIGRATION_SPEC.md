# GodGPT 数据迁移方案 (Data Migration Specification)

> 版本: 1.0
> 日期: 2026-01-11
> 状态: Draft

## 1. 概述

本文档描述从旧架构 (`old/godgpt`) 到新架构 (`Aevatar.Agents.GodGPT`) 的数据迁移方案。

### 1.1 迁移目标

- 将所有 Agent 状态从 Orleans Grain Storage 迁移到 MongoDB 分表存储
- 将数据格式从 Orleans `[GenerateSerializer]` 转换为 Protocol Buffers
- 保证数据完整性和一致性
- 最小化停服时间

### 1.2 试点 Agent

**UserStatistics** - 作为首个迁移试点，原因：
- 数据模型简单 (4 个字段)
- 有嵌套类型 (AppRatingInfo) 可验证转换逻辑
- 有 Dictionary → map 类型转换
- 风险低，即使失败影响有限

---

## 2. 架构差异总结

### 2.1 数据模型对比

| 维度 | 旧架构 | 新架构 |
|------|--------|--------|
| 状态定义 | C# Class + `[GenerateSerializer]` | Protocol Buffers |
| ID 类型 | `Guid` | `string` |
| 时间类型 | `DateTime` | `google.protobuf.Timestamp` |
| 集合类型 | `List<T>` | `repeated T` |
| 字典类型 | `Dictionary<K,V>` | `map<K,V>` |
| 可空类型 | `T?` | `optional T` |

### 2.2 存储层对比

| 维度 | 旧架构 | 新架构 |
|------|--------|--------|
| 存储引擎 | Orleans Grain Storage (Redis/MongoDB) | MongoDB |
| 集合结构 | 统一表 | 分表 (`agent_states_{Type}`) |
| 序列化 | Orleans Binary | Protobuf Binary |
| 文档结构 | Grain State | `{ AgentId, StateData, Version }` |

---

## 3. 迁移工具设计

### 3.1 项目结构

```
tools/
└── MigrationTool/
    ├── MigrationTool.csproj
    ├── Program.cs
    ├── appsettings.json
    ├── Readers/
    │   ├── IOldStateReader.cs
    │   ├── OldStateReaderBase.cs
    │   └── UserStatistics/
    │       └── UserStatisticsOldReader.cs
    ├── Converters/
    │   ├── IStateConverter.cs
    │   └── UserStatistics/
    │       └── UserStatisticsConverter.cs
    ├── Writers/
    │   ├── INewStateWriter.cs
    │   └── MongoDBStateWriter.cs
    ├── Services/
    │   ├── MigrationService.cs
    │   └── VerificationService.cs
    └── Models/
        └── MigrationResult.cs
```

### 3.2 核心接口定义

```csharp
// Readers/IOldStateReader.cs
public interface IOldStateReader<TOldState>
{
    Task<TOldState?> ReadAsync(string agentId);
    IAsyncEnumerable<(string AgentId, TOldState State)> ReadAllAsync();
    Task<int> GetCountAsync();
}

// Converters/IStateConverter.cs
public interface IStateConverter<TOldState, TNewState>
    where TNewState : IMessage<TNewState>, new()
{
    TNewState Convert(TOldState oldState);
    TOldState ConvertBack(TNewState newState); // For verification
}

// Writers/INewStateWriter.cs
public interface INewStateWriter<TNewState>
    where TNewState : IMessage<TNewState>, new()
{
    Task WriteAsync(string agentId, TNewState state);
    Task<TNewState?> ReadAsync(string agentId);
}
```

---

## 4. UserStatistics 迁移实现

### 4.1 旧状态结构 (C#)

```csharp
// old/godgpt/src/GodGPT.GAgents/UserStatistics/UserStatisticsState.cs
[GenerateSerializer]
public class UserStatisticsState : StateBase
{
    [Id(0)] public Guid UserId { get; set; }
    [Id(1)] public bool IsInitialized { get; set; } = false;
    [Id(2)] public Dictionary<string, AppRatingInfo> AppRatings { get; set; } = new();
    [Id(3)] public bool IsRealUser { get; set; } = true;
}

[GenerateSerializer]
public class AppRatingInfo
{
    [Id(0)] public string Platform { get; set; }
    [Id(1)] public string DeviceId { get; set; }
    [Id(2)] public DateTime FirstRatingTime { get; set; }
    [Id(3)] public DateTime LastRatingTime { get; set; }
    [Id(4)] public int RatingCount { get; set; }
}
```

### 4.2 新状态结构 (Protobuf)

```protobuf
// Protos/user_statistics.proto
message UserStatisticsState {
    string user_id = 1;
    bool is_initialized = 2;
    map<string, AppRatingInfo> app_ratings = 3;
    bool is_real_user = 4;
}

message AppRatingInfo {
    string platform = 1;
    string device_id = 2;
    google.protobuf.Timestamp first_rating_time = 3;
    google.protobuf.Timestamp last_rating_time = 4;
    int32 rating_count = 5;
}
```

### 4.3 转换器实现

```csharp
// Converters/UserStatistics/UserStatisticsConverter.cs
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Google.Protobuf.WellKnownTypes;

namespace MigrationTool.Converters.UserStatistics;

public class UserStatisticsConverter : IStateConverter<OldUserStatisticsState, UserStatisticsState>
{
    public UserStatisticsState Convert(OldUserStatisticsState old)
    {
        var newState = new UserStatisticsState
        {
            UserId = old.UserId.ToString(),
            IsInitialized = old.IsInitialized,
            IsRealUser = old.IsRealUser
        };

        // Convert Dictionary to map
        if (old.AppRatings != null)
        {
            foreach (var kvp in old.AppRatings)
            {
                newState.AppRatings[kvp.Key] = ConvertAppRatingInfo(kvp.Value);
            }
        }

        return newState;
    }

    private AppRatingInfo ConvertAppRatingInfo(OldAppRatingInfo old)
    {
        return new AppRatingInfo
        {
            Platform = old.Platform ?? "",
            DeviceId = old.DeviceId ?? "",
            FirstRatingTime = ToTimestamp(old.FirstRatingTime),
            LastRatingTime = ToTimestamp(old.LastRatingTime),
            RatingCount = old.RatingCount
        };
    }

    private static Timestamp ToTimestamp(DateTime dateTime)
    {
        // Ensure UTC
        var utc = dateTime.Kind == DateTimeKind.Utc 
            ? dateTime 
            : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        return Timestamp.FromDateTime(utc);
    }

    // For verification - convert back
    public OldUserStatisticsState ConvertBack(UserStatisticsState newState)
    {
        var old = new OldUserStatisticsState
        {
            UserId = Guid.Parse(newState.UserId),
            IsInitialized = newState.IsInitialized,
            IsRealUser = newState.IsRealUser,
            AppRatings = new Dictionary<string, OldAppRatingInfo>()
        };

        foreach (var kvp in newState.AppRatings)
        {
            old.AppRatings[kvp.Key] = new OldAppRatingInfo
            {
                Platform = kvp.Value.Platform,
                DeviceId = kvp.Value.DeviceId,
                FirstRatingTime = kvp.Value.FirstRatingTime.ToDateTime(),
                LastRatingTime = kvp.Value.LastRatingTime.ToDateTime(),
                RatingCount = kvp.Value.RatingCount
            };
        }

        return old;
    }
}
```

### 4.4 旧状态读取器

```csharp
// Readers/UserStatistics/UserStatisticsOldReader.cs
using MongoDB.Driver;
using Newtonsoft.Json;

namespace MigrationTool.Readers.UserStatistics;

/// <summary>
/// Reads old UserStatistics state from Orleans Grain Storage
/// Note: Adjust based on actual Orleans storage configuration
/// </summary>
public class UserStatisticsOldReader : IOldStateReader<OldUserStatisticsState>
{
    private readonly IMongoCollection<BsonDocument> _grainStateCollection;
    private readonly ILogger<UserStatisticsOldReader> _logger;
    
    // Orleans grain type name pattern
    private const string GrainTypePattern = "Aevatar.Application.Grains.UserStatistics.UserStatisticsGAgent";
    
    public UserStatisticsOldReader(
        IMongoDatabase database,
        ILogger<UserStatisticsOldReader> logger)
    {
        // Orleans MongoDB storage collection name (check your configuration)
        _grainStateCollection = database.GetCollection<BsonDocument>("OrleansGrainState");
        _logger = logger;
    }

    public async Task<OldUserStatisticsState?> ReadAsync(string agentId)
    {
        try
        {
            var filter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Eq("_id", agentId),
                Builders<BsonDocument>.Filter.Regex("GrainType", GrainTypePattern)
            );
            
            var doc = await _grainStateCollection.Find(filter).FirstOrDefaultAsync();
            if (doc == null) return null;

            return DeserializeState(doc);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read UserStatistics state for {AgentId}", agentId);
            return null;
        }
    }

    public async IAsyncEnumerable<(string AgentId, OldUserStatisticsState State)> ReadAllAsync()
    {
        var filter = Builders<BsonDocument>.Filter.Regex("GrainType", GrainTypePattern);
        
        using var cursor = await _grainStateCollection.Find(filter).ToCursorAsync();
        
        while (await cursor.MoveNextAsync())
        {
            foreach (var doc in cursor.Current)
            {
                var agentId = doc["_id"].AsString;
                var state = DeserializeState(doc);
                
                if (state != null)
                {
                    yield return (agentId, state);
                }
            }
        }
    }

    public async Task<int> GetCountAsync()
    {
        var filter = Builders<BsonDocument>.Filter.Regex("GrainType", GrainTypePattern);
        return (int)await _grainStateCollection.CountDocumentsAsync(filter);
    }

    private OldUserStatisticsState? DeserializeState(BsonDocument doc)
    {
        try
        {
            // Orleans stores state in "State" field as JSON or binary
            if (doc.Contains("State"))
            {
                var stateJson = doc["State"].ToJson();
                return JsonConvert.DeserializeObject<OldUserStatisticsState>(stateJson);
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize state from document");
            return null;
        }
    }
}
```

### 4.5 新状态写入器

```csharp
// Writers/MongoDBStateWriter.cs
using Aevatar.Agents.Persistence.MongoDB;
using Google.Protobuf;
using MongoDB.Driver;

namespace MigrationTool.Writers;

public class MongoDBStateWriter<TState> : INewStateWriter<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly IMongoCollection<AgentStateDocument> _collection;
    private readonly string _stateTypeName;

    public MongoDBStateWriter(IMongoDatabase database, string? collectionName = null)
    {
        var name = collectionName ?? $"agent_states_{typeof(TState).Name}";
        _collection = database.GetCollection<AgentStateDocument>(name);
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;
    }

    public async Task WriteAsync(string agentId, TState state)
    {
        var doc = new AgentStateDocument
        {
            AgentId = agentId,
            StateData = state.ToByteArray(),
            StateType = _stateTypeName,
            Version = 1,
            UpdatedAt = DateTime.UtcNow
        };

        await _collection.ReplaceOneAsync(
            x => x.AgentId == agentId,
            doc,
            new ReplaceOptions { IsUpsert = true });
    }

    public async Task<TState?> ReadAsync(string agentId)
    {
        var doc = await _collection.Find(x => x.AgentId == agentId).FirstOrDefaultAsync();
        if (doc?.StateData == null) return null;

        var state = new TState();
        state.MergeFrom(doc.StateData);
        return state;
    }
}
```

---

## 5. 迁移服务实现

### 5.1 迁移服务

```csharp
// Services/MigrationService.cs
namespace MigrationTool.Services;

public class MigrationService<TOldState, TNewState>
    where TNewState : class, IMessage<TNewState>, new()
{
    private readonly IOldStateReader<TOldState> _reader;
    private readonly IStateConverter<TOldState, TNewState> _converter;
    private readonly INewStateWriter<TNewState> _writer;
    private readonly ILogger _logger;

    public MigrationService(
        IOldStateReader<TOldState> reader,
        IStateConverter<TOldState, TNewState> converter,
        INewStateWriter<TNewState> writer,
        ILogger logger)
    {
        _reader = reader;
        _converter = converter;
        _writer = writer;
        _logger = logger;
    }

    public async Task<MigrationResult> MigrateAllAsync(
        CancellationToken ct = default,
        IProgress<int>? progress = null)
    {
        var result = new MigrationResult();
        var total = await _reader.GetCountAsync();
        var processed = 0;

        _logger.LogInformation("Starting migration of {Total} records", total);

        await foreach (var (agentId, oldState) in _reader.ReadAllAsync())
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var newState = _converter.Convert(oldState);
                await _writer.WriteAsync(agentId, newState);
                result.SuccessCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to migrate {AgentId}", agentId);
                result.FailedIds.Add(agentId);
                result.Errors.Add($"{agentId}: {ex.Message}");
            }

            processed++;
            progress?.Report(processed * 100 / total);
        }

        result.TotalCount = total;
        _logger.LogInformation(
            "Migration completed: {Success}/{Total} succeeded, {Failed} failed",
            result.SuccessCount, result.TotalCount, result.FailedIds.Count);

        return result;
    }

    public async Task<bool> MigrateOneAsync(string agentId)
    {
        try
        {
            var oldState = await _reader.ReadAsync(agentId);
            if (oldState == null)
            {
                _logger.LogWarning("No state found for {AgentId}", agentId);
                return false;
            }

            var newState = _converter.Convert(oldState);
            await _writer.WriteAsync(agentId, newState);
            
            _logger.LogInformation("Successfully migrated {AgentId}", agentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to migrate {AgentId}", agentId);
            return false;
        }
    }
}

public class MigrationResult
{
    public int TotalCount { get; set; }
    public int SuccessCount { get; set; }
    public List<string> FailedIds { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}
```

### 5.2 验证服务

```csharp
// Services/VerificationService.cs
namespace MigrationTool.Services;

public class VerificationService<TOldState, TNewState>
    where TNewState : class, IMessage<TNewState>, new()
{
    private readonly IOldStateReader<TOldState> _oldReader;
    private readonly INewStateWriter<TNewState> _newWriter;
    private readonly IStateConverter<TOldState, TNewState> _converter;
    private readonly ILogger _logger;

    public VerificationService(
        IOldStateReader<TOldState> oldReader,
        INewStateWriter<TNewState> newWriter,
        IStateConverter<TOldState, TNewState> converter,
        ILogger logger)
    {
        _oldReader = oldReader;
        _newWriter = newWriter;
        _converter = converter;
        _logger = logger;
    }

    /// <summary>
    /// Verify migrated data by comparing old and new states
    /// </summary>
    public async Task<VerificationResult> VerifyAsync(
        int sampleSize = 100,
        CancellationToken ct = default)
    {
        var result = new VerificationResult();
        var count = 0;

        await foreach (var (agentId, oldState) in _oldReader.ReadAllAsync())
        {
            if (ct.IsCancellationRequested) break;
            if (count >= sampleSize) break;

            try
            {
                var newState = await _newWriter.ReadAsync(agentId);
                if (newState == null)
                {
                    result.MissingInNew.Add(agentId);
                    continue;
                }

                // Convert new back to old and compare
                var convertedBack = _converter.ConvertBack(newState);
                var isMatch = CompareStates(oldState, convertedBack);

                if (isMatch)
                {
                    result.MatchCount++;
                }
                else
                {
                    result.MismatchIds.Add(agentId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Verification failed for {AgentId}", agentId);
                result.ErrorIds.Add(agentId);
            }

            count++;
        }

        result.TotalVerified = count;
        return result;
    }

    private bool CompareStates(TOldState old, TOldState converted)
    {
        // Use JSON serialization for deep comparison
        var oldJson = JsonConvert.SerializeObject(old);
        var convertedJson = JsonConvert.SerializeObject(converted);
        return oldJson == convertedJson;
    }
}

public class VerificationResult
{
    public int TotalVerified { get; set; }
    public int MatchCount { get; set; }
    public List<string> MismatchIds { get; set; } = new();
    public List<string> MissingInNew { get; set; } = new();
    public List<string> ErrorIds { get; set; } = new();
    
    public double MatchRate => TotalVerified > 0 
        ? (double)MatchCount / TotalVerified * 100 
        : 0;
}
```

---

## 6. 执行计划

### 6.1 Phase 1: 准备 (1天)

1. **创建 MigrationTool 项目**
   ```bash
   mkdir -p tools/MigrationTool
   cd tools/MigrationTool
   dotnet new console
   dotnet add reference ../../agents/Aevatar.Agents.GodGPT/
   dotnet add reference ../../src/Aevatar.Agents.Persistence.MongoDB/
   dotnet add package MongoDB.Driver
   dotnet add package Newtonsoft.Json
   ```

2. **配置数据库连接**
   ```json
   // appsettings.json
   {
     "OldMongoDB": {
       "ConnectionString": "mongodb://localhost:27017",
       "Database": "godgpt_old"
     },
     "NewMongoDB": {
       "ConnectionString": "mongodb://localhost:27017",
       "Database": "godgpt_new"
     }
   }
   ```

3. **实现旧状态模型**
   - 复制旧架构的 State 类定义
   - 不需要 `[GenerateSerializer]` 属性

### 6.2 Phase 2: 试点迁移 - UserStatistics (2天)

1. **实现转换器和读取器**
2. **单元测试**
   ```csharp
   [Fact]
   public void Convert_ShouldMapAllFields()
   {
       var old = new OldUserStatisticsState
       {
           UserId = Guid.NewGuid(),
           IsInitialized = true,
           AppRatings = new Dictionary<string, OldAppRatingInfo>
           {
               ["device1"] = new OldAppRatingInfo
               {
                   Platform = "iOS",
                   DeviceId = "device1",
                   FirstRatingTime = DateTime.UtcNow.AddDays(-30),
                   LastRatingTime = DateTime.UtcNow,
                   RatingCount = 5
               }
           }
       };

       var converter = new UserStatisticsConverter();
       var newState = converter.Convert(old);

       Assert.Equal(old.UserId.ToString(), newState.UserId);
       Assert.Equal(old.IsInitialized, newState.IsInitialized);
       Assert.Single(newState.AppRatings);
       Assert.Equal("iOS", newState.AppRatings["device1"].Platform);
   }
   ```

3. **执行试迁移**
   ```bash
   # 迁移单个用户
   dotnet run -- migrate-one --agent-id <test-user-id>
   
   # 验证
   dotnet run -- verify --agent-id <test-user-id>
   ```

### 6.3 Phase 3: 全量迁移 UserStatistics (0.5天)

```bash
# 1. 备份旧数据
mongodump --db godgpt_old --out backup/$(date +%Y%m%d)

# 2. 执行全量迁移
dotnet run -- migrate-all --type UserStatistics --batch-size 1000

# 3. 验证
dotnet run -- verify --type UserStatistics --sample-rate 10%

# 4. 生成报告
dotnet run -- report --output migration_report.json
```

### 6.4 Phase 4: 其他 Agent 迁移 (按优先级)

| 优先级 | Agent | 复杂度 | 估计时间 |
|--------|-------|--------|---------|
| 1 | UserStatistics | 低 | ✅ 试点完成 |
| 2 | UserProfile | 低 | 0.5天 |
| 3 | UserQuota | 中 | 1天 |
| 4 | UserInfo | 低 | 0.5天 |
| 5 | Invitation | 中 | 0.5天 |
| 6 | InviteCode | 低 | 0.5天 |
| 7 | FreeTrialCode | 低 | 0.5天 |
| 8 | ChatManager | 高 | 2天 |
| 9 | GodChat | 高 | 2天 |
| 10 | Awakening | 中 | 1天 |

---

## 7. 验证清单

### 7.1 数据完整性验证

- [ ] 所有 AgentId 都已迁移
- [ ] 字段值正确转换
- [ ] 时间戳精度保持
- [ ] 字典/列表数据完整

### 7.2 功能验证

- [ ] Agent 可正常激活
- [ ] 状态可正常读取
- [ ] 状态可正常更新
- [ ] Event Sourcing 正常工作

### 7.3 性能验证

- [ ] 状态加载时间 < 100ms
- [ ] 状态保存时间 < 200ms
- [ ] 无内存泄漏

---

## 8. 回滚计划

### 8.1 回滚触发条件

- 迁移成功率 < 99%
- 验证匹配率 < 99.9%
- 功能测试失败
- 性能下降 > 20%

### 8.2 回滚步骤

```bash
# 1. 停止新服务
systemctl stop godgpt-new

# 2. 恢复旧服务
systemctl start godgpt-old

# 3. 清理新数据 (可选)
mongosh godgpt_new --eval "db.agent_states_UserStatisticsState.drop()"
```

---

## 9. 监控和告警

### 9.1 迁移监控指标

- `migration_records_processed` - 已处理记录数
- `migration_records_failed` - 失败记录数
- `migration_duration_seconds` - 迁移耗时

### 9.2 运行时监控

- `agent_state_load_duration` - 状态加载耗时
- `agent_state_save_duration` - 状态保存耗时
- `agent_activation_errors` - 激活错误数

---

## 10. 附录

### 10.1 需迁移的 Agent 列表

| Agent | 旧 State 文件 | 新 Proto 文件 |
|-------|--------------|--------------|
| UserStatistics | `UserStatisticsState.cs` | `user_statistics.proto` |
| UserProfile | (in ChatManager) | `user_profile.proto` |
| UserQuota | `UserQuotaGAgentState.cs` | `user_quota.proto` |
| ChatManager | `ChatManagerGAgentState.cs` | `chat_manager.proto` |
| GodChat | (AIGAgentStateBase) | `god_chat.proto` |
| Awakening | `AwakeningState.cs` | `awakening.proto` |
| Anonymous | `AnonymousUserState.cs` | `anonymous_user.proto` |
| Invitation | `InvitationGAgentState.cs` | `invitation.proto` |
| InviteCode | `InviteCodeState.cs` | `invite_code.proto` |
| FreeTrialCode | `FreeTrialCodeState.cs` | `free_trial_code.proto` |
| Configuration | `ConfigurationState.cs` | `configuration.proto` |

### 10.2 类型转换速查表

| C# 类型 | Protobuf 类型 | 转换代码 |
|--------|--------------|---------|
| `Guid` | `string` | `guid.ToString()` |
| `DateTime` | `Timestamp` | `Timestamp.FromDateTime(dt.ToUniversalTime())` |
| `DateTime?` | `optional Timestamp` | null check |
| `List<T>` | `repeated T` | `repeatedField.AddRange(list)` |
| `Dictionary<K,V>` | `map<K,V>` | `foreach + mapField[k] = v` |
| `decimal` | `double` | `(double)decimal` |
| `enum` | `enum` | `(int)enumValue` |

---

*文档结束*
