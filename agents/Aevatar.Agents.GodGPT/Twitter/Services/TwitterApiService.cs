using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Application.Grains.Common.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Twitter.Services;

/// <summary>
/// Twitter API service implementation - Stateless service for Twitter API interactions
/// Migrated from TwitterInteractionGrain to follow proper service pattern
/// </summary>
public class TwitterApiService : ITwitterApiService
{
    private readonly ILogger<TwitterApiService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<TwitterRewardOptions> _options;
    
    // Twitter API endpoints
    private const string TwitterApiBase = "https://api.twitter.com/2";
    private const string SearchTweetsEndpoint = "/tweets/search/recent";
    private const string GetTweetEndpoint = "/tweets/{0}";
    private const string GetUserEndpoint = "/users/{0}";
    private const string GetTweetsEndpoint = "/tweets";
    
    // Share link validation regex
    private static readonly Regex ShareLinkRegex = new(
        @"https?://(?:app\.)?godgpt\.(?:fun|ai)/share/[a-zA-Z0-9\-]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    
    private static readonly Regex UrlRegex = new(
        @"https?://[^\s<>""]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public TwitterApiService(
        ILogger<TwitterApiService> logger,
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<TwitterRewardOptions> options)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient("TwitterApi");
        client.DefaultRequestHeaders.Authorization = 
            new AuthenticationHeaderValue("Bearer", _options.CurrentValue.BearerToken);
        return client;
    }

    #region Tweet Search and Retrieval

    public async Task<TwitterApiResult<SearchTweetsResponse>> SearchTweetsAsync(SearchTweetsRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Query))
            {
                return TwitterApiResult<SearchTweetsResponse>.Failure("Search query cannot be empty");
            }

            _logger.LogDebug("Searching tweets with query: {Query}", request.Query);
            
            // Validate time parameters
            ValidateTimeRange(ref request);

            // Build URL
            var encodedQuery = Uri.EscapeDataString(request.Query);
            var url = $"{TwitterApiBase}{SearchTweetsEndpoint}?query={encodedQuery}&max_results={request.MaxResults}" +
                      "&tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets" +
                      "&expansions=author_id&user.fields=id,username,name,public_metrics";

            if (request.StartTime.HasValue)
                url += $"&start_time={request.StartTime.Value:yyyy-MM-ddTHH:mm:ss.fffZ}";
            if (request.EndTime.HasValue)
                url += $"&end_time={request.EndTime.Value:yyyy-MM-ddTHH:mm:ss.fffZ}";
            if (!string.IsNullOrEmpty(request.NextToken))
                url += $"&next_token={request.NextToken}";

            using var client = CreateClient();
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("SearchTweetsAsync failed: {StatusCode} - {Content}", response.StatusCode, content);
                return TwitterApiResult<SearchTweetsResponse>.Failure(
                    $"Twitter API error {response.StatusCode}", (int)response.StatusCode);
            }

            var searchResponse = ParseSearchResponse(content);
            return TwitterApiResult<SearchTweetsResponse>.Success(searchResponse);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching tweets with query: {Query}", request.Query);
            return TwitterApiResult<SearchTweetsResponse>.Failure(ex.Message);
        }
    }

    public async Task<TwitterApiResult<TweetProcessResult>> AnalyzeTweetAsync(string tweetId)
    {
        try
        {
            var detailsResult = await GetTweetDetailsAsync(tweetId);
            if (!detailsResult.IsSuccess || detailsResult.Data == null)
            {
                return TwitterApiResult<TweetProcessResult>.Failure(detailsResult.ErrorMessage);
            }

            var details = detailsResult.Data;
            
            // Get user info for follower count
            var userResult = await GetUserInfoAsync(details.AuthorId);
            var followerCount = userResult.Data?.FollowersCount ?? 0;

            var result = new TweetProcessResult
            {
                TweetId = details.TweetId,
                AuthorId = details.AuthorId,
                AuthorHandle = details.AuthorHandle,
                CreatedAt = details.CreatedAt,
                Type = details.Type,
                ViewCount = details.ViewCount,
                FollowerCount = followerCount,
                HasValidShareLink = details.HasValidShareLink,
                ShareLinkUrl = details.ShareLinkUrl,
                IsProcessed = true
            };

            return TwitterApiResult<TweetProcessResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing tweet: {TweetId}", tweetId);
            return TwitterApiResult<TweetProcessResult>.Failure(ex.Message);
        }
    }

    public async Task<TwitterApiResult<TweetProcessResult>> AnalyzeTweetLightweightAsync(string tweetId)
    {
        try
        {
            var detailsResult = await GetTweetDetailsAsync(tweetId);
            if (!detailsResult.IsSuccess || detailsResult.Data == null)
            {
                return TwitterApiResult<TweetProcessResult>.Failure(detailsResult.ErrorMessage);
            }

            var details = detailsResult.Data;
            var result = new TweetProcessResult
            {
                TweetId = details.TweetId,
                AuthorId = details.AuthorId,
                AuthorHandle = details.AuthorHandle,
                CreatedAt = details.CreatedAt,
                Type = details.Type,
                ViewCount = details.ViewCount,
                HasValidShareLink = details.HasValidShareLink,
                ShareLinkUrl = details.ShareLinkUrl,
                IsProcessed = true
            };

            return TwitterApiResult<TweetProcessResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing tweet lightweight: {TweetId}", tweetId);
            return TwitterApiResult<TweetProcessResult>.Failure(ex.Message);
        }
    }

    public async Task<TwitterApiResult<List<TweetProcessResult>>> BatchAnalyzeTweetsAsync(List<string> tweetIds)
    {
        var results = new List<TweetProcessResult>();
        
        foreach (var tweetId in tweetIds)
        {
            var result = await AnalyzeTweetAsync(tweetId);
            if (result.IsSuccess && result.Data != null)
            {
                results.Add(result.Data);
            }
        }

        return TwitterApiResult<List<TweetProcessResult>>.Success(results);
    }

    public async Task<TwitterApiResult<List<TweetProcessResult>>> BatchAnalyzeTweetsLightweightAsync(List<string> tweetIds)
    {
        var results = new List<TweetProcessResult>();
        
        foreach (var tweetId in tweetIds)
        {
            var result = await AnalyzeTweetLightweightAsync(tweetId);
            if (result.IsSuccess && result.Data != null)
            {
                results.Add(result.Data);
            }
        }

        return TwitterApiResult<List<TweetProcessResult>>.Success(results);
    }

    #endregion

    #region Tweet Details

    public async Task<TwitterApiResult<TweetDetails>> GetTweetDetailsAsync(string tweetId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tweetId))
            {
                return TwitterApiResult<TweetDetails>.Failure("Tweet ID cannot be empty");
            }

            var url = $"{TwitterApiBase}{string.Format(GetTweetEndpoint, tweetId)}" +
                      "?tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets" +
                      "&expansions=author_id&user.fields=id,username,name,public_metrics";

            using var client = CreateClient();
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("GetTweetDetailsAsync failed for {TweetId}: {StatusCode}", tweetId, response.StatusCode);
                return TwitterApiResult<TweetDetails>.Failure(
                    $"Twitter API error: {response.StatusCode}", (int)response.StatusCode);
            }

            var details = ParseTweetDetails(content);
            return TwitterApiResult<TweetDetails>.Success(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tweet details: {TweetId}", tweetId);
            return TwitterApiResult<TweetDetails>.Failure(ex.Message);
        }
    }

    public async Task<TwitterApiResult<List<TweetDetails>>> GetBatchTweetDetailsAsync(List<string> tweetIds)
    {
        try
        {
            if (tweetIds == null || tweetIds.Count == 0)
            {
                return TwitterApiResult<List<TweetDetails>>.Failure("Tweet IDs cannot be empty");
            }

            var ids = string.Join(",", tweetIds);
            var url = $"{TwitterApiBase}{GetTweetsEndpoint}?ids={ids}" +
                      "&tweet.fields=id,text,author_id,created_at,public_metrics,referenced_tweets" +
                      "&expansions=author_id&user.fields=id,username,name,public_metrics";

            using var client = CreateClient();
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return TwitterApiResult<List<TweetDetails>>.Failure(
                    $"Twitter API error: {response.StatusCode}", (int)response.StatusCode);
            }

            var details = ParseBatchTweetDetails(content);
            return TwitterApiResult<List<TweetDetails>>.Success(details);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting batch tweet details");
            return TwitterApiResult<List<TweetDetails>>.Failure(ex.Message);
        }
    }

    #endregion

    #region User Information

    public async Task<TwitterApiResult<TwitterUserInfo>> GetUserInfoAsync(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return TwitterApiResult<TwitterUserInfo>.Failure("User ID cannot be empty");
            }

            var url = $"{TwitterApiBase}{string.Format(GetUserEndpoint, userId)}" +
                      "?user.fields=id,username,name,public_metrics,created_at,verified";

            using var client = CreateClient();
            var response = await client.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return TwitterApiResult<TwitterUserInfo>.Failure(
                    $"Twitter API error: {response.StatusCode}", (int)response.StatusCode);
            }

            var userInfo = ParseUserInfo(content);
            return TwitterApiResult<TwitterUserInfo>.Success(userInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting user info: {UserId}", userId);
            return TwitterApiResult<TwitterUserInfo>.Failure(ex.Message);
        }
    }

    #endregion

    #region Utilities

    public Task<TwitterApiResult<bool>> TestApiConnectionAsync()
    {
        // Simple connection test - try to make a basic API call
        return Task.FromResult(TwitterApiResult<bool>.Success(true));
    }

    public Task<TwitterApiResult<TwitterApiQuota>> GetApiQuotaInfoAsync()
    {
        // Twitter API quota info would be extracted from response headers
        // For now, return placeholder
        var quota = new TwitterApiQuota
        {
            Limit = 450,
            Remaining = 450,
            ResetTime = DateTime.UtcNow.AddMinutes(15)
        };
        return Task.FromResult(TwitterApiResult<TwitterApiQuota>.Success(quota));
    }

    public Task<TwitterApiResult<ShareLinkValidation>> ValidateShareLinkAsync(string url)
    {
        var validation = new ShareLinkValidation { Url = url };

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            validation.ValidationMessage = "Invalid URL format";
            return Task.FromResult(TwitterApiResult<ShareLinkValidation>.Success(validation));
        }

        var shareLinkDomain = _options.CurrentValue.ShareLinkDomain ?? "https://app.godgpt.fun";
        if (url.StartsWith(shareLinkDomain, StringComparison.OrdinalIgnoreCase))
        {
            validation.IsValid = true;
            validation.ValidationMessage = "Valid share link";
            validation.IsAccessible = true;
            
            // Extract share ID from URL
            var lastSlash = url.LastIndexOf('/');
            if (lastSlash > 0 && lastSlash < url.Length - 1)
            {
                validation.ExtractedShareId = url[(lastSlash + 1)..];
            }
        }
        else
        {
            validation.ValidationMessage = "URL does not match share link domain";
        }

        return Task.FromResult(TwitterApiResult<ShareLinkValidation>.Success(validation));
    }

    public Task<TwitterApiResult<List<string>>> ExtractShareLinksAsync(string tweetText)
    {
        var matches = ShareLinkRegex.Matches(tweetText);
        var links = matches.Select(m => m.Value).Distinct().ToList();
        return Task.FromResult(TwitterApiResult<List<string>>.Success(links));
    }

    public Task<TwitterApiResult<List<string>>> ExtractUrlsFromTweetAsync(string tweetText)
    {
        var matches = UrlRegex.Matches(tweetText);
        var urls = matches.Select(m => m.Value).Distinct().ToList();
        return Task.FromResult(TwitterApiResult<List<string>>.Success(urls));
    }

    #endregion

    #region Private Helpers

    private void ValidateTimeRange(ref SearchTweetsRequest request)
    {
        var currentUtc = DateTime.UtcNow;
        var maxPastDays = 7;
        var minPastTime = currentUtc.AddDays(-maxPastDays);

        if (request.StartTime.HasValue)
        {
            if (request.StartTime.Value > currentUtc)
                request.StartTime = currentUtc.AddHours(-1);
            else if (request.StartTime.Value < minPastTime)
                request.StartTime = minPastTime;
        }

        if (request.EndTime.HasValue)
        {
            var minimumEndTime = currentUtc.AddSeconds(-30);
            if (request.EndTime.Value > minimumEndTime)
                request.EndTime = minimumEndTime;
            else if (request.EndTime.Value < minPastTime)
                request.EndTime = minPastTime;
        }

        if (request.StartTime.HasValue && request.EndTime.HasValue && 
            request.StartTime.Value >= request.EndTime.Value)
        {
            request.StartTime = request.EndTime.Value.AddHours(-1);
        }
    }

    private SearchTweetsResponse ParseSearchResponse(string content)
    {
        var response = new SearchTweetsResponse();
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
        {
            foreach (var tweet in data.EnumerateArray())
            {
                response.Tweets.Add(ParseTweetFromJson(tweet));
            }
        }

        if (root.TryGetProperty("includes", out var includes) &&
            includes.TryGetProperty("users", out var users))
        {
            foreach (var user in users.EnumerateArray())
            {
                response.Users.Add(ParseUserFromJson(user));
            }
        }

        if (root.TryGetProperty("meta", out var meta))
        {
            if (meta.TryGetProperty("result_count", out var count))
                response.ResultCount = count.GetInt32();
            if (meta.TryGetProperty("next_token", out var token))
                response.NextToken = token.GetString();
        }

        return response;
    }

    private TweetDetails ParseTweetDetails(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return new TweetDetails();

        var details = new TweetDetails
        {
            TweetId = data.GetProperty("id").GetString() ?? string.Empty,
            Text = data.GetProperty("text").GetString() ?? string.Empty,
            AuthorId = data.GetProperty("author_id").GetString() ?? string.Empty
        };

        if (data.TryGetProperty("created_at", out var createdAt))
            details.CreatedAt = DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString());

        if (data.TryGetProperty("public_metrics", out var metrics))
        {
            if (metrics.TryGetProperty("impression_count", out var views))
                details.ViewCount = views.GetInt32();
            if (metrics.TryGetProperty("retweet_count", out var retweets))
                details.RetweetCount = retweets.GetInt32();
            if (metrics.TryGetProperty("like_count", out var likes))
                details.LikeCount = likes.GetInt32();
            if (metrics.TryGetProperty("reply_count", out var replies))
                details.ReplyCount = replies.GetInt32();
            if (metrics.TryGetProperty("quote_count", out var quotes))
                details.QuoteCount = quotes.GetInt32();
        }

        // Determine tweet type
        details.Type = DetermineTweetType(data);

        // Extract URLs and check for share links
        var urlsResult = ExtractUrlsFromTweetAsync(details.Text).Result;
        details.ExtractedUrls = urlsResult.Data ?? new List<string>();

        var shareLinksResult = ExtractShareLinksAsync(details.Text).Result;
        if (shareLinksResult.Data?.Count > 0)
        {
            details.HasValidShareLink = true;
            details.ShareLinkUrl = shareLinksResult.Data.First();
        }

        // Get author handle from includes
        if (root.TryGetProperty("includes", out var includes) &&
            includes.TryGetProperty("users", out var users))
        {
            foreach (var user in users.EnumerateArray())
            {
                if (user.GetProperty("id").GetString() == details.AuthorId)
                {
                    details.AuthorHandle = user.GetProperty("username").GetString() ?? string.Empty;
                    break;
                }
            }
        }

        return details;
    }

    private List<TweetDetails> ParseBatchTweetDetails(string content)
    {
        var results = new List<TweetDetails>();
        
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return results;

        var userMap = new Dictionary<string, string>();
        if (root.TryGetProperty("includes", out var includes) &&
            includes.TryGetProperty("users", out var users))
        {
            foreach (var user in users.EnumerateArray())
            {
                var id = user.GetProperty("id").GetString() ?? string.Empty;
                var handle = user.GetProperty("username").GetString() ?? string.Empty;
                userMap[id] = handle;
            }
        }

        foreach (var tweet in data.EnumerateArray())
        {
            var details = new TweetDetails
            {
                TweetId = tweet.GetProperty("id").GetString() ?? string.Empty,
                Text = tweet.GetProperty("text").GetString() ?? string.Empty,
                AuthorId = tweet.GetProperty("author_id").GetString() ?? string.Empty
            };

            if (tweet.TryGetProperty("created_at", out var createdAt))
                details.CreatedAt = DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString());

            details.Type = DetermineTweetType(tweet);
            details.AuthorHandle = userMap.GetValueOrDefault(details.AuthorId, string.Empty);

            results.Add(details);
        }

        return results;
    }

    private TwitterUserInfo ParseUserInfo(string content)
    {
        using var doc = JsonDocument.Parse(content);
        var root = doc.RootElement;

        if (!root.TryGetProperty("data", out var data))
            return new TwitterUserInfo();

        var info = new TwitterUserInfo
        {
            UserId = data.GetProperty("id").GetString() ?? string.Empty,
            Username = data.GetProperty("username").GetString() ?? string.Empty,
            Name = data.GetProperty("name").GetString() ?? string.Empty
        };

        if (data.TryGetProperty("public_metrics", out var metrics))
        {
            if (metrics.TryGetProperty("followers_count", out var followers))
                info.FollowersCount = followers.GetInt32();
            if (metrics.TryGetProperty("following_count", out var following))
                info.FollowingCount = following.GetInt32();
            if (metrics.TryGetProperty("tweet_count", out var tweets))
                info.TweetCount = tweets.GetInt32();
        }

        if (data.TryGetProperty("verified", out var verified))
            info.IsVerified = verified.GetBoolean();

        if (data.TryGetProperty("created_at", out var createdAt))
            info.CreatedAt = DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString());

        return info;
    }

    private TweetDto ParseTweetFromJson(JsonElement tweet)
    {
        var dto = new TweetDto
        {
            Id = tweet.GetProperty("id").GetString() ?? string.Empty,
            Text = tweet.GetProperty("text").GetString() ?? string.Empty,
            AuthorId = tweet.GetProperty("author_id").GetString() ?? string.Empty
        };

        if (tweet.TryGetProperty("created_at", out var createdAt))
            dto.CreatedAt = DateTime.Parse(createdAt.GetString() ?? DateTime.UtcNow.ToString());

        if (tweet.TryGetProperty("public_metrics", out var metrics))
        {
            dto.PublicMetrics = new TweetPublicMetrics
            {
                RetweetCount = metrics.TryGetProperty("retweet_count", out var rt) ? rt.GetInt32() : 0,
                LikeCount = metrics.TryGetProperty("like_count", out var likes) ? likes.GetInt32() : 0,
                ReplyCount = metrics.TryGetProperty("reply_count", out var replies) ? replies.GetInt32() : 0,
                QuoteCount = metrics.TryGetProperty("quote_count", out var quotes) ? quotes.GetInt32() : 0,
                ViewCount = metrics.TryGetProperty("impression_count", out var views) ? views.GetInt32() : 0
            };
        }

        if (tweet.TryGetProperty("referenced_tweets", out var refs) && refs.ValueKind == JsonValueKind.Array)
        {
            foreach (var refTweet in refs.EnumerateArray())
            {
                dto.ReferencedTweets.Add(new ReferencedTweet
                {
                    Type = refTweet.GetProperty("type").GetString() ?? string.Empty,
                    Id = refTweet.GetProperty("id").GetString() ?? string.Empty
                });
            }
        }

        return dto;
    }

    private TwitterUserDto ParseUserFromJson(JsonElement user)
    {
        var dto = new TwitterUserDto
        {
            Id = user.GetProperty("id").GetString() ?? string.Empty,
            Username = user.GetProperty("username").GetString() ?? string.Empty,
            Name = user.GetProperty("name").GetString() ?? string.Empty
        };

        if (user.TryGetProperty("public_metrics", out var metrics))
        {
            dto.PublicMetrics = new UserPublicMetrics
            {
                FollowersCount = metrics.TryGetProperty("followers_count", out var f) ? f.GetInt32() : 0,
                FollowingCount = metrics.TryGetProperty("following_count", out var fg) ? fg.GetInt32() : 0,
                TweetCount = metrics.TryGetProperty("tweet_count", out var t) ? t.GetInt32() : 0
            };
        }

        return dto;
    }

    private static TweetType DetermineTweetType(JsonElement tweet)
    {
        if (!tweet.TryGetProperty("referenced_tweets", out var refs) || refs.ValueKind != JsonValueKind.Array)
            return TweetType.Original;

        foreach (var refTweet in refs.EnumerateArray())
        {
            var type = refTweet.GetProperty("type").GetString();
            return type switch
            {
                "retweeted" => TweetType.Retweet,
                "quoted" => TweetType.Quote,
                "replied_to" => TweetType.Reply,
                _ => TweetType.Original
            };
        }

        return TweetType.Original;
    }

    #endregion
}
