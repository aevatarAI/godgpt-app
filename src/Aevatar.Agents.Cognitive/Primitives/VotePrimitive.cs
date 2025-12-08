using System.Diagnostics;
using Aevatar.Agents.Cognitive.Template;
using Aevatar.Agents.Maker;
using Microsoft.Extensions.AI;

namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  Vote 原语
//  支持语义聚类投票 (使用 MAKER 的 VoteEngine)
// ============================================================

/// <summary>
/// 投票共识原语 - 多次采样直到 K 票共识
/// 
/// 支持两种模式：
/// 1. 语义聚类 (默认) - 使用 embeddings 计算语义相似度
/// 2. 精确匹配 (回退) - 当没有配置 embeddings 时使用哈希匹配
/// 
/// DSL 语法:
/// - id: solve_with_consensus
///   type: vote
///   k: 2                    # K 票共识
///   max_rounds: 10          # 最大轮次
///   similarity: 0.85        # 语义相似度阈值 (用于语义聚类)
///   generator:              # 每轮生成器
///     type: llm_call
///     prompt: "Solve: {{task}}"
///   store: solution
/// </summary>
public class VotePrimitive : IPrimitive
{
    public string Type => "vote";
    
    private readonly TemplateEngine _templateEngine;
    private readonly Func<StepDefinition, PrimitiveContext, Task<PrimitiveResult>> _stepExecutor;
    private readonly IEmbeddingGenerator<string, Embedding<float>>? _embeddingGenerator;
    
    public VotePrimitive(
        TemplateEngine templateEngine,
        Func<StepDefinition, PrimitiveContext, Task<PrimitiveResult>> stepExecutor,
        IEmbeddingGenerator<string, Embedding<float>>? embeddingGenerator = null)
    {
        _templateEngine = templateEngine;
        _stepExecutor = stepExecutor;
        _embeddingGenerator = embeddingGenerator;
    }
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalTokens = 0;
        var totalLlmCalls = 0;
        var embeddingCalls = 0;
        
        try
        {
            // ─────────────────────────────────────────────
            //  1. 获取配置
            // ─────────────────────────────────────────────
            var k = ResolveIntParameter(parameters, "k", context.Variables, 2);
            var maxRounds = ResolveIntParameter(parameters, "max_rounds", context.Variables, 10);
            var similarity = ResolveFloatParameter(parameters, "similarity", context.Variables, 0.85f);
            var generator = ParameterExtensions.GetRequired<StepDefinition>(parameters, "generator");
            
            // ─────────────────────────────────────────────
            //  2. 创建投票引擎 (语义聚类 or 精确匹配)
            // ─────────────────────────────────────────────
            var useSemanticClustering = _embeddingGenerator != null;
            
            using var engine = new VoteEngine(
                k,
                _embeddingGenerator,  // null 时自动回退到精确匹配
                maxRounds,
                similarity);
            
            // 报告进度
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "Voting",
                StepId = context.CurrentStepId,
                Message = $"Starting voting with K={k}, max_rounds={maxRounds}" +
                          (useSemanticClustering ? $", similarity={similarity:F2} (semantic)" : " (exact match)"),
                Voting = new VotingProgressInfo
                {
                    Round = 0,
                    MaxRounds = maxRounds,
                    K = k,
                    VotesForLeader = 0
                }
            });
            
            // ─────────────────────────────────────────────
            //  3. 投票循环
            // ─────────────────────────────────────────────
            VoteResult? consensusResult = null;
            var round = 0;
            
            while (consensusResult == null && round < maxRounds)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                round++;
                
                // 执行生成器
                var generatorContext = context.Clone();
                generatorContext.CurrentStepId = $"{context.CurrentStepId}.gen[{round}]";
                
                var result = await _stepExecutor(generator, generatorContext);
                
                totalTokens += result.TokensUsed;
                totalLlmCalls += result.LlmCalls;
                
                if (!result.Success)
                {
                    // 生成失败，继续尝试
                    continue;
                }
                
                // 提交投票 (语义聚类或精确匹配)
                var proposal = result.Value?.ToString() ?? "";
                consensusResult = await engine.SubmitVoteAsync(proposal, context.CancellationToken);
                
                // 获取进度
                var progress = engine.GetProgress(VotingType.Solution);
                embeddingCalls = engine.EmbeddingCallCount;
                
                // 报告进度
                context.Progress?.Report(new WorkflowProgress
                {
                    Phase = "Voting",
                    StepId = context.CurrentStepId,
                    ProgressPercent = (float)round / maxRounds,
                    Message = consensusResult != null
                        ? $"✓ Consensus reached after {round} rounds (K={k})"
                        : $"Round {round}: {progress.LeaderVotes}/{k} votes for leader ({progress.ClusterCount} clusters)",
                    Voting = new VotingProgressInfo
                    {
                        Round = round,
                        MaxRounds = maxRounds,
                        K = k,
                        VotesForLeader = progress.LeaderVotes,
                        CurrentLeader = GetLeaderPreview(engine)
                    }
                });
            }
            
            // ─────────────────────────────────────────────
            //  4. 返回结果
            // ─────────────────────────────────────────────
            stopwatch.Stop();
            
            if (consensusResult != null && consensusResult.Success)
            {
                return new PrimitiveResult
                {
                    Success = true,
                    Value = consensusResult.WinningContent,
                    TokensUsed = totalTokens,
                    LlmCalls = totalLlmCalls + embeddingCalls,
                    Duration = stopwatch.Elapsed
                };
            }
            
            // 没有达成共识，返回得票最多的
            var bestCandidate = engine.GetBestCandidate();
            return new PrimitiveResult
            {
                Success = true, // 仍然返回结果，只是没有完美共识
                Value = bestCandidate?.Content ?? "",
                Error = $"No consensus reached after {maxRounds} rounds, using best candidate ({bestCandidate?.Votes ?? 0} votes)",
                TokensUsed = totalTokens,
                LlmCalls = totalLlmCalls + embeddingCalls,
                Duration = stopwatch.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = "Voting cancelled",
                TokensUsed = totalTokens,
                LlmCalls = totalLlmCalls + embeddingCalls,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = $"Voting failed: {ex.Message}",
                TokensUsed = totalTokens,
                LlmCalls = totalLlmCalls + embeddingCalls,
                Duration = stopwatch.Elapsed
            };
        }
    }
    
    // ============================================================
    //  辅助方法
    // ============================================================
    
    private int ResolveIntParameter(Dictionary<string, object?> parameters, string key, 
        Dictionary<string, object> variables, int defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        
        return value switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            float f => (int)f,
            string s when int.TryParse(s, out var parsed) => parsed,
            string s => ConvertToInt(_templateEngine.Evaluate(s, variables), defaultValue),
            _ => defaultValue
        };
    }
    
    private float ResolveFloatParameter(Dictionary<string, object?> parameters, string key,
        Dictionary<string, object> variables, float defaultValue)
    {
        var value = ParameterExtensions.GetOptional<object>(parameters, key, defaultValue);
        
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            long l => l,
            string s when float.TryParse(s, out var parsed) => parsed,
            string s => ConvertToFloat(_templateEngine.Evaluate(s, variables), defaultValue),
            _ => defaultValue
        };
    }
    
    /// <summary>
    /// 安全转换为 int，处理 object unboxing
    /// </summary>
    private static int ConvertToInt(object? value, int defaultValue) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        float f => (int)f,
        decimal m => (int)m,
        string s when int.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };
    
    /// <summary>
    /// 安全转换为 float，处理 object unboxing
    /// </summary>
    private static float ConvertToFloat(object? value, float defaultValue) => value switch
    {
        float f => f,
        double d => (float)d,
        int i => i,
        long l => l,
        decimal m => (float)m,
        string s when float.TryParse(s, out var parsed) => parsed,
        _ => defaultValue
    };
    
    private static string? GetLeaderPreview(VoteEngine engine)
    {
        var best = engine.GetBestCandidate();
        if (best?.Content == null) return null;
        
        return best.Content.Length > 50 
            ? best.Content[..50] + "..." 
            : best.Content;
    }
}

// ============================================================
//  简单投票引擎 (保留向后兼容)
//  仅在完全没有 MAKER 依赖时使用
// ============================================================

/// <summary>
/// 简单投票引擎 - 基于哈希的精确匹配
/// 已被 MAKER 的 VoteEngine 取代，保留以供参考
/// </summary>
[Obsolete("Use Aevatar.Agents.Maker.VoteEngine instead")]
public class SimpleVoteEngine
{
    private readonly int _k;
    private readonly int _maxRounds;
    private readonly Dictionary<string, int> _votes = new();
    private readonly Dictionary<string, string> _hashToContent = new();
    
    public int RoundNumber { get; private set; }
    public bool HasConsensus { get; private set; }
    public string? Winner { get; private set; }
    public string? CurrentLeader { get; private set; }
    public int VotesForLeader { get; private set; }
    
    public SimpleVoteEngine(int k, int maxRounds)
    {
        _k = k;
        _maxRounds = maxRounds;
    }
    
    public void SubmitVote(string content)
    {
        RoundNumber++;
        
        var hash = ComputeHash(NormalizeContent(content));
        
        if (!_hashToContent.ContainsKey(hash))
        {
            _hashToContent[hash] = content;
        }
        
        if (!_votes.TryGetValue(hash, out var count))
        {
            count = 0;
        }
        _votes[hash] = count + 1;
        
        if (_votes[hash] >= _k)
        {
            HasConsensus = true;
            Winner = _hashToContent[hash];
        }
        
        var leader = _votes.MaxBy(kv => kv.Value);
        CurrentLeader = _hashToContent.GetValueOrDefault(leader.Key);
        VotesForLeader = leader.Value;
    }
    
    private static string NormalizeContent(string content)
    {
        content = content.Trim();
        content = content.Replace("\r\n", "\n");
        while (content.Contains("  "))
        {
            content = content.Replace("  ", " ");
        }
        return content;
    }
    
    private static string ComputeHash(string content)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes)[..16];
    }
}
