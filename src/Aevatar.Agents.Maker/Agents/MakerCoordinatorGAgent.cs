using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Maker.Checkpoint;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER Coordinator Agent
//  Pure event-driven - ALL communication via Stream
//
//  Architecture (partial class files):
//  - MakerCoordinatorGAgent.cs     : Core state, API, event handlers
//  - MakerTaskExecutor.cs          : Task execution logic
//  - MakerVotingCoordinator.cs     : Voting with workers
//  - MakerProviderValidator.cs     : Provider discovery & validation
//  - MakerProgressReporter.cs      : Progress reporting & red flags
//  - MakerStateRecovery.cs         : State persistence
// ============================================================

/// <summary>
/// MAKER Coordinator Agent - orchestrates the MAKER workflow.
/// All communication with workers happens through events (no direct method calls).
/// 
/// Event Flow:
/// 1. Receives StartMakerTaskRequest -> Creates workers -> Publishes InitializeWorkerRequest (DOWN)
/// 2. Receives WorkerInitialized (from workers)
/// 3. Publishes GenerateProposalRequest (DOWN) -> Workers process
/// 4. Receives ProposalResult (from workers) -> Runs voting
/// 5. Publishes MakerTaskCompleted when done
/// </summary>
public partial class MakerCoordinatorGAgent : AIGAgentBase<MakerCoordinatorState, MakerCoordinatorConfig>
{
    // ============================================================
    //  Strategies (injected via SetDependencies)
    // ============================================================
    
    private IDecompositionStrategy _decomposer = new DefaultDecomposer();
    private ISolutionStrategy _solver = new DefaultSolver();
    private ICompositionStrategy _composer = new DefaultComposer();
    private IRedFlagStrategy _redFlagStrategy = new DefaultEnglishRedFlagStrategy();
    
    // ============================================================
    //  Checkpoint Manager (injected via SetDependencies)
    // ============================================================
    
    private TaskCheckpointManager? _checkpointManager;

    // ============================================================
    //  Runtime State (not persisted, rebuilt on activation)
    // ============================================================
    
    private readonly Stopwatch _stopwatch = new();
    private readonly List<RedFlagEvent> _redFlags = [];
    private Action<MakerProgress>? _progressCallback;

    // ============================================================
    //  Async Coordination State
    // ============================================================
    
    private TaskCompletionSource<bool>? _initCompletionSource;
    private TaskCompletionSource<VoteResult>? _consensusCompletionSource;
    private CancellationTokenSource? _votingCts;
    private VoteEngine? _currentVoteEngine;
    private readonly ConcurrentBag<ProposalResult> _collectedProposals = [];
    private int _workersInitialized;
    private int _expectedWorkers;
    private int _activeWorkerRequests;
    private string? _currentVotingRequestPrefix;
    private bool _isSolutionVoting;
    private int _currentDepth;

    // ============================================================
    //  Execution State
    // ============================================================
    
    private MakerResult? _cachedResult;
    private MakerOptions? _currentOptions;
    private Dictionary<string, string>? _currentContext;

    public MakerCoordinatorGAgent()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"MAKER Coordinator [{CustomState.ExecutionId}] - Workers: {CustomState.ActiveWorkers}");
    }

    #region Public API

    /// <summary>
    /// Set external dependencies (called before sending StartMakerTaskRequest).
    /// This is the ONLY direct call allowed - to inject non-serializable dependencies.
    /// </summary>
    public void SetDependencies(
        IDecompositionStrategy? decomposer = null,
        ISolutionStrategy? solver = null,
        ICompositionStrategy? composer = null,
        IRedFlagStrategy? redFlagStrategy = null,
        RedFlagOptions? redFlagOptions = null,
        Action<MakerProgress>? progressCallback = null,
        TaskCheckpointManager? checkpointManager = null)
    {
        _decomposer = decomposer ?? new DefaultDecomposer();
        _solver = solver ?? new DefaultSolver();
        _composer = composer ?? new DefaultComposer();
        _redFlagStrategy = redFlagStrategy ?? new DefaultEnglishRedFlagStrategy(redFlagOptions ?? new RedFlagOptions());
        _progressCallback = progressCallback;
        _checkpointManager = checkpointManager;
    }

    /// <summary>
    /// Get the execution result (called after checking status is completed).
    /// </summary>
    public MakerResult GetResult()
    {
        return _cachedResult ?? new MakerResult
        {
            Success = false,
            Content = string.Empty,
            Error = "Result not available",
            Trace = new MakerTrace
            {
                ExecutionId = CustomState.ExecutionId,
                RootTask = new TaskNode
                {
                    TaskId = CustomState.ExecutionId,
                    Description = CustomState.TaskDescription,
                    Depth = 0,
                    IsAtomic = false
                },
                TotalLLMCalls = GetTotalLlmCalls(),  // Includes embedding calls
                TotalTokens = CustomState.TotalTokensUsed,
                PromptTokens = CustomState.TotalPromptTokens,
                CompletionTokens = CustomState.TotalCompletionTokens,
                Duration = _stopwatch.Elapsed,
                RedFlags = _redFlags.ToList()
            }
        };
    }

    /// <summary>
    /// Get execution status (0=idle, 1=starting, 2=running, 3=completed, 4=failed, 5=cancelled).
    /// </summary>
    public int GetStatus() => (int)CustomState.ExecutionState;

    /// <summary>Get total LLM calls made (including embedding calls).</summary>
    public int GetTotalLlmCalls() => CustomState.TotalLlmCalls + (_currentVoteEngine?.EmbeddingCallCount ?? 0);

    /// <summary>Get total tokens consumed.</summary>
    public long GetTotalTokens() => CustomState.TotalTokensUsed;

    /// <summary>Get prompt tokens consumed.</summary>
    public long GetPromptTokens() => CustomState.TotalPromptTokens;

    /// <summary>Get completion tokens consumed.</summary>
    public long GetCompletionTokens() => CustomState.TotalCompletionTokens;

    #endregion

    // State Persistence (P0-2) -> MakerStateRecovery.cs

    #region Event Handlers

    /// <summary>
    /// Handle start task request - entry point for execution.
    /// </summary>
    [EventHandler]
    public async Task HandleStartMakerTaskRequest(StartMakerTaskRequest request)
    {
        Logger.LogInformation("Coordinator {Id} received start request for task: {TaskDescription}",
            Id, request.TaskDescription[..Math.Min(50, request.TaskDescription.Length)]);

        try
        {
            await InitializeExecutionStateAsync(request);
            await InitializeWorkersAsync(request);
            await ExecuteAndCompleteAsync(request);
        }
        catch (Exception ex)
        {
            await HandleExecutionFailureAsync(request, ex);
        }
    }

    /// <summary>
    /// Handle worker initialized event.
    /// </summary>
    [EventHandler]
    public Task HandleWorkerInitialized(WorkerInitialized evt)
    {
        Logger.LogDebug("Worker {WorkerId} initialized (index: {Index})", evt.WorkerId, evt.WorkerIndex);

        if (!CustomState.WorkerIds.Contains(evt.WorkerId))
        {
            CustomState.WorkerIds.Add(evt.WorkerId);
        }

        var count = Interlocked.Increment(ref _workersInitialized);
        CustomState.ActiveWorkers = count;

        if (count >= _expectedWorkers)
        {
            _initCompletionSource?.TrySetResult(true);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle real-time streaming tokens from workers for live UI updates.
    /// </summary>
    [EventHandler]
    public Task HandleStreamingToken(StreamingToken token)
    {
        if (token.IsFirstToken)
        {
            Logger.LogInformation("[STREAMING] ▶ Worker {WorkerId} ({Provider}) started: task={TaskId}",
                token.WorkerId, token.ProviderName, token.TaskId);
        }
        else if (token.IsLastToken)
        {
            Logger.LogInformation("[STREAMING] ✓ Worker {WorkerId} ({Provider}) completed: {ContentLen} chars",
                token.WorkerId, token.ProviderName, token.AccumulatedContent?.Length ?? 0);
        }
        
        // Forward streaming token to progress callback for real-time UI display
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Streaming,
            TaskId = token.TaskId,
            Message = token.IsFirstToken ? $"Worker {token.WorkerId} started generating..." 
                    : token.IsLastToken ? $"Worker {token.WorkerId} completed generation"
                    : $"Worker {token.WorkerId} generating...",
            Depth = _currentDepth,
            StreamingToken = new StreamingTokenProgress
            {
                WorkerId = token.WorkerId,
                ProposalId = token.ProposalId,
                Token = token.Token,
                AccumulatedContent = token.AccumulatedContent ?? "",
                TokenIndex = token.TokenIndex,
                IsFirstToken = token.IsFirstToken,
                IsLastToken = token.IsLastToken,
                ProviderName = token.ProviderName,
                // Chat context for SYSTEM_NODES display (only populated on first token)
                SystemPrompt = token.IsFirstToken ? token.SystemPrompt : null,
                UserPrompt = token.IsFirstToken ? token.UserPrompt : null
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>
    /// Handle proposal results from workers - STREAMING RACE PATTERN.
    /// </summary>
    [EventHandler]
    public async Task HandleProposalResult(ProposalResult result)
    {
        var recvMsg = $">>> [PROPOSAL-RECV] {result.ProposalId} from {result.WorkerId} ({result.ProviderName}), RequestId='{result.RequestId}', CurrentPrefix='{_currentVotingRequestPrefix ?? "null"}', ContentLen={result.Content?.Length ?? 0}";
        Logger.LogWarning(recvMsg);  // Use Warning level for visibility

        // Check if this proposal belongs to current voting session
        if (_currentVotingRequestPrefix == null || !result.RequestId.StartsWith(_currentVotingRequestPrefix))
        {
            var discardMsg = $">>> [PROPOSAL-DISCARD] {result.ProposalId} from {result.WorkerId}: RequestId='{result.RequestId}' vs Prefix='{_currentVotingRequestPrefix ?? "null"}'";
            Logger.LogWarning(discardMsg);
            
            // Don't report to UI - these are normal early termination results
            // The proposals are not "late" in a bad way, they're just no longer needed
            // because we already achieved consensus with earlier proposals
            return;
        }

        // Check if voting already completed (early termination)
        if (_consensusCompletionSource?.Task.IsCompleted == true)
        {
            Logger.LogDebug("Ignoring late proposal {ProposalId} - consensus already reached", result.ProposalId);
            return;
        }

        await ProcessProposalAsync(result);
    }

    #endregion

    #region Event Handler Helpers

    private async Task InitializeExecutionStateAsync(StartMakerTaskRequest request)
    {
            // Auto-fallback: if requested provider doesn't exist, use first available
            var providerToUse = request.ProviderName;
            var availableProviders = RequireLLMProviderFactory().GetAvailableProviderNames();
            if (!availableProviders.Contains(providerToUse) && availableProviders.Count > 0)
            {
                providerToUse = availableProviders[0];
                Logger.LogInformation(
                    "Provider '{RequestedProvider}' not found, using '{FallbackProvider}' instead",
                    request.ProviderName, providerToUse);
            }

            CustomState.ExecutionId = request.ExecutionId;
            CustomState.TaskDescription = request.TaskDescription;
            CustomState.ConsensusK = request.ConsensusK;
            CustomState.SamplesPerRound = request.SamplesPerRound;
        CustomState.ExecutionState = MakerExecutionState.MakerStateInitializingWorkers;
            CustomState.StartedAt = Timestamp.FromDateTime(DateTime.UtcNow);
            CustomState.TotalLlmCalls = 0;
            CustomState.RedFlagReasons.Clear();
            _redFlags.Clear();

            CustomState.MaxTotalLlmCalls = request.MaxTotalLlmCalls > 0 ? request.MaxTotalLlmCalls : 100;
            CustomState.MaxTotalTokens = request.MaxTotalTokens > 0 ? request.MaxTotalTokens : 500_000;
        CustomState.MaxDurationMs = request.MaxDurationMs > 0 ? request.MaxDurationMs : 600_000;

            CustomConfig.DefaultConsensusK = request.ConsensusK;
            CustomConfig.BaseTemperature = request.BaseTemperature;
            CustomConfig.TemperatureVariance = request.TemperatureVariance;
            CustomConfig.SemanticSimilarityThreshold = request.SemanticSimilarityThreshold;
            CustomConfig.ClusteringMethod = request.ClusteringMethod;
            CustomConfig.LlmProviderName = providerToUse;
            CustomConfig.WorkerPoolSize = request.SamplesPerRound;
            CustomConfig.MaxTotalLlmCalls = CustomState.MaxTotalLlmCalls;
            CustomConfig.MaxTotalTokens = CustomState.MaxTotalTokens;
            CustomConfig.MaxDurationMs = CustomState.MaxDurationMs;
            CustomConfig.DepthWarningThreshold = request.DepthWarningThreshold > 0 ? request.DepthWarningThreshold : 10;

            _currentOptions = new MakerOptions
            {
                CustomK = request.ConsensusK,
                MaxTotalLlmCalls = CustomState.MaxTotalLlmCalls,
                MaxTotalTokens = CustomState.MaxTotalTokens,
                MaxDuration = TimeSpan.FromMilliseconds(CustomState.MaxDurationMs),
                DepthWarningThreshold = CustomConfig.DepthWarningThreshold,
                BaseTemperature = request.BaseTemperature,
                TemperatureVariance = request.TemperatureVariance,
                SemanticSimilarityThreshold = request.SemanticSimilarityThreshold,
                ClusteringMethod = request.ClusteringMethod,
                ProviderName = providerToUse,
                Decomposer = _decomposer,
                Solver = _solver,
                Composer = _composer,
                OnProgress = _progressCallback
            };

            _currentContext = request.Context != null
                ? new Dictionary<string, string>(request.Context)
                : new Dictionary<string, string>();

            await InitializeAsync(providerToUse, config =>
            {
                config.Temperature = request.BaseTemperature;
                config.MaxOutputTokens = 4096;
            });

            _stopwatch.Restart();

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = request.ExecutionId,
            Message = $"MAKER System Online - K={request.ConsensusK}, N={request.SamplesPerRound}, Budget: {CustomState.MaxTotalLlmCalls} calls / {CustomState.MaxTotalTokens:N0} tokens",
                Depth = 0
            });
    }

    private async Task InitializeWorkersAsync(StartMakerTaskRequest request)
    {
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Starting,
                TaskId = request.ExecutionId,
                Message = $"Initializing {request.SamplesPerRound} worker agents for parallel LLM execution",
                Depth = 0
            });

            _expectedWorkers = request.SamplesPerRound;
            _workersInitialized = 0;
            _initCompletionSource = new TaskCompletionSource<bool>();

            var (validProviders, coordinatorProvider) = await DiscoverAndValidateProvidersAsync(
                request.UseMultipleProviders,
                request.CoordinatorProviderName,
                request.ProviderName,
                request.ExecutionId);

            if (coordinatorProvider != request.ProviderName)
            {
                await InitializeAsync(coordinatorProvider, config =>
                {
                    config.Temperature = request.BaseTemperature;
                    config.MaxOutputTokens = 4096;
                });
                Logger.LogInformation("Coordinator re-initialized with provider: {Provider}", coordinatorProvider);
            }

            for (var i = 0; i < request.SamplesPerRound; i++)
            {
                var workerTemp = DecorrelateTemperature(request.BaseTemperature, i, request.TemperatureVariance);
                var workerProvider = validProviders[i % validProviders.Count];

                await PublishAsync(new InitializeWorkerRequest
                {
                    CoordinatorId = Id.ToString(),
                    ProviderName = workerProvider,
                    WorkerIndex = i,
                    Temperature = workerTemp
                }, EventDirection.Down);

                Logger.LogDebug("Worker {Index} assigned to provider: {Provider}", i, workerProvider);
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _initCompletionSource.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
            throw new TimeoutException($"Workers failed to initialize within timeout. Got {_workersInitialized}/{_expectedWorkers}");
            }

            Logger.LogInformation("All {Count} workers initialized", _expectedWorkers);
    }

    private async Task ExecuteAndCompleteAsync(StartMakerTaskRequest request)
    {
        // Start of execution (runtime will refine phases via progress events).
        CustomState.ExecutionState = MakerExecutionState.MakerStateAssessingAtomicity;

            var (result, node) = await ExecuteTaskAsync(
                taskId: request.ExecutionId,
                description: request.TaskDescription,
                depth: 0,
            context: _currentContext!,
                ct: CancellationToken.None);

            _stopwatch.Stop();

            var success = result != null;
            CustomState.ExecutionState = success
                ? MakerExecutionState.MakerStateCompleted
                : MakerExecutionState.MakerStateFailed;
            CustomState.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

            _cachedResult = new MakerResult
            {
                Success = success,
                Content = result ?? string.Empty,
                Error = success ? null : "Task execution failed",
                Trace = new MakerTrace
                {
                    ExecutionId = request.ExecutionId,
                    RootTask = node,
                    TotalLLMCalls = GetTotalLlmCalls(),  // Includes embedding calls
                    Duration = _stopwatch.Elapsed,
                    RedFlags = _redFlags.ToList(),
                    TotalTokens = CustomState.TotalTokensUsed,
                    PromptTokens = CustomState.TotalPromptTokens,
                    CompletionTokens = CustomState.TotalCompletionTokens
                }
            };

            PersistRuntimeState();

            ReportProgress(new MakerProgress
            {
                Phase = success ? MakerPhase.Completed : MakerPhase.Failed,
                TaskId = request.ExecutionId,
                Message = success ? "Execution completed successfully" : "Execution failed",
                Depth = 0
            });

            await PublishAsync(new MakerTaskCompleted
            {
                ExecutionId = request.ExecutionId,
                Success = success,
                Content = result ?? string.Empty,
                Error = success ? string.Empty : "Task execution failed",
                TotalLlmCalls = GetTotalLlmCalls(),  // Includes embedding calls
                DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds,
            TraceJson = JsonSerializer.Serialize(_cachedResult.Trace)
            });
        }

    private async Task HandleExecutionFailureAsync(StartMakerTaskRequest request, Exception ex)
        {
            Logger.LogError(ex, "Coordinator {Id} failed during execution", Id);
            _stopwatch.Stop();
            CustomState.ExecutionState = MakerExecutionState.MakerStateFailed;

            _cachedResult = new MakerResult
            {
                Success = false,
                Content = string.Empty,
                Error = ex.Message,
                Trace = new MakerTrace
                {
                    ExecutionId = request.ExecutionId,
                    RootTask = new TaskNode
                    {
                        TaskId = request.ExecutionId,
                        Description = request.TaskDescription,
                        Depth = 0,
                        IsAtomic = false
                    },
                    TotalLLMCalls = GetTotalLlmCalls(),  // Includes embedding calls
                    Duration = _stopwatch.Elapsed,
                    RedFlags = _redFlags.ToList(),
                    TotalTokens = CustomState.TotalTokensUsed,
                    PromptTokens = CustomState.TotalPromptTokens,
                    CompletionTokens = CustomState.TotalCompletionTokens
                }
            };

            await PublishAsync(new MakerTaskCompleted
            {
                ExecutionId = request.ExecutionId,
                Success = false,
                Error = ex.Message,
                TotalLlmCalls = GetTotalLlmCalls(),  // Includes embedding calls
                DurationMs = (long)_stopwatch.Elapsed.TotalMilliseconds
            });
    }

    private async Task ProcessProposalAsync(ProposalResult result)
    {
        _collectedProposals.Add(result);
        CustomState.PendingProposals = _collectedProposals.Count;
        CustomState.TotalLlmCalls++;

        if (result.PromptTokens > 0 || result.CompletionTokens > 0)
        {
            CustomState.TotalPromptTokens += result.PromptTokens;
            CustomState.TotalCompletionTokens += result.CompletionTokens;
            CustomState.TotalTokensUsed = CustomState.TotalPromptTokens + CustomState.TotalCompletionTokens;
        }

        var providerLabel = string.IsNullOrEmpty(result.ProviderName) ? "" : $" [{result.ProviderName}]";
        
        // ============================================================
        // 📋 Detailed proposal logging
        // ============================================================
        var contentPreview = result.Content?.Length > 200 
            ? result.Content[..200].Replace("\n", " ") + "..." 
            : result.Content?.Replace("\n", " ") ?? "(empty)";
        var votingType = _isSolutionVoting ? "SOLUTION" : "DECOMPOSITION";
        
        Logger.LogInformation(
            "[{VotingType}] Worker {WorkerId}{Provider} submitted proposal #{ProposalNum}:\n" +
            "  📄 Content preview: {ContentPreview}\n" +
            "  📊 Tokens: {PromptTokens} prompt + {CompletionTokens} completion = {TotalTokens} total\n" +
            "  ⏱️ Latency: {LatencyMs}ms",
            votingType,
            result.WorkerId,
            providerLabel,
            _collectedProposals.Count,
            contentPreview,
            result.PromptTokens,
            result.CompletionTokens,
            result.TotalTokens,
            result.LatencyMs);
        
        ReportProgress(new MakerProgress
        {
            Phase = MakerPhase.Voting,
            TaskId = result.TaskId,
            Message = $"Received proposal #{_collectedProposals.Count} from {result.WorkerId}{providerLabel}",
            Depth = _currentDepth,
            Proposal = new LLMProposal
            {
                ProposalId = result.ProposalId,
                WorkerId = result.WorkerId,
                Content = result.Content ?? "",
                Success = result.Success,
                Error = result.Error ?? "",
                PromptTokens = result.PromptTokens,
                CompletionTokens = result.CompletionTokens,
                LatencyMs = result.LatencyMs,
                ProviderName = result.ProviderName
            }
        });

        if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
        {
            Logger.LogWarning("[{VotingType}] Worker {WorkerId} proposal FAILED: {Error}", 
                votingType, result.WorkerId, result.Error);
            Interlocked.Decrement(ref _activeWorkerRequests);
            return;
        }

        var isValid = _redFlagStrategy.Validate(result.Content, result.ProposalId, out var redFlagReason);
        if (!isValid)
        {
            AddRedFlag(result.TaskId, redFlagReason!);
            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.RedFlag,
                TaskId = result.TaskId,
                Message = $"🚩 Proposal {result.ProposalId} rejected: {redFlagReason}",
                Depth = _currentDepth
            });
            Interlocked.Decrement(ref _activeWorkerRequests);
            return;
        }

        if (_currentVoteEngine != null)
        {
            var cleanedContent = _isSolutionVoting
                ? _solver.ExtractSolution(result.Content)
                : result.Content;

            var voteResult = await _currentVoteEngine.SubmitVoteAsync(cleanedContent, default);
            var progress = _currentVoteEngine.GetProgress(_isSolutionVoting ? VotingType.Solution : VotingType.Decomposition);

            ReportProgress(new MakerProgress
            {
                Phase = MakerPhase.Voting,
                TaskId = result.TaskId,
                Message = voteResult != null
                    ? $"✓ CONSENSUS REACHED after {_collectedProposals.Count} proposals! Early termination."
                    : $"Voting (clusters: {progress.ClusterCount}, leader: {progress.LeaderVotes}/{progress.VotesNeeded})",
                Depth = _currentDepth,
                Voting = progress
            });

            if (voteResult != null)
            {
                // ============================================================
                // ✅ CONSENSUS REACHED - Log the winning content
                // ============================================================
                var winningPreview = voteResult.WinningContent?.Length > 500
                    ? voteResult.WinningContent[..500].Replace("\n", " ") + "..."
                    : voteResult.WinningContent?.Replace("\n", " ") ?? "(empty)";
                
                Logger.LogInformation(
                    "\n╔══════════════════════════════════════════════════════════════╗\n" +
                    "║ ✅ CONSENSUS REACHED ({VotingType})                           \n" +
                    "╠══════════════════════════════════════════════════════════════╣\n" +
                    "║ 📊 Votes: {LeaderVotes}/{VotesNeeded} (K={K})                 \n" +
                    "║ 📝 Proposals collected: {ProposalCount}                       \n" +
                    "║ 🏆 Winning content preview:                                   \n" +
                    "║    {WinningPreview}                                           \n" +
                    "╚══════════════════════════════════════════════════════════════╝",
                    _isSolutionVoting ? "SOLUTION" : "DECOMPOSITION",
                    voteResult.LeaderVotes,
                    progress.VotesNeeded,
                    _currentOptions?.ConsensusK ?? 0,
                    _collectedProposals.Count,
                    winningPreview);

                _consensusCompletionSource?.TrySetResult(voteResult);
                _votingCts?.Cancel();

                Logger.LogWarning("[CANCEL] Broadcasting CancelCurrentRequest to all workers (reason: consensus_reached)");
                await PublishAsync(new CancelCurrentRequest
                {
                    CoordinatorId = Id.ToString(),
                    Reason = "consensus_reached"
                }, EventDirection.Down);

                return;
            }
        }

        Interlocked.Decrement(ref _activeWorkerRequests);
    }

    #endregion
}
