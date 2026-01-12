using Aevatar.Agents.Twitter;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Monitor Agent interface - Stateful agent for scheduled tweet monitoring
/// Migrated from TwitterMonitorGrain to new Agent architecture
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// Note: Does NOT inherit IGAgent - RPC proxy pattern requires plain interface
/// </summary>
public interface ITwitterMonitorGAgent
{
    #region Monitoring Control

    /// <summary>
    /// Start tweet monitoring with scheduled fetching
    /// </summary>
    Task<bool> StartMonitoringAsync();

    /// <summary>
    /// Stop tweet monitoring
    /// </summary>
    Task<bool> StopMonitoringAsync();

    /// <summary>
    /// Get current monitoring status
    /// </summary>
    Task<TweetMonitorStatus> GetMonitoringStatusAsync();

    #endregion

    #region Manual Operations

    /// <summary>
    /// Manually trigger a tweet fetch
    /// </summary>
    Task<TweetFetchResult> FetchTweetsManuallyAsync();

    /// <summary>
    /// Refetch tweets for a specific time range
    /// </summary>
    Task<bool> RefetchTweetsByTimeRangeAsync(TimeRange timeRange);

    #endregion

    #region Query Operations

    /// <summary>
    /// Query tweets by time range
    /// </summary>
    Task<List<TweetRecord>> QueryTweetsByTimeRangeAsync(TimeRange timeRange);

    /// <summary>
    /// Get fetch history
    /// </summary>
    Task<List<TweetFetchHistory>> GetFetchHistoryAsync(int days = 7);

    /// <summary>
    /// Get tweet statistics for a time range
    /// </summary>
    Task<TweetStatistics> GetTweetStatisticsAsync(TimeRange timeRange);

    #endregion

    #region Configuration

    /// <summary>
    /// Get current monitoring configuration
    /// </summary>
    Task<TweetMonitorConfig> GetMonitoringConfigAsync();

    /// <summary>
    /// Clean up expired tweets based on retention policy
    /// </summary>
    Task<int> CleanupExpiredTweetsAsync();

    #endregion
}
