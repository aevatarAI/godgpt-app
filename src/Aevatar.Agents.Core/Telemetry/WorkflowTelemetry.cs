using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  Workflow Telemetry - Comprehensive Cognitive Workflow Monitoring
//  Step Tracking, Parallel Execution, Vote Consensus
// ============================================================

/// <summary>
/// Workflow execution telemetry.
/// Provides comprehensive monitoring for workflows, steps, and parallel tasks.
/// </summary>
public static class WorkflowTelemetry
{
    // ============================================================
    //  Source Configuration
    // ============================================================

    public const string SourceName = "Aevatar.Workflow";
    public const string MeterName = "Aevatar.Workflow";
    public const string Version = "1.0.0";

    private static readonly ActivitySource ActivitySource = new(SourceName, Version);
    private static readonly Meter Meter = new(MeterName, Version);

    // ============================================================
    //  Counters - Cumulative Counts
    // ============================================================

    private static readonly Counter<long> WorkflowsStarted = Meter.CreateCounter<long>(
        "aevatar.workflow.started.total",
        "workflows",
        "Total workflows started");

    private static readonly Counter<long> WorkflowsCompleted = Meter.CreateCounter<long>(
        "aevatar.workflow.completed.total",
        "workflows",
        "Total workflows completed successfully");

    private static readonly Counter<long> WorkflowsFailed = Meter.CreateCounter<long>(
        "aevatar.workflow.failed.total",
        "workflows",
        "Total workflows failed");

    private static readonly Counter<long> StepsExecuted = Meter.CreateCounter<long>(
        "aevatar.workflow.steps.executed.total",
        "steps",
        "Total workflow steps executed");

    private static readonly Counter<long> StepsFailed = Meter.CreateCounter<long>(
        "aevatar.workflow.steps.failed.total",
        "steps",
        "Total workflow steps failed");

    private static readonly Counter<long> ParallelTasksDispatched = Meter.CreateCounter<long>(
        "aevatar.workflow.parallel.dispatched.total",
        "tasks",
        "Total parallel tasks dispatched (fan-out)");

    private static readonly Counter<long> ParallelTasksCompleted = Meter.CreateCounter<long>(
        "aevatar.workflow.parallel.completed.total",
        "tasks",
        "Total parallel tasks completed");

    private static readonly Counter<long> VoteRounds = Meter.CreateCounter<long>(
        "aevatar.workflow.vote.rounds.total",
        "rounds",
        "Total voting rounds");

    private static readonly Counter<long> VoteConsensusReached = Meter.CreateCounter<long>(
        "aevatar.workflow.vote.consensus.total",
        "votes",
        "Total times consensus was reached");

    private static readonly Counter<long> RedFlagsDetected = Meter.CreateCounter<long>(
        "aevatar.workflow.redflags.total",
        "flags",
        "Total red flags detected in LLM responses");

    private static readonly Counter<long> WorkflowTokens = Meter.CreateCounter<long>(
        "aevatar.workflow.tokens.total",
        "tokens",
        "Total tokens consumed by workflows");

    private static readonly Counter<long> WorkflowLLMCalls = Meter.CreateCounter<long>(
        "aevatar.workflow.llm_calls.total",
        "calls",
        "Total LLM calls made by workflows");

    // ============================================================
    //  Histograms - Latency and Distribution
    // ============================================================

    private static readonly Histogram<double> WorkflowDuration = Meter.CreateHistogram<double>(
        "aevatar.workflow.duration",
        "ms",
        "Workflow execution duration");

    private static readonly Histogram<double> StepDuration = Meter.CreateHistogram<double>(
        "aevatar.workflow.step.duration",
        "ms",
        "Step execution duration");

    private static readonly Histogram<double> FanOutDuration = Meter.CreateHistogram<double>(
        "aevatar.workflow.fanout.duration",
        "ms",
        "Fan-out parallel execution duration");

    private static readonly Histogram<double> VoteDuration = Meter.CreateHistogram<double>(
        "aevatar.workflow.vote.duration",
        "ms",
        "Voting step duration");

    private static readonly Histogram<int> FanOutSize = Meter.CreateHistogram<int>(
        "aevatar.workflow.fanout.size",
        "tasks",
        "Number of parallel tasks in fan-out");

    private static readonly Histogram<int> VoteRoundsToConsensus = Meter.CreateHistogram<int>(
        "aevatar.workflow.vote.rounds_to_consensus",
        "rounds",
        "Rounds needed to reach consensus");

    private static readonly Histogram<int> WorkflowStepCount = Meter.CreateHistogram<int>(
        "aevatar.workflow.steps.count",
        "steps",
        "Number of steps in workflow");

    private static readonly Histogram<int> WorkflowDepth = Meter.CreateHistogram<int>(
        "aevatar.workflow.depth",
        "levels",
        "Workflow recursion depth");

    // ============================================================
    //  Gauges - Current State
    // ============================================================

    private static long _activeWorkflows;
    private static long _activeParallelTasks;
    private static long _activeVoteSessions;

    static WorkflowTelemetry()
    {
        Meter.CreateObservableGauge(
            "aevatar.workflow.active",
            () => Interlocked.Read(ref _activeWorkflows),
            "workflows",
            "Currently executing workflows");

        Meter.CreateObservableGauge(
            "aevatar.workflow.parallel.active",
            () => Interlocked.Read(ref _activeParallelTasks),
            "tasks",
            "Currently executing parallel tasks");

        Meter.CreateObservableGauge(
            "aevatar.workflow.vote.active",
            () => Interlocked.Read(ref _activeVoteSessions),
            "sessions",
            "Active voting sessions");
    }

    // ============================================================
    //  Workflow Lifecycle Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start workflow execution tracing.
    /// </summary>
    public static Activity? StartWorkflow(
        string executionId,
        string workflowName,
        Guid coordinatorId)
    {
        Interlocked.Increment(ref _activeWorkflows);

        var activity = ActivitySource.StartActivity("workflow.execute", ActivityKind.Internal);
        activity?.SetTag("workflow.execution_id", executionId);
        activity?.SetTag("workflow.name", workflowName);
        activity?.SetTag("workflow.coordinator_id", coordinatorId.ToString());

        WorkflowsStarted.Add(1,
            new KeyValuePair<string, object?>("workflow.name", workflowName));

        return activity;
    }

    /// <summary>
    /// Record workflow completion.
    /// </summary>
    public static void RecordWorkflowCompleted(
        Activity? activity,
        string workflowName,
        double durationMs,
        int totalSteps,
        int totalTokens,
        int totalLLMCalls,
        int maxDepth = 0)
    {
        Interlocked.Decrement(ref _activeWorkflows);

        var tags = new[]
        {
            new KeyValuePair<string, object?>("workflow.name", workflowName)
        };

        WorkflowsCompleted.Add(1, tags);
        WorkflowDuration.Record(durationMs, tags);
        WorkflowStepCount.Record(totalSteps, tags);
        WorkflowTokens.Add(totalTokens, tags);
        WorkflowLLMCalls.Add(totalLLMCalls, tags);

        if (maxDepth > 0)
        {
            WorkflowDepth.Record(maxDepth, tags);
        }

        if (activity != null)
        {
            activity.SetTag("workflow.duration.ms", durationMs);
            activity.SetTag("workflow.steps.total", totalSteps);
            activity.SetTag("workflow.tokens.total", totalTokens);
            activity.SetTag("workflow.llm_calls.total", totalLLMCalls);
            activity.SetTag("workflow.depth.max", maxDepth);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Record workflow failure.
    /// </summary>
    public static void RecordWorkflowFailed(
        Activity? activity,
        string workflowName,
        double durationMs,
        string error,
        string? failedStepId = null)
    {
        Interlocked.Decrement(ref _activeWorkflows);

        var tags = new[]
        {
            new KeyValuePair<string, object?>("workflow.name", workflowName)
        };

        WorkflowsFailed.Add(1, tags);
        WorkflowDuration.Record(durationMs, tags);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
            activity.SetTag("workflow.error", error);
            activity.SetTag("workflow.duration.ms", durationMs);

            if (failedStepId != null)
            {
                activity.SetTag("workflow.failed_step", failedStepId);
            }
        }
    }

    // ============================================================
    //  Step Execution Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start step execution tracing.
    /// </summary>
    public static Activity? StartStep(
        string executionId,
        string stepId,
        string stepType,
        int depth = 0)
    {
        var activity = ActivitySource.StartActivity("workflow.step", ActivityKind.Internal);
        activity?.SetTag("workflow.execution_id", executionId);
        activity?.SetTag("step.id", stepId);
        activity?.SetTag("step.type", stepType);
        activity?.SetTag("step.depth", depth);

        return activity;
    }

    /// <summary>
    /// Record step completion.
    /// </summary>
    public static void RecordStepCompleted(
        Activity? activity,
        string stepId,
        string stepType,
        double durationMs,
        int tokensUsed = 0,
        int llmCalls = 0)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("step.type", stepType)
        };

        StepsExecuted.Add(1, tags);
        StepDuration.Record(durationMs, tags);

        if (activity != null)
        {
            activity.SetTag("step.duration.ms", durationMs);
            activity.SetTag("step.tokens", tokensUsed);
            activity.SetTag("step.llm_calls", llmCalls);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    /// <summary>
    /// Record step failure.
    /// </summary>
    public static void RecordStepFailed(
        Activity? activity,
        string stepId,
        string stepType,
        double durationMs,
        string error)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("step.type", stepType)
        };

        StepsFailed.Add(1, tags);
        StepDuration.Record(durationMs, tags);

        if (activity != null)
        {
            activity.SetStatus(ActivityStatusCode.Error, error);
            activity.SetTag("step.error", error);
            activity.SetTag("step.duration.ms", durationMs);
        }
    }

    // ============================================================
    //  Fan-Out/Parallel Execution Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start fan-out execution tracing.
    /// </summary>
    public static Activity? StartFanOut(
        string executionId,
        string stepId,
        int taskCount,
        int workerCount)
    {
        Interlocked.Add(ref _activeParallelTasks, taskCount);

        var activity = ActivitySource.StartActivity("workflow.fanout", ActivityKind.Internal);
        activity?.SetTag("workflow.execution_id", executionId);
        activity?.SetTag("fanout.step_id", stepId);
        activity?.SetTag("fanout.task_count", taskCount);
        activity?.SetTag("fanout.worker_count", workerCount);

        ParallelTasksDispatched.Add(taskCount,
            new KeyValuePair<string, object?>("step.id", stepId));

        FanOutSize.Record(taskCount);

        return activity;
    }

    /// <summary>
    /// Record single parallel task completion.
    /// </summary>
    public static void RecordParallelTaskCompleted(
        string stepId,
        bool success)
    {
        Interlocked.Decrement(ref _activeParallelTasks);

        ParallelTasksCompleted.Add(1,
            new KeyValuePair<string, object?>("step.id", stepId),
            new KeyValuePair<string, object?>("success", success));
    }

    /// <summary>
    /// Record fan-out completion.
    /// </summary>
    public static void RecordFanOutCompleted(
        Activity? activity,
        string stepId,
        double durationMs,
        int successCount,
        int failCount)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("step.id", stepId)
        };

        FanOutDuration.Record(durationMs, tags);

        if (activity != null)
        {
            activity.SetTag("fanout.duration.ms", durationMs);
            activity.SetTag("fanout.success_count", successCount);
            activity.SetTag("fanout.fail_count", failCount);
            activity.SetStatus(failCount == 0 ? ActivityStatusCode.Ok : ActivityStatusCode.Error);
        }
    }

    // ============================================================
    //  Vote Execution Traces + Metrics
    // ============================================================

    /// <summary>
    /// Start vote tracing.
    /// </summary>
    public static Activity? StartVote(
        string executionId,
        string stepId,
        int requiredVotes,
        int maxRounds)
    {
        Interlocked.Increment(ref _activeVoteSessions);

        var activity = ActivitySource.StartActivity("workflow.vote", ActivityKind.Internal);
        activity?.SetTag("workflow.execution_id", executionId);
        activity?.SetTag("vote.step_id", stepId);
        activity?.SetTag("vote.required", requiredVotes);
        activity?.SetTag("vote.max_rounds", maxRounds);

        return activity;
    }

    /// <summary>
    /// Record vote round.
    /// </summary>
    public static void RecordVoteRound(
        Activity? activity,
        string stepId,
        int roundNumber,
        int currentVotes,
        bool hasRedFlag = false,
        string? redFlagReason = null)
    {
        VoteRounds.Add(1,
            new KeyValuePair<string, object?>("step.id", stepId));

        if (hasRedFlag)
        {
            RedFlagsDetected.Add(1,
                new KeyValuePair<string, object?>("step.id", stepId),
                new KeyValuePair<string, object?>("reason", redFlagReason ?? "unknown"));
        }

        activity?.AddEvent(new ActivityEvent("vote.round",
            tags: new ActivityTagsCollection
            {
                { "round", roundNumber },
                { "votes", currentVotes },
                { "red_flag", hasRedFlag }
            }));
    }

    /// <summary>
    /// Record vote completion.
    /// </summary>
    public static void RecordVoteCompleted(
        Activity? activity,
        string stepId,
        double durationMs,
        int roundsUsed,
        int finalVotes,
        bool consensusReached,
        int redFlagCount = 0)
    {
        Interlocked.Decrement(ref _activeVoteSessions);

        var tags = new[]
        {
            new KeyValuePair<string, object?>("step.id", stepId)
        };

        VoteDuration.Record(durationMs, tags);
        VoteRoundsToConsensus.Record(roundsUsed, tags);

        if (consensusReached)
        {
            VoteConsensusReached.Add(1, tags);
        }

        if (activity != null)
        {
            activity.SetTag("vote.duration.ms", durationMs);
            activity.SetTag("vote.rounds_used", roundsUsed);
            activity.SetTag("vote.final_votes", finalVotes);
            activity.SetTag("vote.consensus", consensusReached);
            activity.SetTag("vote.red_flags", redFlagCount);
            activity.SetStatus(ActivityStatusCode.Ok);
        }
    }

    // ============================================================
    //  Worker Tracking
    // ============================================================

    /// <summary>
    /// Start worker task tracing.
    /// </summary>
    public static Activity? StartWorkerTask(
        Guid workerId,
        string requestId,
        string stepId,
        string stepType)
    {
        var activity = ActivitySource.StartActivity("workflow.worker", ActivityKind.Consumer);
        activity?.SetTag("worker.id", workerId.ToString());
        activity?.SetTag("worker.request_id", requestId);
        activity?.SetTag("worker.step_id", stepId);
        activity?.SetTag("worker.step_type", stepType);
        return activity;
    }

    /// <summary>
    /// Record worker task completion.
    /// </summary>
    public static void RecordWorkerTaskCompleted(
        Activity? activity,
        double durationMs,
        int tokensUsed,
        bool success,
        string? error = null)
    {
        if (activity != null)
        {
            activity.SetTag("worker.duration.ms", durationMs);
            activity.SetTag("worker.tokens", tokensUsed);

            if (success)
            {
                activity.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity.SetStatus(ActivityStatusCode.Error, error);
                activity.SetTag("worker.error", error);
            }
        }
    }

    // ============================================================
    //  Checkpoint Tracking
    // ============================================================

    /// <summary>
    /// Record checkpoint.
    /// </summary>
    public static void RecordCheckpoint(
        Activity? activity,
        string executionId,
        string stepId,
        int tokensUsed,
        int llmCalls)
    {
        activity?.AddEvent(new ActivityEvent("workflow.checkpoint",
            tags: new ActivityTagsCollection
            {
                { "execution_id", executionId },
                { "step_id", stepId },
                { "tokens_used", tokensUsed },
                { "llm_calls", llmCalls }
            }));
    }
}
