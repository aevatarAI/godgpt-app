using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.CreativeReasoning.Agents;
using Aevatar.Agents.CreativeReasoning.Core;
using Aevatar.Agents.CreativeReasoning.Messages;
using Aevatar.Agents.CreativeReasoning.Strategies;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.CreativeReasoning.Execution;

// ============================================================
//  UoT Executor - Unified Entry Point for Creative Reasoning
//  Supports all three UoT modes:
//  - C-UoT: Combinational (recombine existing thoughts)
//  - E-UoT: Exploratory (discover outside thoughts)
//  - T-UoT: Transformative (challenge rules and assumptions)
// ============================================================

/// <summary>
/// Executor for Universe of Thoughts (UoT) creative reasoning.
/// Automatically routes to appropriate agent based on mode.
/// </summary>
public class UoTExecutor : IUoTExecutor
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UoTExecutor> _logger;
    private readonly string _defaultProviderName;

    public UoTExecutor(
        IGAgentActorFactory actorFactory,
        ILogger<UoTExecutor> logger,
        string? defaultProviderName = null)
    {
        _actorFactory = actorFactory;
        _logger = logger;
        _defaultProviderName = defaultProviderName ?? AevatarAgentsConstants.DefaultProviderName;
    }

    /// <inheritdoc />
    public async Task<UoTResult> ExecuteAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(problem))
            throw new ArgumentException("Problem cannot be empty", nameof(problem));

        return options.Mode switch
        {
            Core.UoTMode.Combinational => await ExecuteCUoTAsync(problem, options, ct),
            Core.UoTMode.Exploratory => await ExecuteEUoTAsync(problem, options, ct),
            Core.UoTMode.Transformative => ConvertTUoTResult(await ExecuteTUoTAsync(problem, options, ct)),
            _ => throw new ArgumentOutOfRangeException(nameof(options.Mode))
        };
    }

    /// <inheritdoc />
    public async Task<TUoTResult> ExecuteTransformativeAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(problem))
            throw new ArgumentException("Problem cannot be empty", nameof(problem));

        return await ExecuteTUoTAsync(problem, options, ct);
    }

    #region C-UoT Execution

    private async Task<UoTResult> ExecuteCUoTAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct)
    {
        var providerName = options.ProviderName ?? _defaultProviderName;
        var executionId = Guid.NewGuid();
        var executionIdShort = executionId.ToString("N")[..8];

        _logger.LogInformation(
            "Starting C-UoT execution {ExecutionId} for: {Problem}",
            executionIdShort, problem[..Math.Min(50, problem.Length)]);

        try
        {
            var coordinatorActor = await _actorFactory.CreateGAgentActorAsync<UoTCoordinatorGAgent>(executionId, ct);
            var coordinator = coordinatorActor.GetAgent() as UoTCoordinatorGAgent
                ?? throw new InvalidOperationException("Failed to get UoTCoordinatorGAgent");

            coordinator.SetStrategies(
                analogyStrategy: new DefaultAnalogyStrategy(),
                decompositionStrategy: new DefaultThoughtDecompositionStrategy(),
                hostSelectionStrategy: new DefaultHostSelectionStrategy(),
                donorSelectionStrategy: new DefaultDonorSelectionStrategy(),
                synthesisStrategy: new DefaultSynthesisStrategy(),
                evaluationStrategy: new DefaultEvaluationStrategy(),
                progressCallback: options.OnProgress);

            var startRequest = new StartCreativeReasoningRequest
            {
                ExecutionId = executionIdShort,
                Problem = problem,
                DomainHint = options.DomainHint ?? "",
                ProviderName = providerName,
                MaxAnalogies = options.MaxAnalogies,
                MaxCandidates = options.MaxCandidates,
                FeasibilityThreshold = options.FeasibilityThreshold,
                UtilityWeight = options.UtilityWeight,
                NoveltyWeight = options.NoveltyWeight
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(startRequest),
                Direction = EventDirection.Down
            };
            await coordinatorActor.HandleEventAsync(envelope, ct);

            return await WaitForCompletion(coordinator, executionIdShort, problem, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("C-UoT {ExecutionId} cancelled", executionIdShort);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "C-UoT {ExecutionId} failed", executionIdShort);
            return CreateErrorResult(executionIdShort, problem, ex.Message);
        }
    }

    private async Task<UoTResult> WaitForCompletion(
        UoTCoordinatorGAgent coordinator,
        string executionId,
        string problem,
        CancellationToken ct)
    {
        var maxWaitTime = TimeSpan.FromMinutes(10);
        var pollInterval = TimeSpan.FromMilliseconds(300);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollInterval, ct);

            var phase = coordinator.GetPhase();
            if (phase == UoTExecutionPhase.UotPhaseCompleted || phase == UoTExecutionPhase.UotPhaseFailed)
            {
                var result = coordinator.GetResult();
                _logger.LogInformation("C-UoT {ExecutionId} completed: Success={Success}", 
                    executionId, result.Success);
                return result;
            }
        }

        return CreateErrorResult(executionId, problem, "Execution timed out");
    }

    #endregion

    #region E-UoT Execution

    private async Task<UoTResult> ExecuteEUoTAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct)
    {
        var providerName = options.ProviderName ?? _defaultProviderName;
        var executionId = Guid.NewGuid();
        var executionIdShort = executionId.ToString("N")[..8];

        _logger.LogInformation(
            "Starting E-UoT execution {ExecutionId} for: {Problem}",
            executionIdShort, problem[..Math.Min(50, problem.Length)]);

        try
        {
            var coordinatorActor = await _actorFactory.CreateGAgentActorAsync<EUoTCoordinatorGAgent>(executionId, ct);
            var coordinator = coordinatorActor.GetAgent() as EUoTCoordinatorGAgent
                ?? throw new InvalidOperationException("Failed to get EUoTCoordinatorGAgent");

            coordinator.SetStrategies(
                analogyStrategy: new DefaultAnalogyStrategy(),
                decompositionStrategy: new DefaultThoughtDecompositionStrategy(),
                exploratoryStrategy: new DefaultExploratoryStrategy(),
                hostSelectionStrategy: new DefaultHostSelectionStrategy(),
                donorSelectionStrategy: new DefaultDonorSelectionStrategy(),
                synthesisStrategy: new DefaultSynthesisStrategy(),
                evaluationStrategy: new DefaultEvaluationStrategy(),
                progressCallback: options.OnProgress);

            var startRequest = new StartEUoTRequest
            {
                ExecutionId = executionIdShort,
                Problem = problem,
                DomainHint = options.DomainHint ?? "",
                ProviderName = providerName,
                MaxAnalogies = options.MaxAnalogies,
                MaxCandidates = options.MaxCandidates,
                FeasibilityThreshold = options.FeasibilityThreshold,
                MaxOutsideThoughts = options.MaxOutsideThoughts,
                ExplorationDirections = options.ExplorationDirections
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(startRequest),
                Direction = EventDirection.Down
            };
            await coordinatorActor.HandleEventAsync(envelope, ct);

            return await WaitForEUoTCompletion(coordinator, executionIdShort, problem, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("E-UoT {ExecutionId} cancelled", executionIdShort);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "E-UoT {ExecutionId} failed", executionIdShort);
            return CreateErrorResult(executionIdShort, problem, ex.Message);
        }
    }

    private async Task<UoTResult> WaitForEUoTCompletion(
        EUoTCoordinatorGAgent coordinator,
        string executionId,
        string problem,
        CancellationToken ct)
    {
        var maxWaitTime = TimeSpan.FromMinutes(15);  // Longer for E-UoT
        var pollInterval = TimeSpan.FromMilliseconds(300);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollInterval, ct);

            var phase = coordinator.GetPhase();
            if (phase == EUoTExecutionPhase.EuotPhaseCompleted || phase == EUoTExecutionPhase.EuotPhaseFailed)
            {
                var result = coordinator.GetResult();
                _logger.LogInformation("E-UoT {ExecutionId} completed: Success={Success}", 
                    executionId, result.Success);
                return result;
            }
        }

        return CreateErrorResult(executionId, problem, "Execution timed out");
    }

    #endregion

    #region T-UoT Execution

    private async Task<TUoTResult> ExecuteTUoTAsync(
        string problem,
        UoTOptions options,
        CancellationToken ct)
    {
        var providerName = options.ProviderName ?? _defaultProviderName;
        var executionId = Guid.NewGuid();
        var executionIdShort = executionId.ToString("N")[..8];

        _logger.LogInformation(
            "Starting T-UoT execution {ExecutionId} for: {Problem}",
            executionIdShort, problem[..Math.Min(50, problem.Length)]);

        try
        {
            var coordinatorActor = await _actorFactory.CreateGAgentActorAsync<TUoTCoordinatorGAgent>(executionId, ct);
            var coordinator = coordinatorActor.GetAgent() as TUoTCoordinatorGAgent
                ?? throw new InvalidOperationException("Failed to get TUoTCoordinatorGAgent");

            coordinator.SetStrategies(
                ruleMutationStrategy: new DefaultRuleMutationStrategy(),
                evaluationStrategy: new DefaultEvaluationStrategy(),
                progressCallback: options.OnProgress);

            var startRequest = new StartTUoTRequest
            {
                ExecutionId = executionIdShort,
                Problem = problem,
                DomainHint = options.DomainHint ?? "",
                ProviderName = providerName,
                MaxRuleSets = options.MaxRuleSets,
                MutationsPerSet = options.MutationsPerSet,
                MinRadicality = options.MinRadicality,
                FeasibilityThreshold = options.FeasibilityThreshold
            };

            var envelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Timestamp = TimestampHelper.GetUtcNow(),
                Payload = Any.Pack(startRequest),
                Direction = EventDirection.Down
            };
            await coordinatorActor.HandleEventAsync(envelope, ct);

            return await WaitForTUoTCompletion(coordinator, executionIdShort, problem, ct);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("T-UoT {ExecutionId} cancelled", executionIdShort);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "T-UoT {ExecutionId} failed", executionIdShort);
            return CreateTUoTErrorResult(executionIdShort, problem, ex.Message);
        }
    }

    private async Task<TUoTResult> WaitForTUoTCompletion(
        TUoTCoordinatorGAgent coordinator,
        string executionId,
        string problem,
        CancellationToken ct)
    {
        var maxWaitTime = TimeSpan.FromMinutes(15);  // Longer for T-UoT
        var pollInterval = TimeSpan.FromMilliseconds(300);
        var startTime = DateTime.UtcNow;

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(pollInterval, ct);

            var phase = coordinator.GetPhase();
            if (phase == TUoTExecutionPhase.TuotPhaseCompleted || phase == TUoTExecutionPhase.TuotPhaseFailed)
            {
                var result = coordinator.GetResult();
                _logger.LogInformation("T-UoT {ExecutionId} completed: Success={Success}", 
                    executionId, result.Success);
                return result;
            }
        }

        return CreateTUoTErrorResult(executionId, problem, "Execution timed out");
    }

    #endregion

    #region Helpers

    private static UoTResult CreateErrorResult(string executionId, string problem, string error) => new()
    {
        Success = false,
        Error = error,
        BestSolution = null,
        AllCandidates = [],
        Trace = new UoTResultTrace
        {
            ExecutionId = executionId,
            OriginalProblem = problem,
            TotalLLMCalls = 0,
            TotalTokens = 0,
            Duration = TimeSpan.Zero
        }
    };

    private static TUoTResult CreateTUoTErrorResult(string executionId, string problem, string error) => new()
    {
        Success = false,
        Error = error,
        BestSolution = null,
        AllSolutions = [],
        Trace = new TUoTResultTrace
        {
            ExecutionId = executionId,
            OriginalProblem = problem,
            TotalLLMCalls = 0,
            TotalTokens = 0,
            Duration = TimeSpan.Zero
        }
    };

    private static UoTResult ConvertTUoTResult(TUoTResult tuotResult)
    {
        // Convert T-UoT result to UoT result for unified API
        var candidates = tuotResult.AllSolutions
            .Select(s => new CandidateSolution
            {
                Id = s.Id,
                Content = s.Content,
                Score = s.Score
            })
            .ToList();

        return new UoTResult
        {
            Success = tuotResult.Success,
            Error = tuotResult.Error,
            BestSolution = tuotResult.BestSolution != null 
                ? new CandidateSolution 
                { 
                    Id = tuotResult.BestSolution.Id, 
                    Content = tuotResult.BestSolution.Content,
                    Score = tuotResult.BestSolution.Score
                } 
                : null,
            AllCandidates = candidates,
            Trace = new UoTResultTrace
            {
                ExecutionId = tuotResult.Trace.ExecutionId,
                OriginalProblem = tuotResult.Trace.OriginalProblem,
                AnalogiesExplored = tuotResult.Trace.RulesExposed,  // Rules as "analogies" in trace
                ThoughtsExtracted = tuotResult.Trace.HiddenAssumptionsFound,
                CandidatesGenerated = tuotResult.Trace.SolutionsGenerated,
                TotalLLMCalls = tuotResult.Trace.TotalLLMCalls,
                TotalTokens = tuotResult.Trace.TotalTokens,
                Duration = tuotResult.Trace.Duration
            }
        };
    }

    #endregion
}
