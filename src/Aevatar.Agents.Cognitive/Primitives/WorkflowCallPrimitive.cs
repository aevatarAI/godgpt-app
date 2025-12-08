using System.Diagnostics;

namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  Workflow Call 原语
// ============================================================

/// <summary>
/// 工作流调用原语 - 支持递归调用
/// 
/// DSL 语法:
/// - id: solve_subtask
///   type: workflow_call
///   workflow: maker              # 调用的工作流名
///   params:
///     task: "{{item}}"
///     reliability: "{{reliability}}"
///   max_depth: 10
/// </summary>
public class WorkflowCallPrimitive : IPrimitive
{
    public string Type => "workflow_call";
    
    private readonly IWorkflowRegistry _registry;
    private readonly Func<WorkflowDefinition, PrimitiveContext, Task<PrimitiveResult>> _workflowExecutor;
    
    public WorkflowCallPrimitive(
        IWorkflowRegistry registry,
        Func<WorkflowDefinition, PrimitiveContext, Task<PrimitiveResult>> workflowExecutor)
    {
        _registry = registry;
        _workflowExecutor = workflowExecutor;
    }
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // ─────────────────────────────────────────────
            //  1. 检查递归深度
            // ─────────────────────────────────────────────
            var maxDepth = ParameterExtensions.GetOptional(parameters, "max_depth", context.MaxDepth);
            
            if (context.CurrentDepth >= maxDepth)
            {
                stopwatch.Stop();
                return new PrimitiveResult
                {
                    Success = false,
                    Error = $"Max recursion depth {maxDepth} exceeded",
                    Duration = stopwatch.Elapsed
                };
            }
            
            // ─────────────────────────────────────────────
            //  2. 获取目标工作流
            // ─────────────────────────────────────────────
            var workflowName = ParameterExtensions.GetRequired<string>(parameters, "workflow");
            var workflow = _registry.Get(workflowName);
            
            if (workflow == null)
            {
                stopwatch.Stop();
                return new PrimitiveResult
                {
                    Success = false,
                    Error = $"Workflow '{workflowName}' not found",
                    Duration = stopwatch.Elapsed
                };
            }
            
            // ─────────────────────────────────────────────
            //  3. 构建子上下文
            // ─────────────────────────────────────────────
            var childParams = ParameterExtensions.GetOptional<Dictionary<string, object?>>(parameters, "params")
                ?? new Dictionary<string, object?>();
            
            var childContext = new PrimitiveContext
            {
                CancellationToken = context.CancellationToken,
                Progress = context.Progress,
                CurrentDepth = context.CurrentDepth + 1,
                MaxDepth = maxDepth,
                RunId = context.RunId,
                CurrentStepId = $"{context.CurrentStepId}.{workflowName}"
            };
            
            // 解析参数中的模板
            foreach (var (key, value) in childParams)
            {
                if (value is string strValue && strValue.Contains("{{"))
                {
                    // 模板表达式，需要从父上下文解析
                    childContext.Variables[key] = ResolveValue(strValue, context.Variables);
                }
                else
                {
                    childContext.Variables[key] = value!;
                }
            }
            
            // ─────────────────────────────────────────────
            //  4. 报告进度
            // ─────────────────────────────────────────────
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "Workflow Call",
                StepId = context.CurrentStepId,
                Message = $"Calling workflow '{workflowName}' at depth {childContext.CurrentDepth}"
            });
            
            // ─────────────────────────────────────────────
            //  5. 执行子工作流
            // ─────────────────────────────────────────────
            var result = await _workflowExecutor(workflow, childContext);
            
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = result.Success,
                Value = result.Value,
                Error = result.Error,
                TokensUsed = result.TokensUsed,
                LlmCalls = result.LlmCalls,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = $"Workflow call failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
    
    /// <summary>
    /// 解析变量引用
    /// </summary>
    private static object ResolveValue(string template, Dictionary<string, object> variables)
    {
        // 简单的 {{var}} 提取
        if (template.StartsWith("{{") && template.EndsWith("}}"))
        {
            var varName = template[2..^2].Trim();
            
            // 处理点号访问
            if (varName.Contains('.'))
            {
                var parts = varName.Split('.');
                object? current = null;
                
                if (variables.TryGetValue(parts[0], out var root))
                {
                    current = root;
                    
                    for (int i = 1; i < parts.Length && current != null; i++)
                    {
                        current = GetProperty(current, parts[i]);
                    }
                }
                
                return current ?? template;
            }
            
            if (variables.TryGetValue(varName, out var value))
            {
                return value;
            }
        }
        
        return template;
    }
    
    private static object? GetProperty(object obj, string propertyName)
    {
        var prop = obj.GetType().GetProperty(propertyName);
        return prop?.GetValue(obj);
    }
}

// ============================================================
//  工作流定义
// ============================================================

/// <summary>
/// 工作流定义
/// </summary>
public class WorkflowDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0";
    public string Description { get; set; } = string.Empty;
    public List<InputParameter> Inputs { get; set; } = [];
    public List<StepDefinition> Steps { get; set; } = [];
    public Dictionary<string, string> Output { get; set; } = new();
}

/// <summary>
/// 输入参数定义
/// </summary>
public class InputParameter
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "string";
    public bool Required { get; set; }
    public object? DefaultValue { get; set; }
}

// ============================================================
//  工作流注册表接口
// ============================================================

/// <summary>
/// 工作流注册表接口
/// </summary>
public interface IWorkflowRegistry
{
    /// <summary>获取工作流定义</summary>
    WorkflowDefinition? Get(string name);
    
    /// <summary>注册工作流</summary>
    void Register(WorkflowDefinition workflow);
    
    /// <summary>列出所有工作流</summary>
    IReadOnlyList<string> List();
}

