using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Twitter;
using Aevatar.Application.Grains.Common.Options;
using Aevatar.Application.Grains.Twitter.Services;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Twitter;

/// <summary>
/// Twitter Reward Agent - Stateful agent for daily reward calculation
/// Migrated from TwitterRewardGrain to new Agent architecture using EventSourcing
/// 
/// This agent depends on TwitterMonitorGAgent to get tweet data for reward calculation.
/// The data flow: Monitor collects tweets -> Reward calculates rewards based on tweet metrics.
/// </summary>
public class TwitterRewardGAgent : GAgentBase<TwitterRewardState>, ITwitterRewardGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public ILogger<TwitterRewardGAgent>? Logger { get; set; }
    public IOptionsMonitor<TwitterRewardOptions>? Options { get; set; }
    public ITwitterApiService? TwitterApiService { get; set; }
    public IGAgentActorFactory? AgentFactory { get; set; }

    /// <summary>
    /// Singleton ID for the Twitter Monitor Agent
    /// Used to fetch tweet data for reward calculation
    /// </summary>
    private const string TwitterMonitorAgentId = "twitter-monitor-singleton";

    // Parameterless constructor required for Orleans activation
    public TwitterRewardGAgent() : base()
    {
    }

    // Helper properties for null safety
    private ILogger<TwitterRewardGAgent> _logger => Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TwitterRewardGAgent>.Instance;
    private IOptionsMonitor<TwitterRewardOptions> _options => Options ?? throw new InvalidOperationException("Options not injected");
    private ITwitterApiService _twitterApiService => TwitterApiService ?? throw new InvalidOperationException("TwitterApiService not injected");
    private IGAgentActorFactory _agentFactory => AgentFactory ?? throw new InvalidOperationException("AgentFactory not injected");

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Twitter Reward Agent - Running: {State.IsRunning}, " +
            $"Users Rewarded: {State.TotalUsersRewarded}, " +
            $"Credits Distributed: {State.TotalCreditsDistributed}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        _logger.LogInformation("TwitterRewardGAgent {AgentId} activating", Id);

        // Initialize configuration from appsettings.json
        var config = CreateConfigFromOptions();
        
        if (State.Config == null || ConfigHasChanged(config))
        {
            RaiseEvent(new RewardConfigUpdatedEvent
            {
                AgentId = Id.ToString(),
                Config = config,
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await ConfirmEventsAsync();
            
            _logger.LogInformation("TwitterRewardGAgent configuration updated");
        }
    }

    #region Calculation Control

    public async Task<bool> StartRewardCalculationAsync()
    {
        _logger.LogInformation("Starting reward calculation");
        
        if (State.IsRunning)
        {
            _logger.LogInformation("Reward calculation is already running");
            return true;
        }

        var nextTriggerTime = GetNextRewardTriggerTimeUtc();

        RaiseEvent(new RewardCalculationStartedEvent
        {
            AgentId = Id.ToString(),
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            TargetDate = Timestamp.FromDateTime(nextTriggerTime)
        });
        await ConfirmEventsAsync();

        _logger.LogInformation("Reward calculation started, next execution at {Time} UTC", 
            nextTriggerTime);
        
        return true;
    }

    public async Task<bool> StopRewardCalculationAsync()
    {
        _logger.LogInformation("Stopping reward calculation");

        if (!State.IsRunning)
        {
            _logger.LogInformation("Reward calculation is not running");
            return true;
        }

        RaiseEvent(new RewardCalculationStoppedEvent
        {
            AgentId = Id.ToString(),
            StoppedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        await ConfirmEventsAsync();

        _logger.LogInformation("Reward calculation stopped");
        return true;
    }

    public Task<RewardCalculationStatus> GetRewardCalculationStatusAsync()
    {
        var status = new RewardCalculationStatus
        {
            IsRunning = State.IsRunning,
            LastCalculationTime = State.LastCalculationTime,
            LastCalculationTimeUtc = State.LastCalculationTimeUtc,
            NextScheduledCalculation = State.NextScheduledCalculation,
            NextScheduledCalculationUtc = State.NextScheduledCalculationUtc,
            LastError = State.LastError,
            Config = State.Config,
            TotalUsersRewarded = State.TotalUsersRewarded,
            TotalCreditsDistributed = State.TotalCreditsDistributed
        };

        return Task.FromResult(status);
    }

    public async Task<bool> TriggerRewardCalculationAsync(DateTime targetDate)
    {
        _logger.LogInformation("Manual reward calculation triggered for date {Date}", 
            targetDate.ToString("yyyy-MM-dd"));
        
        var result = await ExecuteRewardCalculationAsync(targetDate);
        return result.IsSuccess;
    }

    #endregion

    #region Query Operations

    public Task<List<RewardCalculationHistory>> GetRewardCalculationHistoryAsync(int days = 7)
    {
        var cutoffTime = DateTime.UtcNow.AddDays(-days);
        var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

        var recentHistory = State.CalculationHistory
            .Where(h => h.CalculationDateUtc >= cutoffUtc)
            .OrderByDescending(h => h.CalculationDateUtc)
            .ToList();

        return Task.FromResult(recentHistory);
    }

    public Task<List<UserRewardRecord>> GetUserRewardRecordsAsync(string userId, int days = 7)
    {
        var cutoffTime = DateTime.UtcNow.AddDays(-days);
        var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

        var userRewards = new List<UserRewardRecord>();
        
        foreach (var dayRewards in State.UserRewards.Values)
        {
            var userDayRewards = dayRewards.Records
                .Where(r => r.UserId == userId && r.RewardDateUtc >= cutoffUtc)
                .ToList();
            userRewards.AddRange(userDayRewards);
        }

        return Task.FromResult(userRewards.OrderByDescending(r => r.RewardDateUtc).ToList());
    }

    public Task<Dictionary<string, List<UserRewardRecord>>> GetUserRewardsByUserIdAsync(string userId)
    {
        var result = new Dictionary<string, List<UserRewardRecord>>();

        foreach (var kvp in State.UserRewards)
        {
            var dateKey = kvp.Key;
            var dayRewards = kvp.Value.Records
                .Where(r => r.UserId == userId)
                .ToList();

            if (dayRewards.Count > 0)
            {
                result[dateKey] = dayRewards;
            }
        }

        return Task.FromResult(result);
    }

    public Task<DailyRewardStatistics> GetDailyRewardStatisticsAsync(DateTime targetDate)
    {
        var dateKey = targetDate.ToString("yyyy-MM-dd");
        var rewards = State.UserRewards.ContainsKey(dateKey) 
            ? State.UserRewards[dateKey].Records.ToList()
            : new List<UserRewardRecord>();

        var statistics = new DailyRewardStatistics
        {
            StatisticsDate = Timestamp.FromDateTime(targetDate),
            StatisticsDateUtc = ((DateTimeOffset)targetDate).ToUnixTimeSeconds(),
            TotalUsersRewarded = rewards.Count,
            TotalCreditsDistributed = rewards.Sum(r => r.FinalCredits),
            TotalTweetsEligible = rewards.Count,
            TweetsWithShareLinks = rewards.Count(r => r.HasValidShareLink),
            AverageCreditsPerUser = rewards.Count > 0 ? rewards.Average(r => r.FinalCredits) : 0,
            ShareLinkBonusTotal = rewards
                .Where(r => r.HasValidShareLink)
                .Sum(r => r.BonusCredits - r.BonusCreditsBeforeMultiplier)
        };

        // Generate reward tier statistics
        var rewardTiers = State.Config?.RewardTiers;
        if (rewardTiers != null)
        {
            foreach (var tier in rewardTiers)
            {
                var tierRewards = rewards.Count(r => r.BonusCreditsBeforeMultiplier == tier.RewardCredits);
                statistics.RewardsByTier[tier.TierName] = tierRewards;
            }
        }

        // Generate user statistics
        var userGroups = rewards.GroupBy(r => r.UserHandle);
        foreach (var group in userGroups)
        {
            statistics.UsersRewardedMap[group.Key] = group.Sum(r => r.FinalCredits);
        }

        return Task.FromResult(statistics);
    }

    public Task<bool> HasUserReceivedDailyRewardAsync(string userId, DateTime targetDate)
    {
        var dateKey = targetDate.ToString("yyyy-MM-dd");
        var hasReceived = State.UserRewards.ContainsKey(dateKey) &&
                          State.UserRewards[dateKey].Records.Any(r => r.UserId == userId);

        return Task.FromResult(hasReceived);
    }

    #endregion

    #region Configuration

    public Task<RewardConfig> GetRewardConfigAsync()
    {
        return Task.FromResult(State.Config ?? CreateConfigFromOptions());
    }

    public async Task<bool> UpdateRewardConfigAsync(RewardConfig config)
    {
        RaiseEvent(new RewardConfigUpdatedEvent
        {
            AgentId = Id.ToString(),
            Config = config,
            UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        await ConfirmEventsAsync();

        _logger.LogInformation("Reward configuration updated");
        return true;
    }

    public async Task<bool> ClearRewardByDayUtcSecondAsync(long utcSeconds)
    {
        var targetDate = DateTimeOffset.FromUnixTimeSeconds(utcSeconds).DateTime;
        var dateKey = targetDate.ToString("yyyy-MM-dd");
        
        _logger.LogInformation("Clearing reward records for date {Date}", dateKey);

        if (State.UserRewards.ContainsKey(dateKey))
        {
            State.UserRewards[dateKey] = new UserRewardRecordList();
            await ConfirmEventsAsync();
        }

        return true;
    }

    #endregion

    #region Time Control

    public Task<TimeControlStatus> GetTimeControlStatusAsync()
    {
        var currentUtc = DateTime.UtcNow;
        var nextTriggerTime = GetNextRewardTriggerTimeUtc();

        var status = new TimeControlStatus
        {
            CurrentUtcTime = Timestamp.FromDateTime(currentUtc),
            CurrentUtcTimestamp = ((DateTimeOffset)currentUtc).ToUnixTimeSeconds(),
            LastRewardCalculationTime = State.LastCalculationTime ?? 
                Timestamp.FromDateTime(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc)),
            LastRewardCalculationTimestamp = State.LastCalculationTimeUtc,
            NextRewardCalculationTime = Timestamp.FromDateTime(nextTriggerTime),
            NextRewardCalculationTimestamp = ((DateTimeOffset)nextTriggerTime).ToUnixTimeSeconds(),
            TimeUntilNextCalculationMs = (long)(nextTriggerTime - currentUtc).TotalMilliseconds,
            IsRewardCalculationDay = ShouldExecuteRewardCalculation(currentUtc),
            TimezoneInfo = "UTC"
        };

        return Task.FromResult(status);
    }

    #endregion

    #region TransitionState

    protected override void TransitionState(TwitterRewardState state, IMessage evt)
    {
        switch (evt)
        {
            case RewardCalculationStartedEvent started:
                state.IsRunning = true;
                state.NextScheduledCalculation = started.TargetDate;
                state.NextScheduledCalculationUtc = 
                    ((DateTimeOffset)started.TargetDate.ToDateTime()).ToUnixTimeSeconds();
                break;

            case RewardCalculationStoppedEvent:
                state.IsRunning = false;
                break;

            case RewardCalculationCompletedEvent completed:
                state.LastCalculationTime = completed.CompletedAt;
                state.LastCalculationTimeUtc = 
                    ((DateTimeOffset)completed.CompletedAt.ToDateTime()).ToUnixTimeSeconds();
                state.TotalUsersRewarded += completed.UsersRewarded;
                state.TotalCreditsDistributed += completed.TotalCreditsDistributed;
                
                if (!completed.IsSuccess)
                {
                    state.LastError = completed.ErrorMessage;
                }
                else
                {
                    state.LastError = string.Empty;
                }

                // Update next scheduled calculation
                var nextTriggerTime = GetNextRewardTriggerTimeUtc();
                state.NextScheduledCalculation = Timestamp.FromDateTime(nextTriggerTime);
                state.NextScheduledCalculationUtc = 
                    ((DateTimeOffset)nextTriggerTime).ToUnixTimeSeconds();
                break;

            case RewardConfigUpdatedEvent configUpdated:
                state.Config = configUpdated.Config;
                break;
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Get TwitterMonitorGAgent reference for fetching tweet data
    /// </summary>
    private async Task<ITwitterMonitorGAgent?> GetMonitorAgentAsync()
    {
        try
        {
            var actor = await _agentFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(
                TwitterMonitorAgentId);
            return actor.As<ITwitterMonitorGAgent>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TwitterMonitorGAgent reference");
            return null;
        }
    }

    /// <summary>
    /// Get tweets from Monitor Agent for reward calculation
    /// </summary>
    private async Task<List<TweetRecord>> GetTweetsForRewardAsync(DateTime targetDate)
    {
        var monitor = await GetMonitorAgentAsync();
        if (monitor == null)
        {
            _logger.LogWarning("TwitterMonitorGAgent not available, cannot fetch tweets");
            return new List<TweetRecord>();
        }

        // Calculate time range for the target date (full day in UTC)
        var startOfDay = targetDate.Date;
        var endOfDay = startOfDay.AddDays(1).AddSeconds(-1);

        var timeRange = new TimeRange
        {
            StartTime = Timestamp.FromDateTime(startOfDay),
            EndTime = Timestamp.FromDateTime(endOfDay),
            StartTimeUtcSecond = ((DateTimeOffset)startOfDay).ToUnixTimeSeconds(),
            EndTimeUtcSecond = ((DateTimeOffset)endOfDay).ToUnixTimeSeconds()
        };

        _logger.LogInformation("Fetching tweets from Monitor for date {Date}, range: {Start} to {End}",
            targetDate.ToString("yyyy-MM-dd"), startOfDay, endOfDay);

        var tweets = await monitor.QueryTweetsByTimeRangeAsync(timeRange);
        
        _logger.LogInformation("Fetched {Count} tweets from Monitor", tweets.Count);
        return tweets;
    }

    /// <summary>
    /// Calculate reward tier based on tweet metrics
    /// </summary>
    private RewardTier? CalculateRewardTier(TweetRecord tweet)
    {
        var config = State.Config;
        if (config == null || config.RewardTiers.Count == 0)
        {
            return null;
        }

        // Check minimum views requirement
        if (tweet.ViewCount < config.MinViewsForReward)
        {
            return null;
        }

        // Find the highest applicable tier (sorted by reward credits descending)
        var applicableTier = config.RewardTiers
            .OrderByDescending(t => t.RewardCredits)
            .FirstOrDefault(tier => 
                tweet.ViewCount >= tier.MinViews && 
                tweet.FollowerCount >= tier.MinFollowers);

        return applicableTier;
    }

    /// <summary>
    /// Check if a user has exceeded the daily reward limit
    /// </summary>
    private int GetUserDailyCredits(string userId, string dateKey)
    {
        if (!State.UserRewards.ContainsKey(dateKey))
        {
            return 0;
        }

        return State.UserRewards[dateKey].Records
            .Where(r => r.UserId == userId)
            .Sum(r => r.FinalCredits);
    }

    /// <summary>
    /// Execute the full reward calculation for a target date
    /// Fetches tweets from Monitor, calculates rewards, and stores results
    /// </summary>
    private async Task<RewardCalculationResult> ExecuteRewardCalculationAsync(DateTime targetDate)
    {
        var processingStartTime = DateTime.UtcNow;
        var dateKey = targetDate.ToString("yyyy-MM-dd");

        var result = new RewardCalculationResult
        {
            CalculationDate = Timestamp.FromDateTime(targetDate),
            CalculationDateUtc = ((DateTimeOffset)targetDate).ToUnixTimeSeconds(),
            ProcessingStartTime = Timestamp.FromDateTime(processingStartTime),
            ProcessedTimeRange = new TimeRange
            {
                StartTime = Timestamp.FromDateTime(targetDate.Date),
                EndTime = Timestamp.FromDateTime(targetDate.Date.AddDays(1).AddSeconds(-1)),
                StartTimeUtcSecond = ((DateTimeOffset)targetDate.Date).ToUnixTimeSeconds(),
                EndTimeUtcSecond = ((DateTimeOffset)targetDate.Date.AddDays(1).AddSeconds(-1)).ToUnixTimeSeconds()
            },
            IsSuccess = false
        };

        try
        {
            if (State.Config?.EnableRewardCalculation != true)
            {
                result.ErrorMessage = "Reward calculation is disabled";
                return result;
            }

            _logger.LogInformation("Starting reward calculation for {Date}", dateKey);

            // Step 1: Fetch tweets from Monitor Agent
            var tweets = await GetTweetsForRewardAsync(targetDate);
            result.TotalTweetsProcessed = tweets.Count;

            if (tweets.Count == 0)
            {
                _logger.LogInformation("No tweets found for {Date}, skipping reward calculation", dateKey);
                result.IsSuccess = true;
                result.ProcessingEndTime = Timestamp.FromDateTime(DateTime.UtcNow);
                result.ProcessingDurationMs = (long)(DateTime.UtcNow - processingStartTime).TotalMilliseconds;
                
                RaiseEvent(new RewardCalculationCompletedEvent
                {
                    AgentId = Id.ToString(),
                    CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                    TargetDate = Timestamp.FromDateTime(targetDate),
                    TweetsProcessed = 0,
                    IsSuccess = true
                });
                await ConfirmEventsAsync();
                RecordCalculationHistory(result, true);
                return result;
            }

            // Step 2: Filter and group tweets by user
            var eligibleTweets = tweets
                .Where(t => t.Type == TweetTypeProto.TweetTypeOriginal) // Only original tweets
                .Where(t => !State.Config.ExcludedUserIds.Contains(t.AuthorId)) // Exclude blacklisted users
                .Where(t => t.ViewCount >= State.Config.MinViewsForReward) // Meet minimum views
                .ToList();

            result.EligibleTweets = eligibleTweets.Count;
            _logger.LogInformation("Found {Eligible} eligible tweets out of {Total} total",
                eligibleTweets.Count, tweets.Count);

            // Step 3: Group by author and calculate rewards
            var rewardsByUser = new Dictionary<string, UserRewardRecord>();
            var maxDailyCredits = State.Config.MaxDailyCreditsPerUser;
            var shareLinkMultiplier = State.Config.ShareLinkMultiplier;

            foreach (var tweet in eligibleTweets)
            {
                // Calculate reward tier for this tweet
                var tier = CalculateRewardTier(tweet);
                if (tier == null)
                {
                    continue;
                }

                // Check daily limit for this user
                var currentUserCredits = GetUserDailyCredits(tweet.AuthorId, dateKey);
                if (currentUserCredits >= maxDailyCredits)
                {
                    _logger.LogDebug("User {User} has reached daily limit of {Limit}",
                        tweet.AuthorHandle, maxDailyCredits);
                    continue;
                }

                // Calculate base and bonus credits
                var baseCredits = tier.RewardCredits;
                var bonusMultiplier = tweet.HasValidShareLink ? shareLinkMultiplier : 1.0;
                var finalCredits = (int)(baseCredits * bonusMultiplier);

                // Apply daily limit cap
                var remainingCredits = maxDailyCredits - currentUserCredits;
                if (finalCredits > remainingCredits)
                {
                    finalCredits = remainingCredits;
                }

                // Create or update user reward record
                if (!rewardsByUser.ContainsKey(tweet.AuthorId))
                {
                    rewardsByUser[tweet.AuthorId] = new UserRewardRecord
                    {
                        UserId = tweet.AuthorId,
                        UserHandle = tweet.AuthorHandle,
                        TweetId = tweet.TweetId,
                        RewardDate = Timestamp.FromDateTime(targetDate),
                        RewardDateUtc = ((DateTimeOffset)targetDate).ToUnixTimeSeconds(),
                        RegularCredits = 0,
                        BonusCredits = 0,
                        BonusCreditsBeforeMultiplier = 0,
                        TweetCount = 0,
                        ShareLinkMultiplier = shareLinkMultiplier,
                        HasValidShareLink = false,
                        IsRewardSent = false
                    };
                }

                var userRecord = rewardsByUser[tweet.AuthorId];
                userRecord.TweetCount++;
                userRecord.BonusCreditsBeforeMultiplier += baseCredits;
                userRecord.BonusCredits += finalCredits;
                userRecord.FinalCredits += finalCredits;
                
                if (tweet.HasValidShareLink)
                {
                    userRecord.HasValidShareLink = true;
                }

                // Update the dictionary
                rewardsByUser[tweet.AuthorId] = userRecord;

                _logger.LogDebug("Calculated reward for tweet {TweetId} by {User}: {Credits} credits (tier: {Tier})",
                    tweet.TweetId, tweet.AuthorHandle, finalCredits, tier.TierName);
            }

            // Step 4: Store user rewards and raise events
            if (rewardsByUser.Count > 0)
            {
                // Initialize date key in state if not exists
                if (!State.UserRewards.ContainsKey(dateKey))
                {
                    State.UserRewards[dateKey] = new UserRewardRecordList();
                }

                foreach (var (userId, record) in rewardsByUser)
                {
                    State.UserRewards[dateKey].Records.Add(record);
                    result.UserRewards.Add(record);

                    // Raise event for each user reward
                    RaiseEvent(new UserRewardSentEvent
                    {
                        AgentId = Id.ToString(),
                        UserId = record.UserId,
                        UserHandle = record.UserHandle,
                        TweetId = record.TweetId,
                        Credits = record.FinalCredits,
                        SentAt = Timestamp.FromDateTime(DateTime.UtcNow),
                        TransactionId = Guid.NewGuid().ToString("N")
                    });
                }

                await ConfirmEventsAsync();
            }

            // Step 5: Update result and raise completion event
            result.UsersRewarded = rewardsByUser.Count;
            result.TotalCreditsDistributed = rewardsByUser.Values.Sum(r => r.FinalCredits);
            result.ProcessingEndTime = Timestamp.FromDateTime(DateTime.UtcNow);
            result.ProcessingDurationMs = (long)(DateTime.UtcNow - processingStartTime).TotalMilliseconds;
            result.IsSuccess = true;

            RaiseEvent(new RewardCalculationCompletedEvent
            {
                AgentId = Id.ToString(),
                CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                TargetDate = Timestamp.FromDateTime(targetDate),
                UsersRewarded = result.UsersRewarded,
                TotalCreditsDistributed = result.TotalCreditsDistributed,
                TweetsProcessed = result.TotalTweetsProcessed,
                IsSuccess = true
            });
            await ConfirmEventsAsync();

            RecordCalculationHistory(result, true);

            _logger.LogInformation(
                "Reward calculation completed for {Date}: {Users} users rewarded, {Credits} credits distributed, {Tweets} tweets processed",
                dateKey, result.UsersRewarded, result.TotalCreditsDistributed, result.TotalTweetsProcessed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in reward calculation for {Date}", dateKey);
            result.ErrorMessage = ex.Message;
            result.ProcessingEndTime = Timestamp.FromDateTime(DateTime.UtcNow);
            result.ProcessingDurationMs = (long)(DateTime.UtcNow - processingStartTime).TotalMilliseconds;

            RaiseEvent(new RewardCalculationCompletedEvent
            {
                AgentId = Id.ToString(),
                CompletedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                TargetDate = Timestamp.FromDateTime(targetDate),
                IsSuccess = false,
                ErrorMessage = ex.Message
            });
            await ConfirmEventsAsync();

            RecordCalculationHistory(result, false);
        }

        return result;
    }

    private void RecordCalculationHistory(RewardCalculationResult result, bool isSuccess)
    {
        var history = new RewardCalculationHistory
        {
            CalculationDate = result.CalculationDate,
            CalculationDateUtc = result.CalculationDateUtc,
            IsSuccess = isSuccess,
            UsersRewarded = result.UsersRewarded,
            TotalCreditsDistributed = result.TotalCreditsDistributed,
            ProcessingDurationMs = result.ProcessingDurationMs,
            ErrorMessage = result.ErrorMessage,
            ProcessedTimeRangeStart = result.ProcessedTimeRange?.StartTime,
            ProcessedTimeRangeEnd = result.ProcessedTimeRange?.EndTime
        };

        State.CalculationHistory.Add(history);

        // Keep only recent history
        if (State.CalculationHistory.Count > 50)
        {
            State.CalculationHistory.RemoveAt(0);
        }
    }

    private RewardConfig CreateConfigFromOptions()
    {
        var options = _options.CurrentValue;
        var config = new RewardConfig
        {
            TimeRangeStartHours = options.TimeOffsetMinutes / 60 + 
                (options.TimeOffsetMinutes % 60 == 0 ? 0 : 1),
            TimeRangeEndHours = options.TimeWindowMinutes / 60 + 
                (options.TimeOffsetMinutes % 60 == 0 ? 0 : 1),
            ShareLinkMultiplier = options.ShareLinkMultiplier,
            MaxDailyCreditsPerUser = options.DailyRewardLimit,
            EnableRewardCalculation = true,
            ConfigVersion = 1,
            MinViewsForReward = options.MinViewsForReward > 0 ? options.MinViewsForReward : 20
        };

        foreach (var tier in options.RewardTiers)
        {
            config.RewardTiers.Add(new RewardTier
            {
                MinViews = tier.MinViews,
                MinFollowers = tier.MinFollowers,
                RewardCredits = tier.RewardCredits,
                TierName = $"Tier-{tier.MinViews}v-{tier.MinFollowers}f"
            });
        }

        return config;
    }

    private bool ConfigHasChanged(RewardConfig newConfig)
    {
        if (State.Config == null) return true;
        
        return State.Config.TimeRangeStartHours != newConfig.TimeRangeStartHours ||
               State.Config.TimeRangeEndHours != newConfig.TimeRangeEndHours ||
               State.Config.ShareLinkMultiplier != newConfig.ShareLinkMultiplier ||
               State.Config.MaxDailyCreditsPerUser != newConfig.MaxDailyCreditsPerUser ||
               State.Config.MinViewsForReward != newConfig.MinViewsForReward ||
               State.Config.RewardTiers.Count != newConfig.RewardTiers.Count;
    }

    private DateTime GetNextRewardTriggerTimeUtc()
    {
        var now = DateTime.UtcNow;
        var triggerHours = new[] { 0, 4, 8, 12, 16, 20 };
        var today = new DateTime(now.Year, now.Month, now.Day, 0, 0, 0, DateTimeKind.Utc);
        
        foreach (var hour in triggerHours)
        {
            var triggerTime = today.AddHours(hour).AddMinutes(10);
            if (now < triggerTime)
                return triggerTime;
        }
        
        return today.AddDays(1).AddMinutes(10);
    }

    private bool ShouldExecuteRewardCalculation(DateTime currentTime)
    {
        return currentTime.Hour == 0 && currentTime.Minute < 5;
    }

    #endregion
}
