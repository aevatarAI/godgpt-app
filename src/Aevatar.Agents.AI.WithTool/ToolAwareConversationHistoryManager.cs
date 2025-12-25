using Aevatar.Agents.AI.Abstractions;
using Google.Protobuf.Collections;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.Agents.AI.WithTool;

/// <summary>
/// Adds tool-specific history helpers on top of the base conversation manager.
/// </summary>
public class ToolAwareConversationHistoryManager
{
    private readonly RepeatedField<AevatarChatMessage> _history;

    public ToolAwareConversationHistoryManager(RepeatedField<AevatarChatMessage> history)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
    }

    /// <summary>
    /// Adds a message to the conversation history.
    /// </summary>
    public virtual void AddMessage(string content, AevatarChatRole role, string? name = null)
    {
        var message = new AevatarChatMessage
        {
            Role = role,
            Content = content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow)
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            message.Metadata[name] = string.Empty;
        }

        _history.Add(message);
    }

    /// <summary>
    /// Adds a pre-constructed message to the conversation history.
    /// </summary>
    public virtual void AddMessage(AevatarChatMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (message.Timestamp == null)
        {
            message.Timestamp = Timestamp.FromDateTime(DateTime.UtcNow);
        }

        _history.Add(message);
    }

    public virtual void Clear() => _history.Clear();

    public int MessageCount => _history.Count;

    /// <summary>
    /// Add tool call message to history.
    /// </summary>
    public AevatarChatMessage AddToolCallMessage(AevatarFunctionCall functionCall)
    {
        if (functionCall == null)
            throw new ArgumentNullException(nameof(functionCall));

        var toolCallMsg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Assistant,
            Content = $"Calling tool {functionCall.Name} with arguments: {functionCall.Arguments}",
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolCalls = { new ToolCall { ToolName = functionCall.Name, Arguments = functionCall.Arguments } }
        };

        _history.Add(toolCallMsg);
        return toolCallMsg;
    }

    /// <summary>
    /// Add tool result message to history.
    /// </summary>
    public AevatarChatMessage AddToolResultMessage(string toolName, ToolExecutionResult result)
    {
        if (string.IsNullOrEmpty(toolName))
            throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
        if (result == null)
            throw new ArgumentNullException(nameof(result));

        // Console.WriteLine($"[DEBUG] AddToolResultMessage: Tool={toolName}, Content={result.Content}");

        var toolResultMsg = new AevatarChatMessage
        {
            Role = AevatarChatRole.Tool,
            Content = result.Content,
            Timestamp = Timestamp.FromDateTime(DateTime.UtcNow),
            ToolResult = new ToolExecutionResult
            {
                ToolName = toolName,
                Content = result.Content,
                IsSuccess = result.IsSuccess,
                ErrorMessage = result.ErrorMessage
            }
        };

        _history.Add(toolResultMsg);
        return toolResultMsg;
    }
}