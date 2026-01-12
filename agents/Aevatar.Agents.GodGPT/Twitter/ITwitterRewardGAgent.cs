using Aevatar.Agents.Twitter;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Reward Agent interface - Stateful agent for daily reward calculation
/// Migrated from TwitterRewardGrain to new Agent architecture
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// Note: Does NOT inherit IGAgent - RPC proxy pattern requires plain interface
/// </summary>
public interface ITwitterRewardGAgent
{
    #region Calculation Control

    /// <summary>
    /// Start scheduled reward calculation
    /// </summary>
    Task<bool> StartRewardCalculationAsync();

    /// <summary>
    /// Stop scheduled reward calculation
    /// </summary>
    Task<bool> StopRewardCalculationAsync();

    /// <summary>
    /// Get current reward calculation status
    /// </summary>
    Task<RewardCalculationStatus> GetRewardCalculationStatusAsync();

    /// <summary>
    /// Manually trigger reward calculation for a specific date
    /// </summary>
    Task<bool> TriggerRewardCalculationAsync(DateTime targetDate);

    #endregion

    #region Query Operations

    /// <summary>
    /// Get reward calculation history
    /// </summary>
    Task<List<RewardCalculationHistory>> GetRewardCalculationHistoryAsync(int days = 7);

    /// <summary>
    /// Get user reward records
    /// </summary>
    Task<List<UserRewardRecord>> GetUserRewardRecordsAsync(string userId, int days = 7);

    /// <summary>
    /// Get user rewards grouped by date
    /// </summary>
    Task<Dictionary<string, List<UserRewardRecord>>> GetUserRewardsByUserIdAsync(string userId);

    /// <summary>
    /// Get daily reward statistics
    /// </summary>
    Task<DailyRewardStatistics> GetDailyRewardStatisticsAsync(DateTime targetDate);

    /// <summary>
    /// Check if user has received daily reward
    /// </summary>
    Task<bool> HasUserReceivedDailyRewardAsync(string userId, DateTime targetDate);

    #endregion

    #region Configuration

    /// <summary>
    /// Get current reward configuration
    /// </summary>
    Task<RewardConfig> GetRewardConfigAsync();

    /// <summary>
    /// Update reward configuration
    /// </summary>
    Task<bool> UpdateRewardConfigAsync(RewardConfig config);

    /// <summary>
    /// Clear reward records for a specific day
    /// </summary>
    Task<bool> ClearRewardByDayUtcSecondAsync(long utcSeconds);

    #endregion

    #region Time Control

    /// <summary>
    /// Get time control status
    /// </summary>
    Task<TimeControlStatus> GetTimeControlStatusAsync();

    #endregion
}
