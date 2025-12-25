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
    
    /// <summary>默认最大递归深度</summary>
    public int MaxRecursionDepth { get; set; } = 10;
}
