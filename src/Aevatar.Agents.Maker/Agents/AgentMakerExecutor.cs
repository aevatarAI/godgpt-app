using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Core.Hierarchy;
using Aevatar.Agents.Maker.Checkpoint;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  Agent-based MAKER Executor
//  Uses Aevatar Actor Framework with proper hierarchy
//
//  Note: LLM resilience (retry, circuit breaker) is built into
//  AevatarLLMProviderBase - Workers automatically benefit from it.
// ============================================================

/// <summary>
/// MAKER executor using Aevatar Agent Framework.
/// Creates Coordinator and Worker Actors with proper parent-child relationships.
/// All task communication happens through Stream events.
/// </summary>
public sealed class AgentMakerExecutor : IMakerExecutor
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<AgentMakerExecutor> _logger;
    private readonly string _defaultProviderName;
    
    // Custom strategies from DI
    private readonly IDecompositionStrategy _decomposer;
    private readonly ISolutionStrategy _solver;
    private readonly ICompositionStrategy _composer;
    
    // Checkpoint store for recovery
    private readonly ICheckpointStore _checkpointStore;

    public AgentMakerExecutor(
        IGAgentActorFactory actorFactory,
        ILogger<AgentMakerExecutor> logger,
        string? defaultProviderName = null,
        IDecompositionStrategy? decomposer = null,
        ISolutionStrategy? solver = null,
        ICompositionStrategy? composer = null,
        ICheckpointStore? checkpointStore = null)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _defaultProviderName = defaultProviderName ?? "default";
        _decomposer = decomposer ?? new DefaultDecomposer();
        _solver = solver ?? new DefaultSolver();
        _composer = composer ?? new DefaultComposer();
        _checkpointStore = checkpointStore ?? new InMemoryCheckpointStore();
    }

    /// <inheritdoc />
    public async Task<MakerResult> ExecuteAsync(
        string taskDescription,
        MakerOptions? options = null,
        CancellationToken ct = default)
    {
        options ??= new MakerOptions();
        var executionId = Guid.NewGuid();
        var workerCount = options.SamplesPerRound;

        _logger.LogInformation(
            "Starting MAKER execution {ExecutionId} with {WorkerCount} worker agents",
            executionId.ToString("N")[..8],
            workerCount);

        IGAgentActor? coordinatorActor = null;
        var workerActors = new List<IGAgentActor>();

        try
        {
            // Step 1: Create Coordinator Actor
            coordinatorActor = await _actorFactory.CreateGAgentActorAsync<MakerCoordinatorGAgent>(executionId.ToString(), ct);
            _logger.LogDebug("Created coordinator actor {CoordinatorId}", executionId);

            // Step 2: Inject dependencies into coordinator via GetAgent()
            var coordinator = coordinatorActor.GetAgent() as MakerCoordinatorGAgent;
            if (coordinator == null)
            {
                throw new InvalidOperationException("Failed to get MakerCoordinatorGAgent from actor");
            }
            
            // Initialize checkpoint manager if checkpointing is enabled
            TaskCheckpointManager? checkpointManager = null;
            if (options.EnableCheckpointing)
            {
                var store = !string.IsNullOrEmpty(options.CheckpointDirectory)
                    ? new FileCheckpointStore(options.CheckpointDirectory, _logger)
                    : _checkpointStore;
                checkpointManager = new TaskCheckpointManager(store, _logger);
                checkpointManager.BeginExecution(
                    executionId.ToString("N")[..8],
                    taskDescription,
                    options);
                _logger.LogInformation("[CHECKPOINT] Checkpoint tracking enabled for execution {ExecutionId}", 
                    executionId.ToString("N")[..8]);
            }
            
            coordinator.SetDependencies(
                decomposer: options.Decomposer ?? _decomposer,
                solver: options.Solver ?? _solver,
                composer: options.Composer ?? _composer,
                redFlagStrategy: options.RedFlagStrategy,
                redFlagOptions: options.RedFlagOptions,
                progressCallback: options.OnProgress,
                checkpointManager: checkpointManager);

            // Step 3: Create Worker Actors and establish parent-child relationships
            // Note: Workers automatically benefit from LLM resilience built into Provider
            for (var i = 0; i < workerCount; i++)
            {
                var workerId = Guid.NewGuid().ToString();
                var workerActor = await _actorFactory.CreateGAgentActorAsync<MakerWorkerGAgent>(workerId, ct);
                
                // Establish parent-child relationship via ActorHierarchyCoordinator
                await ActorHierarchyCoordinator.LinkAsync(coordinatorActor, workerActor, _logger, ct);
                
                workerActors.Add(workerActor);
                _logger.LogDebug("Created and linked worker actor {WorkerId} (index: {Index})", workerId, i);
            }

            _logger.LogInformation("Created {Count} worker actors with hierarchy", workerCount);

            // Step 4: Start execution by sending StartMakerTaskRequest event
            var providerName = options.ProviderName ?? _defaultProviderName;
            var startRequest = new StartMakerTaskRequest
            {
                ExecutionId = executionId.ToString("N")[..8],
                TaskDescription = taskDescription,
                ProviderName = providerName,
                ConsensusK = options.ConsensusK,
                SamplesPerRound = options.SamplesPerRound,
                BaseTemperature = options.BaseTemperature,
                TemperatureVariance = options.TemperatureVariance,
                SemanticSimilarityThreshold = options.SemanticSimilarityThreshold,
                ClusteringMethod = options.ClusteringMethod,
                // Budget-based limits (replaces MaxDepth)
                MaxTotalLlmCalls = options.MaxTotalLlmCalls,
                MaxTotalTokens = options.MaxTotalTokens,
                MaxDurationMs = (long)options.MaxDuration.TotalMilliseconds,
                DepthWarningThreshold = options.DepthWarningThreshold,
                // Multi-provider support (auto-discovers valid providers)
                UseMultipleProviders = options.UseMultipleProviders,
                CoordinatorProviderName = options.CoordinatorProviderName ?? string.Empty
            };
            
            if (options.UseMultipleProviders)
            {
                _logger.LogInformation("Multi-provider mode enabled. Will auto-discover valid providers.");
            }

            // Add context if provided
            if (options.Context != null)
            {
                foreach (var kvp in options.Context)
                {
                    startRequest.Context[kvp.Key] = kvp.Value;
                }
            }

            // Send start event directly to coordinator (triggers EventHandler)
            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(startRequest),
                Direction = EventDirection.Down
            };
            await coordinatorActor.HandleEventAsync(envelope, ct);
            
            _logger.LogDebug("Sent StartMakerTaskRequest to coordinator via HandleEventAsync");

            // Step 5: Poll for completion
            var maxWaitTime = TimeSpan.FromMinutes(10); // Long timeout for complex tasks
            var pollInterval = TimeSpan.FromMilliseconds(500);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWaitTime)
            {
                ct.ThrowIfCancellationRequested();
                
                await Task.Delay(pollInterval, ct);
                
                // Check coordinator status via public method
                var status = coordinator.GetStatus();
                
                // Status: 3 = Completed, 4 = Failed, 5 = Cancelled
                if (status >= 3)
                {
                    var result = coordinator.GetResult();
                    
                    // Clean up checkpoint on successful completion
                    if (result.Success && checkpointManager != null)
                    {
                        await checkpointManager.CompleteExecutionAsync(ct);
                        _logger.LogDebug("[CHECKPOINT] Checkpoint cleaned up for successful execution");
                    }
                    
                    _logger.LogInformation(
                        "MAKER execution {ExecutionId} completed. Success: {Success}, LLM Calls: {Calls}",
                        executionId.ToString("N")[..8],
                        result.Success,
                        result.TotalLLMCalls);

                    return result;
                }
            }

            // Timeout reached
            _logger.LogWarning("MAKER execution {ExecutionId} timed out", executionId);
            
            return new MakerResult
            {
                Success = false,
                Content = string.Empty,
                Error = "Execution timed out",
                Trace = new MakerTrace
                {
                    ExecutionId = executionId.ToString("N")[..8],
                    RootTask = new TaskNode
                    {
                        TaskId = executionId.ToString("N")[..8],
                        Description = taskDescription,
                        Depth = 0,
                        IsAtomic = false
                    },
                    TotalLLMCalls = coordinator.GetTotalLlmCalls(),
                    Duration = DateTime.UtcNow - startTime,
                    RedFlags = [],
                    TotalTokens = coordinator.GetTotalTokens(),
                    PromptTokens = coordinator.GetPromptTokens(),
                    CompletionTokens = coordinator.GetCompletionTokens()
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("MAKER execution {ExecutionId} was cancelled", executionId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "MAKER execution {ExecutionId} failed", executionId);
            
            return new MakerResult
            {
                Success = false,
                Content = string.Empty,
                Error = ex.Message,
                Trace = new MakerTrace
                {
                    ExecutionId = executionId.ToString("N")[..8],
                    RootTask = new TaskNode
                    {
                        TaskId = executionId.ToString("N")[..8],
                        Description = taskDescription,
                        Depth = 0,
                        IsAtomic = false
                    },
                    TotalLLMCalls = 0,
                    Duration = TimeSpan.Zero,
                    RedFlags = [],
                    TotalTokens = 0,
                    PromptTokens = 0,
                    CompletionTokens = 0
                }
            };
        }
        finally
        {
            // Cleanup: Unlink actors
            foreach (var workerActor in workerActors)
            {
                try
                {
                    if (coordinatorActor != null)
                    {
                        await ActorHierarchyCoordinator.UnlinkAsync(workerActor, coordinatorActor, _logger);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to unlink worker actor");
                }
            }
        }
    }
    
    // ============================================================
    //  Recovery API
    // ============================================================
    
    /// <summary>
    /// Attempt to recover a previously interrupted execution from checkpoint.
    /// </summary>
    public async Task<RecoveryResult> TryRecoverAsync(
        string executionId,
        CancellationToken ct = default)
    {
        var checkpointManager = new TaskCheckpointManager(_checkpointStore, _logger);
        return await checkpointManager.TryRecoverAsync(executionId, ct);
    }
    
    /// <summary>
    /// List all recoverable executions that have checkpoints.
    /// </summary>
    public Task<IReadOnlyList<string>> ListRecoverableExecutionsAsync(CancellationToken ct = default)
    {
        var checkpointManager = new TaskCheckpointManager(_checkpointStore, _logger);
        return checkpointManager.ListRecoverableExecutionsAsync(ct);
    }
}
