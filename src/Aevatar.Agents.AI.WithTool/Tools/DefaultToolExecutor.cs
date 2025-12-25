using System.Diagnostics;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Messages; // For AevatarToolExecutedEvent
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools;

/// <summary>
/// 默认的工具执行器实现
/// 负责工具的执行、监控和事件发布
/// </summary>
public class DefaultToolExecutor : IToolExecutor
{
    private readonly ILogger<DefaultToolExecutor>? _logger;
    
    public DefaultToolExecutor(ILogger<DefaultToolExecutor>? logger = null)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// 执行工具并发布相关事件
    /// </summary>
    public virtual async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var executionId = Guid.NewGuid().ToString();
        ToolExecutionContext? validatedContext = null;
        
        _logger?.LogInformation("Executing tool {ToolName} with execution ID {ExecutionId}", 
            toolName, executionId);
        
        try
        {
            validatedContext = context ?? throw new ArgumentNullException(nameof(context));
            ValidateContext(validatedContext);
            
            // 执行工具
            var result = await validatedContext.ToolManager.ExecuteToolAsync(
                toolName,
                parameters,
                validatedContext,
                cancellationToken);
            
            stopwatch.Stop();
            
            // 发布工具执行事件
            await PublishToolExecutedEventAsync(result, validatedContext, cancellationToken);
            
            // 记录到内存（如果配置了）
            if (validatedContext is { RecordToMemory: true, Memory: not null })
            {
                await RecordExecutionToMemoryAsync(result, validatedContext, cancellationToken);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger?.LogError(ex, "Error executing tool {ToolName}", toolName);
            
            var errorResult = new ToolExecutionResult
            {
                ToolCallId = executionId,
                ToolName = toolName,
                IsSuccess = false,
                // Put the error into Content as well, so event/memory consumers can read it without branching.
                Content = ex.Message,
                ErrorMessage = ex.Message,
                Duration = Duration.FromTimeSpan(stopwatch.Elapsed)
            };
            
            // Publish a best-effort "tool executed" event (Success=false).
            await PublishToolExecutedEventAsync(errorResult, validatedContext, cancellationToken);
            
            return errorResult;
        }
    }
    
    /// <summary>
    /// 批量执行工具
    /// </summary>
    public virtual async Task<IEnumerable<ToolExecutionResult>> ExecuteToolsAsync(
        IEnumerable<ToolExecutionRequest> requests,
        ToolExecutionContext context,
        bool parallel = false,
        CancellationToken cancellationToken = default)
    {
        if (parallel)
        {
            var tasks = requests.Select(r => 
                ExecuteToolAsync(r.ToolName, r.Parameters, context, cancellationToken));
            return await Task.WhenAll(tasks);
        }

        var results = new List<ToolExecutionResult>();
        foreach (var request in requests)
        {
            var result = await ExecuteToolAsync(
                request.ToolName, 
                request.Parameters, 
                context, 
                cancellationToken);
            results.Add(result);
        }
        return results;
    }
    
    /// <summary>
    /// 验证执行上下文
    /// </summary>
    protected virtual void ValidateContext(ToolExecutionContext? context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        
        if (context.ToolManager == null)
        {
            throw new InvalidOperationException("ToolManager is required in ToolExecutionContext");
        }
        
        if (string.IsNullOrEmpty(context.AgentId))
        {
            throw new InvalidOperationException("AgentId is required in ToolExecutionContext");
        }
    }
    
    /// <summary>
    /// 发布工具执行成功事件
    /// </summary>
    protected virtual async Task PublishToolExecutedEventAsync(
        ToolExecutionResult result,
        ToolExecutionContext? context,
        CancellationToken cancellationToken)
    {
        if (context?.PublishEventCallback == null)
        {
            _logger?.LogDebug("PublishEventCallback not provided, skipping event publication");
            return;
        }
        
        var toolEvent = new AevatarToolExecutedEvent
        {
            ToolName = result.ToolName,
            Result = result.Content ?? string.Empty,
            Success = result.IsSuccess,
            ExecutionTimeMs = (long)result.Duration.ToTimeSpan().TotalMilliseconds
        };

        // if (result.Metadata?.TryGetValue("parameters", out var parameters) == true && 
        //     parameters is Dictionary<string, object> paramDict)
        // {
        //     foreach (var param in result)
        //     {
        //         toolEvent.Parameters[param.Key] = param.Value?.ToString() ?? string.Empty;
        //     }
        // }
        
        await context.PublishEventCallback(toolEvent);
        
        _logger?.LogDebug("Published tool executed event for {ToolName}", result.ToolName);
    }
    
    /// <summary>
    /// 记录执行到记忆
    /// </summary>
    protected virtual async Task RecordExecutionToMemoryAsync(
        ToolExecutionResult result,
        ToolExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (context.Memory == null)
        {
            return;
        }
        
        try
        {
            var memoryContent = $"Tool '{result.ToolName}' executed " +
                              $"{(result.IsSuccess ? "successfully" : "with error")}. " +
                              $"Result: {result.Content ?? result.ErrorMessage ?? "N/A"}";
            
            // 使用简化的记忆接口 - 添加为对话记录
            await context.Memory.AddMessageAsync(
                "system",
                memoryContent,
                cancellationToken);
            
            _logger?.LogDebug("Recorded tool execution to memory for {ToolName}", result.ToolName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to record tool execution to memory");
        }
    }
}