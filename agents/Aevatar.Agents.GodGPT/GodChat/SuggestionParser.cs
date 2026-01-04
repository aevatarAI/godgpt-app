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
    /// Parse only the suggestions content (after [SUGGESTIONS] marker was found)
    /// </summary>
    public static List<string>? ParseSuggestionsOnly(string suggestionsContent)
    {
        if (string.IsNullOrEmpty(suggestionsContent))
        {
            return null;
        }
        
        var suggestions = TextProcessingUtils.ExtractNumberedItems(suggestionsContent);
        return suggestions.Count > 0 ? suggestions : null;
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

/// <summary>
/// Streaming filter for [SUGGESTIONS] blocks.
/// 
/// State machine approach (per user's cleaner design):
/// - NORMAL: Send content normally, watch for '['
/// - BUFFERING: Encountered '[', buffering to check if it's [SUGGESTIONS]
/// - DISCARDING: Confirmed [SUGGESTIONS], discard all following content
/// 
/// Logic:
/// 1. See '[' → start buffering
/// 2. Keep appending chars, check if buffer matches "[SUGGESTIONS]" prefix
/// 3. If fully matches "[SUGGESTIONS]" → switch to DISCARDING
/// 4. If buffer reaches 13 chars but doesn't match → flush buffer (it's normal text), back to NORMAL
/// </summary>
public class StreamingSuggestionsFilter
{
    private const string SuggestionsMarker = "[SUGGESTIONS]";
    private const int MarkerLength = 13; // "[SUGGESTIONS]".Length
    
    private enum FilterState { Normal, Buffering, Discarding }
    
    private FilterState _state = FilterState.Normal;
    private readonly System.Text.StringBuilder _buffer = new();
    private readonly System.Text.StringBuilder _suggestionsContent = new();
    
    /// <summary>
    /// Whether the filter is discarding content (found [SUGGESTIONS])
    /// </summary>
    public bool IsAccumulating => _state == FilterState.Discarding;
    
    /// <summary>
    /// Process a single character or chunk of content.
    /// Returns the content that should be sent to client.
    /// </summary>
    public StreamingFilterResult ProcessChunk(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return new StreamingFilterResult("", null, false);
        }
        
        var output = new System.Text.StringBuilder();
        
        foreach (var ch in content)
        {
            switch (_state)
            {
                case FilterState.Normal:
                    if (ch == '[')
                    {
                        // Start buffering to check for [SUGGESTIONS]
                        _state = FilterState.Buffering;
                        _buffer.Clear();
                        _buffer.Append(ch);
                    }
                    else
                    {
                        output.Append(ch);
                    }
                    break;
                    
                case FilterState.Buffering:
                    _buffer.Append(ch);
                    var bufferStr = _buffer.ToString();
                    
                    // Check if buffer matches [SUGGESTIONS] prefix
                    if (SuggestionsMarker.StartsWith(bufferStr, StringComparison.OrdinalIgnoreCase))
                    {
                        // Still a valid prefix
                        if (bufferStr.Length == MarkerLength)
                        {
                            // Exact match! Switch to discarding mode
                            _state = FilterState.Discarding;
                            _suggestionsContent.Clear();
                        }
                        // else: continue buffering
                    }
                    else
                    {
                        // Not a match - flush buffer as normal content
                        output.Append(bufferStr);
                        _buffer.Clear();
                        _state = FilterState.Normal;
                    }
                    break;
                    
                case FilterState.Discarding:
                    // Accumulate for final extraction
                    _suggestionsContent.Append(ch);
                    break;
            }
        }
        
        var outputStr = output.ToString();
        var wasBlocked = _state == FilterState.Discarding || 
                         (_state == FilterState.Buffering && string.IsNullOrEmpty(outputStr));
        
        return new StreamingFilterResult(outputStr, null, wasBlocked);
    }
    
    /// <summary>
    /// Extract final content when stream completes.
    /// If still buffering (incomplete marker), flush buffer as normal content.
    /// If discarding, parse accumulated suggestions.
    /// </summary>
    public StreamingFilterResult ExtractFinalContent()
    {
        if (_state == FilterState.Buffering)
        {
            // Incomplete marker at end - it's just normal text
            var remaining = _buffer.ToString();
            _buffer.Clear();
            _state = FilterState.Normal;
            return new StreamingFilterResult(remaining, null, false);
        }
        
        if (_state == FilterState.Discarding)
        {
            // Parse the accumulated suggestions content
            var accumulated = _suggestionsContent.ToString();
            var parseResult = SuggestionParser.ParseSuggestionsOnly(accumulated);
            
            _state = FilterState.Normal;
            _suggestionsContent.Clear();
            
            return new StreamingFilterResult(
                "", 
                parseResult?.Any() == true ? parseResult : null, 
                false);
        }
        
        return new StreamingFilterResult("", null, false);
    }
    
    /// <summary>
    /// Get any buffered content (for debugging)
    /// </summary>
    public string GetBufferedContent() => _buffer.ToString();
}

