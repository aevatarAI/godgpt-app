namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  工作流执行结果
// ============================================================

/// <summary>
/// 工作流执行结果
/// </summary>
public sealed class WorkflowResult
{
    public bool Success { get; set; }
    public object? Output { get; set; }
    public string? Error { get; set; }
    public int TotalTokens { get; set; }
    public int TotalLlmCalls { get; set; }
    public TimeSpan Duration { get; set; }
    
    public static WorkflowResult Ok(object? output, int tokens = 0, int calls = 0) => new()
    {
        Success = true,
        Output = output,
        TotalTokens = tokens,
        TotalLlmCalls = calls
    };
    
    public static WorkflowResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}

// ============================================================
//  工作流注册表
// ============================================================

/// <summary>
/// 内存工作流注册表
/// </summary>
public sealed class InMemoryWorkflowRegistry : IWorkflowRegistry
{
    private readonly Dictionary<string, WorkflowDefinition> _workflows = new();
    
    public void Register(WorkflowDefinition workflow)
    {
        _workflows[workflow.Name] = workflow;
    }
    
    public WorkflowDefinition? Get(string name)
    {
        return _workflows.GetValueOrDefault(name);
    }
    
    public IReadOnlyList<string> List() => _workflows.Keys.ToList();
    
    public void Remove(string name)
    {
        _workflows.Remove(name);
    }
    
    public void Clear()
    {
        _workflows.Clear();
    }
}

