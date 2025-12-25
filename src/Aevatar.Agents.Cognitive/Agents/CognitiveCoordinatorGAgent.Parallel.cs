using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Utilities;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Parallel execution (fan_out / parallel)
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// 处理 Worker 完成事件 (Protobuf 事件)
    /// </summary>
    [EventHandler]
    public Task HandleStepCompletedEvent(StepCompletedEventProto evt)
    {
        Logger.LogDebug("Coordinator received step completed: {StepId} from {WorkerId}",
            evt.StepId, evt.WorkerId);

        // fan_out completion semantics:
        // - streaming 中间态：Success=false && Error=""（不计入完成）
        // - 终态完成：Success=true 或 Success=false 且 Error!= ""（失败也算“完成”，否则 fan_out 会无意义卡住）
        var isStreaming = !evt.Success && string.IsNullOrEmpty(evt.Error);
        var isTerminal = evt.Success || (!evt.Success && !string.IsNullOrEmpty(evt.Error));

        // 更新统计（只计入终态事件，避免 streaming 中间态反复累计导致爆炸）
        if (isTerminal)
        {
            lock (_statsLock)
            {
                CustomState.TotalTokensUsed += evt.TokensUsed;
                CustomState.TotalLlmCalls += evt.LlmCalls;
            }
        }

        // 收集结果
        _collectedResults[evt.RequestId] = evt;

        _fanOutChildTypes.TryGetValue(evt.RequestId, out var childType);
        if (isTerminal)
        {
            _fanOutChildTypes.TryRemove(evt.RequestId, out _);
            // prompts are only for UI; safe to release after terminal
            _fanOutUserPrompts.TryRemove(evt.RequestId, out _);
            _fanOutSystemPrompts.TryRemove(evt.RequestId, out _);
        }

        // 子任务完成事件（用于前端并行可视化）
        if (!string.IsNullOrEmpty(evt.StepId) && !string.IsNullOrEmpty(childType))
        {
            var childDef = new StepDefinition
            {
                Id = evt.StepId,
                Type = childType
            };

            // UI semantics:
            // - streaming: Running
            // - terminal fail: Failed
            // - terminal success: Completed
            var status = evt.Success ? StepStatus.Completed : (isStreaming ? StepStatus.Running : StepStatus.Failed);
            var userPrompt = _fanOutUserPrompts.TryGetValue(evt.RequestId, out var up) ? up : "";
            var systemPrompt = _fanOutSystemPrompts.TryGetValue(evt.RequestId, out var sp) ? sp : "";
            EmitStepEvent(childDef,
                status,
                evt.Success
                    ? "Subtask completed"
                    : (string.IsNullOrEmpty(evt.Error) ? "Subtask streaming" : $"Subtask failed: {evt.Error}"),
                progress: evt.Success ? 1.0f : 0.0f,
                parentStepId: _currentFanOutStep?.Id,
                assistantResponse: evt.Result,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);
        }

        // 发送并行进度事件
        if (_currentFanOutStep != null)
        {
            var succeeded = _collectedResults.Values.Count(r => r.Success);
            var failed = _collectedResults.Values.Count(r => !r.Success && !string.IsNullOrEmpty(r.Error));
            var terminal = _collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error));

            EmitStepEvent(_currentFanOutStep, StepStatus.Running,
                $"Progress: {terminal}/{_expectedResults} (ok: {succeeded}, failed: {failed})",
                progress: (float)terminal / _expectedResults,
                parallelTotal: _expectedResults,
                // parallelCompleted 表示“终态完成数”（成功+失败），避免失败导致永远不满格
                parallelCompleted: terminal,
                parallelFailed: failed);
        }

        // 检查是否所有结果已收集
        // 终态完成（成功或失败）都算“收集完毕”
        if (_collectedResults.Values.Count(r => r.Success || !string.IsNullOrEmpty(r.Error)) >= _expectedResults)
        {
            _currentFanOutStep = null; // 清除当前 fan-out 步骤
            _fanOutCompletionSource?.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    // ============================================================
    //  并行步骤 - 分发给 Workers (真正的 Actor 并行)
    // ============================================================

    private async Task<PrimitiveResult> ExecuteFanOutAsync(StepDefinition step)
    {
        // 获取迭代列表
        var forEachVar = step.ForEach ?? "";
        if (!_workflowVariables.TryGetValue(forEachVar, out var itemsObj))
        {
            return PrimitiveResult.Fail($"Variable '{forEachVar}' not found");
        }

        var items = ConvertToList(itemsObj);
        if (items.Count == 0)
        {
            return PrimitiveResult.Ok(new List<object>());
        }

        var childStep = step.Step;
        if (childStep == null)
        {
            return PrimitiveResult.Fail("fan_out requires 'step' definition");
        }

        // workflow_call 类型需要 Coordinator 自己处理（递归），不能分发给 Worker
        // Worker 只支持 llm_call
        var isWorkflowCall = childStep.Type == "workflow_call";

        // 检查是否有 Workers（只有 llm_call 才分发）
        if (_workerIds.Count == 0 || isWorkflowCall)
        {
            if (isWorkflowCall)
            {
                Logger.LogInformation("Fan-out contains workflow_call, executing in Coordinator (parallel Tasks)");
            }
            else
            {
                Logger.LogWarning("No workers available, falling back to sequential execution");
            }

            return await ExecuteFanOutSequentialAsync(step, items, childStep);
        }

        Logger.LogInformation("Fan-out executing {Count} items across {Workers} workers",
            items.Count, _workerIds.Count);

        // 准备收集结果
        _collectedResults.Clear();
        _expectedResults = items.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();
        _currentFanOutStep = step;
        _fanOutChildTypes.Clear();
        _fanOutUserPrompts.Clear();
        _fanOutSystemPrompts.Clear();
        _fanOutOutputTypes.Clear();

        // 发送 fan-out 开始事件
        EmitStepEvent(step, StepStatus.Running,
            $"Distributing {items.Count} tasks to {_workerIds.Count} workers",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        // 分发任务给 Workers（真正的 Actor 并行）
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var targetWorker = _workerIds[i % _workerIds.Count];

            // 构建子上下文
            var childVariables = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i,
                // Ensure exactly-one worker executes this task (fan_out uses Down broadcast).
                // Worker will ignore if not matching its own Id.
                ["__target_worker"] = targetWorker
            };

            // 创建 Protobuf 请求事件
            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-{step.Id}-{i}",
                StepId = $"{step.Id}[{i}]",
                StepType = childStep.Type
            };

            // 转换参数和变量为 Protobuf
            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in childVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }

            _fanOutChildTypes[request.RequestId] = childStep.Type;

            // Keep the declared output type so Coordinator can parse worker raw results deterministically.
            // NOTE: We intentionally do NOT remove this mapping on terminal events, because ExecuteFanOutAsync
            //       needs it after fan_out completes to parse and reduce results.
            _fanOutOutputTypes[request.RequestId] =
                childStep.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

            // 预渲染子任务的 prompt，后续用于前端展示
            var renderedPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var renderedSystem = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            renderedPrompt = _templateEngine.Render(renderedPrompt, childVariables);
            if (renderedSystem != null)
            {
                renderedSystem = _templateEngine.Render(renderedSystem, childVariables);
            }

            _fanOutUserPrompts[request.RequestId] = renderedPrompt;
            if (renderedSystem != null)
            {
                _fanOutSystemPrompts[request.RequestId] = renderedSystem;
            }

            // 为子任务发送开始事件（用于前端并行可视化）
            var childDef = new StepDefinition
            {
                Id = request.StepId,
                Type = childStep.Type
            };
            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: renderedSystem,
                userPrompt: renderedPrompt);

            // 发送给 Workers（向下广播，所有 Children 都会收到）
            // Workers 根据 RequestId 判断是否处理
            await PublishAsync(request, EventDirection.Down);
        }

        // 等待所有 Worker 完成（事件驱动，非阻塞等待）
        var timeoutSeconds = ResolveIntParameter(step.Parameters, "timeout_seconds", 600);
        timeoutSeconds = Math.Clamp(timeoutSeconds, 5, 3600);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);
        var completed = await Task.WhenAny(
            _fanOutCompletionSource.Task,
            Task.Delay(timeout));

        if (completed != _fanOutCompletionSource.Task)
        {
            return PrimitiveResult.Fail($"Fan-out timed out after {timeout}");
        }

        // 收集并解析结果：
        // - Worker returns RAW assistant response in StepCompletedEventProto.Result (string)
        // - Coordinator parses it according to the declared output type (json/json_array/first_line/...)
        // - Optionally keep failures as items (include_failures=true) to enable deterministic aggregation
        var includeFailures = ResolveBoolParameter(step.Parameters, "include_failures", false);

        var results = new List<object>();
        foreach (var r in _collectedResults.Values.OrderBy(r => r.StepId))
        {
            // Determine output type (default: text)
            var outputType = _fanOutOutputTypes.TryGetValue(r.RequestId, out var ot) ? ot : "text";
            outputType = string.IsNullOrWhiteSpace(outputType) ? "text" : outputType;

            if (r.Success)
            {
                object? parsed;
                if (string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    // Preserve legacy behavior for text fan_out (no trimming/parsing side effects).
                    parsed = r.Result;
                }
                else
                {
                    parsed = ParseOutput(r.Result ?? "", outputType);
                }

                if (parsed != null)
                {
                    results.Add(parsed);
                }
                else if (includeFailures)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["worker_id"] = r.WorkerId,
                        ["step_id"] = r.StepId,
                        ["success"] = false,
                        ["error"] = "redflag-parse-null"
                    });
                }
            }
            else if (includeFailures)
            {
                results.Add(new Dictionary<string, object>
                {
                    ["worker_id"] = r.WorkerId,
                    ["step_id"] = r.StepId,
                    ["success"] = false,
                    ["error"] = r.Error
                });
            }
        }

        // 汇聚
        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);

        var totalTokens = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls = _collectedResults.Values.Sum(r => r.LlmCalls);

        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }

    /// <summary>
    /// Coordinator 内部串行执行（用于 workflow_call 或无 Worker 场景）
    /// 
    /// ⚠️ Orleans 兼容：
    /// - Grain 是 turn-based 单线程，不能用 Task.Run
    /// - 状态修改必须在 Grain 线程内
    /// - 真正的并行需要分发给 Worker Grains
    /// </summary>
    private async Task<PrimitiveResult> ExecuteFanOutSequentialAsync(
        StepDefinition step, List<object> items, StepDefinition childStep)
    {
        // 发送 fan-out 开始事件
        EmitStepEvent(step, StepStatus.Running,
            $"Executing {items.Count} subtasks (sequential in Coordinator)",
            progress: 0,
            parallelTotal: items.Count, parallelCompleted: 0);

        var includeFailures = ResolveBoolParameter(step.Parameters, "include_failures", false);
        var results = new List<object>();
        var totalTokens = 0;
        var totalCalls = 0;

        // 串行执行每个子任务（Orleans 兼容）
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];

            // 为子任务发送开始事件
            var childDef = new StepDefinition { Id = $"{step.Id}[{i}]", Type = childStep.Type };

            // 预渲染 prompt（使用临时变量）
            var tempVars = new Dictionary<string, object>(_workflowVariables)
            {
                ["item"] = item,
                ["index"] = i
            };
            var userPrompt = childStep.Parameters.GetValueOrDefault("prompt")?.ToString();
            var systemPrompt = childStep.Parameters.GetValueOrDefault("system")?.ToString();
            if (userPrompt != null) userPrompt = _templateEngine.Render(userPrompt, tempVars);
            if (systemPrompt != null) systemPrompt = _templateEngine.Render(systemPrompt, tempVars);

            EmitStepEvent(childDef, StepStatus.Running,
                $"Executing subtask {i + 1}/{items.Count}",
                parentStepId: step.Id,
                systemPrompt: systemPrompt,
                userPrompt: userPrompt);

            // 临时设置变量
            _workflowVariables["item"] = item;
            _workflowVariables["index"] = i;

            try
            {
                var result = await ExecuteStepAsync(childStep);

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // 发送子任务完成事件
                EmitStepEvent(childDef, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Subtask {i + 1} completed" : $"Subtask {i + 1} failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    assistantResponse: result.AssistantResponse);

                // DEBUG: 检查子任务结果
                Logger.LogInformation(
                    "[DEBUG][FanOut] Subtask {Index} result: Success={Success}, Value is null={IsNull}, Value type={Type}",
                    i, result.Success, result.Value == null, result.Value?.GetType().FullName ?? "null");

                if (result.Success && result.Value != null)
                {
                    results.Add(result.Value);
                    Logger.LogInformation("[DEBUG][FanOut] Added subtask {Index} result to collection, total: {Count}",
                        i, results.Count);
                }
                else if (result.Success && result.Value == null)
                {
                    Logger.LogWarning(
                        "[DEBUG][FanOut] ⚠️ Subtask {Index} succeeded but Value is null, NOT added to results!", i);
                    if (includeFailures)
                    {
                        results.Add(new Dictionary<string, object>
                        {
                            ["worker_id"] = "coordinator",
                            ["step_id"] = childDef.Id,
                            ["success"] = false,
                            ["error"] = "redflag-parse-null"
                        });
                    }
                }
                else if (!result.Success && includeFailures)
                {
                    results.Add(new Dictionary<string, object>
                    {
                        ["worker_id"] = "coordinator",
                        ["step_id"] = childDef.Id,
                        ["success"] = false,
                        ["error"] = result.Error ?? ""
                    });
                }

                // 发送进度事件
                EmitStepEvent(step, StepStatus.Running,
                    $"Progress: {i + 1}/{items.Count} subtasks completed",
                    progress: (float)(i + 1) / items.Count,
                    parallelTotal: items.Count, parallelCompleted: i + 1);
            }
            finally
            {
                // 清理临时变量
                _workflowVariables.Remove("item");
                _workflowVariables.Remove("index");
            }
        }

        // 发送 fan-out 完成事件
        EmitStepEvent(step, StepStatus.Completed,
            $"All {items.Count} subtasks completed",
            progress: 1,
            parallelTotal: items.Count, parallelCompleted: results.Count);

        var reducer = step.Reduce ?? "collect";
        var reduced = ApplyReducer(results, reducer);

        return PrimitiveResult.Ok(reduced, totalTokens, totalCalls);
    }

    private async Task<PrimitiveResult> ExecuteParallelAsync(StepDefinition step)
    {
        var steps = step.Parameters.GetValueOrDefault("steps") as List<StepDefinition>;
        if (steps == null || steps.Count == 0)
        {
            return PrimitiveResult.Ok(new Dictionary<string, object?>());
        }

        // 如果没有 Workers，顺序执行
        if (_workerIds.Count == 0)
        {
            var outputs = new Dictionary<string, object?>();
            var totalTokens = 0;
            var totalCalls = 0;

            foreach (var childStep in steps)
            {
                var result = await ExecuteStepAsync(childStep);
                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                if (!string.IsNullOrEmpty(childStep.Store) && result.Value != null)
                {
                    outputs[childStep.Store] = result.Value;
                    _workflowVariables[childStep.Store] = result.Value;
                }
            }

            return PrimitiveResult.Ok(outputs, totalTokens, totalCalls);
        }

        // 有 Workers 时并行执行
        _collectedResults.Clear();
        _expectedResults = steps.Count;
        _fanOutCompletionSource = new TaskCompletionSource<bool>();

        for (int i = 0; i < steps.Count; i++)
        {
            var childStep = steps[i];

            var request = new ExecuteStepRequestEvent
            {
                RequestId = $"{CustomState.ExecutionId}-parallel-{i}",
                StepId = childStep.Id,
                StepType = childStep.Type
            };

            foreach (var (key, value) in childStep.Parameters)
            {
                request.Parameters[key] = ProtoValueConverter.ToProto(value);
            }

            foreach (var (key, value) in _workflowVariables)
            {
                request.Variables[key] = ProtoValueConverter.ToProto(value);
            }

            await PublishAsync(request, EventDirection.Down);
        }

        // 等待完成
        await _fanOutCompletionSource.Task;

        // 汇总结果
        var outputs2 = new Dictionary<string, object?>();
        foreach (var result in _collectedResults.Values.Where(r => r.Success))
        {
            var originalStep = steps.FirstOrDefault(s => s.Id == result.StepId);
            if (originalStep?.Store != null)
            {
                var outputType = originalStep.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";
                var strictParse = ResolveBoolParameter(originalStep.Parameters, "strict_parse", true);

                object? parsed;
                if (string.Equals(outputType, "text", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = result.Result;
                }
                else
                {
                    parsed = ParseOutput(result.Result ?? "", outputType);
                    if (parsed == null && !strictParse)
                    {
                        parsed = result.Result;
                    }
                }

                if (parsed != null)
                {
                    outputs2[originalStep.Store] = parsed;
                    _workflowVariables[originalStep.Store] = parsed;
                }
            }
        }

        var totalTokens2 = _collectedResults.Values.Sum(r => r.TokensUsed);
        var totalCalls2 = _collectedResults.Values.Sum(r => r.LlmCalls);

        return PrimitiveResult.Ok(outputs2, totalTokens2, totalCalls2);
    }
}

