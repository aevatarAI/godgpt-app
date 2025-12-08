using System.Diagnostics;

namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  Checkpoint 原语
// ============================================================

/// <summary>
/// 检查点原语 - 保存当前状态用于断点续传
/// 
/// DSL 语法:
/// - id: save_progress
///   type: checkpoint
///   variables:           # 可选：只保存指定变量
///     - subtasks
///     - current_depth
/// </summary>
public class CheckpointPrimitive : IPrimitive
{
    public string Type => "checkpoint";
    
    private readonly IRunStateManager? _stateManager;
    
    public CheckpointPrimitive(IRunStateManager? stateManager = null)
    {
        _stateManager = stateManager;
    }
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // 获取要保存的变量
            var variableNames = ParameterExtensions.GetOptional<List<string>>(parameters, "variables");
            
            // 选择要持久化的变量
            Dictionary<string, object> variables;
            if (variableNames != null && variableNames.Count > 0)
            {
                variables = context.Variables
                    .Where(kv => variableNames.Contains(kv.Key))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);
            }
            else
            {
                // 保存所有变量
                variables = new Dictionary<string, object>(context.Variables);
            }
            
            // 创建检查点
            var checkpoint = new CheckpointData
            {
                StepId = context.CurrentStepId,
                Timestamp = DateTime.UtcNow,
                Variables = variables,
                Depth = context.CurrentDepth
            };
            
            // 如果有状态管理器，保存到持久化存储
            if (_stateManager != null)
            {
                await _stateManager.SaveCheckpointAsync(context.RunId, checkpoint);
            }
            
            // 报告进度
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "Checkpoint",
                StepId = context.CurrentStepId,
                Message = $"Checkpoint saved: {variables.Count} variables"
            });
            
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = true,
                Value = checkpoint,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = $"Checkpoint failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
}

// ============================================================
//  检查点数据
// ============================================================

/// <summary>
/// 检查点数据
/// </summary>
public class CheckpointData
{
    public string StepId { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public Dictionary<string, object> Variables { get; set; } = new();
    public int Depth { get; set; }
}

// ============================================================
//  运行状态管理接口
// ============================================================

/// <summary>
/// 运行状态管理接口
/// </summary>
public interface IRunStateManager
{
    /// <summary>创建新运行</summary>
    Task<RunState> CreateRunAsync(
        string workflowName,
        string projectId,
        Dictionary<string, object> inputs);
    
    /// <summary>更新运行状态</summary>
    Task UpdateStatusAsync(string runId, RunStatus status);
    
    /// <summary>更新当前步骤</summary>
    Task UpdateCurrentStepAsync(string runId, string stepId);
    
    /// <summary>保存检查点</summary>
    Task SaveCheckpointAsync(string runId, CheckpointData checkpoint);
    
    /// <summary>获取最后一个检查点</summary>
    Task<CheckpointData?> GetLastCheckpointAsync(string runId);
    
    /// <summary>注册 Agent</summary>
    Task RegisterAgentAsync(string runId, Guid agentId, string agentType);
    
    /// <summary>加载运行状态</summary>
    Task<RunState?> LoadRunAsync(string runId);
    
    /// <summary>检查是否可以恢复</summary>
    Task<bool> CanResumeAsync(string runId);
}

/// <summary>
/// 运行状态
/// </summary>
public class RunState
{
    public string RunId { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public string ProjectId { get; set; } = string.Empty;
    public RunStatus Status { get; set; }
    
    public string CurrentStep { get; set; } = string.Empty;
    public int TotalSteps { get; set; }
    public int CompletedSteps { get; set; }
    
    public Dictionary<string, object> Inputs { get; set; } = new();
    public List<AgentRef> Agents { get; set; } = [];
    public List<CheckpointData> Checkpoints { get; set; } = [];
    
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? PausedAt { get; set; }
    
    public string? Error { get; set; }
}

/// <summary>
/// 运行状态枚举
/// </summary>
public enum RunStatus
{
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Agent 引用
/// </summary>
public class AgentRef
{
    public Guid Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public AgentStatus Status { get; set; }
}

/// <summary>
/// Agent 状态
/// </summary>
public enum AgentStatus
{
    Active,
    Idle,
    Terminated
}

