using System.Text.Json;
using Aevatar.Agents.Maker.Messages;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Maker.Agents;

// ============================================================
//  MAKER State Recovery
//  Handles state persistence and recovery after restart
//  Part of MakerCoordinatorGAgent (partial class)
//  Implements runtime state persistence
// ============================================================

public partial class MakerCoordinatorGAgent
{
    // ============================================================
    //  State Persistence
    // ============================================================

    /// <summary>
    /// Persist current runtime state to protobuf state for recovery after restart.
    /// </summary>
    public void PersistRuntimeState()
    {
        // Save execution context
        if (_currentContext != null)
        {
            CustomState.ExecutionContext.Clear();
            foreach (var kvp in _currentContext)
            {
                CustomState.ExecutionContext[kvp.Key] = kvp.Value;
            }
        }

        // Save MakerOptions
        if (_currentOptions != null)
        {
            CustomState.Options = OptionsToProto(_currentOptions);
        }

        // Save result if completed
        if (_cachedResult != null)
        {
            CustomState.ResultContent = _cachedResult.Content;
            CustomState.ResultError = _cachedResult.Error ?? string.Empty;
            CustomState.ResultTraceJson = JsonSerializer.Serialize(_cachedResult.Trace);
        }

        CustomState.ElapsedMs = (long)_stopwatch.Elapsed.TotalMilliseconds;
        CustomState.UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow);

        Logger.LogDebug("Runtime state persisted for execution {ExecutionId}", CustomState.ExecutionId);
    }

    /// <summary>
    /// Restore runtime state from persisted protobuf state.
    /// </summary>
    public void RestoreRuntimeState()
    {
        if (CustomState.ExecutionContext.Count > 0)
        {
            _currentContext = new Dictionary<string, string>(CustomState.ExecutionContext);
        }

        if (CustomState.Options != null)
        {
            _currentOptions = ProtoToOptions(CustomState.Options);
        }

        if (!string.IsNullOrEmpty(CustomState.ResultContent) || !string.IsNullOrEmpty(CustomState.ResultError))
        {
            var defaultTrace = new MakerTrace
            {
                ExecutionId = CustomState.ExecutionId,
                RootTask = new TaskNode { TaskId = CustomState.ExecutionId, Description = CustomState.TaskDescription }
            };

            MakerTrace? deserializedTrace = null;
            if (!string.IsNullOrEmpty(CustomState.ResultTraceJson))
            {
                try { deserializedTrace = JsonSerializer.Deserialize<MakerTrace>(CustomState.ResultTraceJson); }
                catch { /* Ignore deserialization errors */ }
            }

            _cachedResult = new MakerResult
            {
                Success = CustomState.ExecutionState == MakerExecutionState.MakerStateCompleted,
                Content = CustomState.ResultContent,
                Error = string.IsNullOrEmpty(CustomState.ResultError) ? null : CustomState.ResultError,
                Trace = deserializedTrace ?? defaultTrace
            };
        }

        foreach (var reason in CustomState.RedFlagReasons)
        {
            var parts = reason.Split(": ", 2);
            _redFlags.Add(new RedFlagEvent
            {
                TaskId = parts.Length > 1 ? parts[0] : "unknown",
                Reason = parts.Length > 1 ? parts[1] : reason,
                Recovered = false
            });
        }

        Logger.LogDebug("Runtime state restored for execution {ExecutionId}, state={State}",
            CustomState.ExecutionId, CustomState.ExecutionState);
    }

    /// <summary>
    /// Check if there's an interrupted execution that can be resumed.
    /// </summary>
    public bool HasInterruptedExecution()
    {
        if (string.IsNullOrEmpty(CustomState.ExecutionId))
        {
            return false;
        }

        return CustomState.ExecutionState is
            MakerExecutionState.MakerStateInitializingWorkers or
            MakerExecutionState.MakerStateAssessingAtomicity or
            MakerExecutionState.MakerStateDecomposing or
            MakerExecutionState.MakerStateSolvingAtomic or
            MakerExecutionState.MakerStateVoting or
            MakerExecutionState.MakerStateComposing;
    }

    // ============================================================
    //  Options Conversion
    // ============================================================

    private static MakerOptionsProto OptionsToProto(MakerOptions options)
    {
        return new MakerOptionsProto
        {
            ConsensusK = options.ConsensusK,
            SamplesPerRound = options.SamplesPerRound,
            MaxTotalLlmCalls = options.MaxTotalLlmCalls,
            MaxTotalTokens = options.MaxTotalTokens,
            MaxDurationMs = (long)options.MaxDuration.TotalMilliseconds,
            DepthWarningThreshold = options.DepthWarningThreshold,
            HardDepthCap = options.HardDepthCap,
            BaseTemperature = options.BaseTemperature,
            TemperatureVariance = options.TemperatureVariance,
            SemanticSimilarityThreshold = options.SemanticSimilarityThreshold,
            ClusteringMethod = options.ClusteringMethod ?? "semantic",
            ExecutionMode = options.Mode.ToString()
        };
    }

    private static MakerOptions ProtoToOptions(MakerOptionsProto proto)
    {
        var mode = System.Enum.TryParse<ExecutionMode>(proto.ExecutionMode, out var parsedMode)
            ? parsedMode
            : ExecutionMode.Production;

        return new MakerOptions
        {
            CustomK = proto.ConsensusK > 0 ? proto.ConsensusK : 3,
            MaxTotalLlmCalls = proto.MaxTotalLlmCalls > 0 ? proto.MaxTotalLlmCalls : 100,
            MaxTotalTokens = proto.MaxTotalTokens > 0 ? proto.MaxTotalTokens : 500_000,
            MaxDuration = proto.MaxDurationMs > 0 ? TimeSpan.FromMilliseconds(proto.MaxDurationMs) : TimeSpan.FromMinutes(10),
            DepthWarningThreshold = proto.DepthWarningThreshold > 0 ? proto.DepthWarningThreshold : 10,
            HardDepthCap = proto.HardDepthCap > 0 ? proto.HardDepthCap : 50,
            BaseTemperature = proto.BaseTemperature > 0 ? proto.BaseTemperature : 0.7f,
            TemperatureVariance = proto.TemperatureVariance,
            SemanticSimilarityThreshold = proto.SemanticSimilarityThreshold > 0 ? proto.SemanticSimilarityThreshold : 0.85f,
            ClusteringMethod = !string.IsNullOrEmpty(proto.ClusteringMethod) ? proto.ClusteringMethod : "semantic",
            Mode = mode
        };
    }
}

