using Aevatar.Agents.Cognitive.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Step Events (UI/Observability)
//
//  WHY:
//  - 让“执行引擎”和“可视化事件”分离，避免巨型文件失控。
//  - 事件必须线程安全：vote streaming / fan_out 会并发触发回调。
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    /// <summary>
    /// 设置步骤事件回调（用于实时可视化）
    /// </summary>
    public void SetStepEventCallback(Action<WorkflowStepEvent> callback)
    {
        _onStepEvent = callback;
    }

    /// <summary>
    /// 获取所有步骤事件（用于回放）
    /// </summary>
    public IReadOnlyList<WorkflowStepEvent> GetStepEvents()
    {
        // NOTE:
        // - vote / fan_out 可能并发写 _stepEvents
        // - 直接暴露 List 会导致读取端枚举时抛异常或读到撕裂数据
        lock (_stepEventsLock)
        {
            return _stepEvents.ToList();
        }
    }

    private void EmitStepEvent(
        StepDefinition step,
        StepStatus status,
        string? message = null,
        float progress = 0,
        int voteRound = 0,
        int voteMaxRounds = 0,
        int voteK = 0,
        int voteCurrentVotes = 0,
        int parallelTotal = 0,
        int parallelCompleted = 0,
        int parallelFailed = 0,
        string? parentStepId = null,
        // LLM 对话记录
        string? systemPrompt = null,
        string? userPrompt = null,
        string? assistantResponse = null,
        // Red-Flag 信息
        string? redFlagReason = null)
    {
        var now = DateTime.UtcNow;
        var durationMs = 0;

        if (status == StepStatus.Running)
        {
            _stepStartTimes[step.Id] = now;
        }
        else if (_stepStartTimes.TryGetValue(step.Id, out var startTime))
        {
            durationMs = (int)(now - startTime).TotalMilliseconds;
        }

        var evt = new WorkflowStepEvent
        {
            RunId = CustomState.ExecutionId ?? "",
            WorkflowName = CustomState.WorkflowName ?? "",
            StepId = step.Id,
            StepType = step.Type,
            Status = status,
            Progress = progress,
            Message = message ?? GetDefaultMessage(step, status),
            Timestamp = Timestamp.FromDateTime(now),
            ParentStepId = parentStepId ?? "",
            Depth = CustomState.CurrentDepth,
            VoteRound = voteRound,
            VoteMaxRounds = voteMaxRounds,
            VoteK = voteK,
            VoteCurrentVotes = voteCurrentVotes,
            ParallelTotal = parallelTotal,
            ParallelCompleted = parallelCompleted,
            ParallelFailed = parallelFailed,
            DurationMs = durationMs,
            LlmCalls = CustomState.TotalLlmCalls,
            TokensUsed = CustomState.TotalTokensUsed,
            // LLM 对话记录
            SystemPrompt = systemPrompt ?? "",
            UserPrompt = userPrompt ?? "",
            AssistantResponse = assistantResponse ?? "",
            // Red-Flag 信息
            RedFlagReason = redFlagReason ?? ""
        };

        // 多任务并行（vote streaming）时，避免 List 并发写导致内存损坏/卡死
        lock (_stepEventsLock)
        {
            _stepEvents.Add(evt);
        }
        _onStepEvent?.Invoke(evt);

        Logger.LogDebug("[Workflow] Step {StepId} ({Type}): {Status} - {Message}",
            step.Id, step.Type, status, message);
    }

    private static string GetDefaultMessage(StepDefinition step, StepStatus status)
    {
        return status switch
        {
            StepStatus.Pending => $"Step '{step.Id}' pending",
            StepStatus.Running => $"Executing {step.Type}: {step.Id}",
            StepStatus.Completed => $"Step '{step.Id}' completed",
            StepStatus.Failed => $"Step '{step.Id}' failed",
            StepStatus.Skipped => $"Step '{step.Id}' skipped",
            _ => $"Step '{step.Id}' - {status}"
        };
    }
}

