using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Cognitive.Agents;
using Aevatar.AxiomReasoning.Models;
using Aevatar.AxiomReasoning.Services;

namespace Aevatar.AxiomReasoning.AgUi;

// ============================================================
//  AG-UI BOOTSTRAP (Snapshots for reconnect)
//
//  WHY:
//  - SSE reconnect / new subscriber should not rely on replaying hundreds of progress/token events.
//  - Instead, provide deterministic snapshots:
//    - MESSAGES_SNAPSHOT: user input + last N completed assistant outputs
//    - STATE_SNAPSHOT: dependency graph (best-effort) from GraphStore
//
//  NOTE:
//  - Source of truth for UI hydration:
//    - Short-term: AIGAgentBase.State.History (bounded window; persisted via StateStore if configured)
//    - Long-term: IAevatarAIMemory (optional; MongoDB-backed in this repo)
//  - We do NOT rely on transcript.jsonl as a primary source (it's a debug artifact).
// ============================================================

public static class AxiomAgUiBootstrap
{
    public sealed record AgUiBootstrapMessages
    {
        public required List<AgUiMessage> Messages { get; init; }
        public List<AgUiEvent> ExtraEvents { get; init; } = [];
    }

    public static async Task<AgUiBootstrapMessages> BuildMessagesSnapshotAsync(
        AxiomSession session,
        IGAgentActorManager actorManager,
        IAevatarAIMemoryFactory? memoryFactory,
        int maxAssistantMessages,
        CancellationToken ct)
    {
        var messages = new List<AgUiMessage>(capacity: Math.Max(8, maxAssistantMessages + 1));
        var extra = new List<AgUiEvent>(capacity: Math.Max(8, maxAssistantMessages));

        // Always include user input as the first message.
        messages.Add(new AgUiMessage
        {
            Id = $"msg:{session.Id}:user:input",
            Role = "user",
            Content = BuildUserInputMessage(session)
        });

        if (maxAssistantMessages <= 0)
        {
            return new AgUiBootstrapMessages { Messages = messages, ExtraEvents = extra };
        }

        // ============================================================
        //  Collect per-step conversation from agents
        //
        //  - Coordinator/Workers ids are deterministic: derived from session_id
        //  - We read State.History via RPC (GetState) so this works across runtimes
        //  - AIMemory (optional) is used as long-term append-only log
        // ============================================================

        var threadId = session.Id;
        var workerCount = ComputeWorkerCount(session);

        var stableSessionKey = session.Id;
        var coordinatorRawId = DeterministicGuid
            .FromString($"cognitive:{stableSessionKey}:coordinator")
            .ToString("D");

        var stableWorkerRawIds = new List<string>(capacity: workerCount);
        for (var i = 0; i < workerCount; i++)
        {
            stableWorkerRawIds.Add(
                DeterministicGuid
                    .FromString($"cognitive:{stableSessionKey}:worker:{i}")
                    .ToString("D"));
        }

        var steps = new Dictionary<string, StepConversation>(StringComparer.Ordinal);

        // Coordinator (also contains vote proposal LLM calls that map to logical workers)
        await TryCollectFromActorAsync(
            actorManager,
            memoryFactory,
            coordinatorRawId,
            agentKind: "cognitive_coordinator",
            workerCount,
            steps,
            ct);

        // Workers (fan_out)
        for (var i = 0; i < stableWorkerRawIds.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await TryCollectFromActorAsync(
                actorManager,
                memoryFactory,
                stableWorkerRawIds[i],
                agentKind: "cognitive_worker",
                workerCount,
                steps,
                ct);
        }

        // Sort by timestamp, take tail, then emit in chronological order
        var ordered = steps.Values
            .Where(s => !string.IsNullOrWhiteSpace(s.AssistantResponse))
            .OrderBy(s => s.Timestamp)
            .ToList();

        if (ordered.Count > maxAssistantMessages)
        {
            ordered = ordered.Skip(Math.Max(0, ordered.Count - maxAssistantMessages)).ToList();
        }

        var emitted = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in ordered)
        {
            ct.ThrowIfCancellationRequested();

            var workerId = string.IsNullOrWhiteSpace(s.WorkerId) ? "coordinator" : s.WorkerId.Trim();
            var stepId = string.IsNullOrWhiteSpace(s.StepId) ? "main" : s.StepId.Trim();

            var messageId = $"msg:{threadId}:{workerId}:{stepId}";
            if (!emitted.Add(messageId))
                continue;

            messages.Add(new AgUiMessage
            {
                Id = messageId,
                Role = "assistant",
                Content = s.AssistantResponse ?? ""
            });

            // Emit per-message meta so Workers UI can restore system/user prompts on refresh.
            extra.Add(new CustomEvent
            {
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Name = "aevatar.axiom.message_meta",
                Value = new
                {
                    messageId,
                    workerId,
                    stepId = s.StepId,
                    stepType = s.StepType,
                    systemPrompt = s.SystemPrompt,
                    userPrompt = s.UserPrompt
                }
            });
        }

        return new AgUiBootstrapMessages { Messages = messages, ExtraEvents = extra };
    }

    public static async Task<GraphEvent?> TryBuildGraphSnapshotAsync(
        IGraphStore graphStore,
        string sessionId,
        CancellationToken ct)
    {
        try
        {
            var snap = await graphStore.GetSnapshotAsync(sessionId, ct);
            if ((snap.Nodes is null || snap.Nodes.Count == 0) && (snap.Edges is null || snap.Edges.Count == 0))
                return null;

            // Build edges lookup: to -> [from]
            var deps = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var e in snap.Edges ?? [])
            {
                if (string.IsNullOrWhiteSpace(e.ToId) || string.IsNullOrWhiteSpace(e.FromId)) continue;
                if (!deps.TryGetValue(e.ToId, out var list))
                {
                    list = [];
                    deps[e.ToId] = list;
                }
                list.Add(e.FromId);
            }

            var axioms = (snap.Nodes ?? [])
                .Where(n => n.Kind == DagNodeKind.Axiom)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => n.Label ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            var assumptions = (snap.Nodes ?? [])
                .Where(n => n.Kind == DagNodeKind.Assumption)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n => new AssumptionNode
                {
                    Id = n.Id ?? "",
                    Statement = n.Label ?? "",
                    Motivation = n.Proof ?? ""
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToList();

            var theorems = (snap.Nodes ?? [])
                .Where(n => n.Kind == DagNodeKind.Theorem)
                .OrderBy(n => n.Id, StringComparer.Ordinal)
                .Select(n =>
                {
                    deps.TryGetValue(n.Id, out var d);
                    var dependsOn = d is null
                        ? []
                        : d.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
                    return new TheoremNode
                    {
                        Id = n.Id ?? "",
                        Statement = n.Label ?? "",
                        Proof = n.Proof ?? "",
                        DependsOn = dependsOn
                    };
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                .ToList();

            var iteration = InferIteration(theorems);

            return new GraphEvent
            {
                SessionId = sessionId,
                Timestamp = DateTimeOffset.UtcNow,
                Iteration = iteration,
                Axioms = axioms,
                Assumptions = assumptions,
                Theorems = theorems
            };
        }
        catch
        {
            return null;
        }
    }

    private static int InferIteration(List<TheoremNode> theorems)
    {
        var max = 0;
        foreach (var t in theorems)
        {
            var id = (t.Id ?? "").Trim();
            if (id.Length < 2) continue;
            if (!id.StartsWith('T')) continue;
            if (!int.TryParse(id[1..], out var n)) continue;
            if (n > max) max = n;
        }

        return max > 0 ? max : theorems.Count;
    }

    private static string BuildUserInputMessage(AxiomSession session)
    {
        var axioms = string.IsNullOrWhiteSpace(session.AxiomsText) ? "(empty)" : session.AxiomsText.Trim();
        var goal = string.IsNullOrWhiteSpace(session.Goal) ? "(none)" : session.Goal.Trim();

        return $"""
               AXIOMS:
               {axioms}

               GOAL / FOCUS:
               {goal}
               """;
    }

    private sealed class StepConversation
    {
        public string StepId { get; init; } = "";
        public string StepType { get; set; } = "";
        public string WorkerId { get; init; } = "";
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
        public string? SystemPrompt { get; set; }
        public string? UserPrompt { get; set; }
        public string? AssistantResponse { get; set; }
    }

    private static int ComputeWorkerCount(AxiomSession session)
    {
        // Must match AxiomReasoningService.BuildReasoningOptions()
        var k = Math.Clamp(session.K, 1, 9);
        return Math.Clamp(2 * k - 1, 1, 15);
    }

    private static async Task TryCollectFromActorAsync(
        IGAgentActorManager actorManager,
        IAevatarAIMemoryFactory? memoryFactory,
        string actorRawId,
        string agentKind,
        int workerCount,
        Dictionary<string, StepConversation> steps,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(actorRawId))
        {
            return;
        }

        actorRawId = actorRawId.Trim();

        // ActorManager stores actors by Actor.Id.
        // - Orleans: "AgentTypeShortName:RawId"
        // - Local/Proto: "RawId"
        // We try both formats to keep this collector runtime-agnostic.
        var actorTypeName = agentKind == "cognitive_coordinator"
            ? typeof(CognitiveCoordinatorGAgent).Name
            : typeof(CognitiveWorkerGAgent).Name;
        var orleansStyleId = $"{actorTypeName}:{actorRawId}";

        IGAgentActor? actor = await actorManager.GetActorAsync(orleansStyleId);
        actor ??= await actorManager.GetActorAsync(actorRawId);
        if (actor == null)
        {
            // Best-effort: re-create actor so it can load persisted state (if StateStore is configured).
            try
            {
                actor = agentKind == "cognitive_coordinator"
                    ? await actorManager.CreateAndRegisterAsync<CognitiveCoordinatorGAgent>(actorRawId, ct)
                    : await actorManager.CreateAndRegisterAsync<CognitiveWorkerGAgent>(actorRawId, ct);
            }
            catch
            {
                return;
            }
        }

        // 1) Short-term: State.History
        try
        {
            var state = await actor.InvokeAsync<AevatarAIAgentState>("GetState");
            MergeFromStateHistory(state, agentKind, workerCount, steps);
        }
        catch
        {
            // ignored (best-effort)
        }

        // 2) Long-term: AIMemory (optional)
        if (memoryFactory != null)
        {
            try
            {
                var memory = memoryFactory.Create(actor.Id);
                var history = await memory.GetHistoryAsync(limit: 800, cancellationToken: ct);
                MergeFromMemoryHistory(history, workerCount, steps);
            }
            catch
            {
                // ignored (best-effort)
            }
        }
    }

    private static void MergeFromStateHistory(
        AevatarAIAgentState state,
        string agentKind,
        int workerCount,
        Dictionary<string, StepConversation> steps)
    {
        if (state?.History == null || state.History.Count == 0)
            return;

        foreach (var m in state.History)
        {
            if (m == null) continue;

            var stepId = TryGetMeta(m, "step_id");
            if (string.IsNullOrWhiteSpace(stepId)) continue;

            var stepType = TryGetMeta(m, "step_type") ?? "llm_call";
            var workerId = NormalizeWorkerId(stepId, workerCount);

            var key = $"{workerId}|{stepId}";
            if (!steps.TryGetValue(key, out var s))
            {
                s = new StepConversation
                {
                    StepId = stepId,
                    StepType = stepType,
                    WorkerId = workerId
                };
                steps[key] = s;
            }

            if (!string.IsNullOrWhiteSpace(stepType) && string.IsNullOrWhiteSpace(s.StepType))
                s.StepType = stepType;

            var ts = m.Timestamp?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow;
            if (ts > s.Timestamp) s.Timestamp = ts;

            var content = (m.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(content)) continue;

            switch (m.Role)
            {
                case AevatarChatRole.System:
                    s.SystemPrompt ??= content;
                    break;
                case AevatarChatRole.User:
                    s.UserPrompt ??= content;
                    break;
                case AevatarChatRole.Assistant:
                    s.AssistantResponse ??= content;
                    break;
            }
        }
    }

    private static void MergeFromMemoryHistory(
        IReadOnlyList<AevatarConversationEntry> history,
        int workerCount,
        Dictionary<string, StepConversation> steps)
    {
        if (history == null || history.Count == 0)
            return;

        foreach (var e in history)
        {
            var raw = (e.Content ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (!raw.StartsWith('{')) continue;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;

                if (!root.TryGetProperty("kind", out var kindEl) || kindEl.ValueKind != JsonValueKind.String)
                    continue;
                if (!string.Equals(kindEl.GetString(), "aevatar.cognitive.llm_interaction.v1", StringComparison.Ordinal))
                    continue;

                var stepId = root.TryGetProperty("stepId", out var sid) && sid.ValueKind == JsonValueKind.String
                    ? sid.GetString() ?? ""
                    : "";
                if (string.IsNullOrWhiteSpace(stepId)) continue;

                var stepType = root.TryGetProperty("stepType", out var st) && st.ValueKind == JsonValueKind.String
                    ? st.GetString() ?? "llm_call"
                    : "llm_call";

                var systemPrompt = root.TryGetProperty("systemPrompt", out var sp) && sp.ValueKind == JsonValueKind.String
                    ? sp.GetString()
                    : null;
                var userPrompt = root.TryGetProperty("userPrompt", out var up) && up.ValueKind == JsonValueKind.String
                    ? up.GetString()
                    : null;
                var assistantResponse = root.TryGetProperty("assistantResponse", out var ar) && ar.ValueKind == JsonValueKind.String
                    ? ar.GetString()
                    : null;

                var workerId = NormalizeWorkerId(stepId, workerCount);
                var key = $"{workerId}|{stepId}";
                if (!steps.TryGetValue(key, out var s))
                {
                    s = new StepConversation
                    {
                        StepId = stepId,
                        StepType = stepType,
                        WorkerId = workerId
                    };
                    steps[key] = s;
                }

                s.SystemPrompt ??= systemPrompt;
                s.UserPrompt ??= userPrompt;
                s.AssistantResponse ??= assistantResponse;
            }
            catch
            {
                // ignore malformed entries
            }
        }
    }

    private static string? TryGetMeta(AevatarChatMessage msg, string key)
    {
        try
        {
            return msg.Metadata.TryGetValue(key, out var v) ? v : null;
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeWorkerId(string stepId, int workerCount)
    {
        if (string.IsNullOrEmpty(stepId)) return "coordinator";

        var lower = stepId.ToLowerInvariant();

        // Coordinator patterns: check_atomic, compose, vote steps
        if (lower.Contains("check_atomic") ||
            lower.Contains("coordinator") ||
            (lower.Contains("compose") && !lower.Contains("gen[")) ||
            lower.EndsWith(".vote"))
        {
            return "coordinator";
        }

        // Worker patterns: gen[index] -> worker-{(index-1) % workerCount}
        var match = System.Text.RegularExpressions.Regex.Match(stepId, @"gen\[(\d+)\]");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var genIndex))
        {
            var workerIndex = workerCount > 0 ? (genIndex - 1) % workerCount : 0;
            return $"worker-{workerIndex}";
        }

        // Fan-out patterns: "{stepIdPrefix}[i]" -> worker-{i % workerCount}
        var bracketStart = stepId.IndexOf('[');
        if (bracketStart > 0)
        {
            var bracketEnd = stepId.IndexOf(']', bracketStart + 1);
            if (bracketEnd > bracketStart + 1)
            {
                var prefix = stepId[..bracketStart];
                var indexText = stepId[(bracketStart + 1)..bracketEnd];
                if (IsFanOutPrefix(prefix) && int.TryParse(indexText, out var i))
                {
                    var workerIndex = workerCount > 0 ? i % workerCount : 0;
                    return $"worker-{workerIndex}";
                }
            }
        }

        return "coordinator";
    }

    private static bool IsFanOutPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return false;

        // Keep this list aligned with CognitiveStrategy.FanOutStepPrefixes.
        // NOTE: We intentionally keep it conservative; unknown prefixes fall back to coordinator.
        return prefix is
            "prove_with_workers" or
            "refute_scout" or
            "prove_or_refute_with_workers" or
            "execute_subtasks" or
            "solve_subtasks" or
            "decompose_thoughts" or
            "synthesize" or
            "synthesize_candidates" or
            "evaluate_candidates";
    }
}

