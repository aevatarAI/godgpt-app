using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Stats;

/// <summary>
/// Interface for Lumen Stats Snapshot GAgent - stores periodic snapshots from Redis
/// </summary>
public interface ILumenStatsSnapshotGAgent : IGAgent
{
    /// <summary>
    /// Save snapshot of all stats
    /// </summary>
    Task SnapshotAsync(
        Dictionary<string, MethodStatsValue> globalStats, 
        Dictionary<string, UserMethodStats> userStats);
    
    /// <summary>
    /// Get the current snapshot
    /// </summary>
    Task<GetStatsSnapshotResult> GetSnapshotAsync();
}

/// <summary>
/// Lumen Stats Snapshot GAgent - stores periodic snapshots from Redis
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class LumenStatsSnapshotGAgent : GAgentBase<LumenStatsSnapshotState>, ILumenStatsSnapshotGAgent
{
    /// <summary>
    /// Required: Parameterless constructor for activation
    /// </summary>
    public LumenStatsSnapshotGAgent() : base()
    {
    }

    public override Task<string> GetDescriptionAsync()
    {
        var lastSnapshot = State.LastSnapshotAt?.ToDateTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "never";
        return Task.FromResult($"Lumen stats snapshot management - last snapshot: {lastSnapshot}");
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public void HandleStatsSnapshotEvent(StatsSnapshotEvent evt)
    {
        TransitionState(State, evt);
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(LumenStatsSnapshotState state, IMessage evt)
    {
        switch (evt)
        {
            case StatsSnapshotEvent snapshotEvent:
                state.GlobalStats.Clear();
                foreach (var kvp in snapshotEvent.GlobalStats)
                {
                    state.GlobalStats[kvp.Key] = kvp.Value;
                }
                
                state.UserStats.Clear();
                foreach (var kvp in snapshotEvent.UserStats)
                {
                    state.UserStats[kvp.Key] = kvp.Value;
                }
                
                state.LastSnapshotAt = snapshotEvent.SnapshotAt;
                break;
        }
    }

    // ============================================================================
    // Public Methods (RPC Interface Implementation)
    // ============================================================================

    public async Task SnapshotAsync(
        Dictionary<string, MethodStatsValue> globalStats, 
        Dictionary<string, UserMethodStats> userStats)
    {
        try
        {
            Logger.LogInformation(
                "[LumenStatsSnapshotGAgent][SnapshotAsync] Creating snapshot with {GlobalCount} global methods, {UserCount} users",
                globalStats.Count, userStats.Count);

            var evt = new StatsSnapshotEvent
            {
                SnapshotAt = Timestamp.FromDateTime(DateTime.UtcNow)
            };
            
            foreach (var kvp in globalStats)
            {
                evt.GlobalStats[kvp.Key] = kvp.Value;
            }
            
            foreach (var kvp in userStats)
            {
                evt.UserStats[kvp.Key] = kvp.Value;
            }

            RaiseEvent(evt);
            await ConfirmEventsAsync();

            Logger.LogInformation("[LumenStatsSnapshotGAgent][SnapshotAsync] Snapshot completed successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenStatsSnapshotGAgent][SnapshotAsync] Error creating snapshot");
            throw;
        }
    }

    public Task<GetStatsSnapshotResult> GetSnapshotAsync()
    {
        var result = new GetStatsSnapshotResult
        {
            Success = true,
            Message = string.Empty,
            LastSnapshotAt = State.LastSnapshotAt
        };
        
        foreach (var kvp in State.GlobalStats)
        {
            result.GlobalStats[kvp.Key] = kvp.Value;
        }
        
        foreach (var kvp in State.UserStats)
        {
            result.UserStats[kvp.Key] = kvp.Value;
        }
        
        return Task.FromResult(result);
    }
}

