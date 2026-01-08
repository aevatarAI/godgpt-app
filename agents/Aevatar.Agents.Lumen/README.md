# Aevatar.Agents.Lumen

Lumen 模块 - 占星预测智能体系统

## 概述

Lumen 是一个基于 Actor Model 的占星预测系统，提供以下功能：
- 用户配置管理 (生日、出生地、MBTI等)
- 每日/年度/终身预测生成
- 多语言支持 (en, zh, zh-tw, es)
- 用户反馈收集
- 预测收藏管理

## 模块结构

```
Aevatar.Agents.Lumen/
├── Protos/                      # Protobuf 定义
│   ├── lumen_common.proto       # 公共枚举和消息
│   ├── lumen_user_profile.proto # 用户配置
│   ├── lumen_prediction.proto   # 预测相关
│   ├── lumen_feedback.proto     # 反馈相关
│   └── lumen_favourite.proto    # 收藏相关
├── UserProfile/                 # 用户配置 Agent
├── Prediction/                  # 预测生成 Agent
├── History/                     # 历史记录 Agent
├── Feedback/                    # 反馈管理 Agent
├── Favourite/                   # 收藏管理 Agent
├── Calculators/                 # 计算工具类
│   ├── LumenCalculator.cs       # 主计算器
│   ├── WesternAstrologyCalculator.cs
│   └── SolarTermCalculator.cs
└── Helpers/                     # 辅助工具类
    ├── LumenTimezoneHelper.cs
    └── SolarTermData.cs
```

## Agent 列表

| Agent | 描述 | 状态 |
|-------|------|------|
| LumenUserProfileGAgent | 用户配置管理 | ⏳ 待迁移 |
| LumenPredictionGAgent | 预测生成 | ⏳ 待迁移 |
| LumenPredictionHistoryGAgent | 历史记录 | ✅ 已完成 |
| LumenFeedbackGAgent | 反馈管理 | ✅ 已完成 |
| LumenFavouriteGAgent | 收藏管理 | ✅ 已完成 |
| LumenStatsSnapshotGAgent | 统计快照 | ✅ 已完成 |
| LumenDailyYearlyHistoryGAgent | 日/年历史 | ✅ 已完成 |

## 迁移进度

详见 [LUMEN_MIGRATION_PLAN.md](../../docs/LUMEN_MIGRATION_PLAN.md)

## 开发指南

### 构建

```bash
cd agents/Aevatar.Agents.Lumen
dotnet build
```

### 运行测试

```bash
dotnet test ../test/Aevatar.Agents.Lumen.Tests/
```

### Proto 生成

Proto 文件在 build 时自动编译，生成的 C# 代码位于 `obj/` 目录。

## 依赖

- Aevatar.Agents.Core - 框架核心
- Aevatar.Agents.Abstractions - 接口定义
- Google.Protobuf - 序列化
- SwissEphNet - 天文计算 (用于精确星座计算)

## 注意事项

1. **所有 State 和 Event 必须使用 Protobuf 定义**
2. 使用 `GAgentBase<TState>` 而非旧版 `GAgentBase<TState, TEventLog>`
3. 使用 `await ConfirmEventsAsync()` 而非 `ConfirmEvents()`
4. 通过 `ActorFactory.CreateGAgentActorAsync<T>()` 获取 Agent

---

*最后更新: 2025-12-31*

