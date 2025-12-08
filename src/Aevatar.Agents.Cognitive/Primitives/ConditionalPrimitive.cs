using System.Diagnostics;
using Aevatar.Agents.Cognitive.Template;

namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  条件分支原语
// ============================================================

/// <summary>
/// 条件分支原语 - 根据条件执行不同分支
/// 
/// DSL 语法:
/// - id: check
///   type: conditional
///   condition: "{{is_atomic(task)}}"
///   if_true:
///     - type: llm_call
///       prompt: "Solve: {{task}}"
///   if_false:
///     - type: llm_call
///       prompt: "Decompose: {{task}}"
/// </summary>
public class ConditionalPrimitive : IPrimitive
{
    public string Type => "conditional";
    
    private readonly TemplateEngine _templateEngine;
    private readonly Func<StepDefinition, PrimitiveContext, Task<PrimitiveResult>> _stepExecutor;
    
    public ConditionalPrimitive(
        TemplateEngine templateEngine,
        Func<StepDefinition, PrimitiveContext, Task<PrimitiveResult>> stepExecutor)
    {
        _templateEngine = templateEngine;
        _stepExecutor = stepExecutor;
    }
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        var totalTokens = 0;
        var totalLlmCalls = 0;
        
        try
        {
            // ─────────────────────────────────────────────
            //  1. 评估条件
            // ─────────────────────────────────────────────
            var conditionExpr = ParameterExtensions.GetRequired<string>(parameters, "condition");
            var conditionResult = _templateEngine.Evaluate(conditionExpr, context.Variables);
            
            var isTrue = conditionResult switch
            {
                bool b => b,
                string s => s.Equals("true", StringComparison.OrdinalIgnoreCase) 
                         || s.Equals("yes", StringComparison.OrdinalIgnoreCase)
                         || s == "1",
                int i => i != 0,
                double d => d != 0,
                _ => conditionResult != null
            };
            
            // 报告进度
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "Conditional",
                StepId = context.CurrentStepId,
                Message = $"Condition '{conditionExpr}' evaluated to {isTrue}"
            });
            
            // ─────────────────────────────────────────────
            //  2. 选择分支
            // ─────────────────────────────────────────────
            var branch = isTrue
                ? ParameterExtensions.GetOptional<List<StepDefinition>>(parameters, "if_true")
                : ParameterExtensions.GetOptional<List<StepDefinition>>(parameters, "if_false");
            
            if (branch == null || branch.Count == 0)
            {
                stopwatch.Stop();
                return new PrimitiveResult
                {
                    Success = true,
                    Value = null,
                    Duration = stopwatch.Elapsed
                };
            }
            
            // ─────────────────────────────────────────────
            //  3. 执行分支步骤
            // ─────────────────────────────────────────────
            PrimitiveResult? lastResult = null;
            
            foreach (var step in branch)
            {
                lastResult = await _stepExecutor(step, context);
                
                totalTokens += lastResult.TokensUsed;
                totalLlmCalls += lastResult.LlmCalls;
                
                if (!lastResult.Success)
                {
                    stopwatch.Stop();
                    return new PrimitiveResult
                    {
                        Success = false,
                        Error = $"Branch step '{step.Id}' failed: {lastResult.Error}",
                        TokensUsed = totalTokens,
                        LlmCalls = totalLlmCalls,
                        Duration = stopwatch.Elapsed
                    };
                }
                
                // 存储结果
                if (!string.IsNullOrEmpty(step.Store) && lastResult.Value != null)
                {
                    context.Variables[step.Store] = lastResult.Value;
                }
            }
            
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = true,
                Value = lastResult?.Value,
                TokensUsed = totalTokens,
                LlmCalls = totalLlmCalls,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = $"Conditional failed: {ex.Message}",
                TokensUsed = totalTokens,
                LlmCalls = totalLlmCalls,
                Duration = stopwatch.Elapsed
            };
        }
    }
}

// ============================================================
//  步骤定义（用于分支）
// ============================================================

/// <summary>
/// 步骤定义
/// </summary>
public class StepDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public Dictionary<string, object?> Parameters { get; set; } = new();
    public string? Store { get; set; }
    
    // 用于 conditional
    public string? Condition { get; set; }
    public List<StepDefinition>? IfTrue { get; set; }
    public List<StepDefinition>? IfFalse { get; set; }
    
    // 用于 vote
    public StepDefinition? Generator { get; set; }
    
    // 用于 fan_out (支持模板变量)
    public string? ForEach { get; set; }
    public StepDefinition? Step { get; set; }
    public string? Reduce { get; set; }
    public object? MaxConcurrency { get; set; }  // int 或 "{{var}}"
    
    // 用于 workflow_call (支持模板变量)
    public string? Workflow { get; set; }
    public Dictionary<string, object?>? Params { get; set; }
    public object? MaxDepth { get; set; }  // int 或 "{{var}}"
}

