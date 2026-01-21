# Twitter 模块功能文档

> 创建日期: 2026-01-12
> 最后更新: 2026-01-12
> 模块位置: `agents/Aevatar.Agents.GodGPT/Twitter/`

---

## 📋 功能总览

Twitter 模块提供完整的 Twitter 社交媒体集成功能，包括：

| 功能模块 | 类型 | 说明 |
|---------|------|------|
| **Twitter Auth** | 有状态 Agent | OAuth2 PKCE 认证流程 |
| **Identity Binding** | 有状态 Agent | Twitter ID 与系统用户 ID 映射 |
| **Twitter Monitor** | 有状态 Agent | 定时监控和拉取推文 |
| **Twitter Reward** | 有状态 Agent | 推文奖励计算和发放 |
| **Twitter API Service** | 无状态服务 | Twitter API 封装 |

---

## 🔐 1. Twitter Auth (OAuth2 认证)

**Agent**: `ITwitterAuthGAgent` / `TwitterAuthGAgent`

### 功能列表

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GeneratePkcePlainAsync()` | 生成 PKCE 验证码 (Plain 方式) | `PkceResultProto` |
| `GeneratePkceAsync()` | 生成 PKCE 验证码 (S256 方式) | `PkceResultProto` |
| `VerifyAuthCodeAsync(platform, code, redirectUri)` | 验证授权码并绑定账号 | `TwitterAuthResultProto` |
| `GetBindStatusAsync()` | 获取 Twitter 绑定状态 | `TwitterBindStatusProto` |
| `GetAuthParamsAsync()` | 获取 OAuth2 授权参数 | `TwitterAuthParamsProto` |

### 使用场景

```
用户点击"绑定Twitter" → 
  GeneratePkceAsync() → 获取 code_verifier/code_challenge → 
  重定向到 Twitter 授权页 → 
  用户授权后回调 → 
  VerifyAuthCodeAsync() → 绑定完成
```

### State 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `code_verifier` | string | PKCE 验证码 |
| `twitter_user_id` | string | Twitter 用户 ID |
| `twitter_username` | string | Twitter 用户名 |
| `access_token` | string | 访问令牌 |
| `refresh_token` | string | 刷新令牌 |
| `token_expires_at` | Timestamp | 令牌过期时间 |
| `is_bound` | bool | 是否已绑定 |

---

## 🔗 2. Twitter Identity Binding (身份绑定)

**Agent**: `ITwitterIdentityBindingGAgent` / `TwitterIdentityBindingGAgent`

### 功能列表

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `CreateOrUpdateBindingAsync(twitterUserId, userId, username, profileImageUrl)` | 创建或更新绑定关系 | `TwitterAuthResultProto` |
| `GetUserIdAsync()` | 根据 Twitter ID 获取系统用户 ID | `Guid?` |
| `GetBindStatusAsync()` | 获取绑定状态 | `TwitterBindStatusProto` |

### 使用场景

- 用户首次绑定 Twitter 账号时创建映射
- 奖励发放时根据 Twitter ID 查找系统用户 ID
- 检查用户是否已绑定 Twitter

### State 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `user_id` | string | 系统用户 ID (GUID) |
| `twitter_username` | string | Twitter 用户名 |
| `profile_image_url` | string | Twitter 头像 URL |
| `created_at` | Timestamp | 绑定创建时间 |
| `updated_at` | Timestamp | 最后更新时间 |

---

## 📡 3. Twitter Monitor (推文监控)

**Agent**: `ITwitterMonitorGAgent` / `TwitterMonitorGAgent`

### 功能列表

#### 监控控制

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `StartMonitoringAsync()` | 启动定时监控 | `bool` |
| `StopMonitoringAsync()` | 停止监控 | `bool` |
| `GetMonitoringStatusAsync()` | 获取监控状态 | `TweetMonitorStatus` |

#### 手动操作

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `FetchTweetsManuallyAsync()` | 手动触发推文拉取 | `TweetFetchResult` |
| `RefetchTweetsByTimeRangeAsync(timeRange)` | 按时间范围重新拉取 | `bool` |

#### 查询操作

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `QueryTweetsByTimeRangeAsync(timeRange)` | 按时间范围查询推文 | `List<TweetRecord>` |
| `GetFetchHistoryAsync(days)` | 获取拉取历史 | `List<TweetFetchHistory>` |
| `GetTweetStatisticsAsync(timeRange)` | 获取推文统计 | `TweetStatistics` |

#### 配置管理

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetMonitoringConfigAsync()` | 获取监控配置 | `TweetMonitorConfig` |
| `CleanupExpiredTweetsAsync()` | 清理过期推文 | `int` (清理数量) |

### 监控配置参数

| 参数 | 说明 | 默认值 |
|------|------|--------|
| `fetch_interval_minutes` | 拉取间隔 (分钟) | 由配置决定 |
| `max_tweets_per_fetch` | 每次最大拉取数 | 100 |
| `data_retention_days` | 数据保留天数 | 7 |
| `search_query` | 搜索关键词 | 配置的 @handle |
| `filter_original_only` | 只保留原创推文 | true |
| `enable_auto_cleanup` | 启用自动清理 | true |

### State 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `is_running` | bool | 监控是否运行中 |
| `last_fetch_time` | Timestamp | 上次拉取时间 |
| `stored_tweets` | map<string, TweetRecord> | 存储的推文 |
| `fetch_history` | repeated TweetFetchHistory | 拉取历史 |
| `last_error` | string | 最后错误信息 |
| `next_scheduled_fetch` | Timestamp | 下次计划拉取时间 |

---

## 🎁 4. Twitter Reward (奖励计算)

**Agent**: `ITwitterRewardGAgent` / `TwitterRewardGAgent`

### 功能列表

#### 计算控制

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `StartRewardCalculationAsync()` | 启动定时奖励计算 | `bool` |
| `StopRewardCalculationAsync()` | 停止奖励计算 | `bool` |
| `GetRewardCalculationStatusAsync()` | 获取计算状态 | `RewardCalculationStatus` |
| `TriggerRewardCalculationAsync(targetDate)` | 手动触发指定日期的计算 | `bool` |

#### 查询操作

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetRewardCalculationHistoryAsync(days)` | 获取计算历史 | `List<RewardCalculationHistory>` |
| `GetUserRewardRecordsAsync(userId, days)` | 获取用户奖励记录 | `List<UserRewardRecord>` |
| `GetUserRewardsByUserIdAsync(userId)` | 按日期分组获取用户奖励 | `Dictionary<string, List<UserRewardRecord>>` |
| `GetDailyRewardStatisticsAsync(targetDate)` | 获取每日奖励统计 | `DailyRewardStatistics` |
| `HasUserReceivedDailyRewardAsync(userId, date)` | 检查用户是否已领奖 | `bool` |

#### 配置管理

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetRewardConfigAsync()` | 获取奖励配置 | `RewardConfig` |
| `UpdateRewardConfigAsync(config)` | 更新奖励配置 | `bool` |
| `ClearRewardByDayUtcSecondAsync(utcSeconds)` | 清除指定日期的奖励记录 | `bool` |

#### 时间控制

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetTimeControlStatusAsync()` | 获取时间控制状态 | `TimeControlStatus` |

### 奖励配置参数

| 参数 | 说明 |
|------|------|
| `time_range_start_hours` | 奖励时间范围开始 (小时) |
| `time_range_end_hours` | 奖励时间范围结束 (小时) |
| `share_link_multiplier` | 分享链接奖励倍数 |
| `max_daily_credits_per_user` | 每用户每日最大积分 |
| `min_views_for_reward` | 最低阅读量要求 |
| `reward_tiers` | 奖励层级配置 |

### 奖励层级 (Reward Tiers)

| 参数 | 说明 |
|------|------|
| `min_views` | 最低阅读量 |
| `min_followers` | 最低粉丝数 |
| `reward_credits` | 奖励积分 |
| `tier_name` | 层级名称 |

### State 字段

| 字段 | 类型 | 说明 |
|------|------|------|
| `is_running` | bool | 奖励计算是否运行中 |
| `last_calculation_time` | Timestamp | 上次计算时间 |
| `user_rewards` | map<string, UserRewardRecordList> | 用户奖励记录 (按日期) |
| `calculation_history` | repeated RewardCalculationHistory | 计算历史 |
| `total_users_rewarded` | int32 | 总奖励用户数 |
| `total_credits_distributed` | int32 | 总发放积分 |

---

## 🔌 5. Twitter API Service (API 服务)

**Service**: `ITwitterApiService` / `TwitterApiService`

> 这是无状态服务，通过 DI 注入使用

### 功能列表

#### 推文搜索与分析

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `SearchTweetsAsync(request)` | 搜索包含指定内容的推文 | `TwitterApiResult<SearchTweetsResponse>` |
| `AnalyzeTweetAsync(tweetId)` | 综合推文分析 (含用户信息) | `TwitterApiResult<TweetProcessResult>` |
| `AnalyzeTweetLightweightAsync(tweetId)` | 轻量推文分析 (不含用户信息) | `TwitterApiResult<TweetProcessResult>` |
| `BatchAnalyzeTweetsAsync(tweetIds)` | 批量推文分析 | `TwitterApiResult<List<TweetProcessResult>>` |
| `BatchAnalyzeTweetsLightweightAsync(tweetIds)` | 批量轻量分析 | `TwitterApiResult<List<TweetProcessResult>>` |

#### 推文详情

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetTweetDetailsAsync(tweetId)` | 获取推文详情 | `TwitterApiResult<TweetDetails>` |
| `GetBatchTweetDetailsAsync(tweetIds)` | 批量获取推文详情 | `TwitterApiResult<List<TweetDetails>>` |

#### 用户信息

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `GetUserInfoAsync(userId)` | 获取 Twitter 用户信息 | `TwitterApiResult<TwitterUserInfo>` |

#### 工具方法

| 方法 | 说明 | 返回类型 |
|------|------|---------|
| `TestApiConnectionAsync()` | 测试 API 连接 | `TwitterApiResult<bool>` |
| `GetApiQuotaInfoAsync()` | 获取 API 配额信息 | `TwitterApiResult<TwitterApiQuota>` |
| `ValidateShareLinkAsync(url)` | 验证分享链接有效性 | `TwitterApiResult<ShareLinkValidation>` |
| `ExtractShareLinksAsync(tweetText)` | 从推文文本提取分享链接 | `TwitterApiResult<List<string>>` |
| `ExtractUrlsFromTweetAsync(tweetText)` | 从推文文本提取所有 URL | `TwitterApiResult<List<string>>` |

### 推文类型枚举

| 类型 | 值 | 说明 |
|------|-----|------|
| `Original` | 0 | 原创推文 |
| `Reply` | 1 | 回复推文 |
| `Retweet` | 2 | 转发推文 |
| `Quote` | 3 | 引用推文 |
| `Unknown` | 99 | 未知类型 |

### 推文指标

| 指标 | 说明 |
|------|------|
| `ViewCount` | 阅读量 |
| `RetweetCount` | 转发数 |
| `LikeCount` | 点赞数 |
| `ReplyCount` | 回复数 |
| `QuoteCount` | 引用数 |

### 用户指标

| 指标 | 说明 |
|------|------|
| `FollowersCount` | 粉丝数 |
| `FollowingCount` | 关注数 |
| `TweetCount` | 推文数 |
| `IsVerified` | 是否认证 |

---

## 📁 文件结构

```
agents/Aevatar.Agents.GodGPT/
├── Protos/
│   ├── twitter.proto              # Auth & Identity Binding 定义
│   ├── twitter_monitor.proto      # Monitor State/Events/DTOs
│   └── twitter_reward.proto       # Reward State/Events/DTOs
│
└── Twitter/
    ├── ITwitterAuthGAgent.cs              # OAuth2 认证接口
    ├── TwitterAuthGAgent.cs               # OAuth2 认证实现
    │
    ├── ITwitterIdentityBindingGAgent.cs   # 身份绑定接口
    ├── TwitterIdentityBindingGAgent.cs    # 身份绑定实现
    │
    ├── ITwitterMonitorGAgent.cs           # 推文监控接口
    ├── TwitterMonitorGAgent.cs            # 推文监控实现
    │
    ├── ITwitterRewardGAgent.cs            # 奖励计算接口
    ├── TwitterRewardGAgent.cs             # 奖励计算实现
    │
    └── Services/
        ├── ITwitterApiService.cs          # API 服务接口
        └── TwitterApiService.cs           # API 服务实现
```

---

## 🔄 业务流程

### 推文奖励完整流程

```
1. 用户绑定 Twitter
   └─ TwitterAuthGAgent.VerifyAuthCodeAsync()
   └─ TwitterIdentityBindingGAgent.CreateOrUpdateBindingAsync()

2. 监控推文
   └─ TwitterMonitorGAgent.StartMonitoringAsync()
   └─ 定时调用 TwitterApiService.SearchTweetsAsync()
   └─ 过滤并存储符合条件的推文

3. 计算奖励
   └─ TwitterRewardGAgent.TriggerRewardCalculationAsync()
   └─ 从 Monitor 获取推文数据
   └─ 根据阅读量、粉丝数计算奖励层级
   └─ 应用分享链接倍数
   └─ 发放积分到用户账户

4. 查询奖励
   └─ TwitterRewardGAgent.GetUserRewardRecordsAsync()
   └─ 显示用户的奖励历史
```

### Monitor 与 Reward 关联架构

```
┌─────────────────────────────────────────────────────────────────┐
│                      数据流向 (上游 → 下游)                       │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   Twitter API                                                    │
│       │                                                          │
│       ▼                                                          │
│   ┌───────────────────────┐                                     │
│   │  TwitterMonitorGAgent │  ← 数据收集层                        │
│   │  • 定期拉取推文         │                                     │
│   │  • 存储推文记录         │                                     │
│   │  • 过滤非原创推文       │                                     │
│   └───────────┬───────────┘                                     │
│               │                                                  │
│               │ QueryTweetsByTimeRangeAsync()                    │
│               ▼                                                  │
│   ┌───────────────────────┐                                     │
│   │  TwitterRewardGAgent  │  ← 业务计算层                        │
│   │  • 获取推文数据         │                                     │
│   │  • 计算奖励层级         │                                     │
│   │  • 应用倍数加成         │                                     │
│   │  • 发放用户积分         │                                     │
│   └───────────────────────┘                                     │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

#### 关联实现细节

**TwitterRewardGAgent** 通过 `IGAgentActorFactory` 获取 **TwitterMonitorGAgent** 的引用：

```csharp
// 获取 Monitor Agent 引用
private async Task<ITwitterMonitorGAgent?> GetMonitorAgentAsync()
{
    var actor = await _agentFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(
        TwitterMonitorAgentId);  // "twitter-monitor-singleton"
    return actor.As<ITwitterMonitorGAgent>();
}

// 从 Monitor 获取推文数据
var tweets = await monitor.QueryTweetsByTimeRangeAsync(timeRange);
```

#### 奖励计算流程

1. **获取推文**: 从 Monitor 查询指定日期的推文
2. **过滤推文**: 
   - 只保留原创推文 (`TweetTypeOriginal`)
   - 排除黑名单用户 (`ExcludedUserIds`)
   - 检查最低阅读量 (`MinViewsForReward`)
3. **计算层级**: 根据 `RewardTiers` 配置匹配奖励层级
4. **应用倍数**: 包含分享链接则应用 `ShareLinkMultiplier`
5. **限制检查**: 每用户每日最多 `MaxDailyCreditsPerUser`
6. **存储记录**: 使用 EventSourcing 存储 `UserRewardRecord`

### 推文筛选规则

1. **只保留原创推文** - 过滤掉回复、转发、引用
2. **排除黑名单账号** - 过滤系统账号和作弊账号
3. **每用户每日限制** - 每个用户每天最多 N 条推文参与奖励
4. **最低阅读量要求** - 推文需达到最低阅读量才能获得奖励
5. **分享链接验证** - 包含有效分享链接可获得额外奖励倍数

---

## ⚙️ 配置说明

### appsettings.json 配置项

```json
{
  "TwitterRewardOptions": {
    "BearerToken": "xxx",           // Twitter API Bearer Token
    "MonitorHandle": "@godgpt",     // 监控的 Twitter 账号
    "PullIntervalMinutes": 60,      // 拉取间隔 (分钟)
    "BatchFetchSize": 100,          // 每批拉取数量
    "DataRetentionDays": 7,         // 数据保留天数
    "ShareLinkDomain": "https://app.godgpt.fun", // 分享链接域名
    "ShareLinkMultiplier": 1.5,     // 分享链接奖励倍数
    "DailyRewardLimit": 500,        // 每日最大奖励积分
    "MinViewsForReward": 20,        // 最低阅读量要求
    "RewardTiers": [                // 奖励层级
      { "MinViews": 100, "MinFollowers": 0, "RewardCredits": 10 },
      { "MinViews": 500, "MinFollowers": 100, "RewardCredits": 20 },
      // ...
    ],
    "ExcludedAccountIds": ["123", "456"], // 排除的账号 ID
    "TimeWindowHours": 4,           // 时间窗口 (小时)
    "MaxTweetsPerUser": 10,         // 每用户每日最大推文数
    "TweetProcessingDelayMs": 3000  // 推文处理延迟 (毫秒)
  }
}
```

---

## 🌐 6. HTTP API 接口

> 以下 API 定义于旧架构的 Controller 中，迁移后将由新 Agent 提供支持

### 6.1 用户级 API

**Controller**: `GodGPTInvitationController`  
**路由前缀**: `/api/godgpt/invitation`  
**权限**: 需要用户登录 (`[Authorize]`)

| HTTP 方法 | 路径 | 说明 | Agent 方法 |
|-----------|------|------|-----------|
| GET | `/twitter/params` | 获取 OAuth2 认证参数 (PKCE) | `TwitterAuthGAgent.GetAuthParamsAsync()` |
| POST | `/twitter/verify` | 验证 Twitter 授权码并绑定账号 | `TwitterAuthGAgent.VerifyAuthCodeAsync()` |

#### GET /twitter/params

获取 Twitter OAuth2 PKCE 认证所需的参数。

**请求**: 无参数

**响应**: `TwitterAuthParamsDto`

```json
{
  "authorizationUrl": "https://twitter.com/i/oauth2/authorize?...",
  "codeVerifier": "abc123...",
  "codeChallenge": "xyz789...",
  "state": "random-state-string"
}
```

**流程**:
1. 前端调用此 API 获取认证参数
2. 使用 `authorizationUrl` 重定向用户到 Twitter 授权页面
3. 用户授权后，Twitter 回调前端携带 `code` 和 `state`

---

#### POST /twitter/verify

验证 Twitter 回调的授权码，完成账号绑定。

**请求**: `TwitterAuthVerifyInput`

```json
{
  "code": "authorization_code_from_twitter",
  "state": "state_from_callback",
  "codeVerifier": "code_verifier_from_params"
}
```

**响应**: `TwitterAuthResultDto`

```json
{
  "success": true,
  "twitterUserId": "123456789",
  "twitterUsername": "user_handle",
  "message": "绑定成功"
}
```

**流程**:
1. 前端收到 Twitter 回调后，携带 `code` 和 `state` 调用此 API
2. 后端使用 `code_verifier` 交换 access_token
3. 获取 Twitter 用户信息并绑定到当前用户

---

### 6.2 管理级 API

**Controller**: `GodGPTTwitterManagementController`  
**路由前缀**: `/api/godgpt/twitter-management`  
**权限**: 需要管理员权限 (`[Authorize]` + `BeforeCheckUserIsManager()`)

#### 6.2.1 监控管理 (Monitor)

| HTTP 方法 | 路径 | 说明 | Agent 方法 |
|-----------|------|------|-----------|
| POST | `/monitor/fetch-manually` | 手动触发推文抓取 | `TwitterMonitorGAgent.FetchTweetsManuallyAsync()` |
| POST | `/monitor/refetch-by-time-range` | 按时间范围重新抓取 | `TwitterMonitorGAgent.RefetchTweetsByTimeRangeAsync()` |
| POST | `/monitor/start` | 启动自动监控任务 | `TwitterMonitorGAgent.StartMonitoringAsync()` |
| POST | `/monitor/stop` | 停止自动监控任务 | `TwitterMonitorGAgent.StopMonitoringAsync()` |
| GET | `/monitor/status` | 获取监控状态 | `TwitterMonitorGAgent.GetMonitoringStatusAsync()` |

##### POST /monitor/fetch-manually

手动触发一次推文抓取，用于测试或紧急更新。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

```json
{
  "isSuccess": true,
  "errorMessage": null,
  "data": {
    "fetchedCount": 42,
    "processedCount": 38,
    "filteredCount": 4
  }
}
```

---

##### POST /monitor/refetch-by-time-range

按指定时间范围重新抓取推文，用于补数据。

**请求**: Query 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `startTimeUtcSecond` | long | 开始时间 (UTC 秒级时间戳) |
| `endTimeUtcSecond` | long | 结束时间 (UTC 秒级时间戳) |

**示例**: `POST /monitor/refetch-by-time-range?startTimeUtcSecond=1704067200&endTimeUtcSecond=1704153600`

**响应**: `TwitterOperationResultDto`

---

##### POST /monitor/start

启动自动监控任务，开始定期抓取推文。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

---

##### POST /monitor/stop

停止自动监控任务。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

---

##### GET /monitor/status

获取当前监控任务的运行状态。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

```json
{
  "isSuccess": true,
  "data": {
    "isRunning": true,
    "lastFetchTime": "2026-01-12T10:30:00Z",
    "nextScheduledFetch": "2026-01-12T11:30:00Z",
    "totalTweetsStored": 1250,
    "lastError": null
  }
}
```

---

#### 6.2.2 奖励管理 (Rewards)

| HTTP 方法 | 路径 | 说明 | Agent 方法 |
|-----------|------|------|-----------|
| POST | `/rewards/trigger-calculation` | 手动触发奖励计算 | `TwitterRewardGAgent.TriggerRewardCalculationAsync()` |
| DELETE | `/rewards/clear-by-day` | 清除指定日期奖励记录 | `TwitterRewardGAgent.ClearRewardByDayUtcSecondAsync()` |
| POST | `/rewards/start` | 启动奖励计算任务 | `TwitterRewardGAgent.StartRewardCalculationAsync()` |
| POST | `/rewards/stop` | 停止奖励计算任务 | `TwitterRewardGAgent.StopRewardCalculationAsync()` |
| GET | `/rewards/user/{userId}` | 获取用户奖励记录 | `TwitterRewardGAgent.GetUserRewardsByUserIdAsync()` |
| GET | `/rewards/calculation-history` | 获取计算历史 | `TwitterRewardGAgent.GetRewardCalculationHistoryAsync()` |

##### POST /rewards/trigger-calculation

手动触发指定日期的奖励计算。

**请求**: Query 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `targetDateUtcSeconds` | long | 目标日期 (UTC 秒级时间戳) |

**示例**: `POST /rewards/trigger-calculation?targetDateUtcSeconds=1704067200`

**响应**: `TwitterOperationResultDto`

---

##### DELETE /rewards/clear-by-day

清除指定日期的奖励记录，用于测试或重新计算。

**请求**: Query 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `targetDateUtcSeconds` | long | 目标日期 (UTC 秒级时间戳) |

**响应**: `TwitterOperationResultDto`

---

##### POST /rewards/start

启动自动奖励计算任务。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

---

##### POST /rewards/stop

停止自动奖励计算任务。

**请求**: 无参数

**响应**: `TwitterOperationResultDto`

---

##### GET /rewards/user/{userId}

获取指定用户的奖励记录，按日期分组。

**请求**: Path 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `userId` | string | 用户 ID (GUID 字符串) |

**响应**: `Dictionary<string, List<ManagerUserRewardRecordDto>>`

```json
{
  "2026-01-12": [
    {
      "tweetId": "123456789",
      "tweetText": "Check out @godgpt...",
      "rewardCredits": 20,
      "viewCount": 500,
      "hasShareLink": true,
      "createdAt": "2026-01-12T08:00:00Z"
    }
  ],
  "2026-01-11": [...]
}
```

---

##### GET /rewards/calculation-history

获取奖励计算的历史记录。

**请求**: 无参数

**响应**: `List<ManagerRewardCalculationHistoryDto>`

```json
[
  {
    "calculationDate": "2026-01-12",
    "totalUsersRewarded": 150,
    "totalCreditsDistributed": 3500,
    "totalTweetsProcessed": 420,
    "calculatedAt": "2026-01-12T00:05:00Z",
    "status": "Completed"
  }
]
```

---

#### 6.2.3 测试管理

| HTTP 方法 | 路径 | 说明 | Agent 方法 |
|-----------|------|------|-----------|
| POST | `/awakening/reset-for-testing` | 重置唤醒状态 (测试用) | `GodGPTService.ResetAwakeningStateForTestingAsync()` |

##### POST /awakening/reset-for-testing

重置指定用户的唤醒状态，仅用于测试环境。

**请求**: Query 参数

| 参数 | 类型 | 说明 |
|------|------|------|
| `userId` | Guid | 用户 ID |

**响应**: `TwitterOperationResultDto`

---

### 6.3 API 响应格式

#### TwitterOperationResultDto

所有管理 API 的标准响应格式：

```json
{
  "isSuccess": true,
  "errorMessage": "错误信息 (失败时)",
  "data": {}  // 可选的附加数据
}
```

---

### 6.4 API 与 Agent 映射汇总

```
┌─────────────────────────────────────────────────────────────────┐
│                     HTTP API Layer                               │
├─────────────────────────────────────────────────────────────────┤
│ GodGPTInvitationController                                       │
│   GET  /twitter/params  ─────────────┐                          │
│   POST /twitter/verify  ─────────────┼───▶ TwitterAuthGAgent    │
├─────────────────────────────────────────────────────────────────┤
│ GodGPTTwitterManagementController                                │
│   POST /monitor/fetch-manually  ─────┐                          │
│   POST /monitor/refetch-by-time-range│                          │
│   POST /monitor/start  ──────────────┼───▶ TwitterMonitorGAgent │
│   POST /monitor/stop  ───────────────┤                          │
│   GET  /monitor/status  ─────────────┘                          │
│                                                                  │
│   POST   /rewards/trigger-calculation ┐                         │
│   DELETE /rewards/clear-by-day  ──────┤                         │
│   POST   /rewards/start  ─────────────┼──▶ TwitterRewardGAgent  │
│   POST   /rewards/stop  ──────────────┤                         │
│   GET    /rewards/user/{userId}  ─────┤                         │
│   GET    /rewards/calculation-history ┘                         │
└─────────────────────────────────────────────────────────────────┘
```

---

*最后更新: 2026-01-12*
*文档版本: 4.0*
*迁移状态: ✅ 全部完成*
*Agent 关联: ✅ Monitor → Reward 数据流已实现*