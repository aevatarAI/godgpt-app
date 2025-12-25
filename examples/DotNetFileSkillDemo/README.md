# DotNet File Skill Demo

这个 demo 用来验证：把 **C# 单文件**当作 “skill/tool” 并通过 `.NET 10` 的 `dotnet run --file` 执行。

## 运行

```bash
cd examples/DotNetFileSkillDemo
dotnet run
```

## 配置（真实 LLM 调用 skills）

把 API Key 放到 `appsettings.secrets.json`（不提交 git）：

```json
{
  "LLMProviders": {
    "deepseek": {
      "ApiKey": "your-api-key-here"
    }
  }
}
```

## 包含的 skills

- `skills/get_time.cs`：返回当前 UTC / 本地时间
- `skills/system_info.cs`：返回系统/运行时信息
- `skills/get_env.cs`：按 key 获取环境变量

