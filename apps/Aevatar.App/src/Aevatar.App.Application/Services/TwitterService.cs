using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Twitter;
using Aevatar.Application.Grains.Twitter;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.GodGPT.Dtos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Aevatar.Application.Grains.Common.Options;
using Volo.Abp.Application.Services;

namespace Aevatar.App.Application.Services;

/// <summary>
/// Service implementation for Twitter-related operations
/// Connects HTTP API layer to Twitter GAgents
/// </summary>
public class TwitterService : ApplicationService, ITwitterService
{
    private readonly ILogger<TwitterService> _logger;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly TwitterAuthOptions _twitterOptions;
    
    // Well-known IDs for singleton agents
    private const string MonitorAgentId = "twitter-monitor";
    private const string RewardAgentId = "twitter-reward";

    public TwitterService(
        ILogger<TwitterService> logger,
        IGAgentActorFactory actorFactory,
        IOptions<TwitterAuthOptions> twitterOptions)
    {
        _logger = logger;
        _actorFactory = actorFactory;
        _twitterOptions = twitterOptions.Value;
    }

    // ==========================================
    // User-Level Twitter Operations
    // ==========================================

    public async Task<TwitterAuthParamsDto> GetAuthParamsAsync(Guid userId)
    {
        _logger.LogDebug("[TwitterService][GetAuthParamsAsync] userId: {UserId}", userId);
        
        var actor = await _actorFactory.CreateGAgentActorAsync<TwitterAuthGAgent>(userId.ToString());
        
        // Use direct RPC invocation instead of As<T>() proxy
        var result = await actor.InvokeAsync<Aevatar.Agents.GodGPT.Protos.Twitter.TwitterAuthParamsProto>(
            nameof(ITwitterAuthGAgent.GetAuthParamsAsync));
        
        // Build authorization URL from params
        var authUrl = BuildAuthorizationUrl(result);
        
        return new TwitterAuthParamsDto
        {
            AuthorizationUrl = authUrl,
            CodeVerifier = result.CodeChallenge, // CodeVerifier is internal, use challenge for client
            State = result.State
        };
    }

    private string BuildAuthorizationUrl(Aevatar.Agents.GodGPT.Protos.Twitter.TwitterAuthParamsProto authParams)
    {
        var baseUrl = "https://twitter.com/i/oauth2/authorize";
        // Get redirect URI from PostLoginRedirectUrls dictionary (default to web platform)
        var redirectUri = _twitterOptions.PostLoginRedirectUrls.TryGetValue("web", out var uri) 
            ? Uri.EscapeDataString(uri) 
            : "";
        
        return $"{baseUrl}?response_type={authParams.ResponseType}" +
               $"&client_id={authParams.ClientId}" +
               $"&redirect_uri={redirectUri}" +
               $"&scope={Uri.EscapeDataString(authParams.Scope)}" +
               $"&state={authParams.State}" +
               $"&code_challenge={authParams.CodeChallenge}" +
               $"&code_challenge_method={authParams.CodeChallengeMethod}";
    }

    public async Task<TwitterAuthResultDto> VerifyAuthCodeAsync(Guid userId, TwitterAuthVerifyInput input)
    {
        _logger.LogDebug("[TwitterService][VerifyAuthCodeAsync] userId: {UserId}", userId);
        
        var actor = await _actorFactory.CreateGAgentActorAsync<TwitterAuthGAgent>(userId.ToString());
        
        // Use direct RPC invocation
        var result = await actor.InvokeAsync<Aevatar.Agents.GodGPT.Protos.Twitter.TwitterAuthResultProto>(
            nameof(ITwitterAuthGAgent.VerifyAuthCodeAsync), input.Platform, input.Code, input.RedirectUri);
        
        return new TwitterAuthResultDto
        {
            Success = result.Success,
            ErrorMessage = result.Error,
            TwitterUserId = result.TwitterId,
            TwitterUsername = result.Username
        };
    }

    public async Task<TwitterBindStatusDto> GetBindStatusAsync(Guid userId)
    {
        _logger.LogDebug("[TwitterService][GetBindStatusAsync] userId: {UserId}", userId);
        
        var actor = await _actorFactory.CreateGAgentActorAsync<TwitterAuthGAgent>(userId.ToString());
        
        // Use direct RPC invocation
        var result = await actor.InvokeAsync<Aevatar.Agents.GodGPT.Protos.Twitter.TwitterBindStatusProto>(
            nameof(ITwitterAuthGAgent.GetBindStatusAsync));
        
        return new TwitterBindStatusDto
        {
            IsBound = result.IsBound,
            TwitterUserId = result.TwitterId,
            TwitterUsername = result.Username,
            ProfileImageUrl = result.ProfileImageUrl
        };
    }

    public Task<TwitterOperationResultDto> UnbindTwitterAsync(Guid userId)
    {
        _logger.LogDebug("[TwitterService][UnbindTwitterAsync] userId: {UserId}", userId);
        
        // Unbind is currently not implemented - just return success
        // The actual unbind logic would need to be added to ITwitterAuthGAgent
        return Task.FromResult(new TwitterOperationResultDto 
        { 
            IsSuccess = false, 
            ErrorMessage = "Unbind operation not implemented" 
        });
    }

    // ==========================================
    // Management-Level Twitter Operations
    // ==========================================

    public async Task<TwitterOperationResultDto> FetchTweetsManuallyAsync()
    {
        _logger.LogInformation("[TwitterService][FetchTweetsManuallyAsync] Starting manual tweet fetch");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(MonitorAgentId);
            var result = await actor.InvokeAsync<TweetFetchResult>(nameof(ITwitterMonitorGAgent.FetchTweetsManuallyAsync));
            
            // TweetFetchResult doesn't have IsSuccess, check if error is empty
            var success = string.IsNullOrEmpty(result.ErrorMessage);
            return new TwitterOperationResultDto
            {
                IsSuccess = success,
                ErrorMessage = result.ErrorMessage
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][FetchTweetsManuallyAsync] Failed to fetch tweets manually");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> RefetchTweetsByTimeRangeAsync(long startTimeUtcSecond, long endTimeUtcSecond)
    {
        _logger.LogInformation("[TwitterService][RefetchTweetsByTimeRangeAsync] Refetch from {Start} to {End}", 
            startTimeUtcSecond, endTimeUtcSecond);
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(MonitorAgentId);
            
            var timeRange = new TimeRange
            {
                StartTimeUtcSecond = startTimeUtcSecond,
                EndTimeUtcSecond = endTimeUtcSecond
            };
            
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterMonitorGAgent.RefetchTweetsByTimeRangeAsync), timeRange);
            
            return new TwitterOperationResultDto
            {
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to refetch tweets"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][RefetchTweetsByTimeRangeAsync] Failed to refetch tweets");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> StartTweetMonitoringAsync()
    {
        _logger.LogInformation("[TwitterService][StartTweetMonitoringAsync] Starting tweet monitoring");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(MonitorAgentId);
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterMonitorGAgent.StartMonitoringAsync));
            
            return new TwitterOperationResultDto 
            { 
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to start monitoring"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][StartTweetMonitoringAsync] Failed to start monitoring");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> StopTweetMonitoringAsync()
    {
        _logger.LogInformation("[TwitterService][StopTweetMonitoringAsync] Stopping tweet monitoring");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(MonitorAgentId);
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterMonitorGAgent.StopMonitoringAsync));
            
            return new TwitterOperationResultDto 
            { 
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to stop monitoring"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][StopTweetMonitoringAsync] Failed to stop monitoring");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterMonitorStatusDto> GetTweetMonitoringStatusAsync()
    {
        _logger.LogDebug("[TwitterService][GetTweetMonitoringStatusAsync] Getting monitor status");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterMonitorGAgent>(MonitorAgentId);
            var status = await actor.InvokeAsync<TweetMonitorStatus>(nameof(ITwitterMonitorGAgent.GetMonitoringStatusAsync));
            
            return new TwitterMonitorStatusDto
            {
                IsRunning = status.IsRunning,
                LastFetchTime = status.LastFetchTimeUtc > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(status.LastFetchTimeUtc).UtcDateTime 
                    : null,
                TotalTweetsFetched = status.TotalTweetsStored,
                ErrorMessage = status.LastError
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][GetTweetMonitoringStatusAsync] Failed to get status");
            return new TwitterMonitorStatusDto { IsRunning = false, ErrorMessage = ex.Message };
        }
    }

    // Reward Management

    public async Task<TwitterOperationResultDto> TriggerRewardCalculationAsync(long targetDateUtcSeconds)
    {
        _logger.LogInformation("[TwitterService][TriggerRewardCalculationAsync] Trigger for date: {Date}", targetDateUtcSeconds);
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            
            var targetDate = DateTimeOffset.FromUnixTimeSeconds(targetDateUtcSeconds).UtcDateTime;
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterRewardGAgent.TriggerRewardCalculationAsync), targetDate);
            
            return new TwitterOperationResultDto
            {
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to trigger reward calculation"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][TriggerRewardCalculationAsync] Failed to trigger calculation");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> ClearRewardByDayAsync(long targetDateUtcSeconds)
    {
        _logger.LogInformation("[TwitterService][ClearRewardByDayAsync] Clear for date: {Date}", targetDateUtcSeconds);
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterRewardGAgent.ClearRewardByDayUtcSecondAsync), targetDateUtcSeconds);
            
            return new TwitterOperationResultDto 
            { 
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to clear rewards"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][ClearRewardByDayAsync] Failed to clear rewards");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> StartRewardCalculationAsync()
    {
        _logger.LogInformation("[TwitterService][StartRewardCalculationAsync] Starting reward calculation");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterRewardGAgent.StartRewardCalculationAsync));
            
            return new TwitterOperationResultDto 
            { 
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to start reward calculation"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][StartRewardCalculationAsync] Failed to start calculation");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterOperationResultDto> StopRewardCalculationAsync()
    {
        _logger.LogInformation("[TwitterService][StopRewardCalculationAsync] Stopping reward calculation");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var result = await actor.InvokeAsync<bool>(nameof(ITwitterRewardGAgent.StopRewardCalculationAsync));
            
            return new TwitterOperationResultDto 
            { 
                IsSuccess = result,
                ErrorMessage = result ? null : "Failed to stop reward calculation"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][StopRewardCalculationAsync] Failed to stop calculation");
            return new TwitterOperationResultDto { IsSuccess = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<TwitterRewardStatusDto> GetRewardCalculationStatusAsync()
    {
        _logger.LogDebug("[TwitterService][GetRewardCalculationStatusAsync] Getting reward status");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var status = await actor.InvokeAsync<RewardCalculationStatus>(nameof(ITwitterRewardGAgent.GetRewardCalculationStatusAsync));
            
            return new TwitterRewardStatusDto
            {
                IsRunning = status.IsRunning,
                LastCalculationTime = status.LastCalculationTimeUtc > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(status.LastCalculationTimeUtc).UtcDateTime 
                    : null,
                TotalCalculations = status.TotalUsersRewarded, // Use total users as a proxy for calculations
                ErrorMessage = status.LastError
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][GetRewardCalculationStatusAsync] Failed to get status");
            return new TwitterRewardStatusDto { IsRunning = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<Dictionary<string, List<ManagerUserRewardRecordDto>>> GetUserRewardsByUserIdAsync(string userId)
    {
        _logger.LogDebug("[TwitterService][GetUserRewardsByUserIdAsync] Getting rewards for user: {UserId}", userId);
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var result = await actor.InvokeAsync<Google.Protobuf.Collections.MapField<string, UserRewardRecordList>>(
                nameof(ITwitterRewardGAgent.GetUserRewardsByUserIdAsync), userId);
            
            // Convert to DTO format
            var dict = new Dictionary<string, List<ManagerUserRewardRecordDto>>();
            foreach (var kvp in result)
            {
                var records = new List<ManagerUserRewardRecordDto>();
                foreach (var record in kvp.Value.Records)
                {
                    records.Add(new ManagerUserRewardRecordDto
                    {
                        UserId = record.UserId,
                        TwitterUsername = record.UserHandle,
                        RewardAmount = record.FinalCredits,
                        RewardDate = record.RewardDateUtc > 0 
                            ? DateTimeOffset.FromUnixTimeSeconds(record.RewardDateUtc).UtcDateTime 
                            : DateTime.UtcNow,
                        RewardReason = $"Tweet engagement (Tweet: {record.TweetId})",
                        TransactionId = record.RewardTransactionId ?? string.Empty,
                        Status = record.IsRewardSent ? "Completed" : "Pending"
                    });
                }
                dict[kvp.Key] = records;
            }
            
            return dict;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][GetUserRewardsByUserIdAsync] Failed to get user rewards");
            return new Dictionary<string, List<ManagerUserRewardRecordDto>>();
        }
    }

    public async Task<List<ManagerRewardCalculationHistoryDto>> GetCalculationHistoryListAsync()
    {
        _logger.LogDebug("[TwitterService][GetCalculationHistoryListAsync] Getting calculation history");
        
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<TwitterRewardGAgent>(RewardAgentId);
            var result = await actor.InvokeAsync<Google.Protobuf.Collections.RepeatedField<RewardCalculationHistory>>(
                nameof(ITwitterRewardGAgent.GetRewardCalculationHistoryAsync), 30);
            
            var list = new List<ManagerRewardCalculationHistoryDto>();
            foreach (var record in result)
            {
                list.Add(new ManagerRewardCalculationHistoryDto
                {
                    CalculationDate = record.CalculationDateUtc > 0 
                        ? DateTimeOffset.FromUnixTimeSeconds(record.CalculationDateUtc).UtcDateTime 
                        : DateTime.UtcNow,
                    CalculationDateUtc = record.CalculationDateUtc,
                    IsSuccess = record.IsSuccess,
                    UsersRewarded = record.UsersRewarded,
                    TotalCreditsDistributed = record.TotalCreditsDistributed,
                    ProcessingDuration = TimeSpan.FromMilliseconds(record.ProcessingDurationMs),
                    ErrorMessage = record.ErrorMessage ?? string.Empty,
                    ProcessedTimeRangeStart = record.ProcessedTimeRangeStart?.ToDateTime() ?? DateTime.UtcNow,
                    ProcessedTimeRangeEnd = record.ProcessedTimeRangeEnd?.ToDateTime() ?? DateTime.UtcNow
                });
            }
            
            return list;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TwitterService][GetCalculationHistoryListAsync] Failed to get history");
            return new List<ManagerRewardCalculationHistoryDto>();
        }
    }
}
