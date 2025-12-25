using System.Text;
using Aevatar.Agents.Maker.Messages;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Task Executor
//  Handles task execution logic using iterative stack-based approach
//  Part of MakerCoordinatorGAgent (partial class)
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  Task Execution State Machine
    // ============================================================

    /// <summary>
    /// Task execution state for iterative processing.
    /// </summary>
    private enum TaskExecutionPhase
    {
        Pending,           // Not yet started
        Assessing,         // Assessing atomicity
        Solving,           // Solving as atomic
        Decomposing,       // Running decomposition voting
        ExecutingChildren, // Executing subtasks
        Composing,         // Composing results
        Completed          // Done
    }

    /// <summary>
    /// Execution context for a single task in the iterative stack.
    /// </summary>
    private sealed class TaskExecutionContext
    {
        public required string TaskId { get; init; }
        public required string Description { get; init; }
        public required int Depth { get; init; }
        public required Dictionary<string, string> Context { get; init; }

        public TaskExecutionPhase Phase { get; set; } = TaskExecutionPhase.Pending;
        public TaskNode? Node { get; set; }
        public string? Result { get; set; }

        // For decomposition
        public List<(string StepId, string Description)>? Subtasks { get; set; }
        public int CurrentSubtaskIndex { get; set; }
        public Dictionary<string, string> SubtaskResults { get; } = new();
        public List<TaskNode> ChildNodes { get; } = [];
        public List<VotingSession> VotingSessions { get; } = [];

        // For atomic solve fallback
        public bool SolveFailed { get; set; }
        public string? BestCandidate { get; set; }
    }

    // ============================================================
    //  Iterative Task Execution
    //  Uses explicit stack to prevent StackOverflowException
    // ============================================================

    /// <summary>
    /// Execute task using iterative approach (stack-based, no recursion).
    /// This prevents StackOverflowException for deeply nested task trees.
    /// </summary>
    private async Task<(string? Result, TaskNode Node)> ExecuteTaskAsync(
        string taskId,
        string description,
        int depth,
        Dictionary<string, string> context,
        CancellationToken ct)
    {
        var options = _currentOptions ?? new MakerOptions();

        // Task execution stack - replaces call stack
        var taskStack = new Stack<TaskExecutionContext>();

        // Results storage - maps taskId to (Result, Node)
        var completedTasks = new Dictionary<string, (string? Result, TaskNode Node)>();

        // Push initial task
        taskStack.Push(new TaskExecutionContext
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            Context = context,
            Phase = TaskExecutionPhase.Pending
        });

        // Main execution loop - process tasks until stack is empty
        while (taskStack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var current = taskStack.Peek();
            CustomState.CurrentTaskId = current.TaskId;
            CustomState.CurrentDepth = current.Depth;

            switch (current.Phase)
            {
                case TaskExecutionPhase.Pending:
                    await ProcessPendingPhaseAsync(current, taskStack, completedTasks, options, ct);
                    continue;

                case TaskExecutionPhase.Assessing:
                    await ProcessAssessingPhaseAsync(current, options, ct);
                    continue;

                case TaskExecutionPhase.Solving:
                    await ProcessSolvingPhaseAsync(current, taskStack, completedTasks, options, ct);
                    continue;

                case TaskExecutionPhase.Decomposing:
                    await ProcessDecomposingPhaseAsync(current, taskStack, completedTasks, options, ct);
                    continue;

                case TaskExecutionPhase.ExecutingChildren:
                    ProcessExecutingChildrenPhase(current, completedTasks);
                    continue;

                case TaskExecutionPhase.Composing:
                    await ProcessComposingPhaseAsync(current, taskStack, completedTasks, ct);
                    continue;

                case TaskExecutionPhase.Completed:
                    taskStack.Pop();
                    continue;
            }
        }

        // Return root task result
        if (completedTasks.TryGetValue(taskId, out var rootResult))
        {
            return rootResult;
        }

        // Fallback - should not reach here
        return (null, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = true
        });
    }

    // ============================================================
    //  Phase Processing Methods
    // ============================================================

    private async Task ProcessPendingPhaseAsync(
        TaskExecutionContext current,
        Stack<TaskExecutionContext> taskStack,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks,
        MakerOptions options,
        CancellationToken ct)
    {
        // Initialize task node
        current.Node = new TaskNode
        {
            TaskId = current.TaskId,
            Description = current.Description,
            Depth = current.Depth,
            IsAtomic = false
        };
        
        // Checkpoint: Task started
        _checkpointManager?.OnTaskStarted(current.TaskId, current.Description, current.Depth, current.Context);

        // Hard depth cap check (safety net)
        if (current.Depth >= options.HardDepthCap)
        {
            AddRedFlag(current.TaskId, $"🛑 HARD DEPTH CAP reached ({current.Depth})");
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.RedFlag,
                TaskId = current.TaskId,
                Message = $"🛑 HARD DEPTH CAP ({options.HardDepthCap}) reached.",
                Depth = current.Depth
            });

            // Force solve as atomic
            var (forceResult, forceNode) = await SolveAtomicTaskAsync(
                current.TaskId, current.Description, current.Context, current.Depth, ct);
            await CompleteTaskWithCheckpointAsync(current.TaskId, forceResult, completedTasks, forceNode, ct);
            taskStack.Pop();
            return;
        }

        // Budget check
        var budgetStatus = CheckBudget(options);
        if (!budgetStatus.WithinBudget)
        {
            AddRedFlag(current.TaskId, $"Budget exhausted: {budgetStatus.Reason}");
            var budgetNode = new TaskNode
            {
                TaskId = current.TaskId,
                Description = current.Description,
                Depth = current.Depth,
                IsAtomic = true,
                Result = null
            };
            completedTasks[current.TaskId] = (null, budgetNode);
            taskStack.Pop();
            return;
        }

        // Depth warning
        if (current.Depth >= options.DepthWarningThreshold)
        {
            AddRedFlag(current.TaskId, $"Depth warning: {current.Depth}");
        }

        current.Phase = TaskExecutionPhase.Assessing;
    }

    private async Task ProcessAssessingPhaseAsync(
        TaskExecutionContext current,
        MakerOptions options,
        CancellationToken ct)
    {
        var taskPreview = current.Description.Length > 100 
            ? current.Description[..100].Replace("\n", " ") + "..." 
            : current.Description.Replace("\n", " ");
        
        Logger.LogInformation(
            "\n┌─────────────────────────────────────────────────────────────────┐\n" +
            "│ 📋 ASSESSING TASK [{Mode}]                                      \n" +
            "├─────────────────────────────────────────────────────────────────┤\n" +
            "│ Task ID: {TaskId}                                               \n" +
            "│ Depth: {Depth}                                                  \n" +
            "│ Description: {TaskPreview}                                      \n" +
            "└─────────────────────────────────────────────────────────────────┘",
            options.Mode,
            current.TaskId,
            current.Depth,
            taskPreview);
        
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Assessing,
            TaskId = current.TaskId,
            Message = $"[{options.Mode}] Assessing at depth {current.Depth}",
            Depth = current.Depth
        });

        if (options.Mode == ExecutionMode.Academic)
        {
            // ─────────────────────────────────────────────────────────
            //  Academic mode: Decompose first, but respect strategy's IsAtomic
            //  This allows strategies to define atomic levels (e.g., depth 1+)
            //  while still getting the benefits of Academic mode's forced decomposition
            // ─────────────────────────────────────────────────────────
            if (_decomposer.IsAtomic(current.Description, current.Depth))
            {
                Logger.LogInformation(
                    "[ACADEMIC] Depth {Depth}: Strategy says ATOMIC → SOLVING (skipping decomposition)",
                    current.Depth);
                current.Phase = TaskExecutionPhase.Solving;
            }
            else
            {
                Logger.LogInformation(
                    "[ACADEMIC] Depth {Depth}: Forcing DECOMPOSITION (Academic mode)",
                    current.Depth);
            current.Phase = TaskExecutionPhase.Decomposing;
            }
        }
        else
        {
            // Production mode: assess atomicity
            var isAtomic = await AssessAtomicityAsync(current.Description, current.Depth, ct);
            Logger.LogInformation("[PRODUCTION] Depth {Depth}: Task assessed as {Result} → {NextPhase}",
                current.Depth,
                isAtomic ? "ATOMIC" : "COMPLEX",
                isAtomic ? "SOLVING" : "DECOMPOSING");
            current.Phase = isAtomic ? TaskExecutionPhase.Solving : TaskExecutionPhase.Decomposing;
        }
    }

    private async Task ProcessSolvingPhaseAsync(
        TaskExecutionContext current,
        Stack<TaskExecutionContext> taskStack,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks,
        MakerOptions options,
        CancellationToken ct)
    {
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Solving,
            TaskId = current.TaskId,
            Message = $"Solving atomic task at depth {current.Depth}",
            Depth = current.Depth
        });

        var (solveResult, solveNode) = await SolveAtomicTaskAsync(
            current.TaskId, current.Description, current.Context, current.Depth, ct);

        if (solveResult != null)
        {
            var resultPreview = solveResult.Length > 300 
                ? solveResult[..300].Replace("\n", " ") + "..." 
                : solveResult.Replace("\n", " ");
            Logger.LogInformation(
                "\n╔══════════════════════════════════════════════════════════════╗\n" +
                "║ ✅ SOLVE SUCCESSFUL                                           \n" +
                "╠══════════════════════════════════════════════════════════════╣\n" +
                "║ Task: {TaskId} (depth {Depth})                                \n" +
                "║ Result preview: {ResultPreview}                               \n" +
                "╚══════════════════════════════════════════════════════════════╝",
                current.TaskId, current.Depth, resultPreview);
            await CompleteTaskWithCheckpointAsync(current.TaskId, solveResult, completedTasks, solveNode, ct);
            taskStack.Pop();
            return;
        }

        // Solve failed - try decomposition if budget allows
        current.BestCandidate = solveNode.VotingSessions.Count > 0
            ? solveNode.VotingSessions[0].Candidates.OrderByDescending(c => c.Votes).FirstOrDefault()?.Content
            : null;
        current.SolveFailed = true;
        current.VotingSessions.AddRange(solveNode.VotingSessions);

        // ============================================================
        // ⚠️ SOLVE FAILED - Log voting results and next action
        // ============================================================
        var votingStats = solveNode.VotingSessions.FirstOrDefault();
        var candidateInfo = votingStats?.Candidates?.OrderByDescending(c => c.Votes)
            .Take(3)
            .Select((c, i) => $"#{i + 1}: {c.Votes} votes")
            .ToList() ?? new List<string>();
        
        Logger.LogWarning(
            "\n╔══════════════════════════════════════════════════════════════╗\n" +
            "║ ⚠️ SOLVE FAILED - NO CONSENSUS                                \n" +
            "╠══════════════════════════════════════════════════════════════╣\n" +
            "║ Task: {TaskId} (depth {Depth})                                \n" +
            "║ Voting results: {VotingResults}                               \n" +
            "║ Best candidate saved: {HasBestCandidate}                      \n" +
            "║ Next action: {NextAction}                                     \n" +
            "╚══════════════════════════════════════════════════════════════╝",
            current.TaskId,
            current.Depth,
            string.Join(", ", candidateInfo),
            current.BestCandidate != null ? "Yes" : "No",
            CheckBudget(options).WithinBudget ? "RE-DECOMPOSE (finer granularity)" : "USE BEST CANDIDATE (budget exhausted)");

        if (CheckBudget(options).WithinBudget)
        {
            AddRedFlag(current.TaskId, "Solution consensus failed, decomposing");
            Logger.LogInformation("[SOLVE→DECOMPOSE] Task {TaskId}: Consensus failed, triggering finer-grained decomposition", current.TaskId);
            current.Phase = TaskExecutionPhase.Decomposing;
        }
        else
        {
            // Budget exhausted - use best candidate as fallback
            if (current.BestCandidate != null)
            {
                solveNode.Result = current.BestCandidate;
                completedTasks[current.TaskId] = (current.BestCandidate, solveNode);
            }
            else
            {
                completedTasks[current.TaskId] = (null, solveNode);
            }

            taskStack.Pop();
        }
    }

    private async Task ProcessDecomposingPhaseAsync(
        TaskExecutionContext current,
        Stack<TaskExecutionContext> taskStack,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks,
        MakerOptions options,
        CancellationToken ct)
    {
        var isFallbackDecomposition = current.SolveFailed;
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Decomposing,
            TaskId = current.TaskId,
            Message = isFallbackDecomposition 
                ? $"Re-decomposing after solve failure at depth {current.Depth}" 
                : $"Decomposing at depth {current.Depth}",
            Depth = current.Depth
        });

        // Build context with solve failure info for smarter decomposition
        var decompContext = new Dictionary<string, string>(current.Context)
        {
            ["current_depth"] = current.Depth.ToString(),
            ["solve_failed"] = isFallbackDecomposition.ToString().ToLower(),
            ["best_candidate_preview"] = isFallbackDecomposition && current.BestCandidate != null
                ? current.BestCandidate.Length > 500 ? current.BestCandidate[..500] + "..." : current.BestCandidate
                : ""
        };

        // Get decomposition with enriched context
        var decompPrompt = _decomposer.BuildDecompositionPrompt(
            current.Description, decompContext, options.Granularity);
        var (decompResult, bestDecomp, decompSession) = await RunVotingWithWorkersAsync(
            current.TaskId, decompPrompt, isSolution: false, options, current.Depth, ct);

        if (decompSession != null) current.VotingSessions.Add(decompSession);

        var decompositionToUse = decompResult ?? bestDecomp;
        var steps = string.IsNullOrWhiteSpace(decompositionToUse)
            ? new List<(string, string)>()
            : _decomposer.ParseDecomposition(decompositionToUse).ToList();

        if (steps.Count == 0)
        {
            // No valid decomposition - solve as atomic
            if (!current.SolveFailed)
            {
                Logger.LogInformation(
                    "[DECOMPOSITION] Task {TaskId} at depth {Depth}: No subtasks returned → treating as ATOMIC, switching to SOLVING",
                    current.TaskId, current.Depth);
                current.Phase = TaskExecutionPhase.Solving;
                return;
            }

            // Already tried solving, use fallback
            Logger.LogWarning(
                "[DECOMPOSITION] Task {TaskId} at depth {Depth}: Both SOLVING and DECOMPOSING failed! Using best candidate as fallback.",
                current.TaskId, current.Depth);
            var fallbackNode = new TaskNode
            {
                TaskId = current.TaskId,
                Description = current.Description,
                Depth = current.Depth,
                IsAtomic = true,
                Result = current.BestCandidate,
                VotingSessions = current.VotingSessions
            };
            completedTasks[current.TaskId] = (current.BestCandidate, fallbackNode);
            taskStack.Pop();
            return;
        }

        // ============================================================
        // 🔀 Decomposition successful - log subtasks
        // ============================================================
        Logger.LogInformation(
            "\n╔══════════════════════════════════════════════════════════════╗\n" +
            "║ 🔀 DECOMPOSITION SUCCESSFUL                                   \n" +
            "╠══════════════════════════════════════════════════════════════╣\n" +
            "║ Task: {TaskId} (depth {Depth})                                \n" +
            "║ Decomposed into {SubtaskCount} subtasks:                      \n" +
            "{SubtaskList}" +
            "╚══════════════════════════════════════════════════════════════╝",
            current.TaskId,
            current.Depth,
            steps.Count,
            string.Join("", steps.Select((s, i) => $"║   {i + 1}. [{s.Item1}] {(s.Item2.Length > 60 ? s.Item2[..60] + "..." : s.Item2)}\n")));
        
        // Store subtasks and push them to stack (in reverse order)
        current.Subtasks = steps;
        current.CurrentSubtaskIndex = 0;
        current.Phase = TaskExecutionPhase.ExecutingChildren;

        // Push subtasks in reverse so first subtask is processed first
        for (var i = steps.Count - 1; i >= 0; i--)
        {
            var (stepId, stepDesc) = steps[i];
            var childContext = BuildChildContext(
                current.Context, current.SubtaskResults, stepId, options.ContextIsolation);

            taskStack.Push(new TaskExecutionContext
            {
                TaskId = $"{current.TaskId}:{stepId}",
                Description = stepDesc,
                Depth = current.Depth + 1,
                Context = childContext,
                Phase = TaskExecutionPhase.Pending
            });
        }
    }

    private void ProcessExecutingChildrenPhase(
        TaskExecutionContext current,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks)
    {
        // Check if all children are done
        var allChildrenDone = current.Subtasks!.All(s =>
            completedTasks.ContainsKey($"{current.TaskId}:{s.StepId}"));

        if (!allChildrenDone)
        {
            // Children still processing - this shouldn't happen with correct stack order
            return;
        }

        // Collect child results
        foreach (var (stepId, _) in current.Subtasks!)
        {
            var childTaskId = $"{current.TaskId}:{stepId}";
            if (completedTasks.TryGetValue(childTaskId, out var childResult))
            {
                current.ChildNodes.Add(childResult.Node);
                if (childResult.Result != null)
                {
                    current.SubtaskResults[stepId] = childResult.Result;
                }
            }
        }

        current.Phase = TaskExecutionPhase.Composing;
    }

    private async Task ProcessComposingPhaseAsync(
        TaskExecutionContext current,
        Stack<TaskExecutionContext> taskStack,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks,
        CancellationToken ct)
    {
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Composing,
            TaskId = current.TaskId,
            Message = "Composing subtask results",
            Depth = current.Depth
        });

        // Compose results
        var composed = _composer.Compose(current.Description, current.SubtaskResults, current.Context);
        if (composed == null)
        {
            var synthPrompt = _composer.BuildSynthesisPrompt(
                current.Description, current.SubtaskResults, current.Context);
            
            // Use streaming for real-time UI updates
            composed = await GenerateResponseWithStreamingAsync(
                synthPrompt, current.TaskId, "COMPOSE", ct);
            composed = composed.Trim();
            
            if (string.IsNullOrWhiteSpace(composed))
            {
                composed = string.Join("\n\n", current.SubtaskResults.Values);
            }
            CustomState.TotalLlmCalls++;
        }

        // Create final node with all children and results
        var composedNode = new TaskNode
        {
            TaskId = current.TaskId,
            Description = current.Description,
            Depth = current.Depth,
            IsAtomic = false,
            Result = composed,
            Children = current.ChildNodes,
            VotingSessions = current.VotingSessions
        };

        await CompleteTaskWithCheckpointAsync(current.TaskId, composed, completedTasks, composedNode, ct);
        taskStack.Pop();
    }

    // ============================================================
    //  Budget Check
    // ============================================================

    /// <summary>
    /// Check if we're still within budget.
    /// </summary>
    private (bool WithinBudget, string? Reason) CheckBudget(MakerOptions options)
    {
        // Check LLM calls
        if (options.MaxTotalLlmCalls > 0 && CustomState.TotalLlmCalls >= options.MaxTotalLlmCalls)
        {
            return (false, $"LLM calls exhausted ({CustomState.TotalLlmCalls}/{options.MaxTotalLlmCalls})");
        }

        // Check tokens
        if (options.MaxTotalTokens > 0 && CustomState.TotalTokensUsed >= options.MaxTotalTokens)
        {
            return (false, $"Token budget exhausted ({CustomState.TotalTokensUsed:N0}/{options.MaxTotalTokens:N0})");
        }

        // Check duration
        if (options.MaxDuration > TimeSpan.Zero && _stopwatch.Elapsed >= options.MaxDuration)
        {
            return (false, $"Time limit reached ({_stopwatch.Elapsed.TotalMinutes:F1} min)");
        }

        return (true, null);
    }
    
    // ============================================================
    //  Task Completion with Checkpointing
    // ============================================================
    
    /// <summary>
    /// Complete a task with checkpoint persistence.
    /// </summary>
    private async Task CompleteTaskWithCheckpointAsync(
        string taskId,
        string? result,
        Dictionary<string, (string? Result, TaskNode Node)> completedTasks,
        TaskNode node,
        CancellationToken ct)
    {
        completedTasks[taskId] = (result, node);
        
        // Persist checkpoint for recovery
        if (_checkpointManager != null)
        {
            await _checkpointManager.OnTaskCompletedAsync(
                taskId,
                result,
                CustomState.TotalLlmCalls,
                CustomState.TotalTokensUsed,
                ct);
        }
    }

    // ============================================================
    //  Atomicity Assessment
    // ============================================================

    /// <summary>
    /// LLM-based atomicity assessment with strategy override.
    /// 
    /// Academic Mode: LLM always decides (strategy IsAtomic is ignored)
    /// Production Mode: Strategy IsAtomic is checked first, then LLM
    /// 
    /// Priority (Production): 1. Strategy says atomic → atomic (no LLM needed)
    ///                        2. LLM assessment
    ///                        3. Fallback heuristics
    /// Priority (Academic):   1. LLM assessment (always)
    ///                        2. Fallback to strategy heuristics on error
    /// </summary>
    private async Task<bool> AssessAtomicityAsync(
        string taskDescription,
        int currentDepth,
        CancellationToken ct)
    {
        var options = _currentOptions ?? new MakerOptions();
        
        // ACADEMIC MODE: Skip strategy check, let LLM decide decomposition depth
        // This ensures maximum exploration and correctness over efficiency
        if (options.Mode == ExecutionMode.Academic)
        {
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = CustomState.CurrentTaskId ?? "unknown",
                Message = $"[ACADEMIC] LLM will decide atomicity at depth {currentDepth}",
                Depth = currentDepth
            });
            // Fall through to LLM assessment below
        }
        // PRODUCTION MODE: Strategy-defined atomicity takes priority (faster)
        else if (_decomposer.IsAtomic(taskDescription, currentDepth))
        {
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = CustomState.CurrentTaskId ?? "unknown",
                Message = $"Strategy says ATOMIC at depth {currentDepth} - skipping LLM assessment",
                Depth = currentDepth
            });
            return true;
        }

        // PRIORITY 2: LLM-based assessment with depth context
        var prompt = $$"""
                       You are a task complexity analyzer. Determine if the following task can be reliably solved in a SINGLE LLM inference step, or if it needs to be broken down into subtasks.

                       IMPORTANT CONTEXT:
                       - Current recursion depth: {{currentDepth}}
                       - This is a SUB-TASK of a larger problem
                       - If this looks like a leaf-level task (e.g., "Summarize Section X", "Calculate Y", "Write paragraph about Z"), it is probably ATOMIC
                       - DO NOT decompose tasks that are already specific enough

                       Criteria for ATOMIC (return true):
                       - Task focuses on ONE specific section/topic/aspect
                       - Task can be answered with a single coherent response
                       - Task description already includes "Section", "Step", "Part", or similar specificity
                       - Task is a direct question or simple generation request

                       Criteria for DECOMPOSE (return false):
                       - Task requires covering MULTIPLE distinct topics
                       - Task explicitly mentions "comprehensive", "complete", "all aspects"
                       - Task would benefit from parallel independent subtasks

                       Task: {{taskDescription}}

                       Respond with ONLY valid JSON (no markdown):
                       {"atomic": true, "reason": "..."}
                       or
                       {"atomic": false, "reason": "..."}
                       """;

        try
        {
            // Use streaming for real-time UI updates
            var taskId = CustomState.CurrentTaskId ?? "unknown";
            var content = await GenerateResponseWithStreamingAsync(
                prompt, taskId, "ASSESS", ct);
            CustomState.TotalLlmCalls++;

            content = content.Trim();

            // Parse JSON response
            var (isAtomic, reason) = ParseAtomicityResponse(content);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Assessing,
                TaskId = taskId,
                Message = $"LLM atomicity assessment: {(isAtomic ? "ATOMIC" : "DECOMPOSE")} - {reason}",
                Depth = currentDepth
            });

            return isAtomic;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "Atomicity assessment failed, falling back to heuristic");
            AddRedFlag(CustomState.CurrentTaskId ?? "unknown", $"Atomicity LLM failed: {ex.Message}");

            // Fallback to strategy-based assessment
            return _decomposer.IsAtomic(taskDescription, currentDepth);
        }
    }

    // ============================================================
    //  Atomic Task Solving
    // ============================================================

    private async Task<(string? Result, TaskNode Node)> SolveAtomicTaskAsync(
        string taskId,
        string description,
        Dictionary<string, string> context,
        int depth,
        CancellationToken ct)
    {
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Solving,
            TaskId = taskId,
            Message = "Solving atomic task with worker agents",
            Depth = depth
        });

        var prompt = _solver.BuildSolvePrompt(description, context);
        var options = _currentOptions ?? new MakerOptions();
        var (result, bestCandidate, session) = await RunVotingWithWorkersAsync(
            taskId, prompt, isSolution: true, options, depth, ct);

        var sessions = session != null ? new List<VotingSession> { session } : new List<VotingSession>();

        return (result, new TaskNode
        {
            TaskId = taskId,
            Description = description,
            Depth = depth,
            IsAtomic = true,
            Result = result,
            FallbackCandidate = bestCandidate,
            VotingSessions = sessions
        });
    }

    // ============================================================
    //  Context Building
    // ============================================================

    /// <summary>
    /// Build child context based on isolation mode.
    /// </summary>
    private static Dictionary<string, string> BuildChildContext(
        Dictionary<string, string> parentContext,
        Dictionary<string, string> siblingResults,
        string currentStepId,
        ContextIsolationMode isolationMode)
    {
        return isolationMode switch
        {
            // Full: Inherit everything from parent + all sibling results
            ContextIsolationMode.Full => new Dictionary<string, string>(parentContext)
                .Concat(siblingResults.Select(kv => new KeyValuePair<string, string>($"result_{kv.Key}", kv.Value)))
                .GroupBy(kv => kv.Key)
                .ToDictionary(g => g.Key, g => g.Last().Value),

            // Minimal: Only inherit explicit domain context + previous sibling's result
            ContextIsolationMode.Minimal => BuildMinimalContext(parentContext, siblingResults, currentStepId),

            // None: Start completely fresh
            ContextIsolationMode.None => new Dictionary<string, string>(),

            _ => new Dictionary<string, string>(parentContext)
        };
    }

    /// <summary>
    /// Build minimal context - only essential domain info + previous step's result.
    /// </summary>
    private static Dictionary<string, string> BuildMinimalContext(
        Dictionary<string, string> parentContext,
        Dictionary<string, string> siblingResults,
        string currentStepId)
    {
        var result = new Dictionary<string, string>();

        // Only inherit non-result keys from parent (domain context like "language", "style")
        foreach (var kv in parentContext)
        {
            if (!kv.Key.StartsWith("result_", StringComparison.OrdinalIgnoreCase))
            {
                result[kv.Key] = kv.Value;
            }
        }

        // Only include the immediately previous sibling's result (if any)
        if (siblingResults.Count > 0)
        {
            var lastResult = siblingResults.Last();
            result["previous_result"] = lastResult.Value;
        }

        return result;
    }

    // ============================================================
    //  Streaming Generation Helper
    // ============================================================

    /// <summary>
    /// Generate response using streaming API with real-time progress updates.
    /// This is used by Coordinator for internal LLM calls (composition, atomicity assessment).
    /// </summary>
    private async Task<string> GenerateResponseWithStreamingAsync(
        string prompt,
        string taskId,
        string operationType,
        CancellationToken ct,
        string? systemPrompt = null)
    {
        var sb = new StringBuilder();
        var proposalId = $"COORD_{operationType}_{Guid.NewGuid():N}"[..24];
        var tokenIndex = 0;
        var isFirstToken = true;

        Logger.LogTrace("[STREAMING] Coordinator {OpType} starting for task {TaskId}, proposalId={ProposalId}",
            operationType, taskId, proposalId);

        await foreach (var token in GenerateResponseStreamAsync(prompt, ct))
        {
            if (string.IsNullOrEmpty(token)) continue;

            sb.Append(token);

            if (isFirstToken)
            {
                Logger.LogTrace("[STREAMING] Coordinator {OpType} first token received: '{Token}'",
                    operationType, token.Length > 50 ? token[..50] + "..." : token);
            }

            // Report streaming progress
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Streaming,
                TaskId = taskId,
                Message = isFirstToken ? $"Coordinator {operationType} started..."
                        : $"Coordinator {operationType} generating...",
                Depth = _currentDepth,
                StreamingToken = new StreamingTokenProgress
                {
                    WorkerId = "COORDINATOR",
                    ProposalId = proposalId,
                    Token = token,
                    AccumulatedContent = sb.ToString(),
                    TokenIndex = tokenIndex++,
                    IsFirstToken = isFirstToken,
                    IsLastToken = false,
                    ProviderName = CustomConfig.LlmProviderName,
                    // Include prompts on first token for chat card display
                    SystemPrompt = isFirstToken ? systemPrompt : null,
                    UserPrompt = isFirstToken ? prompt : null
                }
            });

            isFirstToken = false;
        }

        // Send final token marker with prompts for card creation
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Streaming,
            TaskId = taskId,
            Message = $"Coordinator {operationType} completed",
            Depth = _currentDepth,
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = "COORDINATOR",
                ProposalId = proposalId,
                Token = string.Empty,
                AccumulatedContent = sb.ToString(),
                TokenIndex = tokenIndex,
                IsFirstToken = false,
                IsLastToken = true,
                ProviderName = CustomConfig.LlmProviderName,
                // Include prompts on final token for card creation
                SystemPrompt = systemPrompt,
                UserPrompt = prompt
            }
        });

        return sb.ToString();
    }
}

