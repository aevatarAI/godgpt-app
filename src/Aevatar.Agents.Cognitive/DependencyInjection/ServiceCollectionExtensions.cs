using Aevatar.Agents.Cognitive.Engine;
using Aevatar.Agents.Cognitive.Primitives;
using Aevatar.Agents.Cognitive.Template;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.Agents.Cognitive.DependencyInjection;

/// <summary>
/// 依赖注入扩展
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Cognitive Agent 服务
    /// </summary>
    public static IServiceCollection AddCognitiveAgents(
        this IServiceCollection services,
        Action<CognitiveAgentOptions>? configure = null)
    {
        var options = new CognitiveAgentOptions();
        configure?.Invoke(options);
        
        // 注册模板引擎
        services.AddSingleton<TemplateEngine>();
        services.AddSingleton<OutputParserFactory>();
        
        // 注册工作流解析器
        services.AddSingleton<WorkflowParser>();
        
        // 注册工作流注册表
        services.AddSingleton<IWorkflowRegistry>(sp =>
        {
            var registry = new InMemoryWorkflowRegistry();
            
            // 加载内置工作流
            if (options.LoadBuiltInWorkflows)
            {
                LoadBuiltInWorkflows(registry);
            }
            
            // 从目录加载工作流
            if (!string.IsNullOrEmpty(options.WorkflowsDirectory))
            {
                var parser = sp.GetRequiredService<WorkflowParser>();
                foreach (var workflow in parser.ParseDirectory(options.WorkflowsDirectory))
                {
                    registry.Register(workflow);
                }
            }
            
            return registry;
        });
        
        // 注册运行状态管理器（可选）
        if (options.EnablePersistence)
        {
            services.AddSingleton<IRunStateManager>(sp =>
            {
                var runsDirectory = options.RunsDirectory ?? "runs";
                return new FileBasedRunStateManager(runsDirectory);
            });
        }
        
        return services;
    }
    
    private static void LoadBuiltInWorkflows(InMemoryWorkflowRegistry registry)
    {
        // Direct 策略
        registry.Register(new WorkflowDefinition
        {
            Name = "direct",
            Version = "1.0",
            Description = "单次 LLM 调用",
            Inputs =
            [
                new InputParameter { Name = "task", Type = "string", Required = true },
                new InputParameter { Name = "system_prompt", Type = "string", DefaultValue = "You are a helpful AI assistant." }
            ],
            Steps =
            [
                new StepDefinition
                {
                    Id = "respond",
                    Type = "llm_call",
                    Parameters = new Dictionary<string, object?>
                    {
                        ["prompt"] = "{{task}}",
                        ["system"] = "{{system_prompt}}",
                        ["output"] = "text"
                    },
                    Store = "response"
                }
            ],
            Output = new Dictionary<string, string>
            {
                ["result"] = "{{response}}"
            }
        });
    }
}

/// <summary>
/// Cognitive Agent 配置选项
/// </summary>
public class CognitiveAgentOptions
{
    /// <summary>是否加载内置工作流</summary>
    public bool LoadBuiltInWorkflows { get; set; } = true;
    
    /// <summary>工作流目录（用于热加载）</summary>
    public string? WorkflowsDirectory { get; set; }
    
    /// <summary>是否启用持久化</summary>
    public bool EnablePersistence { get; set; }
    
    /// <summary>运行状态存储目录</summary>
    public string? RunsDirectory { get; set; }
    
    /// <summary>默认最大递归深度</summary>
    public int MaxRecursionDepth { get; set; } = 10;
}

// ============================================================
//  文件系统运行状态管理器
// ============================================================

/// <summary>
/// 基于文件系统的运行状态管理器
/// </summary>
public class FileBasedRunStateManager : IRunStateManager
{
    private readonly string _runsDirectory;
    
    public FileBasedRunStateManager(string runsDirectory)
    {
        _runsDirectory = runsDirectory;
        Directory.CreateDirectory(runsDirectory);
    }
    
    public Task<RunState> CreateRunAsync(
        string workflowName,
        string projectId,
        Dictionary<string, object> inputs)
    {
        var runId = $"run-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}"[..32];
        
        var state = new RunState
        {
            RunId = runId,
            WorkflowName = workflowName,
            ProjectId = projectId,
            Status = RunStatus.Running,
            Inputs = inputs,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        
        SaveState(state);
        return Task.FromResult(state);
    }
    
    public Task UpdateStatusAsync(string runId, RunStatus status)
    {
        var state = LoadState(runId);
        if (state != null)
        {
            state.Status = status;
            state.UpdatedAt = DateTime.UtcNow;
            
            if (status == RunStatus.Paused)
                state.PausedAt = DateTime.UtcNow;
            
            SaveState(state);
        }
        return Task.CompletedTask;
    }
    
    public Task UpdateCurrentStepAsync(string runId, string stepId)
    {
        var state = LoadState(runId);
        if (state != null)
        {
            state.CurrentStep = stepId;
            state.UpdatedAt = DateTime.UtcNow;
            SaveState(state);
        }
        return Task.CompletedTask;
    }
    
    public Task SaveCheckpointAsync(string runId, CheckpointData checkpoint)
    {
        var state = LoadState(runId);
        if (state != null)
        {
            state.Checkpoints.Add(checkpoint);
            state.CurrentStep = checkpoint.StepId;
            state.UpdatedAt = DateTime.UtcNow;
            SaveState(state);
        }
        return Task.CompletedTask;
    }
    
    public Task<CheckpointData?> GetLastCheckpointAsync(string runId)
    {
        var state = LoadState(runId);
        return Task.FromResult(state?.Checkpoints.LastOrDefault());
    }
    
    public Task RegisterAgentAsync(string runId, Guid agentId, string agentType)
    {
        var state = LoadState(runId);
        if (state != null)
        {
            state.Agents.Add(new AgentRef
            {
                Id = agentId,
                Type = agentType,
                Status = AgentStatus.Active
            });
            SaveState(state);
        }
        return Task.CompletedTask;
    }
    
    public Task<RunState?> LoadRunAsync(string runId)
    {
        return Task.FromResult(LoadState(runId));
    }
    
    public Task<bool> CanResumeAsync(string runId)
    {
        var state = LoadState(runId);
        return Task.FromResult(
            state != null && 
            state.Status == RunStatus.Paused && 
            state.Checkpoints.Count > 0);
    }
    
    // ============================================================
    //  私有方法
    // ============================================================
    
    private string GetStatePath(string runId)
    {
        return Path.Combine(_runsDirectory, runId, "run.json");
    }
    
    private void SaveState(RunState state)
    {
        var dir = Path.Combine(_runsDirectory, state.RunId);
        Directory.CreateDirectory(dir);
        
        var json = System.Text.Json.JsonSerializer.Serialize(state, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        File.WriteAllText(GetStatePath(state.RunId), json);
    }
    
    private RunState? LoadState(string runId)
    {
        var path = GetStatePath(runId);
        if (!File.Exists(path))
            return null;
        
        var json = File.ReadAllText(path);
        return System.Text.Json.JsonSerializer.Deserialize<RunState>(json);
    }
}

