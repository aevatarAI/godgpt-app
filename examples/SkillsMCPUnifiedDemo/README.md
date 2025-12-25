# Skills + MCP Unified Demo

这个 demo 把三件事串在一起：

1) **dotnet-file skills**：把 `skills/*.cs` 作为 Tool（通过 `.NET 10` 的 `dotnet run --file` 执行）
2) **Agent Skills (SKILL.md)**：通过 `skills_list/skills_load` 让 LLM 按需加载 skill（并支持 `allowed-tools` 硬约束）
3) **MCP (Model Context Protocol)**：把 MCP server 的 tools 注册进 Agent（可选：docker filesystem / Context7）

## 运行

```bash
dotnet run --project examples/SkillsMCPUnifiedDemo/SkillsMCPUnifiedDemo.csproj
```

## 配置真实 LLM

在 `examples/SkillsMCPUnifiedDemo/appsettings.secrets.json` 写入：

```json
{
  "LLMProviders": {
    "providers": {
      "deepseek": {
        "apiKey": "YOUR_API_KEY_HERE"
      }
    }
  }
}
```

## MCP（可选）

- **Docker Filesystem MCP**：需要本机安装并启动 docker（demo 会 try/catch，没装也能跑，只是少 MCP tools）
- **Context7 MCP**：在 `appsettings.secrets.json` 里配置 `Context7.ApiKey` / `Context7.McpUrl`

## 包含的示例 skills

- `agent_skills/time-helper/SKILL.md`：`allowed-tools: [get_time]`（演示 allowlist）
- `skills/get_time.cs`：dotnet-file tool（真实时间）
- `skills/system_info.cs`：dotnet-file tool（系统信息）

