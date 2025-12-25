using System.Collections.Concurrent;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.AI.WithTool.Tools;

/// <summary>
/// Default implementation of the tool manager.
/// <para/>
/// Provides thread-safe tool registration, discovery, execution, and Function Calling support.
/// </summary>
public class AevatarToolManager : IAevatarToolManager
{
    private readonly ConcurrentDictionary<string, ToolDefinition> _tools = new();
    private readonly ILogger<AevatarToolManager> _logger;

    public AevatarToolManager(ILogger<AevatarToolManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task RegisterToolAsync(ToolDefinition tool, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tool);

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            throw new ArgumentException("Tool name cannot be null or empty", nameof(tool));
        }

        if (!tool.CanBeOverridden && _tools.ContainsKey(tool.Name))
        {
            _logger.LogWarning("Tool {ToolName} already exists and cannot be overridden", tool.Name);
            return Task.CompletedTask;
        }

        _tools[tool.Name] = tool;
        _logger.LogInformation("Registered tool: {ToolName} (Category: {Category}, Version: {Version})",
            tool.Name, tool.Category, tool.Version);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ToolDefinition>> GetAvailableToolsAsync(CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        return _tools.Values.Where(t => t.IsEnabled).ToList();
    }

    /// <inheritdoc/>
    public async Task<ToolExecutionResult> ExecuteToolAsync(
        string toolName,
        Dictionary<string, object> parameters,
        ToolExecutionContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);
        ArgumentNullException.ThrowIfNull(parameters);

        if (!_tools.TryGetValue(toolName, out var tool))
        {
            _logger.LogError("Tool '{ToolName}' not found", toolName);
            return new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' not found",
                ToolName = toolName
            };
        }

        if (!tool.IsEnabled)
        {
            _logger.LogWarning("Tool '{ToolName}' is disabled", toolName);
            return new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' is disabled",
                ToolName = toolName
            };
        }

        if (tool.ExecuteAsync == null)
        {
            _logger.LogError("Tool '{ToolName}' has no execution function", toolName);
            return new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool '{toolName}' has no execution function",
                ToolName = toolName
            };
        }

        _logger.LogDebug("Executing tool: {ToolName} for agent: {AgentId}", toolName, context?.AgentId ?? "unknown");

        try
        {
            // Execute tool
            var result = await tool.ExecuteAsync(parameters, context, cancellationToken);

            _logger.LogDebug("Tool executed successfully: {ToolName}", toolName);

            return new ToolExecutionResult
            {
                IsSuccess = true,
                Content = JsonFormatter.Default.Format(result),
                ToolName = toolName
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool execution failed: {ToolName}", toolName);
            return new ToolExecutionResult
            {
                IsSuccess = false,
                ErrorMessage = $"Tool execution failed: {ex.Message}",
                ToolName = toolName
            };
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<AevatarFunctionDefinition>> GenerateFunctionDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var tools = await GetAvailableToolsAsync(cancellationToken);
        var definitions = new List<AevatarFunctionDefinition>();

        foreach (var tool in tools)
        {
            if (tool.ExecuteAsync != null)
            {
                var functionDef = new AevatarFunctionDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    Parameters = ConvertToFunctionParameters(tool.Parameters)
                };

                definitions.Add(functionDef);
            }
        }

        return definitions;
    }

    /// <summary>
    /// Get a single tool by name.
    /// </summary>
    public async Task<ToolDefinition?> GetToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolName);

        await Task.CompletedTask;
        return _tools.TryGetValue(toolName, out var tool) ? tool : null;
    }

    /// <summary>
    /// Check if a tool exists.
    /// </summary>
    public bool HasTool(string toolName)
    {
        return !string.IsNullOrWhiteSpace(toolName) && _tools.ContainsKey(toolName);
    }

    /// <summary>
    /// Enable a tool.
    /// </summary>
    public async Task<bool> EnableToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (_tools.TryGetValue(toolName, out var tool))
        {
            tool.IsEnabled = true;
            _logger.LogInformation("Enabled tool: {ToolName}", toolName);
            return true;
        }

        _logger.LogWarning("Cannot enable tool '{ToolName}': tool not found", toolName);
        return false;
    }

    /// <summary>
    /// Disable a tool.
    /// </summary>
    public async Task<bool> DisableToolAsync(string toolName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        if (_tools.TryGetValue(toolName, out var tool))
        {
            tool.IsEnabled = false;
            _logger.LogInformation("Disabled tool: {ToolName}", toolName);
            return true;
        }

        _logger.LogWarning("Cannot disable tool '{ToolName}': tool not found", toolName);
        return false;
    }

    /// <summary>
    /// Convert tool parameters to function parameters.
    /// </summary>
    private Dictionary<string, AevatarParameterDefinition> ConvertToFunctionParameters(ToolParameters parameters)
    {
        var functionParams = new Dictionary<string, AevatarParameterDefinition>();

        if (parameters?.Items == null)
        {
            return functionParams;
        }

        foreach (var param in parameters.Items)
        {
            functionParams[param.Key] = new AevatarParameterDefinition
            {
                Type = ParseTypeString(param.Value.Type),
                Description = param.Value.Description,
                Required = parameters.Required?.Contains(param.Key) ?? false,
                Default = param.Value.DefaultValue,
                Enum = param.Value.Enum?
                    .Select(e => e?.ToString())
                    .Where(s => !string.IsNullOrWhiteSpace(s))
                    .Select(s => s!)
                    .ToList()
            };
        }

        return functionParams;
    }

    /// <summary>
    /// Parse type string.
    /// </summary>
    private static string ParseTypeString(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "string" => "string",
            "integer" or "int" or "int32" => "integer",
            "number" or "float" or "double" or "decimal" => "number",
            "boolean" or "bool" => "boolean",
            "array" => "array",
            "object" => "object",
            _ => "string" // Default
        };
    }
}
