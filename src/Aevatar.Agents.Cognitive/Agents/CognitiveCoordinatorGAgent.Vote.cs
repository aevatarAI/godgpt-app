using Aevatar.Agents.Cognitive.Messages;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.Logging;

using StepDefinition = Aevatar.Agents.Cognitive.Primitives.StepDefinition;
using VoteResult = Aevatar.Agents.Maker.VoteResult;

namespace Aevatar.Agents.Cognitive.Agents;

// ============================================================
//  CognitiveCoordinatorGAgent - Vote (consensus)
//
//  WHY:
//  - vote 是“并行 + streaming + 语义聚类 + 红旗”的复杂节点。
//  - 独立成文件，避免 Coordinator 主文件继续膨胀。
// ============================================================

public partial class CognitiveCoordinatorGAgent
{
    private async Task<PrimitiveResult> ExecuteVoteAsync(StepDefinition step)
    {
        var k = ResolveIntParameter(step.Parameters, "k", 3);
        var maxRounds = ResolveIntParameter(step.Parameters, "max_rounds", 10);
        var similarity = ResolveFloatParameter(step.Parameters, "similarity", _semanticSimilarityThreshold);
        var generator = step.Generator;

        if (generator == null)
        {
            return PrimitiveResult.Fail("vote requires 'generator' definition");
        }

        // ─────────────────────────────────────────────
        //  Red-Flag 策略（可插拔）
        //  优先级：步骤配置 > Coordinator 默认配置 > null (禁用)
        // ─────────────────────────────────────────────
        var redFlagStrategy = ResolveRedFlagStrategy(step.Parameters);
        var redFlagCount = 0;
        var maxRedFlags = ResolveIntParameter(step.Parameters, "max_red_flags", maxRounds * 2);
        // Compatibility: allow max_red_flags to be nested under red_flag config (maker-v2.yaml style).
        if (!step.Parameters.ContainsKey("max_red_flags") &&
            step.Parameters.TryGetValue("red_flag", out var rfObj) &&
            rfObj is Dictionary<string, object?> rfConfig &&
            rfConfig.TryGetValue("max_red_flags", out var nestedMax) &&
            nestedMax != null)
        {
            maxRedFlags = ConvertToInt(nestedMax, maxRedFlags);
        }

        // ─────────────────────────────────────────────
        //  使用 MAKER 的语义聚类 VoteEngine
        //  如果没有配置 embedding generator，自动回退到精确匹配
        // ─────────────────────────────────────────────
        using var engine = new VoteEngine(
            k,
            _embeddingGenerator, // null 时自动回退到精确匹配
            maxRounds,
            similarity);

        var useSemanticClustering = _embeddingGenerator != null;
        Logger.LogDebug(
            "Vote step {StepId}: K={K}, maxRounds={MaxRounds}, semantic={Semantic}, redFlag={RedFlag}",
            step.Id, k, maxRounds, useSemanticClustering, redFlagStrategy?.GetType().Name ?? "disabled");

        var totalTokens = 0;
        var totalCalls = 0;
        VoteResult? consensusResult = null;
        var round = 0;
        // 记录达成共识的轮次（用于 UI 与日志一致性）
        int? consensusReachedAtRound = null;

        // 并行批次大小：每批同时生成 N 个提案（受 Worker 数量和 K 值限制）
        var batchSize = Math.Max(1, Math.Min(k + 1, _workerIds.Count > 0 ? _workerIds.Count : 3));
        Logger.LogDebug(
            "[VOTE] batchSize={BatchSize}, k={K}, workers={WorkerCount}",
            batchSize, k, _workerIds.Count);

        while (consensusResult == null && round < maxRounds)
        {
            // 红旗过多时提前终止
            if (redFlagCount >= maxRedFlags)
            {
                Logger.LogWarning(
                    "Vote step {StepId}: Too many red flags ({RedFlags}), terminating early",
                    step.Id, redFlagCount);
                break;
            }

            var batchRound = round / batchSize + 1;

            // 发送投票进度事件
            EmitStepEvent(step, StepStatus.Running,
                $"Voting batch {batchRound}, generating {batchSize} proposals in parallel",
                progress: (float)round / maxRounds,
                voteRound: round, voteMaxRounds: maxRounds, voteK: k,
                parallelTotal: batchSize, parallelCompleted: 0);

            // 预渲染 prompt（所有并行任务共用）
            var genPrompt = generator.Parameters.GetValueOrDefault("prompt")?.ToString() ?? "";
            var genSystem = generator.Parameters.GetValueOrDefault("system")?.ToString();

            if (Logger.IsEnabled(LogLevel.Debug))
            {
                Logger.LogDebug(
                    "[Vote {StepId}] generator.Type={GenType}, generator.Parameters=[{GenParams}], workflowVars=[{Vars}]",
                    step.Id,
                    generator.Type,
                    string.Join(", ", generator.Parameters.Keys),
                    string.Join(", ", _workflowVariables.Keys));
                Logger.LogDebug("[Vote {StepId}] genPrompt length={Len}", step.Id, genPrompt.Length);
            }

            // ============================================================
            //  关键诊断：compose 卡住时，最常见是“模板渲染”或“LLM 首 token”卡住
            //  这里先把模板渲染耗时打出来，便于定位是否卡在 Render()
            // ============================================================
            var renderSw = System.Diagnostics.Stopwatch.StartNew();
            Logger.LogInformation("[VOTE] {StepId}: Rendering generator template...", step.Id);
            genPrompt = _templateEngine.Render(genPrompt, _workflowVariables);
            if (genSystem != null) genSystem = _templateEngine.Render(genSystem, _workflowVariables);
            renderSw.Stop();
            Logger.LogInformation("[VOTE] {StepId}: Template rendered in {Ms}ms, promptLen={Len}",
                step.Id, renderSw.ElapsedMilliseconds, genPrompt.Length);

            // ─────────────────────────────────────────────
            //  并行生成提案：同时启动多个 LLM 调用
            //  ⚡ 关键：先发送所有开始事件，实现视觉并行
            // ─────────────────────────────────────────────
            var batchTasks = new List<(int index, StepDefinition step, Task<PrimitiveResult> task)>();

            Logger.LogDebug(
                "[VOTE] Generating batch: round={Round}, batchSize={BatchSize}, maxRounds={MaxRounds}",
                round, batchSize, maxRounds);
            for (int i = 0; i < batchSize && round + i < maxRounds; i++)
            {
                var proposalIndex = round + i + 1;
                var genStepId = $"{step.Id}.gen[{proposalIndex}]";
                var genStep = new StepDefinition { Id = genStepId, Type = "llm_call" };

                // 发送开始事件（所有卡片同时出现）
                // parallelTotal = batchSize，确保前端能正确计算 workerCount
                EmitStepEvent(genStep, StepStatus.Running,
                    $"Generating proposal #{proposalIndex}",
                    progress: 0,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: genSystem,
                    userPrompt: genPrompt);

                // 启动 LLM 调用（不 await）
                var task = ExecuteLlmCallWithStreamingAsync(generator, genStep, genSystem, genPrompt);
                batchTasks.Add((proposalIndex, genStep, task));
            }

            // 等待所有并行任务完成
            var results = await Task.WhenAll(batchTasks.Select(t => t.task));

            // 处理结果
            for (int i = 0; i < results.Length; i++)
            {
                round++;
                var (proposalIndex, genStep, _) = batchTasks[i];
                var result = results[i];

                totalTokens += result.TokensUsed;
                totalCalls += result.LlmCalls;

                // 发送完成事件
                EmitStepEvent(genStep, result.Success ? StepStatus.Completed : StepStatus.Failed,
                    result.Success ? $"Proposal #{proposalIndex} generated" : $"Generation failed: {result.Error}",
                    progress: 1,
                    parentStepId: step.Id,
                    parallelTotal: batchSize,
                    systemPrompt: result.SystemPrompt,
                    userPrompt: result.UserPrompt,
                    assistantResponse: result.AssistantResponse);

                if (!result.Success) continue;

                // ✅ 已达成共识：仍然要把本批次剩余提案的完成事件都发出去（否则 UI 会出现某个 worker 永远空/卡住），
                // 但不再继续投票消耗。
                if (consensusResult != null) continue;

                // 使用原始 LLM 响应进行投票（不是解析后的对象）
                // VoteEngine 需要原始字符串来做语义聚类
                var proposal = result.AssistantResponse ?? result.Value?.ToString() ?? "";

                // Red-Flag 验证
                if (redFlagStrategy != null)
                {
                    var proposalId = $"{step.Id}.round{proposalIndex}";
                    if (!redFlagStrategy.Validate(proposal, proposalId, out var reason))
                    {
                        redFlagCount++;
                        Logger.LogWarning("🚩 Red flag in {StepId} round {Round}: {Reason}",
                            step.Id, proposalIndex, reason);

                        EmitStepEvent(step, StepStatus.Running,
                            $"🚩 Round {proposalIndex}: {reason}",
                            progress: (float)round / maxRounds,
                            voteRound: proposalIndex, voteMaxRounds: maxRounds, voteK: k,
                            redFlagReason: reason);
                        continue;
                    }
                }

                // 提交投票
                consensusResult = await engine.SubmitVoteAsync(proposal);
                if (consensusResult != null && consensusReachedAtRound == null)
                {
                    // 保留“首次达成共识”的轮次，不被后续（未投票的）提案影响
                    consensusReachedAtRound = proposalIndex;
                }
            }

            // 发送当前投票状态
            var currentVotes = consensusResult?.LeaderVotes ?? 0;
            var displayRound = consensusReachedAtRound ?? round;
            EmitStepEvent(step, StepStatus.Running,
                $"Round {displayRound}: {currentVotes}/{k} votes" + (redFlagCount > 0 ? $" (🚩{redFlagCount})" : ""),
                progress: (float)round / maxRounds,
                voteRound: displayRound, voteMaxRounds: maxRounds, voteK: k,
                voteCurrentVotes: currentVotes);

            if (consensusResult != null)
            {
                Logger.LogInformation(
                    "✓ Vote consensus reached at round {Round}: {LeaderVotes}/{K} votes (🚩{RedFlags})",
                    displayRound, consensusResult.LeaderVotes, k, redFlagCount);
            }
        }

        // 添加 embedding 调用计数
        var embeddingCalls = engine.EmbeddingCallCount;

        // 获取原始内容
        string rawContent;
        if (consensusResult != null && consensusResult.Success)
        {
            // WinningContent is nullable in some implementations
            rawContent = consensusResult.WinningContent ?? "";
        }
        else
        {
            // 没有达成共识，返回得票最多的
            var bestCandidate = engine.GetBestCandidate();
            rawContent = bestCandidate?.Content ?? "";

            Logger.LogWarning(
                "Vote step {StepId}: No consensus after {Rounds} rounds (🚩{RedFlags}), using best candidate ({Votes} votes)",
                step.Id, maxRounds, redFlagCount, bestCandidate?.Votes ?? 0);
        }

        // ─────────────────────────────────────────────
        //  关键修复：根据 generator.output 解析结果
        //  例如 output: json_array 应该返回 List<object>
        // ─────────────────────────────────────────────
        var outputType = generator.Parameters.GetValueOrDefault("output")?.ToString() ?? "text";

        // DEBUG: 检查 rawContent
        Logger.LogInformation("[DEBUG][Vote] rawContent length: {Len}", rawContent.Length);
        Logger.LogInformation("[DEBUG][Vote] rawContent preview: {Preview}",
            rawContent.Length > 500 ? rawContent[..500] : rawContent);
        Logger.LogInformation("[DEBUG][Vote] outputType: {Type}", outputType);

        var parsedResult = ParseOutput(rawContent, outputType);
        if (parsedResult == null)
        {
            return PrimitiveResult.Fail("redflag-parse-null");
        }

        // DEBUG: 检查解析结果
        Logger.LogInformation("[DEBUG][Vote] parsedResult is null: {IsNull}", parsedResult == null);
        if (parsedResult is System.Collections.IList list)
        {
            Logger.LogInformation("[DEBUG][Vote] parsedResult is List with {Count} items", list.Count);
        }

        // ============================================================
        //  将“最终共识内容”透出到步骤事件（AssistantResponse）
        //
        //  WHY:
        //  - vote step 本身不是 llm_call，它的 PrimitiveResult 默认不会携带 AssistantResponse
        //  - 这会导致上层 UI 只能看到 proposals，却不知道最终共识选择了哪一个
        //  - PaperReview 需要把共识结论绑定到 atomic point 上进行可视化
        // ============================================================
        return new PrimitiveResult
        {
            Success = true,
            Value = parsedResult,
            TokensUsed = totalTokens,
            LlmCalls = totalCalls + embeddingCalls,
            AssistantResponse = rawContent
        };
    }
}

