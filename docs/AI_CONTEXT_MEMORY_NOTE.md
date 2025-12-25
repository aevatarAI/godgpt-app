## AI 上下文/记忆（Context & Memory）备忘录

目的：记录 Aevatar Agent Framework 当前“记住上下文”的能力边界，以及在**不浪费 token**的前提下应如何演进。

---

### 现状结论（先说人话）

- **LLM 本质无状态**：模型不会“自动记住上一次请求”。所谓“记忆”，只能靠框架把历史/摘要/检索结果再次放进 prompt 发送给模型。
- **框架支持“对话历史（history）”**：可以把历史消息存入 agent state，并在下一次请求中作为 `Messages` 发送给 provider。
- **但当前没有自动的“压缩/摘要/max_history”**：如果把 history 全量回放，token 会线性增长，反而更浪费。

换句话说：**能记，但不省 token；要省 token，必须做压缩或检索裁剪。**

---

### 当前能力（代码层面）

- **请求结构支持历史**：`AevatarLLMRequest.Messages` 可携带 `AevatarChatMessage` 列表（历史对话）。
- **Agent State 里也有 history 字段（Protobuf）**：`AevatarAIAgentState.history`，因此跨 runtime 边界的状态序列化是安全的（符合框架 Protobuf 铁律）。
- **部分 Agent（如 Tool Agent）会自动把 State.History 塞回请求**：因此“对话式 AI Agent”可实现持续对话。

⚠️ 注意：Cognitive DSL（`llm_call`）目前主要走 `SystemPrompt + UserPrompt` 的一次性 prompt，并不自动使用该 history 机制。

---

### 问题：为什么这不能直接省 token？

- 如果每次都把 history 追加进 `Messages`，token 仍然随着历史增长而增长；
- 如果 history 里包含大量原始上下文（axioms/theorems/长证明），增长会非常快；
- 因此必须引入“**有损压缩**”与“**相关性裁剪**”：
  - **摘要（Summarization）**：把旧内容压缩成短 memory
  - **检索（Retrieval / RAG）**：只带与当前问题相关的 top‑k facts

---

### 下一步建议（A：让 AI Agent 记忆更省 token）

目标：让“记忆”从“历史回放”升级为“**短摘要 + 小窗口历史**”。

- **max_history**：限制历史消息条数/总 token（滑动窗口）
- **自动摘要**：当 history 超阈值，触发 summarizer 把“旧历史→摘要 memory”
- **结构化 memory**：摘要不要写散文，应写成：
  - 已确认事实列表（id + 一句话）
  - 未解决问题/待验证假设列表
  - 关键约束（不能引入新假设、只能引用给定 axioms 等）

落地方式建议：
- 为 `ConversationHistoryManager` 增加：
  - `Trim(maxMessages/maxTokens)`（硬裁剪）
  - `SummarizeAndCompress(...)`（触发式压缩）
- 摘要与裁剪的输出也应存入 Protobuf state（例如 `custom_state` 或专门字段），保证跨边界可序列化。

---

### 适用边界（避免误用）

对“从公理推导大量定理”这类任务：
- 纯 history 回放通常不是最省 token 的方案；
- 更理想的是 **检索相关 facts + 引用 id**（把定理库当知识库，而不是聊天记录）。


