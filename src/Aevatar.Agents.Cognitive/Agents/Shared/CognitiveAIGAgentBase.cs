using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveAIGAgentBase
//
//  WHY:
//  - CognitiveCoordinator/Worker 都需要：
//    1) 复用 AIGAgentBase.ChatAsync/ChatStreamAsync（避免手写 Provider 调用链）
//    2) State.History 仅用于 UI hydration（不参与下一次 LLM prompt 构建）
//    3) 每步写入稳定 metadata（step_id/step_type/...）以支持并行/重放/刷新恢复
//
//  NOTE:
//  - 这个基类只做“LLM 请求形态 + 历史落盘策略”的统一，不碰业务编排。
// ============================================================
public abstract class CognitiveAIGAgentBase<TCustomState> : AIGAgentBase<TCustomState>
    where TCustomState : class, IMessage<TCustomState>, new()
{
    // ============================================================
    //  History compaction policy (no extra LLM calls)
    //
    //  WHY:
    //  - AIGAgentBase 默认 compaction 可能调用 LLM 做 summary
    //  - Cognitive workflow 有严格预算；隐藏 LLM 调用不可接受
    // ============================================================
    protected override Task<string?> UpdateHistorySummaryAsync(
        string? existingSummary,
        IReadOnlyList<AevatarChatMessage> newlyArchivedMessages,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<string?>(null);
    }

    // ============================================================
    //  Tools policy
    //
    //  WHY:
    //  - Cognitive DSL 是 prompt-driven，Function Calling 会污染 prompt 语义
    // ============================================================
    protected override Task RegisterToolsAsync(CancellationToken cancellationToken = default)
    {
        // Do NOT register built-in tools for Cognitive agents by default.
        return Task.CompletedTask;
    }

    // ============================================================
    //  Prompt policy: keep requests stateless
    //
    //  WHY:
    //  - State.History 只用于 UI hydration（短期窗口），不能 replay 回 LLM
    //  - Cognitive prompt 已经包含工作流变量/上下文；replay history 只会放大 token + 噪音
    // ============================================================
    protected override AevatarLLMRequest BuildLLMRequest(ChatRequest request)
    {
        var settings = GetLLMSettings(request);
        if (request.StopSequences.Count > 0)
        {
            settings.StopSequences = new List<string>(request.StopSequences);
        }

        var messages = new List<AevatarChatMessage>
        {
            new()
            {
                Role = AevatarChatRole.User,
                Content = request.Message
            }
        };

        // Per-step system prompt override.
        var systemPrompt = GetEffectiveSystemPrompt() ?? string.Empty;
        if (request.Context.TryGetValue("system_prompt", out var overrideSp) &&
            !string.IsNullOrWhiteSpace(overrideSp))
        {
            systemPrompt = overrideSp.Trim();
        }

        var llmRequest = new AevatarLLMRequest
        {
            SystemPrompt = systemPrompt,
            Messages = messages,
            Settings = settings
        };

        if (!string.IsNullOrWhiteSpace(request.StageHint))
        {
            llmRequest.Context = new Dictionary<string, object>
            {
                ["stage_hint"] = request.StageHint
            };
        }

        return llmRequest;
    }

    // ============================================================
    //  Step-scoped history metadata injection
    // ============================================================

    private static readonly AsyncLocal<StepHistoryContext?> StepHistory = new();

    private sealed class StepHistoryContext
    {
        public Dictionary<string, string> Metadata { get; init; } = new();
        public string? SystemPrompt { get; init; }
        public bool SystemWritten { get; set; }
    }

    private sealed class StepHistoryScope : IDisposable
    {
        private readonly StepHistoryContext? _prev;

        public StepHistoryScope(StepHistoryContext ctx)
        {
            _prev = StepHistory.Value;
            StepHistory.Value = ctx;
        }

        public void Dispose()
        {
            StepHistory.Value = _prev;
        }
    }

    protected IDisposable BeginStepHistory(
        string stepId,
        string stepType,
        string? systemPrompt = null,
        string? requestId = null)
    {
        var meta = new Dictionary<string, string>(capacity: 8)
        {
            ["step_id"] = stepId ?? string.Empty,
            ["step_type"] = stepType ?? string.Empty,
            ["agent_kind"] = AgentKind
        };

        if (!string.IsNullOrWhiteSpace(requestId))
        {
            meta["request_id"] = requestId!;
        }

        AppendAgentHistoryMetadata(meta);

        return new StepHistoryScope(new StepHistoryContext
        {
            Metadata = meta,
            SystemPrompt = systemPrompt
        });
    }

    /// <summary>
    /// Agent kind identifier for history metadata (e.g. "cognitive_worker").
    /// </summary>
    protected abstract string AgentKind { get; }

    /// <summary>
    /// Allow derived classes to inject extra metadata (e.g. worker_id/execution_id).
    /// </summary>
    protected virtual void AppendAgentHistoryMetadata(Dictionary<string, string> metadata)
    {
    }

    protected override void AddMessageToHistory(string content, AevatarChatRole role, string? name = null)
    {
        var ctx = StepHistory.Value;
        if (ctx == null)
        {
            base.AddMessageToHistory(content, role, name);
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
            return;

        // Ensure per-step system prompt is captured once (before the user message).
        if (role == AevatarChatRole.User &&
            !ctx.SystemWritten &&
            !string.IsNullOrWhiteSpace(ctx.SystemPrompt))
        {
            base.AddMessageToHistory(BuildStepHistoryMessage(AevatarChatRole.System, ctx.SystemPrompt!, ctx.Metadata));
            ctx.SystemWritten = true;
        }

        base.AddMessageToHistory(BuildStepHistoryMessage(role, content, ctx.Metadata));
    }

    private static AevatarChatMessage BuildStepHistoryMessage(
        AevatarChatRole role,
        string content,
        Dictionary<string, string> metadata)
    {
        var msg = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        foreach (var (k, v) in metadata)
        {
            msg.Metadata[k] = v;
        }

        return msg;
    }
}

