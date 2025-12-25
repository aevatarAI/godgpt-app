using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Utilities;
using Microsoft.Extensions.Logging;

using WorkflowDefinition = Aevatar.Agents.Cognitive.Primitives.WorkflowDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Workflow lifecycle
//
//  WHY:
//  - 把“启动/失败/输出构建/主循环”从巨型文件里拆出来。
//  - 让核心执行逻辑更容易被复用为 AevatarKit 的 Run Orchestrator。
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// 直接启动工作流执行（API 调用）
    /// </summary>
    public Task StartWorkflowAsync(string workflowName, Dictionary<string, object>? variables = null)
    {
        var request = new StartWorkflowRequestEvent { WorkflowName = workflowName };
        if (variables != null)
        {
            foreach (var (key, value) in variables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }
        }

        return HandleStartWorkflowRequest(request);
    }

    /// <summary>
    /// 启动工作流执行 (Protobuf 事件)
    /// </summary>
    [EventHandler]
    public async Task HandleStartWorkflowRequest(StartWorkflowRequestEvent request)
    {
        Logger.LogInformation("Coordinator {Id} starting workflow: {WorkflowName}",
            Id, request.WorkflowName);

        CustomState.ExecutionId = Guid.NewGuid().ToString("N")[..16];
        CustomState.WorkflowName = request.WorkflowName;
        CustomState.Status = ExecutionStatus.EsRunning;
        CustomState.CurrentPhase = "Starting";

        try
        {
            // 获取工作流定义
            var workflow = _workflowRegistry.Get(request.WorkflowName);
            if (workflow == null)
            {
                await FailExecutionAsync($"Workflow '{request.WorkflowName}' not found");
                return;
            }

            // 注入初始变量
            _workflowVariables.Clear();

            // 1. 先应用 inputs 的默认值
            foreach (var input in workflow.Inputs)
            {
                if (input.DefaultValue != null)
                {
                    _workflowVariables[input.Name] = input.DefaultValue;
                }
            }

            // 2. 再覆盖为传入的变量（传入的优先级更高）
            foreach (var (key, value) in request.Variables)
            {
                _workflowVariables[key] = ProtoValueConverter.FromProto(value);
            }

            // ============================================================
            //  输入驱动的 MaxDepth（消灭硬编码）
            //
            //  约定：
            //  - 统一使用 `max_depth` 作为工作流递归深度控制输入
            //  - 若未提供，则保留 OnActivateAsync 的默认值或 YAML input 默认值
            // ============================================================
            if (TryGetPositiveInt(_workflowVariables, "max_depth", out var inputMaxDepth))
            {
                // 安全阀：避免配置错误导致极端深度把系统打爆
                CustomState.MaxDepth = Math.Clamp(inputMaxDepth, 1, 200);
            }

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                var taskLen = _workflowVariables.GetValueOrDefault("task")?.ToString()?.Length ?? 0;
                Logger.LogDebug(
                    "[Workflow Start] Variables: [{Vars}], task length={TaskLen}",
                    string.Join(", ", _workflowVariables.Keys),
                    taskLen);
            }

            // 执行工作流
            await ExecuteWorkflowAsync(workflow);

            // 完成
            CustomState.Status = ExecutionStatus.EsCompleted;
            CustomState.CurrentPhase = "Completed";

            await PublishAsync(new WorkflowCompletedEventProto
            {
                ExecutionId = CustomState.ExecutionId,
                Success = true,
                Result = _workflowVariables.GetValueOrDefault("_output")?.ToString() ?? ""
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[WORKFLOW] Execution failed: {Message}", ex.Message);
            await FailExecutionAsync(ex.Message);
        }
    }

    private async Task ExecuteWorkflowAsync(WorkflowDefinition workflow)
    {
        Logger.LogInformation("[DEBUG][Workflow] Executing workflow '{Name}' with {Count} steps: [{Steps}]",
            workflow.Name, workflow.Steps.Count, string.Join(", ", workflow.Steps.Select(s => s.Id)));

        for (int stepIndex = 0; stepIndex < workflow.Steps.Count; stepIndex++)
        {
            var step = workflow.Steps[stepIndex];
            Logger.LogInformation("[DEBUG][Workflow] >>> Executing step {Index}/{Total}: '{StepId}' (type={Type})",
                stepIndex + 1, workflow.Steps.Count, step.Id, step.Type);

            CustomState.CurrentPhase = $"Step: {step.Id}";
            CustomState.CurrentStepId = step.Id;

            var result = await ExecuteStepAsync(step);

            if (!result.Success)
            {
                throw new Exception($"Step '{step.Id}' failed: {result.Error}");
            }

            // 存储结果
            if (!string.IsNullOrEmpty(step.Store))
            {
                Logger.LogInformation(
                    "[DEBUG][Workflow] Step '{StepId}' store='{Store}', result.Value is null: {IsNull}",
                    step.Id, step.Store, result.Value == null);

                if (result.Value != null)
                {
                    _workflowVariables[step.Store] = result.Value;
                    Logger.LogInformation("[DEBUG][Workflow] Stored '{Store}' type: {Type}",
                        step.Store, result.Value.GetType().FullName);
                }
                else
                {
                    Logger.LogWarning("[DEBUG][Workflow] ⚠️ Step '{StepId}' returned null, NOT storing to '{Store}'",
                        step.Id, step.Store);
                }
            }
        }

        // 构建输出
        _workflowVariables["_output"] = BuildOutput(workflow.Output);
    }

    private async Task FailExecutionAsync(string error)
    {
        CustomState.Status = ExecutionStatus.EsFailed;
        CustomState.CurrentPhase = "Failed";
        CustomState.Error = error;

        // IMPORTANT:
        // - 之前这里不打日志，导致“后端没有报错但系统停了”的错觉
        // - 失败必须在日志里可见（至少包含 executionId / 当前 step）
        Logger.LogError("[WORKFLOW] Failed (executionId={ExecutionId}, step={StepId}, phase={Phase}): {Error}",
            CustomState.ExecutionId, CustomState.CurrentStepId, CustomState.CurrentPhase, error);

        await PublishAsync(new WorkflowCompletedEventProto
        {
            ExecutionId = CustomState.ExecutionId,
            Success = false,
            Error = error
        });
    }

    private object BuildOutput(Dictionary<string, string> outputDef)
    {
        if (outputDef.Count == 0)
            return _workflowVariables;

        var output = new Dictionary<string, object?>();
        foreach (var (key, template) in outputDef)
        {
            output[key] = _templateEngine.ResolveValue(template, _workflowVariables);
        }

        return output;
    }
}

