## SkillsMCPUnifiedDemo

目的：提供一个最小可跑的“统一 demo”，把 **Agent Skills / dotnet-file skills / MCP tools** 三条能力链串起来。

### 目录结构

```
examples/SkillsMCPUnifiedDemo/
├── Program.cs
├── UnifiedAgent.cs
├── SkillsMCPUnifiedDemo.csproj
├── appsettings.json
├── appsettings.secrets.json              # 不提交 git
├── agent_skills/
│   └── time-helper/
│       └── SKILL.md
├── skills/
│   ├── get_time.cs
│   └── system_info.cs
└── docs/
    └── ARCHITECTURE.md
```

### 关键设计

- `UnifiedAgent`：继承 `AIGAgentBase`，在 `RegisterToolsAsync` 里统一注册：
  - Aevatar 内置 tools（state/event/memory + skills_list/skills_load）
  - dotnet-file tools（`dotnet run --file`）
  - MCP tools（docker filesystem / context7，均为 best-effort）
- `allowed-tools`：当 `skills_load` 返回 `allowedTools`，本轮 tool-loop 内会启用 allowlist：
  - 过滤给 LLM 的 Functions
  - 拦截执行层（防止 hallucinate）

