using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Maker;
using Aevatar.PaperReview.Models;

namespace Aevatar.PaperReview.Services;

// ============================================================
//  PAPER REVIEW SERVICE
//  论文评审核心服务 - 基于 MAKER 策略的多专家共识系统
// ============================================================

/// <summary>
/// 论文评审服务。
/// 使用 MAKER 系统进行多专家协同评审，达成共识后输出综合评审意见。
/// </summary>
public sealed class PaperReviewService
{
    private readonly IMakerExecutor _makerExecutor;
    private readonly SupabaseService _supabase;
    private readonly ILogger<PaperReviewService> _logger;
    private readonly ConcurrentDictionary<string, ReviewSession> _sessions = new();
    private readonly string _uploadsBasePath;
    private readonly string _outputBasePath;

    public PaperReviewService(
        IMakerExecutor makerExecutor,
        SupabaseService supabase,
        ILogger<PaperReviewService> logger)
    {
        _makerExecutor = makerExecutor;
        _supabase = supabase;
        _logger = logger;
        _uploadsBasePath = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        _outputBasePath = Path.Combine(Directory.GetCurrentDirectory(), "output");
        Directory.CreateDirectory(_uploadsBasePath);
        Directory.CreateDirectory(_outputBasePath);
    }

    // ─────────────────────────────────────────────────────────
    //  会话管理
    // ─────────────────────────────────────────────────────────

    public IEnumerable<object> GetSessions() =>
        _sessions.Values
            .OrderByDescending(s => s.CreatedAt)
            .Select(s => new
            {
                s.Id,
                s.Title,
                s.Authors,
                type = s.Type.ToString(),
                s.VenueType,
                status = s.Status.ToString(),
                s.CreatedAt,
                s.ProgressPercent,
                s.TotalLlmCalls,
                s.TotalTokens
            });

    public async Task<object> CreateSessionAsync(string configJson)
    {
        try
        {
            var request = JsonSerializer.Deserialize<CreateSessionRequest>(configJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? throw new ArgumentException("Invalid request");

            var session = new ReviewSession
            {
                Title = request.Title ?? "Untitled Paper",
                Authors = request.Authors ?? "Unknown",
                Type = Enum.TryParse<ReviewType>(request.ReviewType, true, out var rt) ? rt : ReviewType.DetailedReview,
                VenueType = request.VenueType ?? "AI Conference",
                UploadId = request.UploadId
            };

            if (!string.IsNullOrEmpty(request.PaperContent))
            {
                session.PaperContent = request.PaperContent;
            }
            else if (!string.IsNullOrEmpty(request.UploadId))
            {
                session.PaperContent = await LoadPaperFromUploadAsync(request.UploadId);
            }

            _sessions[session.Id] = session;

            _logger.LogInformation("Created review session: {SessionId} - {Title}", session.Id, session.Title);

            return new
            {
                success = true,
                sessionId = session.Id,
                session.Title,
                type = session.Type.ToString(),
                hasPaper = !string.IsNullOrEmpty(session.PaperContent)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create session");
            return new { success = false, error = ex.Message };
        }
    }

    public async Task<object> UploadPaperAsync(IFormFileCollection files, CancellationToken ct)
    {
        try
        {
            if (files.Count == 0)
                return new { success = false, error = "No file uploaded" };

            var file = files[0];
            var uploadId = Guid.NewGuid().ToString("N")[..12];
            var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
            Directory.CreateDirectory(uploadDir);

            var fileName = Path.GetFileName(file.FileName);
            var filePath = Path.Combine(uploadDir, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream, ct);

            _logger.LogInformation("Uploaded paper: {FileName} ({Size} bytes)", fileName, file.Length);

            return new
            {
                success = true,
                uploadId,
                fileName,
                size = file.Length
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload paper");
            return new { success = false, error = ex.Message };
        }
    }

    private async Task<string> LoadPaperFromUploadAsync(string uploadId)
    {
        var uploadDir = Path.Combine(_uploadsBasePath, uploadId);
        if (!Directory.Exists(uploadDir))
            return "";

        var files = Directory.GetFiles(uploadDir);
        if (files.Length == 0)
            return "";

        return await File.ReadAllTextAsync(files[0]);
    }

    // ─────────────────────────────────────────────────────────
    //  评审执行
    // ─────────────────────────────────────────────────────────

    public async Task<object> StartReviewAsync(string sessionId, CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status == ReviewStatus.Reviewing)
            return new { success = false, error = "Already reviewing" };

        if (string.IsNullOrEmpty(session.PaperContent))
            return new { success = false, error = "No paper content" };

        session.Status = ReviewStatus.Reviewing;
        session.OutputDir = Path.Combine(_outputBasePath, session.Id);
        Directory.CreateDirectory(session.OutputDir);

        _logger.LogInformation("Starting review for session {SessionId}: {Title}", sessionId, session.Title);

        _ = ExecuteReviewAsync(session, session.CancellationTokenSource.Token);

        return new { success = true, sessionId };
    }

    public object StopReview(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        if (session.Status != ReviewStatus.Reviewing)
            return new { success = false, error = "Not reviewing" };

        try
        {
            session.CancellationTokenSource.Cancel();
            session.Status = ReviewStatus.Cancelled;
            session.Error = "Stopped by user";

            session.EventChannel.Writer.TryWrite(new Models.ErrorEvent
            {
                SessionId = sessionId,
                Message = "Review stopped by user"
            });
            session.EventChannel.Writer.TryComplete();

            _logger.LogInformation("Stopped review for session {SessionId}", sessionId);
            return new { success = true };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop review {SessionId}", sessionId);
            return new { success = false, error = ex.Message };
        }
    }

    private async Task ExecuteReviewAsync(ReviewSession session, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        // 保存初始记录到 Supabase
        await _supabase.SaveReviewAsync(
            session.Id,
            session.Title,
            session.Authors,
            session.Type.ToString(),
            session.VenueType,
            "Reviewing",
            null, null,
            0, 0, 0);

        try
        {
            var reviewTask = BuildReviewTask(session);
            var options = BuildMakerOptions(session);

            var result = await _makerExecutor.ExecuteAsync(reviewTask, options, ct);

            stopwatch.Stop();
            session.Status = result.Success ? ReviewStatus.Completed : ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.TotalLlmCalls = result.TotalLLMCalls;
            session.TotalTokens = result.TotalTokens;

            _logger.LogInformation("[{Session}] Review completed. Success={Success}, Duration={Duration}s",
                session.Id, result.Success, stopwatch.Elapsed.TotalSeconds);

            string? reportUrl = null;
            if (!string.IsNullOrEmpty(result.Content))
            {
                var report = FormatReviewReport(session, result);
                SaveFile(session, "reports", "review_report.md", report);
                
                // 上传报告到 Supabase Storage
                reportUrl = await _supabase.UploadReportAsync(session.Id, report);
            }

            // 更新 Supabase 记录
            await _supabase.UpdateReviewAsync(
                session.Id,
                session.Status.ToString(),
                result.Content,
                result.Error,
                result.TotalLLMCalls,
                result.TotalTokens,
                session.Duration.TotalSeconds,
                reportUrl);

            SendEvent(session, new Models.ResultEvent
            {
                SessionId = session.Id,
                Success = result.Success,
                Content = result.Content,
                Error = result.Error,
                TotalLlmCalls = result.TotalLLMCalls,
                TotalTokens = result.TotalTokens
            });
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            session.Status = ReviewStatus.Failed;
            session.Duration = stopwatch.Elapsed;
            session.Error = ex.Message;

            _logger.LogError(ex, "[{Session}] Review failed", session.Id);

            // 更新失败状态到 Supabase
            await _supabase.UpdateReviewAsync(
                session.Id,
                "Failed",
                null,
                ex.Message,
                session.TotalLlmCalls,
                session.TotalTokens,
                session.Duration.TotalSeconds);

            SendEvent(session, new Models.ErrorEvent
            {
                SessionId = session.Id,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            });
        }
        finally
        {
            session.EventChannel.Writer.TryComplete();
        }
    }

    private string BuildReviewTask(ReviewSession session)
    {
        var reviewTypePrompt = session.Type switch
        {
            ReviewType.QuickReview => "Provide a quick preliminary review focusing on major issues.",
            ReviewType.DetailedReview => "Provide a detailed peer review with specific actionable feedback.",
            ReviewType.DeepAnalysis => "Conduct deep analysis of methodology, experiments, and theoretical foundations.",
            ReviewType.RevisionSuggestion => "Focus on specific revision suggestions to improve the paper.",
            _ => "Provide a comprehensive academic review."
        };

        return $"""
            You are a senior reviewer for a top-tier {session.VenueType}.
            
            {reviewTypePrompt}
            
            Paper Title: {session.Title}
            Authors: {session.Authors}
            
            Review the following paper and provide:
            
            1. **Summary** (2-3 sentences summarizing the paper's contribution)
            
            2. **Strengths** (3-5 major strengths)
               - Be specific about what the paper does well
               - Reference specific sections/results when possible
            
            3. **Weaknesses** (3-5 major weaknesses)
               - Be constructive and suggest how to address each weakness
               - Distinguish between major and minor issues
            
            4. **Questions for Authors** (3-5 clarifying questions)
            
            5. **Detailed Comments** (section-by-section feedback)
               - For each issue, quote the problematic text
               - Explain why it's problematic
               - Provide a concrete revision suggestion
            
            6. **Overall Recommendation**
               - Strong Accept / Accept / Weak Accept / Borderline / Weak Reject / Reject / Strong Reject
               - Justify your recommendation
            
            7. **Confidence Score** (1-5)
               - How confident are you in this review?
            
            ═══════════════════════════════════════════════════════════════
            PAPER TO REVIEW
            ═══════════════════════════════════════════════════════════════
            
            {session.PaperContent}
            
            ═══════════════════════════════════════════════════════════════
            END OF PAPER
            ═══════════════════════════════════════════════════════════════
            """;
    }

    private MakerOptions BuildMakerOptions(ReviewSession session)
    {
        var reliability = session.Type switch
        {
            ReviewType.QuickReview => ReliabilityLevel.Low,
            ReviewType.DetailedReview => ReliabilityLevel.Medium,
            ReviewType.DeepAnalysis => ReliabilityLevel.High,
            ReviewType.RevisionSuggestion => ReliabilityLevel.Medium,
            _ => ReliabilityLevel.Medium
        };

        return new MakerOptions
        {
            ProviderName = AevatarAgentsConstants.DefaultProviderName,
            Reliability = reliability,
            MaxTotalLlmCalls = 100,
            MaxTotalTokens = 500_000,
            MaxDuration = TimeSpan.FromMinutes(30),
            OnProgress = p => HandleProgress(session, p)
        };
    }

    private void HandleProgress(ReviewSession session, MakerProgress p)
    {
        session.Timeline.Add(new TimelineEntry(p.Phase.ToString(), p.Message, DateTimeOffset.UtcNow));
        session.CurrentPhase = p.Phase.ToString();

        SendEvent(session, new Models.ProgressEvent
        {
            SessionId = session.Id,
            Phase = p.Phase.ToString(),
            Message = p.Message,
            ProgressPercent = session.ProgressPercent,
            Depth = p.Depth
        });

        if (p.Voting != null)
        {
            SendEvent(session, new ConsensusEvent
            {
                SessionId = session.Id,
                Round = p.Voting.Round,
                TotalVotes = p.Voting.TotalVotes,
                VotesNeeded = p.Voting.VotesNeeded,
                LeaderVotes = p.Voting.LeaderVotes,
                Reached = p.Voting.LeaderVotes >= p.Voting.VotesNeeded
            });
        }
    }

    private string FormatReviewReport(ReviewSession session, MakerResult result)
    {
        return $"""
            # 📝 Paper Review Report
            
            **Title:** {session.Title}  
            **Authors:** {session.Authors}  
            **Venue:** {session.VenueType}  
            **Review Type:** {session.Type}  
            **Session ID:** {session.Id}  
            
            ---
            
            **Status:** {(result.Success ? "✓ Completed" : "✗ Failed")}  
            **Duration:** {session.Duration.TotalSeconds:F1}s  
            **LLM Calls:** {result.TotalLLMCalls}  
            **Total Tokens:** {result.TotalTokens:N0}
            
            ---
            
            {result.Content}
            """;
    }

    private void SendEvent(ReviewSession session, ReviewEvent evt)
    {
        session.EventChannel.Writer.TryWrite(evt);
    }

    private void SaveFile(ReviewSession session, string category, string fileName, string content)
    {
        session.Files.GetOrAdd(category, _ => new ConcurrentDictionary<string, string>())[fileName] = content;

        if (!string.IsNullOrEmpty(session.OutputDir))
        {
            try
            {
                var dir = Path.Combine(session.OutputDir, category);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, fileName), content);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to save file: {Category}/{FileName}", category, fileName);
            }
        }
    }

    // ─────────────────────────────────────────────────────────
    //  状态查询
    // ─────────────────────────────────────────────────────────

    public object? GetStatus(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { status = "not_found" };

        return new
        {
            status = session.Status.ToString().ToLower(),
            sessionId = session.Id,
            session.Title,
            type = session.Type.ToString(),
            session.ProgressPercent,
            phase = session.CurrentPhase,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public object? GetResult(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return new { success = false, error = "Session not found" };

        var content = session.Files.TryGetValue("reports", out var reports) 
            ? reports.GetValueOrDefault("review_report.md") 
            : null;

        return new
        {
            success = session.Status == ReviewStatus.Completed,
            content,
            error = session.Error,
            session.TotalLlmCalls,
            session.TotalTokens,
            duration = session.Duration.TotalSeconds
        };
    }

    public IEnumerable<object> GetHistory(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return [];

        return session.Timeline.Select(e => new
        {
            phase = e.Phase,
            message = e.Message,
            timestamp = e.Timestamp
        });
    }

    public async IAsyncEnumerable<ReviewEvent> GetEventStreamAsync(
        string sessionId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            yield break;

        await foreach (var evt in session.EventChannel.Reader.ReadAllAsync(ct))
        {
            yield return evt;
        }
    }
}
