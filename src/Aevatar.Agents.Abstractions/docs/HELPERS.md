## Helpers

本目录记录 `Aevatar.Agents.Abstractions/Helpers/` 的关键工具函数与设计意图（用于跨模块复用）。

### 目录结构

```
src/Aevatar.Agents.Abstractions/
└── Helpers/
    ├── DeterministicGuid.cs   # 把字符串稳定映射为 Guid（用于 session->actor 的确定性身份）
    └── TimestampHelper.cs     # Timestamp/DateTime 转换辅助
```

### DeterministicGuid

- **用途**：把一个稳定的业务 key（如 `session_id`）映射成稳定的 `Guid`，用于 Actor/Agent 的确定性 ID。
- **Why**：
  - 前端要“无状态刷新”时，后端必须能在重连时稳定定位同一批 Actor（Coordinator/Workers）。
  - 在 Local/Orleans/ProtoActor 运行时里，`Guid` 是天然的 Actor Key。
  - 避免在服务层维护额外的 “sessionId -> agentId” 映射表（易丢失/易漂移）。
- **实现**：
  - `SHA256(input)` 取前 16 字节作为 Guid payload
  - 设置 version/variant bits（便于排查与一致性）

### 变更日志

- **2025-12**：新增 `DeterministicGuid`，用于 CognitiveStrategy 的 session-aware stable AgentId。

