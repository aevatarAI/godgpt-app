# Coding Agent 任务清单

> 目标：补齐 `AIGAgentBase`（工具能力已合并）直到具备 https://github.com/ghuntley/how-to-build-a-coding-agent 教程中的全部 coding agent 能力。

## 1. Proto 与消息层
- [ ] 定义 `coding_tools.proto`，覆盖 `ReadFileRequest/Response`、`ListFilesRequest/Response`、`RunCommandRequest/Response`、`EditFileRequest/Response`、`CodeSearchRequest/Response` 等所有跨边界结构。
- [ ] 生成并引用新 protobuf，确保所有工具输出结构化结果而非字符串。

## 2. 核心工具能力
- [ ] `WorkspaceReadFileTool`：限定在 workspace root，下行返回文件片段 + metadata（大小/编码/截断标记）。
- [ ] `WorkspaceListFilesTool`：支持相对路径、深度限制与简单模式过滤，防止列出整个磁盘。
- [ ] `SandboxCommandTool`：提供命令白名单、超时、stdout/stderr 限长，支持 `RequiresConfirmation` 与执行日志。
- [ ] `WorkspaceEditFileTool`：实现 `apply_patch` 式编辑/创建，带文件锁/冲突检测与回滚提示。
- [ ] `WorkspaceCodeSearchTool`：封装 ripgrep，允许 type 过滤、结果条数/上下文控制。

## 3. 工具执行框架
- [ ] 为 `AIGAgentBase.ExecuteToolAsync` 填充 `ToolExecutionContext`：包含 `AgentId`、`WorkspaceRoot`、`AllowedCommands`、`MaxOutputBytes` 等。
- [ ] 重构 `HandleFunctionCallAsync` 为循环（或尾递归），直到 LLM 返回纯文本，支持多轮工具调用。
- [ ] 为危险工具启用默认安全策略：`RateLimit`、`RequiresConfirmation`、操作审计事件。

## 4. 示例与测试
- [ ] 新建 `examples/CodingAgentDemo`，展示读取/搜索/编辑/命令执行的端到端流程。
- [ ] 为每个工具添加至少一个单元测试，验证 happy path 与越界输入（路径逃逸、命令禁用等）。
- [ ] 增加集成测试，模拟“LLM -> 多次工具 -> 最终回答”的事件循环，确保历史与工具结果写入一致。

## 5. 文档与配置
- [ ] 在文档中说明如何配置 `workspaceRoot`、命令白名单与确认策略，包含安全注意事项。
- [ ] 补充示例 README，列出可用工具、调用说明与常见故障排查。

完成以上清单即达到 coding agent 教程中第 6 阶段（code_search）的功能水平。
