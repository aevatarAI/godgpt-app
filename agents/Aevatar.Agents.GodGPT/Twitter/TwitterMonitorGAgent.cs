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
/// Twitter Monitor Agent - Stateful agent for scheduled tweet monitoring
/// Migrated from TwitterMonitorGrain to new Agent architecture using EventSourcing
/// </summary>
public class TwitterMonitorGAgent : GAgentBase<TwitterMonitorState>, ITwitterMonitorGAgent
{
    // Dependency injection via properties for Orleans compatibility
    public ILogger<TwitterMonitorGAgent>? Logger { get; set; }
    public IOptionsMonitor<TwitterRewardOptions>? Options { get; set; }
    public ITwitterApiService? TwitterApiService { get; set; }

    // Parameterless constructor required for Orleans activation
    public TwitterMonitorGAgent() : base()
    {
    }

    // Helper properties for null safety
    private ILogger<TwitterMonitorGAgent> _logger => Logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TwitterMonitorGAgent>.Instance;
    private IOptionsMonitor<TwitterRewardOptions> _options => Options ?? throw new InvalidOperationException("Options not injected");
    private ITwitterApiService _twitterApiService => TwitterApiService ?? throw new InvalidOperationException("TwitterApiService not injected");

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Twitter Monitor Agent - Running: {State.IsRunning}, " +
            $"Tweets Stored: {State.StoredTweets.Count}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        _logger.LogInformation("TwitterMonitorGAgent {AgentId} activating", Id);

        // Initialize configuration from appsettings.json
        var config = CreateConfigFromOptions();
        
        if (State.Config == null || ConfigHasChanged(config))
        {
            RaiseEvent(new MonitorConfigUpdatedEvent
            {
                AgentId = Id.ToString(),
                Config = config,
                UpdatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await ConfirmEventsAsync();
            
            _logger.LogInformation("TwitterMonitorGAgent configuration updated from appsettings.json");
        }
    }

    #region Monitoring Control

    public async Task<bool> StartMonitoringAsync()
    {
        _logger.LogInformation("Starting tweet monitoring");
        
        if (State.IsRunning)
        {
            _logger.LogInformation("Monitoring is already running");
            return true;
        }

        var config = State.Config ?? CreateConfigFromOptions();

        RaiseEvent(new TwitterMonitoringStartedEvent
        {
            AgentId = Id.ToString(),
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            FetchIntervalMinutes = config.FetchIntervalMinutes,
            SearchQuery = config.SearchQuery
        });
        await ConfirmEventsAsync();

        _logger.LogInformation("Tweet monitoring started with interval {Interval} minutes", 
            config.FetchIntervalMinutes);
        
        return true;
    }

    public async Task<bool> StopMonitoringAsync()
    {
        _logger.LogInformation("Stopping tweet monitoring");

        if (!State.IsRunning)
        {
            _logger.LogInformation("Monitoring is not running");
            return true;
        }

        RaiseEvent(new TwitterMonitoringStoppedEvent
        {
            AgentId = Id.ToString(),
            StoppedAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });
        await ConfirmEventsAsync();

        _logger.LogInformation("Tweet monitoring stopped");
        return true;
    }

    public Task<TweetMonitorStatus> GetMonitoringStatusAsync()
    {
        var status = new TweetMonitorStatus
        {
            IsRunning = State.IsRunning,
            LastFetchTime = State.LastFetchTime,
            LastFetchTimeUtc = State.LastFetchTimeUtc,
            TotalTweetsStored = State.StoredTweets.Count,
            TweetsFetchedToday = GetTweetsFetchedToday(),
            LastError = State.LastError,
            Config = State.Config,
            NextScheduledFetch = State.NextScheduledFetch,
            NextScheduledFetchUtc = State.NextScheduledFetchUtc
        };

        return Task.FromResult(status);
    }

    #endregion

    #region Manual Operations

    public async Task<TweetFetchResult> FetchTweetsManuallyAsync()
    {
        _logger.LogInformation("Manual tweet fetch requested");
        return await FetchTweetsInternalAsync();
    }

    public async Task<bool> RefetchTweetsByTimeRangeAsync(TimeRange timeRange)
    {
        _logger.LogInformation("Starting refetch for time range {Start} to {End}",
            timeRange.StartTime, timeRange.EndTime);

        var endTime = timeRange.EndTime.ToDateTime();
        var maxEndTime = DateTime.UtcNow;
        
        if (endTime > maxEndTime)
        {
            endTime = maxEndTime;
        }

        var startTime = timeRange.StartTime.ToDateTime();
        if (startTime >= endTime)
        {
            _logger.LogWarning("Invalid time range: StartTime >= EndTime");
            return false;
        }

        // Process in time windows
        var currentStart = startTime;
        var windowHours = _options.CurrentValue.TimeWindowHours;
        
        while (currentStart < endTime)
        {
            var currentEnd = currentStart.AddHours(windowHours);
            if (currentEnd > endTime)
                currentEnd = endTime;

            var request = new SearchTweetsRequest
            {
                Query = State.Config?.SearchQuery ?? _options.CurrentValue.MonitorHandle,
                MaxResults = _options.CurrentValue.MaxTweetsPerWindow,
                StartTime = currentStart,
                EndTime = currentEnd
            };

            await FetchTweetsWithRequestAsync(request);
            currentStart = currentEnd;

            // Delay between windows
            if (currentStart < endTime)
            {
                await Task.Delay(TimeSpan.FromMinutes(_options.CurrentValue.MinTimeWindowMinutes));
            }
        }

        return true;
    }

    #endregion

    #region Query Operations

    public Task<List<TweetRecord>> QueryTweetsByTimeRangeAsync(TimeRange timeRange)
    {
        var startUtc = timeRange.StartTimeUtcSecond;
        var endUtc = timeRange.EndTimeUtcSecond;

        var filteredTweets = State.StoredTweets.Values
            .Where(tweet => tweet.CreatedAtUtc >= startUtc && tweet.CreatedAtUtc <= endUtc)
            .OrderBy(tweet => tweet.CreatedAtUtc)
            .ToList();

        return Task.FromResult(filteredTweets);
    }

    public Task<List<TweetFetchHistory>> GetFetchHistoryAsync(int days = 7)
    {
        var cutoffTime = DateTime.UtcNow.AddDays(-days);
        var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

        var recentHistory = State.FetchHistory
            .Where(h => h.FetchTimeUtc >= cutoffUtc)
            .OrderByDescending(h => h.FetchTimeUtc)
            .ToList();

        return Task.FromResult(recentHistory);
    }

    public Task<TweetStatistics> GetTweetStatisticsAsync(TimeRange timeRange)
    {
        var tweets = State.StoredTweets.Values
            .Where(tweet => tweet.CreatedAtUtc >= timeRange.StartTimeUtcSecond && 
                           tweet.CreatedAtUtc <= timeRange.EndTimeUtcSecond)
            .ToList();

        var statistics = new TweetStatistics
        {
            TotalTweets = tweets.Count,
            OriginalTweets = tweets.Count(t => t.Type == TweetTypeProto.TweetTypeOriginal),
            TweetsWithShareLinks = tweets.Count(t => t.HasValidShareLink),
            UnprocessedTweets = tweets.Count(t => !t.IsProcessed),
            StatisticsGeneratedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            QueryRange = timeRange
        };

        // Generate hourly statistics
        var tweetsByHour = tweets
            .GroupBy(t => t.CreatedAt.ToDateTime().ToString("yyyy-MM-dd HH:00"))
            .ToDictionary(g => g.Key, g => g.Count());
        
        foreach (var kvp in tweetsByHour)
        {
            statistics.TweetsByHour[kvp.Key] = kvp.Value;
        }

        // Generate top authors
        var topAuthors = tweets
            .GroupBy(t => t.AuthorHandle)
            .OrderByDescending(g => g.Count())
            .Take(10)
            .ToDictionary(g => g.Key, g => g.Count());
        
        foreach (var kvp in topAuthors)
        {
            statistics.TopAuthors[kvp.Key] = kvp.Value;
        }

        return Task.FromResult(statistics);
    }

    #endregion

    #region Configuration

    public Task<TweetMonitorConfig> GetMonitoringConfigAsync()
    {
        return Task.FromResult(State.Config ?? CreateConfigFromOptions());
    }

    public async Task<int> CleanupExpiredTweetsAsync()
    {
        var retentionDays = State.Config?.DataRetentionDays ?? 
            _options.CurrentValue.DataRetentionDays;
        var cutoffTime = DateTime.UtcNow.AddDays(-retentionDays);
        var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

        var expiredTweetIds = State.StoredTweets
            .Where(kvp => kvp.Value.CreatedAtUtc < cutoffUtc)
            .Select(kvp => kvp.Key)
            .ToList();

        if (expiredTweetIds.Count > 0)
        {
            RaiseEvent(new ExpiredTweetsCleanedEvent
            {
                AgentId = Id.ToString(),
                CleanedCount = expiredTweetIds.Count,
                CleanedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            await ConfirmEventsAsync();
        }

        _logger.LogInformation("Cleaned up {Count} expired tweets", expiredTweetIds.Count);
        return expiredTweetIds.Count;
    }

    #endregion

    #region TransitionState

    protected override void TransitionState(TwitterMonitorState state, IMessage evt)
    {
        switch (evt)
        {
            case TwitterMonitoringStartedEvent started:
                state.IsRunning = true;
                state.NextScheduledFetch = Timestamp.FromDateTime(
                    DateTime.UtcNow.AddMinutes(started.FetchIntervalMinutes));
                state.NextScheduledFetchUtc = 
                    ((DateTimeOffset)state.NextScheduledFetch.ToDateTime()).ToUnixTimeSeconds();
                break;

            case TwitterMonitoringStoppedEvent:
                state.IsRunning = false;
                break;

            case TweetsFetchedEvent fetched:
                state.LastFetchTime = fetched.FetchedAt;
                state.LastFetchTimeUtc = fetched.FetchedAt.ToDateTime()
                    .Subtract(DateTime.UnixEpoch).Ticks / TimeSpan.TicksPerSecond;
                state.LastError = string.Empty;
                
                // Update next scheduled fetch
                var interval = state.Config?.FetchIntervalMinutes ?? 60;
                state.NextScheduledFetch = Timestamp.FromDateTime(
                    DateTime.UtcNow.AddMinutes(interval));
                state.NextScheduledFetchUtc = 
                    ((DateTimeOffset)state.NextScheduledFetch.ToDateTime()).ToUnixTimeSeconds();
                break;

            case MonitorConfigUpdatedEvent configUpdated:
                state.Config = configUpdated.Config;
                break;

            case ExpiredTweetsCleanedEvent:
                var retentionDays = state.Config?.DataRetentionDays ?? 7;
                var cutoffTime = DateTime.UtcNow.AddDays(-retentionDays);
                var cutoffUtc = ((DateTimeOffset)cutoffTime).ToUnixTimeSeconds();

                var keysToRemove = state.StoredTweets
                    .Where(kvp => kvp.Value.CreatedAtUtc < cutoffUtc)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    state.StoredTweets.Remove(key);
                }

                // Also cleanup fetch history
                var historyToKeep = state.FetchHistory
                    .Where(h => h.FetchTimeUtc >= cutoffUtc)
                    .ToList();
                
                state.FetchHistory.Clear();
                foreach (var history in historyToKeep)
                {
                    state.FetchHistory.Add(history);
                }
                break;
        }
    }

    #endregion

    #region Private Helpers

    private async Task<TweetFetchResult> FetchTweetsInternalAsync()
    {
        var fetchStartTime = DateTime.UtcNow;
        var result = new TweetFetchResult
        {
            FetchStartTime = Timestamp.FromDateTime(fetchStartTime),
            FetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds()
        };

        try
        {
            var startTime = State.LastFetchTime?.ToDateTime() ?? 
                DateTime.UtcNow.AddHours(-1);
            var endTime = DateTime.UtcNow;

            if (startTime > endTime)
            {
                startTime = endTime.AddHours(-1);
            }

            var request = new SearchTweetsRequest
            {
                Query = State.Config?.SearchQuery ?? _options.CurrentValue.MonitorHandle,
                MaxResults = State.Config?.MaxTweetsPerFetch ?? 
                    _options.CurrentValue.BatchFetchSize,
                StartTime = startTime,
                EndTime = endTime
            };

            result = await FetchTweetsWithRequestAsync(request);
            
            RaiseEvent(new TweetsFetchedEvent
            {
                AgentId = Id.ToString(),
                FetchedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                TotalFetched = result.TotalFetched,
                NewTweets = result.NewTweets,
                DuplicatesSkipped = result.DuplicateSkipped,
                FilteredOut = result.FilteredOut,
                NewTweetIds = { result.NewTweetIds }
            });
            await ConfirmEventsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in internal tweet fetch");
            result.ErrorMessage = ex.Message;
        }

        result.FetchEndTime = Timestamp.FromDateTime(DateTime.UtcNow);
        result.FetchEndTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        
        return result;
    }

    private async Task<TweetFetchResult> FetchTweetsWithRequestAsync(SearchTweetsRequest request)
    {
        var fetchStartTime = DateTime.UtcNow;
        var result = new TweetFetchResult
        {
            FetchStartTime = Timestamp.FromDateTime(fetchStartTime),
            FetchStartTimeUtc = ((DateTimeOffset)fetchStartTime).ToUnixTimeSeconds()
        };

        try
        {
            var searchResult = await _twitterApiService.SearchTweetsAsync(request);
            if (!searchResult.IsSuccess)
            {
                result.ErrorMessage = searchResult.ErrorMessage;
                return result;
            }

            result.TotalFetched = searchResult.Data?.Tweets.Count ?? 0;

            foreach (var tweet in searchResult.Data?.Tweets ?? new List<TweetDto>())
            {
                if (State.StoredTweets.ContainsKey(tweet.Id))
                {
                    result.DuplicateSkipped++;
                    continue;
                }

                if (_options.CurrentValue.ExcludedAccountIds.Contains(tweet.AuthorId))
                {
                    result.FilteredOut++;
                    continue;
                }

                // Analyze tweet
                var analysisResult = await _twitterApiService.AnalyzeTweetLightweightAsync(tweet.Id);
                if (!analysisResult.IsSuccess)
                {
                    result.FilteredOut++;
                    continue;
                }

                var tweetDetails = analysisResult.Data!;

                // Filter non-original tweets if configured
                var filterOriginal = State.Config?.FilterOriginalOnly ?? true;
                if (filterOriginal && tweetDetails.Type != Services.TweetType.Original)
                {
                    result.FilteredOut++;
                    continue;
                }

                // Create tweet record
                var tweetRecord = new TweetRecord
                {
                    TweetId = tweet.Id,
                    AuthorId = tweetDetails.AuthorId,
                    AuthorHandle = tweetDetails.AuthorHandle,
                    AuthorName = tweetDetails.AuthorName,
                    CreatedAt = Timestamp.FromDateTime(tweetDetails.CreatedAt),
                    CreatedAtUtc = new DateTimeOffset(tweetDetails.CreatedAt).ToUnixTimeSeconds(),
                    Text = string.Empty, // Privacy: don't store text
                    Type = MapTweetType(tweetDetails.Type),
                    ViewCount = tweetDetails.ViewCount,
                    FollowerCount = tweetDetails.FollowerCount,
                    HasValidShareLink = tweetDetails.HasValidShareLink,
                    ShareLinkUrl = string.Empty, // Privacy: don't store URL
                    IsProcessed = false,
                    FetchedAt = Timestamp.FromDateTime(fetchStartTime)
                };

                State.StoredTweets[tweet.Id] = tweetRecord;
                result.NewTweetIds.Add(tweet.Id);
                result.NewTweets++;

                // Delay between processing tweets
                if (_options.CurrentValue.TweetProcessingDelayMs > 0)
                {
                    await Task.Delay(_options.CurrentValue.TweetProcessingDelayMs);
                }
            }

            // Record fetch history
            RecordFetchHistory(result, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching tweets with request");
            result.ErrorMessage = ex.Message;
            RecordFetchHistory(result, false);
        }

        result.FetchEndTime = Timestamp.FromDateTime(DateTime.UtcNow);
        result.FetchEndTimeUtc = ((DateTimeOffset)DateTime.UtcNow).ToUnixTimeSeconds();
        
        return result;
    }

    private void RecordFetchHistory(TweetFetchResult result, bool isSuccess)
    {
        var history = new TweetFetchHistory
        {
            FetchTime = result.FetchStartTime,
            FetchTimeUtc = result.FetchStartTimeUtc,
            TweetsFetched = result.TotalFetched,
            NewTweets = result.NewTweets,
            IsSuccess = isSuccess,
            ErrorMessage = result.ErrorMessage,
            DurationMs = result.FetchEndTimeUtc - result.FetchStartTimeUtc
        };

        State.FetchHistory.Add(history);

        // Keep only recent history
        if (State.FetchHistory.Count > 50)
        {
            State.FetchHistory.RemoveAt(0);
        }
    }

    private TweetMonitorConfig CreateConfigFromOptions()
    {
        return new TweetMonitorConfig
        {
            FetchIntervalMinutes = _options.CurrentValue.PullIntervalMinutes,
            MaxTweetsPerFetch = _options.CurrentValue.BatchFetchSize,
            DataRetentionDays = _options.CurrentValue.DataRetentionDays,
            SearchQuery = _options.CurrentValue.MonitorHandle,
            FilterOriginalOnly = true,
            EnableAutoCleanup = true,
            ConfigVersion = 1
        };
    }

    private bool ConfigHasChanged(TweetMonitorConfig newConfig)
    {
        if (State.Config == null) return true;
        
        return State.Config.FetchIntervalMinutes != newConfig.FetchIntervalMinutes ||
               State.Config.MaxTweetsPerFetch != newConfig.MaxTweetsPerFetch ||
               State.Config.DataRetentionDays != newConfig.DataRetentionDays ||
               State.Config.SearchQuery != newConfig.SearchQuery;
    }

    private int GetTweetsFetchedToday()
    {
        var todayStart = DateTime.UtcNow.Date;
        var todayStartUtc = ((DateTimeOffset)todayStart).ToUnixTimeSeconds();
        
        return State.FetchHistory
            .Where(h => h.FetchTimeUtc >= todayStartUtc && h.IsSuccess)
            .Sum(h => h.NewTweets);
    }

    private static TweetTypeProto MapTweetType(Services.TweetType type)
    {
        return type switch
        {
            Services.TweetType.Original => TweetTypeProto.TweetTypeOriginal,
            Services.TweetType.Reply => TweetTypeProto.TweetTypeReply,
            Services.TweetType.Retweet => TweetTypeProto.TweetTypeRetweet,
            Services.TweetType.Quote => TweetTypeProto.TweetTypeQuote,
            _ => TweetTypeProto.TweetTypeUnknown
        };
    }

    #endregion
}
