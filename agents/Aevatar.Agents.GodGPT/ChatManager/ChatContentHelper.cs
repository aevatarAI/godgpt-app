using Aevatar.GAgents.AI.Abstractions;
using Aevatar.GAgents.ChatAgent.Dtos;

namespace Aevatar.Application.Grains.Agents.ChatManager;

/// <summary>
/// Static helper class for extracting and processing chat content.
/// These are pure functions with no state dependencies.
/// </summary>
public static class ChatContentHelper
{
    private const int DefaultMaxPreviewLength = 60;
    private const int MinimumSubstantialContentLength = 5;

    /// <summary>
    /// Extract chat content preview from chat messages
    /// </summary>
    /// <param name="messages">List of chat messages</param>
    /// <returns>Content preview (first 60 characters)</returns>
    public static string ExtractChatContent(List<ChatMessage> messages, int maxLength = DefaultMaxPreviewLength)
    {
        if (messages == null || messages.Count == 0)
        {
            return string.Empty;
        }

        try
        {
            // Priority 1: Find user messages with substantial content (>5 chars)
            var userMessage = messages
                .Where(m => m != null &&
                            m.ChatRole == ChatRole.User &&
                            !string.IsNullOrWhiteSpace(m.Content) &&
                            m.Content.Trim().Length > MinimumSubstantialContentLength)
                .FirstOrDefault();

            if (userMessage?.Content != null)
            {
                return SafeTruncateContent(userMessage.Content.Trim(), maxLength);
            }

            // Priority 2: Find assistant messages with substantial content
            var assistantMessage = messages
                .Where(m => m != null &&
                            m.ChatRole == ChatRole.Assistant &&
                            !string.IsNullOrWhiteSpace(m.Content) &&
                            m.Content.Trim().Length > MinimumSubstantialContentLength)
                .FirstOrDefault();

            if (assistantMessage?.Content != null)
            {
                return SafeTruncateContent(assistantMessage.Content.Trim(), maxLength);
            }

            // Fallback: any non-empty message
            var anyMessage = messages
                .Where(m => m != null && !string.IsNullOrWhiteSpace(m.Content))
                .FirstOrDefault();

            if (anyMessage?.Content != null)
            {
                return SafeTruncateContent(anyMessage.Content.Trim(), maxLength);
            }
        }
        catch (Exception)
        {
            // Return empty string for graceful degradation
        }

        return string.Empty;
    }

    /// <summary>
    /// Safely truncate content to 60 characters with ellipsis
    /// </summary>
    /// <param name="content">Content to truncate</param>
    /// <returns>Truncated content</returns>
    public static string SafeTruncateContent(string content, int maxLength = DefaultMaxPreviewLength)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (maxLength <= 0)
        {
            maxLength = DefaultMaxPreviewLength;
        }

        try
        {
            return content.Length <= maxLength 
                ? content 
                : content.Substring(0, maxLength) + "...";
        }
        catch (Exception)
        {
            // Fallback in case of unexpected string issues
            return content.Length > maxLength 
                ? content[..Math.Min(maxLength, content.Length)] + "..." 
                : content;
        }
    }
}

