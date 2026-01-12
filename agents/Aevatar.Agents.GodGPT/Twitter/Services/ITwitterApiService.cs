namespace Aevatar.Application.Grains.Twitter.Services;

/// <summary>
/// Twitter API service interface - Stateless service for Twitter API interactions
/// This replaces the old TwitterInteractionGrain with a proper service pattern
/// </summary>
public interface ITwitterApiService
{
    #region Tweet Search and Retrieval

    /// <summary>
    /// Search tweets containing specified content
    /// </summary>
    Task<TwitterApiResult<SearchTweetsResponse>> SearchTweetsAsync(SearchTweetsRequest request);

    /// <summary>
    /// Comprehensive tweet analysis - Get tweet type, user info, share links in one call
    /// </summary>
    Task<TwitterApiResult<TweetProcessResult>> AnalyzeTweetAsync(string tweetId);

    /// <summary>
    /// Lightweight tweet analysis without user info (for bulk fetching)
    /// </summary>
    Task<TwitterApiResult<TweetProcessResult>> AnalyzeTweetLightweightAsync(string tweetId);

    /// <summary>
    /// Batch analyze tweets with comprehensive information
    /// </summary>
    Task<TwitterApiResult<List<TweetProcessResult>>> BatchAnalyzeTweetsAsync(List<string> tweetIds);

    /// <summary>
    /// Batch analyze tweets without user info
    /// </summary>
    Task<TwitterApiResult<List<TweetProcessResult>>> BatchAnalyzeTweetsLightweightAsync(List<string> tweetIds);

    #endregion

    #region Tweet Details

    /// <summary>
    /// Get tweet details by ID
    /// </summary>
    Task<TwitterApiResult<TweetDetails>> GetTweetDetailsAsync(string tweetId);

    /// <summary>
    /// Batch get tweet details
    /// </summary>
    Task<TwitterApiResult<List<TweetDetails>>> GetBatchTweetDetailsAsync(List<string> tweetIds);

    #endregion

    #region User Information

    /// <summary>
    /// Get Twitter user information by ID
    /// </summary>
    Task<TwitterApiResult<TwitterUserInfo>> GetUserInfoAsync(string userId);

    #endregion

    #region Utilities

    /// <summary>
    /// Test Twitter API connection
    /// </summary>
    Task<TwitterApiResult<bool>> TestApiConnectionAsync();

    /// <summary>
    /// Get API quota information
    /// </summary>
    Task<TwitterApiResult<TwitterApiQuota>> GetApiQuotaInfoAsync();

    /// <summary>
    /// Validate a share link URL
    /// </summary>
    Task<TwitterApiResult<ShareLinkValidation>> ValidateShareLinkAsync(string url);

    /// <summary>
    /// Extract share links from tweet text
    /// </summary>
    Task<TwitterApiResult<List<string>>> ExtractShareLinksAsync(string tweetText);

    /// <summary>
    /// Extract all URLs from tweet text
    /// </summary>
    Task<TwitterApiResult<List<string>>> ExtractUrlsFromTweetAsync(string tweetText);

    #endregion
}

#region DTOs - These will be replaced with Protobuf messages later

/// <summary>
/// Generic Twitter API result wrapper
/// </summary>
public class TwitterApiResult<T>
{
    public bool IsSuccess { get; set; }
    public T? Data { get; set; }
    public string ErrorMessage { get; set; } = string.Empty;
    public int StatusCode { get; set; }

    public static TwitterApiResult<T> Success(T data) => new() { IsSuccess = true, Data = data };
    public static TwitterApiResult<T> Failure(string error, int statusCode = 0) => 
        new() { IsSuccess = false, ErrorMessage = error, StatusCode = statusCode };
}

/// <summary>
/// Tweet type enumeration
/// </summary>
public enum TweetType
{
    Original = 0,
    Reply = 1,
    Retweet = 2,
    Quote = 3,
    Unknown = 99
}

/// <summary>
/// Search tweets request
/// </summary>
public class SearchTweetsRequest
{
    public string Query { get; set; } = string.Empty;
    public int MaxResults { get; set; } = 100;
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public string? NextToken { get; set; }
}

/// <summary>
/// Search tweets response
/// </summary>
public class SearchTweetsResponse
{
    public List<TweetDto> Tweets { get; set; } = new();
    public List<TwitterUserDto> Users { get; set; } = new();
    public int ResultCount { get; set; }
    public string? NextToken { get; set; }
}

/// <summary>
/// Tweet data transfer object
/// </summary>
public class TweetDto
{
    public string Id { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public TweetPublicMetrics PublicMetrics { get; set; } = new();
    public List<ReferencedTweet> ReferencedTweets { get; set; } = new();
}

/// <summary>
/// Twitter user DTO
/// </summary>
public class TwitterUserDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public UserPublicMetrics PublicMetrics { get; set; } = new();
}

/// <summary>
/// Tweet public metrics
/// </summary>
public class TweetPublicMetrics
{
    public int RetweetCount { get; set; }
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
    public int QuoteCount { get; set; }
    public int ViewCount { get; set; }
}

/// <summary>
/// User public metrics
/// </summary>
public class UserPublicMetrics
{
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int TweetCount { get; set; }
}

/// <summary>
/// Referenced tweet information
/// </summary>
public class ReferencedTweet
{
    public string Type { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

/// <summary>
/// Tweet processing result
/// </summary>
public class TweetProcessResult
{
    public string TweetId { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorHandle { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public TweetType Type { get; set; }
    public int ViewCount { get; set; }
    public int FollowerCount { get; set; }
    public bool HasValidShareLink { get; set; }
    public string ShareLinkUrl { get; set; } = string.Empty;
    public bool IsProcessed { get; set; }
    public int RewardCredits { get; set; }
    public double ShareLinkMultiplier { get; set; } = 1.0;
}

/// <summary>
/// Tweet details
/// </summary>
public class TweetDetails
{
    public string TweetId { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public string AuthorId { get; set; } = string.Empty;
    public string AuthorHandle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public TweetType Type { get; set; }
    public int ViewCount { get; set; }
    public int RetweetCount { get; set; }
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
    public int QuoteCount { get; set; }
    public bool HasValidShareLink { get; set; }
    public string ShareLinkUrl { get; set; } = string.Empty;
    public List<string> ExtractedUrls { get; set; } = new();
}

/// <summary>
/// Twitter user information
/// </summary>
public class TwitterUserInfo
{
    public string UserId { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int FollowersCount { get; set; }
    public int FollowingCount { get; set; }
    public int TweetCount { get; set; }
    public bool IsVerified { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Twitter API quota information
/// </summary>
public class TwitterApiQuota
{
    public int Limit { get; set; }
    public int Remaining { get; set; }
    public DateTime ResetTime { get; set; }
    public int UsedCount { get; set; }
    public double UsagePercentage { get; set; }
}

/// <summary>
/// Share link validation result
/// </summary>
public class ShareLinkValidation
{
    public bool IsValid { get; set; }
    public string Url { get; set; } = string.Empty;
    public string ValidationMessage { get; set; } = string.Empty;
    public string ExtractedShareId { get; set; } = string.Empty;
    public bool IsAccessible { get; set; }
}

#endregion
