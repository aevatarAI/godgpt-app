# AI Agent With Tool Demo

这个示例展示了如何使用 `AIGAgentBase` 创建一个带工具调用能力的 AI 智能助手。

## 功能特性

- ✅ 工具注册和管理
- ✅ 自动工具调用
- ✅ 对话历史管理
- ✅ 多轮对话支持

## 包含的工具

1. **Calculator Tool** - 数学计算工具
   - 支持加减乘除
   - 自动处理参数验证

2. **Weather Tool** - 天气查询工具  
   - 模拟天气数据查询
   - 返回温度和天气状况

## 运行方式

```bash
cd examples/AIAgentWithToolDemo
dotnet run
```

## 配置

确保 `appsettings.secrets.json` 中配置了正确的 API Key：

```json
{
  "LLMProviders": {
    "deepseek": {
      "ApiKey": "your-api-key-here"
    }
  }
}
```

## 测试场景

1. **数学计算**: "帮我算一下 123 加 456 等于多少？"
2. **天气查询**: "北京今天天气怎么样？"
3. **复杂计算**: "50 乘以 8 是多少？然后除以 4"
4. **普通对话**: "你好，请介绍一下你自己"
