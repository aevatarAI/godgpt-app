using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Abstractions;

/// <summary>
/// AI工具接口
/// 定义了实现工具的标准契约
/// </summary>
public interface IAevatarTool
{
    /// <summary>
    /// 工具名称（唯一标识）
    /// </summary>
    string Name { get; }
    
    /// <summary>
    /// 工具描述
    /// </summary>
    string Description { get; }
    
    /// <summary>
    /// 工具类别
    /// </summary>
    ToolCategory Category { get; }
    
    /// <summary>
    /// 工具版本
    /// </summary>
    string Version { get; }
    
    /// <summary>
    /// 工具标签
    /// </summary>
    IList<string> Tags { get; }
    
    /// <summary>
    /// 创建工具定义
    /// </summary>
    /// <param name="context">工具上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <returns>配置好的工具定义</returns>
    ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger = null);
    
    /// <summary>
    /// 创建参数定义
    /// </summary>
    /// <returns>工具参数定义</returns>
    ToolParameters CreateParameters();
    
    /// <summary>
    /// 执行工具逻辑
    /// </summary>
    /// <param name="parameters">执行参数</param>
    /// <param name="context">工具上下文</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 验证参数
    /// </summary>
    /// <param name="parameters">要验证的参数</param>
    /// <returns>验证结果</returns>
    ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters);
}

/// <summary>
/// 工具基类
/// 提供 IAevatarTool 的默认实现
/// </summary>
public abstract class AevatarToolBase : IAevatarTool
{
    /// <inheritdoc />
    public abstract string Name { get; }
    
    /// <inheritdoc />
    public abstract string Description { get; }
    
    /// <inheritdoc />
    public virtual ToolCategory Category { get; } = ToolCategory.Custom;
    
    /// <inheritdoc />
    public virtual string Version { get; } = "1.0.0";
    
    /// <inheritdoc />
    public virtual IList<string> Tags { get; } = new List<string>();
    
    /// <inheritdoc />
    public virtual ToolDefinition CreateToolDefinition(ToolContext context, ILogger? logger = null)
    {
        return new ToolDefinition
        {
            Name = Name,
            Description = Description,
            Category = Category,
            Version = Version,
            Tags = Tags,
            Parameters = CreateParameters(),
            ExecuteAsync = (parameters, executionContext, ct) =>
                ExecuteAsync(parameters, BuildRuntimeToolContext(context, executionContext, logger), logger, ct),
            RequiresInternalAccess = RequiresInternalAccess(),
            CanBeOverridden = CanBeOverridden(),
            RequiresConfirmation = RequiresConfirmation(),
            IsDangerous = IsDangerous(),
            RateLimit = GetRateLimit(),
            Timeout = GetTimeout()
        };
    }

    // ============================================================
    //  ToolContext composition
    //
    //  WHY:
    //  - ToolDefinition.ExecuteAsync receives a per-call ToolExecutionContext (memory, session, callbacks...).
    //  - IAevatarTool.ExecuteAsync expects ToolContext.
    //  - We merge "registration-time context" (static) + "execution-time context" (dynamic) into a runtime ToolContext.
    //
    //  NOTE:
    //  - This keeps backward compatibility: if executionContext is null, tools behave exactly as before.
    // ============================================================
    private static ToolContext BuildRuntimeToolContext(
        ToolContext baseContext,
        ToolExecutionContext? executionContext,
        ILogger? logger)
    {
        // Clone base context first (avoid tools mutating shared captured instance)
        var merged = new ToolContext
        {
            AgentId = baseContext.AgentId,
            AgentType = baseContext.AgentType,
            IncludeCoreTools = baseContext.IncludeCoreTools,
            Categories = baseContext.Categories,
            GetStateCallback = baseContext.GetStateCallback,
            PublishEventCallback = baseContext.PublishEventCallback,
            Memory = baseContext.Memory,
            GetSessionIdCallback = baseContext.GetSessionIdCallback,
            Logger = baseContext.Logger ?? logger,
            Metadata = baseContext.Metadata != null
                ? new Dictionary<string, object>(baseContext.Metadata)
                : new Dictionary<string, object>()
        };

        if (executionContext == null)
            return merged;

        if (!string.IsNullOrWhiteSpace(executionContext.AgentId))
            merged.AgentId = executionContext.AgentId;

        if (executionContext.Memory != null)
            merged.Memory = executionContext.Memory;

        if (executionContext.PublishEventCallback != null)
            merged.PublishEventCallback = executionContext.PublishEventCallback;

        if (executionContext.GetSessionId != null)
            merged.GetSessionIdCallback = () => executionContext.GetSessionId();

        if (executionContext.Logger != null)
            merged.Logger = executionContext.Logger;

        if (executionContext.Metadata is { Count: > 0 })
        {
            foreach (var (k, v) in executionContext.Metadata)
            {
                merged.Metadata[k] = v;
            }
        }

        return merged;
    }
    
    /// <inheritdoc />
    public abstract ToolParameters CreateParameters();
    
    public abstract Task<IMessage> ExecuteAsync(
        Dictionary<string, object> parameters,
        ToolContext context,
        ILogger? logger,
        CancellationToken cancellationToken = default);
    
    /// <inheritdoc />
    public virtual ToolParameterValidationResult ValidateParameters(Dictionary<string, object?> parameters)
    {
        var result = new ToolParameterValidationResult { IsValid = true };
        var paramDefinitions = CreateParameters();
        
        // 验证必需参数
        foreach (var requiredParam in paramDefinitions.Required)
        {
            if (!parameters.ContainsKey(requiredParam) || parameters[requiredParam] == null)
            {
                result.IsValid = false;
                result.Errors.Add($"Required parameter '{requiredParam}' is missing");
            }
        }
        
        // 验证参数类型和枚举值
        foreach (var param in parameters)
        {
            if (paramDefinitions.Items.TryGetValue(param.Key, out var paramDef))
            {
                // 验证枚举值
                if (paramDef.Enum != null && paramDef.Enum.Count > 0)
                {
                    var value = param.Value?.ToString();
                    if (value != null && !paramDef.Enum.Contains(value))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"Parameter '{param.Key}' value '{value}' is not in allowed values: {string.Join(", ", paramDef.Enum)}");
                    }
                }
                
                // 验证类型（简化版本）
                if (!ValidateType(param.Value, paramDef.Type))
                {
                    result.IsValid = false;
                    result.Errors.Add($"Parameter '{param.Key}' type mismatch, expected: {paramDef.Type}");
                }
            }
        }
        
        return result;
    }

    /// <summary>
    /// Convert ExecuteAsync parameters (Dictionary&lt;string, object&gt;) to the validator shape
    /// (Dictionary&lt;string, object?&gt;).
    ///
    /// Note: nullability annotations are not part of the CLR type identity, so this is purely for
    /// compile-time flow analysis and to keep call sites clean.
    /// </summary>
    protected static Dictionary<string, object?> ToNullableParameters(Dictionary<string, object> parameters)
    {
        var result = new Dictionary<string, object?>(parameters.Count, StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
        {
            result[key] = value;
        }
        return result;
    }

    /// <summary>
    /// 是否需要内部访问权限
    /// </summary>
    protected virtual bool RequiresInternalAccess() => false;
    
    /// <summary>
    /// 是否可以被覆盖
    /// </summary>
    protected virtual bool CanBeOverridden() => true;
    
    /// <summary>
    /// 是否需要确认
    /// </summary>
    protected virtual bool RequiresConfirmation() => false;
    
    /// <summary>
    /// 是否是危险操作
    /// </summary>
    protected virtual bool IsDangerous() => false;
    
    /// <summary>
    /// 获取速率限制
    /// </summary>
    protected virtual int? GetRateLimit() => null;
    
    /// <summary>
    /// 获取超时时间
    /// </summary>
    protected virtual TimeSpan? GetTimeout() => null;
    
    /// <summary>
    /// 验证参数类型
    /// </summary>
    private bool ValidateType(object? value, string? expectedType)
    {
        if (value == null || string.IsNullOrEmpty(expectedType))
            return true;
        
        return expectedType.ToLower() switch
        {
            "string" => value is string,
            "integer" or "int" => value is int or long or short or byte,
            "number" or "float" or "double" => value is float or double or decimal or int or long,
            "boolean" or "bool" => value is bool,
            "object" => value is IDictionary<string, object> or object,
            "array" => value is IEnumerable<object>,
            _ => true
        };
    }
}

/// <summary>
/// 参数验证结果
/// </summary>
public class ToolParameterValidationResult
{
    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 错误列表
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 警告列表
    /// </summary>
    public List<string> Warnings { get; set; } = new();
}