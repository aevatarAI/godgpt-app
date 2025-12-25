# Aevatar.Agents.Cognitive

> DSL 驱动的认知 Agent - 真正的 Actor 并行

## 🎯 核心架构

```
Coordinator + Worker 模式 (真正的 Actor 并行)
────────────────────────────────────────────────

┌─────────────────────────────────────────────────┐
│           CognitiveCoordinatorGAgent            │
│                                                 │
│  ┌─────────────┐  ┌─────────────┐              │
│  │ DSL Parser  │  │ TemplateEng │              │
│  └─────────────┘  └─────────────┘              │
│                                                 │
│  工作流调度 + 结果收集                           │
└─────────────────────────────────────────────────┘
         │                    ▲
         │ ExecuteStepRequest │ StepCompletedEvent
         ▼                    │
┌─────────────────────────────────────────────────┐
│              Worker Pool (Actor 并行)            │
│                                                 │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐          │
│  │Worker #1│ │Worker #2│ │Worker #3│   ...    │
│  │ (LLM)   │ │ (LLM)   │ │ (LLM)   │          │
│  └─────────┘ └─────────┘ └─────────┘          │
│                                                 │
│  由 Actor Runtime (Orleans/ProtoActor) 调度     │
└─────────────────────────────────────────────────┘
```

## 📦 项目结构

```
Aevatar.Agents.Cognitive/
├── Aevatar.Agents.Cognitive.csproj
├── cognitive_messages.proto          # Protobuf 定义
├── README.md
│
├── Agents/
│   ├── CognitiveCoordinatorGAgent.cs                     # Coordinator: state + step dispatcher ⭐
│   ├── CognitiveCoordinatorGAgent.Workflow.cs            # workflow start/fail/output build
│   ├── CognitiveCoordinatorGAgent.Parallel.cs            # fan_out/parallel + worker completion
│   ├── CognitiveCoordinatorGAgent.Llm.cs                 # Coordinator-side LLM (streaming + guardrails)
│   ├── CognitiveCoordinatorGAgent.Vote.cs                # vote consensus (semantic clustering + red-flag)
│   ├── CognitiveCoordinatorGAgent.StepEvents.cs          # step events for UI/observability
│   ├── CognitiveCoordinatorGAgent.Parameters.cs          # output parsing + parameter helpers + red-flag config
│   └── CognitiveWorkerGAgent.cs                          # Worker: execute llm_call and report results ⭐
│
├── Template/
│   ├── TemplateEngine.cs             # 模板渲染 (Scriban)
│   └── OutputParser.cs               # 输出解析器
│
├── Primitives/
│   ├── IPrimitive.cs                 # 原语上下文 + PrimitiveResult + 参数扩展
│   ├── WorkflowDefinition.cs         # WorkflowDefinition/StepDefinition/InputParameter + IWorkflowRegistry
│   └── WorkflowResult.cs             # 执行结果 + InMemoryWorkflowRegistry
│
├── Engine/
│   └── WorkflowParser.cs             # YAML 解析器
│
├── DependencyInjection/
│   └── ServiceCollectionExtensions.cs
│
└── workflows/
    ├── direct.yaml                   # Direct 策略
    ├── maker.yaml                    # MAKER 策略
    └── uot-combinational.yaml        # UoT 策略
```

## 🚀 使用示例

```csharp
// 1. 创建 Coordinator
var coordinatorId = Guid.NewGuid();
var coordinator = await actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(coordinatorId);
var agent = coordinator.GetAgent() as CognitiveCoordinatorGAgent;

// 2. 设置 ActorManager（用于创建 Workers）
agent!.SetActorManager(actorManager);

// 3. 创建 Worker 池（真正的 Actor 并行）
await agent.CreateWorkerPoolAsync(poolSize: 5);

// 4. 配置语义聚类投票 (可选，推荐)
var embeddingGenerator = llmProviderFactory.GetEmbeddingGenerator("openai");
agent.SetEmbeddingGenerator(embeddingGenerator, semanticSimilarityThreshold: 0.85f);

// 5. 注册 DSL 工作流
var parser = new WorkflowParser();
var workflow = parser.ParseFile("workflows/uot-combinational.yaml");
agent.RegisterWorkflow(workflow);

// 6. 发送启动事件
await coordinator.PublishEventAsync(new StartWorkflowRequestEvent
{
    WorkflowName = "uot-combinational",
    Variables = { ["problem"] = Value.ForString("如何优化城市交通?") }
});

// 7. 监听完成事件
// Coordinator 会发布 WorkflowCompletedEventProto
```

## 🗳️ 语义聚类投票

投票步骤支持两种模式：

### 1. 语义聚类 (默认，需配置 EmbeddingGenerator)

```csharp
// 配置 embedding generator
var embeddingGenerator = llmProviderFactory.GetEmbeddingGenerator("openai");
agent.SetEmbeddingGenerator(embeddingGenerator, semanticSimilarityThreshold: 0.85f);
```

特点：
- 使用 embeddings 计算语义相似度
- 相似回答自动聚类
- 即使文字不同，语义相近也算同一票
- **推荐用于生产环境**

### 2. 精确匹配 (回退模式)

```csharp
// 不配置 embedding generator
// 或显式设置为 null
agent.SetEmbeddingGenerator(null);
```

特点：
- 使用 SHA256 哈希匹配
- 只有完全相同的回答才算同一票
- 适用于开发测试

### DSL 配置

```yaml
- id: solve_with_consensus
  type: vote
  k: 2                    # K 票共识
  max_rounds: 10          # 最大轮次
  similarity: 0.85        # 语义相似度阈值 (仅语义聚类模式)
  generator:
    type: llm_call
    prompt: "Solve: {{task}}"
  store: solution
```

## 🔄 并行执行流程

```
Fan-out: 5 items, 3 workers
─────────────────────────────────────────────

   Coordinator                    Workers
       │                      ┌───────────┐
       │── Request[0] ───────>│ Worker #1 │──┐
       │── Request[1] ───────>│ Worker #2 │──┤
       │── Request[2] ───────>│ Worker #3 │──┤
       │                      │ (并行LLM) │  │
       │<── Completed[0] ─────│ Worker #1 │<─┤
       │<── Completed[2] ─────│ Worker #3 │<─┤
       │── Request[3] ───────>│ Worker #1 │──┤ (复用)
       │── Request[4] ───────>│ Worker #3 │──┤ (复用)
       │<── Completed[1] ─────│ Worker #2 │<─┤
       │<── Completed[3] ─────│ Worker #1 │<─┤
       │<── Completed[4] ─────│ Worker #3 │<─┘
       │                      └───────────┘
       ▼
  (汇聚结果，继续下一步)
```

## 📝 DSL 工作流示例

```yaml
# workflows/uot-combinational.yaml
name: uot-combinational
version: "1.0"

inputs:
  - name: problem
    type: string
    required: true

steps:
  # Step 1: 类比检索
  - id: retrieve_analogies
    type: llm_call
    prompt: |
      Find 5 analogous problems:
      {{problem}}
    output: json_array
    store: analogies

  # Step 2: 并行分解（由 Workers 执行）
  - id: decompose_thoughts
    type: fan_out
    for_each: analogies
    max_concurrency: 5
    step:
      type: llm_call
      prompt: |
        Decompose solution: {{item.solution}}
    reduce: flatten
    store: thoughts

  # ... more steps

output:
  result: "{{final_solution}}"
```

## 🔧 Protobuf 事件

```protobuf
// Coordinator → Worker
message ExecuteStepRequestEvent {
    string request_id = 1;
    string step_id = 2;
    string step_type = 3;
    map<string, Value> parameters = 4;
    map<string, Value> variables = 5;
}

// Worker → Coordinator
message StepCompletedEventProto {
    string request_id = 1;
    string step_id = 2;
    string worker_id = 3;
    bool success = 4;
    string result = 5;
    string error = 6;
    int32 tokens_used = 7;
    int32 llm_calls = 8;
}

// External → Coordinator
message StartWorkflowRequestEvent {
    string workflow_name = 1;
    map<string, Value> variables = 2;
}

// Coordinator → External
message WorkflowCompletedEventProto {
    string execution_id = 1;
    bool success = 2;
    string result = 3;
    string error = 4;
}
```

## 📋 DSL 重新实现 MAKER 和 UoT

### MAKER System v2 (`workflows/maker-v2.yaml`)

核心算法重新用 DSL 表达：

```yaml
steps:
  # 1. 判断任务是否为原子任务
  - id: check_atomic
    type: llm_call
    prompt: "Analyze if task is ATOMIC or COMPLEX: {{task}}"
    store: atomic_check

  # 2. 条件分支
  - id: process_task
    type: conditional
    condition: "{{atomic_check | contains: 'ATOMIC'}}"
    
    if_true:
      # 原子任务：投票解决
      - id: solve_atomic
        type: vote
        generator:
          type: llm_call
          prompt: "Solve: {{task}}"
        k: 2
        store: atomic_solution
    
    if_false:
      # 复杂任务：分解 → 递归 → 合成
      - id: decompose
        type: vote
        generator:
          type: llm_call
          prompt: "Break down: {{task}}"
          output: json_array
        k: 2
        store: subtasks
      
      # 并行递归 (真正的 Actor 并行)
      - id: execute_subtasks
        type: fan_out
        for_each: subtasks
        step:
          type: workflow_call
          workflow: maker-v2
          params:
            task: "{{item.description}}"
        store: subtask_results
      
      # 合成
      - id: compose
        type: vote
        generator:
          type: llm_call
          prompt: "Compose results: {{subtask_results}}"
        store: composed_solution

output:
  solution: "{{atomic_solution | default: composed_solution}}"
```

### UoT C-UoT v2 (`workflows/uot-combinational-v2.yaml`)

六步创意推理流程：

```yaml
steps:
  # Step 1: 类比检索
  - id: retrieve_analogies
    type: llm_call
    prompt: "Find analogous problems from different domains: {{problem}}"
    output: json_array
    store: analogies

  # Step 2: 并行分解思想 (Actor 并行)
  - id: decompose_thoughts
    type: fan_out
    for_each: analogies
    step:
      type: llm_call
      prompt: "Extract atomic thoughts from: {{item}}"
      output: json_array
    reduce: flatten
    store: all_thoughts

  # Step 3: 选择宿主
  - id: select_host
    type: llm_call
    prompt: "Select best host solution from: {{analogies}}"
    output: json
    store: host_selection

  # Step 4: 选择捐赠思想
  - id: select_donors
    type: llm_call
    prompt: "Select donor thoughts using Far-then-Analogical heuristic"
    output: json_array
    store: donor_selections

  # Step 5: 并行合成候选方案 (Actor 并行)
  - id: synthesize_candidates
    type: fan_out
    for_each: donor_selections
    step:
      type: llm_call
      prompt: "Synthesize solution with donor: {{item}}"
      output: json
    store: candidates

  # Step 6: 并行评估 (Actor 并行)
  - id: evaluate_candidates
    type: fan_out
    for_each: candidates
    step:
      type: llm_call
      prompt: "Evaluate: Feasibility/Utility/Novelty"
      output: json
    store: evaluations

  # Step 7: 选择最佳
  - id: select_best
    type: llm_call
    prompt: "Select best candidate based on evaluations"
    output: json
    store: best_selection

output:
  solution: "{{best_selection.best_solution}}"
  score: "{{best_selection.composite_score}}"
```

### 使用方法

```csharp
// 1. 创建 Coordinator
var coordinatorId = Guid.NewGuid();
var coordinator = await actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(coordinatorId);
var agent = coordinator.GetAgent() as CognitiveCoordinatorGAgent;

// 2. 设置 ActorManager 并创建 Worker 池
agent!.SetActorManager(actorManager);
await agent.CreateWorkerPoolAsync(poolSize: 5);

// 3. 加载并注册工作流
var parser = new WorkflowParser();
var makerWorkflow = parser.ParseFile("workflows/maker-v2.yaml");
var uotWorkflow = parser.ParseFile("workflows/uot-combinational-v2.yaml");
agent.RegisterWorkflow(makerWorkflow);
agent.RegisterWorkflow(uotWorkflow);

// 4. 执行 MAKER
await coordinator.PublishEventAsync(new StartWorkflowRequestEvent
{
    WorkflowName = "maker-v2",
    Variables = { ["task"] = Value.ForString("设计一个分布式缓存系统") }
});

// 5. 执行 UoT
await coordinator.PublishEventAsync(new StartWorkflowRequestEvent
{
    WorkflowName = "uot-combinational-v2",
    Variables = { ["problem"] = Value.ForString("如何解决城市交通拥堵?") }
});
```

## 🔗 相关资源

- [设计文档](../../cognitive-mesh/docs/COGNITIVE_AGENT_BASE_DESIGN.md)
- [路线图](../../cognitive-mesh/docs/ROADMAP.md)
