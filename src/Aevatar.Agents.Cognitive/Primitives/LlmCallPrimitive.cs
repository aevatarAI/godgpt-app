using System.Diagnostics;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Cognitive.Template;

namespace Aevatar.Agents.Cognitive.Primitives;

// ============================================================
//  LLM Call 原语
// ============================================================

/// <summary>
/// LLM 调用原语 - 所有 AI 推理的基础
/// 
/// DSL 语法:
/// - id: analyze
///   type: llm_call
///   prompt: |
///     Analyze the following:
///     {{content}}
///   system: "You are an expert analyst."
///   output: json
///   store: result
/// </summary>
public class LlmCallPrimitive : IPrimitive
{
    public string Type => "llm_call";
    
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly TemplateEngine _templateEngine;
    private readonly OutputParserFactory _parserFactory;
    
    public LlmCallPrimitive(
        IAevatarLLMProvider llmProvider,
        TemplateEngine templateEngine,
        OutputParserFactory parserFactory)
    {
        _llmProvider = llmProvider;
        _templateEngine = templateEngine;
        _parserFactory = parserFactory;
    }
    
    public async Task<PrimitiveResult> ExecuteAsync(
        PrimitiveContext context,
        Dictionary<string, object?> parameters)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // ─────────────────────────────────────────────
            //  1. 获取并渲染 prompt
            // ─────────────────────────────────────────────
            var promptTemplate = ParameterExtensions.GetRequired<string>(parameters, "prompt");
            var prompt = _templateEngine.Render(promptTemplate, context.Variables);
            
            // ─────────────────────────────────────────────
            //  2. 获取系统提示（可选）
            // ─────────────────────────────────────────────
            var systemPrompt = ParameterExtensions.GetOptional<string>(parameters, "system");
            if (systemPrompt != null)
            {
                systemPrompt = _templateEngine.Render(systemPrompt, context.Variables);
            }
            
            // ─────────────────────────────────────────────
            //  3. 构建请求
            // ─────────────────────────────────────────────
            var request = new AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = prompt
            };
            
            // 报告进度
            context.Progress?.Report(new WorkflowProgress
            {
                Phase = "LLM Call",
                StepId = context.CurrentStepId,
                Message = $"Calling LLM with {prompt.Length} chars prompt"
            });
            
            // ─────────────────────────────────────────────
            //  4. 调用 LLM
            // ─────────────────────────────────────────────
            var response = await _llmProvider.GenerateAsync(
                request, 
                context.CancellationToken);
            
            // ─────────────────────────────────────────────
            //  5. 解析输出
            // ─────────────────────────────────────────────
            var outputType = ParameterExtensions.GetOptional(parameters, "output", "text")!;
            var parser = _parserFactory.Create(outputType);
            var parsed = parser.Parse(response.Content);
            
            stopwatch.Stop();
            
            var promptTokens = response.Usage?.PromptTokens ?? 0;
            var completionTokens = response.Usage?.CompletionTokens ?? 0;
            
            return new PrimitiveResult
            {
                Success = true,
                Value = parsed,
                TokensUsed = promptTokens + completionTokens,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                LlmCalls = 1,
                Duration = stopwatch.Elapsed
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new PrimitiveResult
            {
                Success = false,
                Error = $"LLM call failed: {ex.Message}",
                Duration = stopwatch.Elapsed
            };
        }
    }
}
