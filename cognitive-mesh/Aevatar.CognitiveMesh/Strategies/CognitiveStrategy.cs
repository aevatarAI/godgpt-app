using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.CognitiveMesh.Abstractions;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.AI;

namespace Aevatar.CognitiveMesh.Strategies;

// ============================================================
//  COGNITIVE DSL STRATEGY
//  DSL 驱动的认知策略 - Coordinator + Worker 真正并行
// ============================================================

/// <summary>
/// Cognitive DSL 策略适配器。
/// 使用 YAML 定义的工作流，通过 CognitiveCoordinatorGAgent 执行。
/// 
/// 特点：
/// - DSL 定义工作流（YAML）
/// - Coordinator + Worker 真正的 Actor 并行
/// - 语义聚类投票（可选）
/// - 支持递归工作流调用
/// </summary>
public sealed class CognitiveStrategy : IReasoningStrategy
{
    private readonly IGAgentActorManager _actorManager;
    private readonly ILLMProviderFactory _llmFactory;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    private readonly ILogger<CognitiveStrategy> _logger;
    private readonly string _workflowsPath;
    
    // 工作流注册表
    private readonly InMemoryWorkflowRegistry _workflowRegistry = new();
    private bool _workflowsLoaded;

    public CognitiveStrategy(
        IGAgentActorManager actorManager,
        ILLMProviderFactory llmFactory,
        ILogger<CognitiveStrategy> logger,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        _actorManager = actorManager;
        _llmFactory = llmFactory;
        _embeddingGenerator = embeddingGenerator;
        _logger = logger;
        
        // 工作流文件路径查找（按优先级）
        var searchPaths = new[]
        {
            // 1. 执行目录下的 workflows
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workflows"),
            // 2. 当前目录下的 workflows
            Path.Combine(Directory.GetCurrentDirectory(), "workflows"),
            // 3. 源代码路径 (开发时)
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "src", "Aevatar.Agents.Cognitive", "workflows"),
            // 4. 相对于 cognitive-mesh 的源代码路径
            Path.Combine(Directory.GetCurrentDirectory(), "..", "src", "Aevatar.Agents.Cognitive", "workflows"),
            // 5. 绝对路径回退
            "/Users/zhaoyiqi/Code/aevatar-agent-framework/src/Aevatar.Agents.Cognitive/workflows"
        };
        
        _workflowsPath = searchPaths.FirstOrDefault(Directory.Exists) 
            ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "workflows");
        
        _logger.LogInformation("CognitiveStrategy initialized. Workflows path: {Path} (exists: {Exists})", 
            _workflowsPath, Directory.Exists(_workflowsPath));
    }

    public StrategyKind Kind => StrategyKind.Cognitive;
    public string DisplayName => "Cognitive DSL";
    public string Description => "DSL 定义工作流，Actor 真正并行";

    public ValidationResult ValidateOptions(ReasoningOptions options)
    {
        // Cognitive 策略需要指定工作流名称
        if (string.IsNullOrEmpty(options.CognitiveWorkflow))
        {
            return ValidationResult.Failed("CognitiveWorkflow", "Cognitive strategy requires a workflow name");
        }
        
        return ValidationResult.Success();
    }

    public async Task<ReasoningResult> ExecuteAsync(
        string task,
        ReasoningOptions options,
        IProgress<ReasoningProgress>? progress = null,
        CancellationToken ct = default)
    {
        var startTime = DateTime.UtcNow;
        
        try
        {
            // ─── 阶段 1：加载工作流 ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "LOADING",
                Message = "Loading workflows...",
                ProgressPercent = 0.05f
            });
            
            await EnsureWorkflowsLoadedAsync();
            
            var workflowName = options.CognitiveWorkflow ?? "maker-v2";
            var workflow = _workflowRegistry.Get(workflowName);
            
            if (workflow == null)
            {
                _logger.LogError("Workflow not found: {Name}", workflowName);
                return ReasoningResult.Failed(
                    $"Workflow '{workflowName}' not found. Available: {string.Join(", ", _workflowRegistry.List())}",
                    DateTime.UtcNow - startTime);
            }
            
            _logger.LogInformation("Executing workflow: {Name} v{Version}", workflow.Name, workflow.Version);
            
            // ─── 阶段 2：创建 Coordinator ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "INITIALIZING",
                Message = "Creating Coordinator and Workers...",
                ProgressPercent = 0.1f
            });
            
            var coordinatorId = Guid.NewGuid();
            var coordinatorActor = await _actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(coordinatorId, ct);
            var coordinator = coordinatorActor.GetAgent() as CognitiveCoordinatorGAgent;
            
            if (coordinator == null)
            {
                return ReasoningResult.Failed("Failed to create Coordinator agent", DateTime.UtcNow - startTime);
            }
            
            // 初始化 AI Agent（设置 LLM Provider）
            var providerName = options.ProviderName ?? AevatarAgentsConstants.DefaultProviderName;
            await coordinator.InitializeAsync(providerName, cancellationToken: ct);
            
            // 配置 Coordinator
            coordinator.SetActorManager(_actorManager);
            
            // 配置语义聚类投票（如果有）
            if (_embeddingGenerator != null)
            {
                coordinator.SetEmbeddingGenerator(_embeddingGenerator, options.CognitiveSemanticSimilarity ?? 0.85f);
            }
            
            // 设置步骤事件回调 - 转发给 progress reporter
            coordinator.SetStepEventCallback(stepEvent =>
            {
                progress?.Report(new ReasoningProgress
                {
                    Phase = $"STEP:{stepEvent.StepType.ToUpper()}",
                    Message = stepEvent.Message,
                    ProgressPercent = 0.2f + 0.7f * stepEvent.Progress,
                    StepId = stepEvent.StepId,
                    StepType = stepEvent.StepType,
                    StepStatus = stepEvent.Status.ToString(),
                    VoteRound = stepEvent.VoteRound,
                    VoteMaxRounds = stepEvent.VoteMaxRounds,
                    VoteK = stepEvent.VoteK,
                    VoteCurrentVotes = stepEvent.VoteCurrentVotes,
                    ParallelTotal = stepEvent.ParallelTotal,
                    ParallelCompleted = stepEvent.ParallelCompleted,
                    ParallelFailed = stepEvent.ParallelFailed,
                    // 传递 LLM 对话记录
                    SystemPrompt = stepEvent.SystemPrompt,
                    UserPrompt = stepEvent.UserPrompt,
                    AssistantResponse = stepEvent.AssistantResponse
                });
            });
            
            // 注册工作流
            foreach (var wf in _workflowRegistry.List())
            {
                var w = _workflowRegistry.Get(wf);
                if (w != null) coordinator.RegisterWorkflow(w);
            }
            
            // 创建 Worker 池
            var workerPoolSize = options.CognitiveWorkerCount > 0 ? options.CognitiveWorkerCount.Value : 5;
            await coordinator.CreateWorkerPoolAsync(workerPoolSize);
            
            _logger.LogInformation("Created Coordinator {Id} with {Workers} workers", coordinatorId, workerPoolSize);
            
            // ─── 阶段 3：执行工作流 ───
            progress?.Report(new ReasoningProgress
            {
                Phase = "EXECUTING",
                Message = $"Executing workflow: {workflowName}",
                ProgressPercent = 0.2f
            });
            
            // 设置完成回调
            var completionSource = new TaskCompletionSource<WorkflowCompletedEventProto>();
            
            // 订阅完成事件（通过检查状态）
            // 注意：由于是事件驱动，我们需要轮询或使用事件订阅
            
            // 构建初始变量
            var initialVariables = new Dictionary<string, object>
            {
                ["task"] = task
            };
            
            // 添加额外参数
            if (options.CognitiveConsensusK > 0)
            {
                initialVariables["k"] = options.CognitiveConsensusK.Value;
            }
            if (options.CognitiveMaxRounds > 0)
            {
                initialVariables["max_rounds"] = options.CognitiveMaxRounds.Value;
            }
            if (options.CognitiveMaxDepth > 0)
            {
                initialVariables["max_depth"] = options.CognitiveMaxDepth.Value;
            }
            
            // 直接调用 Coordinator 启动工作流（不通过事件流）
            _ = coordinator.StartWorkflowAsync(workflowName, initialVariables);
            
            // ─── 阶段 4：等待完成（轮询状态）───
            var result = await WaitForCompletionAsync(coordinator, progress, ct);
            
            // ─── 阶段 5：清理 ───
            await _actorManager.DeactivateAndUnregisterAsync(coordinatorId, ct);
            
            var duration = DateTime.UtcNow - startTime;
            var workflowResult = coordinator.GetResult();
            
            if (workflowResult.Success)
            {
                progress?.Report(new ReasoningProgress
                {
                    Phase = "COMPLETE",
                    Message = "Workflow completed successfully",
                    ProgressPercent = 1.0f,
                    TotalLlmCalls = workflowResult.TotalLlmCalls,
                    TotalPromptTokens = workflowResult.TotalTokens / 2, // 估算
                    TotalCompletionTokens = workflowResult.TotalTokens / 2
                });
                
                return ReasoningResult.Succeeded(
                    workflowResult.Output?.ToString() ?? "",
                    duration,
                    workflowResult.TotalLlmCalls,
                    workflowResult.TotalTokens / 2,
                    workflowResult.TotalTokens / 2);
            }
            else
            {
                return ReasoningResult.Failed(workflowResult.Error ?? "Workflow failed", duration);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Cognitive workflow cancelled");
            return ReasoningResult.Failed("Cancelled by user", DateTime.UtcNow - startTime);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cognitive workflow failed");
            return ReasoningResult.Failed(ex.Message, DateTime.UtcNow - startTime);
        }
    }
    
    // ============================================================
    //  辅助方法
    // ============================================================
    
    private Task EnsureWorkflowsLoadedAsync()
    {
        if (_workflowsLoaded) return Task.CompletedTask;
        
        lock (_workflowRegistry)
        {
            if (_workflowsLoaded) return Task.CompletedTask;
            
            if (Directory.Exists(_workflowsPath))
            {
                var parser = new WorkflowParser();
                var count = 0;
                
                foreach (var workflow in parser.ParseDirectory(_workflowsPath))
                {
                    _workflowRegistry.Register(workflow);
                    count++;
                    _logger.LogDebug("Loaded workflow: {Name}", workflow.Name);
                }
                
                _logger.LogInformation("Loaded {Count} workflows from {Path}", count, _workflowsPath);
            }
            else
            {
                _logger.LogWarning("Workflows directory not found: {Path}", _workflowsPath);
            }
            
            _workflowsLoaded = true;
        }
        
        return Task.CompletedTask;
    }
    
    private async Task<WorkflowResult> WaitForCompletionAsync(
        CognitiveCoordinatorGAgent coordinator,
        IProgress<ReasoningProgress>? progress,
        CancellationToken ct)
    {
        var pollInterval = TimeSpan.FromMilliseconds(500);
        var timeout = TimeSpan.FromMinutes(30);
        var elapsed = TimeSpan.Zero;
        
        while (!ct.IsCancellationRequested && elapsed < timeout)
        {
            await Task.Delay(pollInterval, ct);
            elapsed += pollInterval;
            
            // 检查状态
            var description = await coordinator.GetDescriptionAsync();
            
            // 报告进度
            progress?.Report(new ReasoningProgress
            {
                Phase = "RUNNING",
                Message = description,
                ProgressPercent = 0.2f + 0.7f * (float)(elapsed.TotalSeconds / timeout.TotalSeconds)
            });
            
            // 检查是否完成
            var result = coordinator.GetResult();
            if (result.Success || !string.IsNullOrEmpty(result.Error))
            {
                return result;
            }
        }
        
        // 超时
        return new WorkflowResult
        {
            Success = false,
            Error = "Workflow execution timed out"
        };
    }
    
    /// <summary>
    /// 获取可用工作流列表。
    /// </summary>
    public IReadOnlyList<string> GetAvailableWorkflows()
    {
        EnsureWorkflowsLoadedAsync().GetAwaiter().GetResult();
        return _workflowRegistry.List();
    }
}
