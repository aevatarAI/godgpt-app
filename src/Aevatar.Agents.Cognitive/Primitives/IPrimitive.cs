namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  原语执行上下文
// ============================================================

/// <summary>
/// 原语执行上下文，包含变量、取消令牌、进度报告等
/// </summary>
public class PrimitiveContext
{
    /// <summary>工作流变量存储</summary>
    public Dictionary<string, object> Variables { get; } = new();
    
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }
    
    /// <summary>进度报告器</summary>
    public IProgress<WorkflowProgress>? Progress { get; init; }
    
    /// <summary>当前递归深度</summary>
    public int CurrentDepth { get; init; }
    
    /// <summary>最大递归深度</summary>
    public int MaxDepth { get; init; } = 10;
    
    /// <summary>运行 ID</summary>
    public string RunId { get; init; } = string.Empty;
    
    /// <summary>当前步骤 ID</summary>
    public string CurrentStepId { get; set; } = string.Empty;
    
    /// <summary>克隆上下文（用于子任务）</summary>
    public PrimitiveContext Clone()
    {
        var clone = new PrimitiveContext
        {
            CancellationToken = CancellationToken,
            Progress = Progress,
            CurrentDepth = CurrentDepth,
            MaxDepth = MaxDepth,
            RunId = RunId,
            CurrentStepId = CurrentStepId
        };
        
        foreach (var (key, value) in Variables)
        {
            clone.Variables[key] = value;
        }
        
        return clone;
    }
}

// ============================================================
//  原语执行结果
// ============================================================

/// <summary>
/// 原语执行结果
/// </summary>
public record PrimitiveResult
{
    /// <summary>是否成功</summary>
    public bool Success { get; init; }
    
    /// <summary>返回值</summary>
    public object? Value { get; init; }
    
    /// <summary>错误信息</summary>
    public string? Error { get; init; }
    
    /// <summary>使用的 Token 数</summary>
    public int TokensUsed { get; init; }
    
    /// <summary>提示词 Token 数</summary>
    public int PromptTokens { get; init; }
    
    /// <summary>补全 Token 数</summary>
    public int CompletionTokens { get; init; }
    
    /// <summary>LLM 调用次数</summary>
    public int LlmCalls { get; init; }
    
    /// <summary>执行时长</summary>
    public TimeSpan Duration { get; init; }
    
    // ─────────────────────────────────────────────────────────
    //  LLM 对话记录 (用于前端可视化)
    // ─────────────────────────────────────────────────────────
    
    /// <summary>系统提示词</summary>
    public string? SystemPrompt { get; init; }
    
    /// <summary>用户提示词</summary>
    public string? UserPrompt { get; init; }
    
    /// <summary>助手响应</summary>
    public string? AssistantResponse { get; init; }
    
    /// <summary>创建成功结果</summary>
    public static PrimitiveResult Ok(object? value = null, int tokensUsed = 0, int llmCalls = 0) => new()
    {
        Success = true,
        Value = value,
        TokensUsed = tokensUsed,
        LlmCalls = llmCalls
    };
    
    /// <summary>创建失败结果</summary>
    public static PrimitiveResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

// ============================================================
//  工作流进度
// ============================================================

/// <summary>
/// 工作流进度报告
/// </summary>
public record WorkflowProgress
{
    public required string Phase { get; init; }
    public string? StepId { get; init; }
    public float ProgressPercent { get; init; }
    public string? Message { get; init; }
    
    // 投票进度
    public VotingProgressInfo? Voting { get; init; }
    
    // Fan-out 进度
    public FanOutProgressInfo? FanOut { get; init; }
}

public record VotingProgressInfo
{
    public int Round { get; init; }
    public int MaxRounds { get; init; }
    public int VotesForLeader { get; init; }
    public int K { get; init; }
    public string? CurrentLeader { get; init; }
}

public record FanOutProgressInfo
{
    public int Total { get; init; }
    public int Completed { get; init; }
    public int Failed { get; init; }
}

// ============================================================
//  原语接口
// ============================================================

/// <summary>
/// 原语接口 - 所有 DSL 步骤类型的统一抽象
/// </summary>
public interface IPrimitive
{
    /// <summary>原语类型名</summary>
    string Type { get; }
    
    /// <summary>执行原语</summary>
    Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters);
}

// ============================================================
//  参数辅助扩展
// ============================================================

public static class ParameterExtensions
{
    /// <summary>获取必需参数</summary>
    public static T GetRequired<T>(this Dictionary<string, object?> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            throw new ArgumentException($"Required parameter '{key}' is missing");
        
        if (value is T typed)
            return typed;
        
        // 尝试转换
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            throw new ArgumentException($"Parameter '{key}' cannot be converted to {typeof(T).Name}");
        }
    }
    
    /// <summary>获取可选参数</summary>
    public static T? GetOptional<T>(this Dictionary<string, object?> parameters, string key, T? defaultValue = default)
    {
        if (!parameters.TryGetValue(key, out var value) || value == null)
            return defaultValue;
        
        if (value is T typed)
            return typed;
        
        // 尝试转换
        try
        {
            return (T)Convert.ChangeType(value, typeof(T));
        }
        catch
        {
            return defaultValue;
        }
    }
}

public static class VariableExtensions
{
    /// <summary>从上下文变量获取必需值</summary>
    public static T GetRequired<T>(this Dictionary<string, object> variables, string key)
    {
        if (!variables.TryGetValue(key, out var value))
            throw new KeyNotFoundException($"Variable '{key}' not found in context");
        
        if (value is T typed)
            return typed;
        
        throw new InvalidCastException($"Variable '{key}' is {value.GetType().Name}, expected {typeof(T).Name}");
    }
}

