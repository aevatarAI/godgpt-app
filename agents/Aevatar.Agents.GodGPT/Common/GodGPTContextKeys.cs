using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Application.Grains.Agents.ChatManager.Common;

/// <summary>
/// GodGPT-specific agent context keys.
/// Extends the framework-provided AgentContextKeys with domain-specific keys.
/// </summary>
public static class GodGPTContextKeys
{
    /// <summary>
    /// GodGPT language setting for localization.
    /// </summary>
    public static readonly AgentContextKey<string> GodGPTLanguage =
        new("GodGPTLanguage", Common.GodGPTLanguage.English.ToString());

    /// <summary>
    /// Conversation suggestions parsed from AI response.
    /// Used internally during streaming response processing.
    /// </summary>
    public static readonly AgentContextKey<List<string>> ConversationSuggestions =
        new("ConversationSuggestions");

    /// <summary>
    /// Clean main content after removing suggestions.
    /// Used internally during streaming response processing.
    /// </summary>
    public static readonly AgentContextKey<string> CleanMainContent =
        new("CleanMainContent");

    /// <summary>
    /// Flag indicating content is being accumulated for suggestion parsing.
    /// </summary>
    public static readonly AgentContextKey<bool> AccumulatedContent =
        new("AccumulatedContent", false);

    /// <summary>
    /// Names of GodGPT-specific keys.
    /// </summary>
    public static readonly IReadOnlyList<string> GodGPTKeyNames = new[]
    {
        "GodGPTLanguage",
        "ConversationSuggestions",
        "CleanMainContent",
        "AccumulatedContent"
    };
}

