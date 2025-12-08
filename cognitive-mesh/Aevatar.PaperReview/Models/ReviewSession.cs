using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.CognitiveMesh.Abstractions;

namespace Aevatar.PaperReview.Models;

// ============================================================
//  REVIEW SESSION
//  论文评审会话模型
// ============================================================

/// <summary>
/// 评审会话状态。
/// </summary>
public enum ReviewStatus
{
    /// <summary>待开始</summary>
    Pending,
    /// <summary>评审中</summary>
    Reviewing,
    /// <summary>已完成</summary>
    Completed,
    /// <summary>已失败</summary>
    Failed,
    /// <summary>已取消</summary>
    Cancelled
}

/// <summary>
/// 评审类型。
/// </summary>
public enum ReviewType
{
    /// <summary>快速预审</summary>
    QuickReview,
    /// <summary>详细评审</summary>
    DetailedReview,
    /// <summary>深度分析</summary>
    DeepAnalysis,
    /// <summary>修改建议</summary>
    RevisionSuggestion
}

/// <summary>
/// 论文评审会话。
/// </summary>
public sealed class ReviewSession
{
    /// <summary>会话ID</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N")[..12];
    
    /// <summary>论文标题</summary>
    public string Title { get; set; } = "";
    
    /// <summary>论文作者</summary>
    public string Authors { get; set; } = "";
    
    /// <summary>评审类型</summary>
    public ReviewType Type { get; set; } = ReviewType.DetailedReview;
    
    /// <summary>会议/期刊类型</summary>
    public string VenueType { get; set; } = "AI Conference";
    
    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    
    /// <summary>评审状态</summary>
    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
    
    /// <summary>论文内容</summary>
    public string PaperContent { get; set; } = "";
    
    /// <summary>上传ID</summary>
    public string? UploadId { get; set; }
    
    /// <summary>评审结果</summary>
    public ReasoningResult? Result { get; set; }
    
    /// <summary>错误信息</summary>
    public string? Error { get; set; }
    
    /// <summary>执行耗时</summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>LLM 调用次数</summary>
    public int TotalLlmCalls { get; set; }
    
    /// <summary>总 Token 数</summary>
    public long TotalTokens { get; set; }
    
    /// <summary>当前进度</summary>
    public int ProgressPercent { get; set; }
    
    /// <summary>当前阶段</summary>
    public string CurrentPhase { get; set; } = "";
    
    /// <summary>时间线</summary>
    public List<TimelineEntry> Timeline { get; } = [];
    
    /// <summary>SSE 事件通道</summary>
    public Channel<ReviewEvent> EventChannel { get; } = Channel.CreateUnbounded<ReviewEvent>();
    
    /// <summary>取消令牌源</summary>
    public CancellationTokenSource CancellationTokenSource { get; } = new();
    
    /// <summary>生成的文件</summary>
    public ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Files { get; } = new();
    
    /// <summary>输出目录</summary>
    public string? OutputDir { get; set; }
}

/// <summary>
/// 时间线条目。
/// </summary>
public record TimelineEntry(string Phase, string Message, DateTimeOffset Timestamp);

// ============================================================
//  REVIEW EVENTS (SSE 事件)
// ============================================================

/// <summary>
/// 评审事件基类。
/// </summary>
public abstract record ReviewEvent
{
    public string SessionId { get; init; } = "";
    public string Type => GetType().Name;
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// 进度事件。
/// </summary>
public record ProgressEvent : ReviewEvent
{
    public string Phase { get; init; } = "";
    public string? Message { get; init; }
    public int ProgressPercent { get; init; }
    public int? Depth { get; init; }
}

/// <summary>
/// 评审员事件（Worker 提交评审意见）。
/// </summary>
public record ReviewerEvent : ReviewEvent
{
    public string ReviewerId { get; init; } = "";
    public string ReviewerRole { get; init; } = "";
    public string Content { get; init; } = "";
    public int? PromptTokens { get; init; }
    public int? CompletionTokens { get; init; }
}

/// <summary>
/// 共识事件（投票结果）。
/// </summary>
public record ConsensusEvent : ReviewEvent
{
    public int Round { get; init; }
    public int TotalVotes { get; init; }
    public int VotesNeeded { get; init; }
    public int LeaderVotes { get; init; }
    public bool Reached { get; init; }
}

/// <summary>
/// 结果事件。
/// </summary>
public record ResultEvent : ReviewEvent
{
    public bool Success { get; init; }
    public string? Content { get; init; }
    public string? Error { get; init; }
    public int TotalLlmCalls { get; init; }
    public long TotalTokens { get; init; }
}

/// <summary>
/// 错误事件。
/// </summary>
public record ErrorEvent : ReviewEvent
{
    public string Message { get; init; } = "";
    public string? StackTrace { get; init; }
}

// ============================================================
//  REQUEST MODELS
// ============================================================

/// <summary>
/// 创建会话请求。
/// </summary>
public record CreateSessionRequest
{
    public string? Title { get; init; }
    public string? Authors { get; init; }
    public string? ReviewType { get; init; }
    public string? VenueType { get; init; }
    public string? UploadId { get; init; }
    public string? PaperContent { get; init; }
}
