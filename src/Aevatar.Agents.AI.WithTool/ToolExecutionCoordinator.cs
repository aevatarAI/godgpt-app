using System.Text.Json;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Abstractions.Providers;
using Aevatar.Agents.AI.WithTool.Abstractions;
using Aevatar.Agents.AI.WithTool.Exceptions;
using Aevatar.Agents.AI.WithTool.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aevatar.Agents.AI.WithTool;

// ============================================================
//  Tool Execution Coordinator
//  Coordinates tool execution workflow including parsing,
//  execution, and response generation.
// ============================================================

/// <summary>
/// Coordinates tool execution workflow including parsing, execution, and response generation.
/// Separates tool execution logic from the main agent class.
/// </summary>
public class ToolExecutionCoordinator
{
    private readonly IAevatarToolManager _toolManager;
    private readonly IAevatarLLMProvider _llmProvider;
    private readonly ToolAwareConversationHistoryManager _historyManager;
    private readonly ILogger _logger;

    /// <summary>
    /// Whether to throw exceptions on argument parse failures.
    /// Default: false (returns empty dict for backward compatibility)
    /// </summary>
    public bool ThrowOnParseError { get; set; }

    public ToolExecutionCoordinator(
        IAevatarToolManager toolManager,
        IAevatarLLMProvider llmProvider,
        ToolAwareConversationHistoryManager historyManager,
        ILogger? logger = null)
    {
        _toolManager = toolManager ?? throw new ArgumentNullException(nameof(toolManager));
        _llmProvider = llmProvider ?? throw new ArgumentNullException(nameof(llmProvider));
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
        _logger = logger ?? NullLogger.Instance;
    }

    /// <summary>
    /// Handle complete tool execution workflow.
    /// </summary>
    public async Task<(ToolExecutionResult Result, AevatarLLMResponse FinalResponse)> ExecuteToolWorkflowAsync(
        AevatarFunctionCall functionCall,
        AevatarLLMRequest llmRequest,
        ToolExecutionContext? executionContext = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(functionCall);

        _logger.LogDebug("Executing tool: {ToolName}", functionCall.Name);

        // 1. Add tool call to history
        var toolCallMsg = _historyManager.AddToolCallMessage(functionCall);

        // 2. Execute tool
        var result = await ExecuteToolAsync(functionCall, executionContext, cancellationToken);

        // 3. Add tool result to history
        var toolResultMsg = _historyManager.AddToolResultMessage(functionCall.Name, result);

        // 4. Generate final LLM response with tool result
        llmRequest.Messages.Add(toolCallMsg);
        llmRequest.Messages.Add(toolResultMsg);

        var finalResponse = await _llmProvider.GenerateAsync(llmRequest, cancellationToken);

        // 5. Add assistant response to history
        _historyManager.AddMessage(finalResponse.Content, AevatarChatRole.Assistant);

        return (result, finalResponse);
    }

    /// <summary>
    /// Execute tool with given function call.
    /// </summary>
    private async Task<ToolExecutionResult> ExecuteToolAsync(
        AevatarFunctionCall functionCall,
        ToolExecutionContext? executionContext,
        CancellationToken cancellationToken)
    {
        // Parse arguments with proper error handling
        var parseResult = TryParseToolArguments(functionCall.Arguments, functionCall.Name);

        if (!parseResult.Success && ThrowOnParseError)
        {
            throw new ToolArgumentParseException(
                functionCall.Name,
                functionCall.Arguments,
                new JsonException(parseResult.Error));
        }

        // Execute tool via tool manager
        return await _toolManager.ExecuteToolAsync(
            functionCall.Name,
            parseResult.Parameters,
            context: executionContext,
            cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Parse tool arguments from JSON string with structured result.
    /// </summary>
    /// <param name="argumentsJson">JSON string containing arguments</param>
    /// <param name="toolName">Tool name for error context</param>
    /// <returns>Parse result with success/failure info</returns>
    public ToolArgumentParseResult TryParseToolArguments(string argumentsJson, string? toolName = null)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
            return ToolArgumentParseResult.Ok(new Dictionary<string, object>());

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson);
            if (dict == null)
                return ToolArgumentParseResult.Ok(new Dictionary<string, object>());

            var result = new Dictionary<string, object>();
            foreach (var kvp in dict)
            {
                result[kvp.Key] = kvp.Value.ValueKind switch
                {
                    JsonValueKind.String => kvp.Value.GetString() ?? string.Empty,
                    JsonValueKind.Number => kvp.Value.GetDouble(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Null => null!,
                    // For arrays and objects, keep the JsonElement for flexible handling
                    _ => kvp.Value
                };
            }

            _logger.LogDebug("Parsed {Count} arguments for tool {ToolName}",
                result.Count, toolName ?? "unknown");

            return ToolArgumentParseResult.Ok(result);
        }
        catch (JsonException ex)
        {
            var error = $"JSON parse error: {ex.Message}";
            _logger.LogWarning(ex, "Failed to parse tool arguments for {ToolName}: {Arguments}",
                toolName ?? "unknown", TruncateForLog(argumentsJson));

            return ToolArgumentParseResult.Fail(error);
        }
    }

    /// <summary>
    /// Create ChatResponse with tool execution information.
    /// </summary>
    public ChatResponse CreateResponseWithToolInfo(
        string requestId,
        AevatarLLMResponse llmResponse,
        AevatarFunctionCall functionCall,
        ToolExecutionResult toolResult)
    {
        var response = new ChatResponse
        {
            Content = llmResponse.Content,
            RequestId = requestId,
            ToolCalled = true,
            ToolCall = new ToolCallInfo
            {
                ToolName = functionCall.Name,
                Result = toolResult.Content ?? string.Empty
            }
        };

        // Populate tool call arguments
        if (!string.IsNullOrEmpty(functionCall.Arguments))
        {
            var parseResult = TryParseToolArguments(functionCall.Arguments, functionCall.Name);
            foreach (var arg in parseResult.Parameters)
            {
                response.ToolCall.Arguments[arg.Key] = arg.Value?.ToString() ?? string.Empty;
            }
        }

        // Add usage information
        if (llmResponse.Usage != null)
        {
            response.Usage = new AevatarTokenUsage
            {
                PromptTokens = llmResponse.Usage.PromptTokens,
                CompletionTokens = llmResponse.Usage.CompletionTokens,
                TotalTokens = llmResponse.Usage.TotalTokens
            };
        }

        return response;
    }

    private static string TruncateForLog(string value, int maxLength = 200)
    {
        if (string.IsNullOrEmpty(value)) return "(empty)";
        return value.Length > maxLength ? value[..maxLength] + "..." : value;
    }
}
