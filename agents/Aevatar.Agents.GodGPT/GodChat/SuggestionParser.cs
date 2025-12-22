using GodGPT.GAgents.Common.Constants;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.GodChat;

/// <summary>
/// Parser for extracting conversation suggestions from AI responses.
/// Handles streaming suggestion filtering and extraction.
/// Extracted from GodChatGAgent to follow single responsibility principle.
/// </summary>
public static class SuggestionParser
{
    /// <summary>
    ///  Parse AI response to extract main content and conversation suggestions
    /// </summary>
    public static SuggestionParseResult ParseResponseWithSuggestions(string fullResponse)
    {
        if (string.IsNullOrEmpty(fullResponse))
        {
            return new SuggestionParseResult(fullResponse, new List<string>());
        }

        // Pattern to match conversation suggestions block using precompiled regex
        var match = ChatRegexPatterns.ConversationSuggestionsBlock.Match(fullResponse);

        if (match.Success)
        {
            // Extract main content by removing everything from the start of [SUGGESTIONS] to the end
            var suggestionStartIndex = fullResponse.IndexOf("[SUGGESTIONS]", StringComparison.OrdinalIgnoreCase);
            var mainContent = suggestionStartIndex > 0
                ? fullResponse.Substring(0, suggestionStartIndex).Trim()
                : "";

            var suggestionSection = match.Groups[1].Value;
            var suggestions = TextProcessingUtils.ExtractNumberedItems(suggestionSection);

            return new SuggestionParseResult(mainContent, suggestions);
        }

        return new SuggestionParseResult(fullResponse, new List<string>());
    }

    /// <summary>
    /// Checks if the text contains a partial suggestions marker using prefix matching
    /// </summary>
    public static bool IsPartialSuggestionsMarker(ReadOnlySpan<char> content)
    {
        ReadOnlySpan<char> target = "[SUGGESTIONS]".AsSpan();

        // Check all occurrences of '[' in the content
        int startIndex = 0;
        while (true)
        {
            int index = content.Slice(startIndex).IndexOf('[');
            if (index == -1) break;

            index += startIndex; // Adjust to absolute position
            ReadOnlySpan<char> remaining = content.Slice(index);

            if (target.StartsWith(remaining, StringComparison.OrdinalIgnoreCase) &&
                remaining.Length < target.Length)
            {
                return true;
            }

            startIndex = index + 1;
        }

        return false;
    }
}

/// <summary>
/// Result of parsing a response for suggestions
/// </summary>
public class SuggestionParseResult
{
    public string MainContent { get; }
    public List<string> Suggestions { get; }

    public SuggestionParseResult(string mainContent, List<string> suggestions)
    {
        MainContent = mainContent;
        Suggestions = suggestions;
    }
}

/// <summary>
/// Result of streaming content filtering
/// </summary>
public class StreamingFilterResult
{
    public string FilteredContent { get; }
    public List<string>? ExtractedSuggestions { get; }
    public bool WasBlocked { get; }

    public StreamingFilterResult(string filteredContent, List<string>? extractedSuggestions, bool wasBlocked)
    {
        FilteredContent = filteredContent;
        ExtractedSuggestions = extractedSuggestions;
        WasBlocked = wasBlocked;
    }
}

