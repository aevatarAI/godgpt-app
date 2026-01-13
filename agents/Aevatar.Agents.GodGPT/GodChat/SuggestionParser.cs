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
    /// Clean any remaining SUGGESTIONS markers from content.
    /// This is a safety net to ensure no SUGGESTIONS-related content leaks through.
    /// Removes:
    /// - [SUGGESTIONS] markers (case-insensitive)
    /// - [/SUGGESTIONS] markers (case-insensitive)
    /// - Partial markers like "[SUGGESTIONS", "SUGGESTIONS]", etc.
    /// - Any content after [SUGGESTIONS] marker
    /// </summary>
    public static string CleanRemainingSuggestionsMarkers(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return content;
        }
        
        // Find the start of [SUGGESTIONS] marker (case-insensitive)
        var suggestionsIndex = content.IndexOf("[SUGGESTIONS]", StringComparison.OrdinalIgnoreCase);
        if (suggestionsIndex >= 0)
        {
            // Remove everything from [SUGGESTIONS] to the end
            var cleaned = content.Substring(0, suggestionsIndex).TrimEnd();
            
            // Also check for partial markers that might have leaked through
            // Remove any trailing partial markers
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned, 
                @"\[SUGGESTIONS?[^\]]*$", 
                "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            cleaned = System.Text.RegularExpressions.Regex.Replace(
                cleaned, 
                @"\[/SUGGESTIONS?[^\]]*$", 
                "", 
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            
            return cleaned.TrimEnd();
        }
        
        // Check for partial markers at the end
        var cleaned2 = System.Text.RegularExpressions.Regex.Replace(
            content, 
            @"\[SUGGESTIONS?[^\]]*$", 
            "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        cleaned2 = System.Text.RegularExpressions.Regex.Replace(
            cleaned2, 
            @"\[/SUGGESTIONS?[^\]]*$", 
            "", 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        
        return cleaned2.TrimEnd();
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
/// Fixed "I don't understand" suggestions for different languages
/// </summary>
public static class FixedSuggestions
{
    public const string English = "I don't understand";
    public const string SimplifiedChinese = "我不理解";
    public const string TraditionalChinese = "我不明白";
    public const string Spanish = "No entiendo";
    
    /// <summary>
    /// Ensure the suggestions list contains exactly 4 items, with the 4th being the fixed "I don't understand" option.
    /// If the 4th item already matches the fixed suggestion (in any language), keep it.
    /// If fewer than 4 items, add the fixed suggestion.
    /// </summary>
    public static List<string> EnsureFixedSuggestion(List<string>? suggestions, string? userLanguage = null)
    {
        var result = suggestions?.ToList() ?? new List<string>();
        
        // Determine the fixed suggestion based on user language
        var fixedSuggestion = GetFixedSuggestionForLanguage(userLanguage);
        
        // Check if any of the existing items is a fixed suggestion (any language)
        bool hasFixedSuggestion = result.Any(s => IsFixedSuggestion(s));
        
        if (result.Count < 4)
        {
            // Add the fixed suggestion if we have fewer than 4 items
            if (!hasFixedSuggestion)
            {
                result.Add(fixedSuggestion);
            }
        }
        else if (result.Count == 4 && !hasFixedSuggestion)
        {
            // Replace the 4th item with fixed suggestion if it's not already a fixed suggestion
            result[3] = fixedSuggestion;
        }
        
        // Ensure we have exactly 4 items (truncate if more)
        if (result.Count > 4)
        {
            // Keep first 3 plus the fixed suggestion
            var first3 = result.Take(3).ToList();
            first3.Add(hasFixedSuggestion ? result.First(s => IsFixedSuggestion(s)) : fixedSuggestion);
            return first3;
        }
        
        return result;
    }
    
    private static string GetFixedSuggestionForLanguage(string? language)
    {
        if (string.IsNullOrEmpty(language))
            return English;
            
        return language.ToLowerInvariant() switch
        {
            "zh" or "zh-cn" or "chinese" or "simplified chinese" => SimplifiedChinese,
            "zh-tw" or "zh-hk" or "traditional chinese" => TraditionalChinese,
            "es" or "spanish" => Spanish,
            _ => English
        };
    }
    
    private static bool IsFixedSuggestion(string suggestion)
    {
        if (string.IsNullOrEmpty(suggestion))
            return false;
            
        var normalized = suggestion.Trim().ToLowerInvariant();
        return normalized == English.ToLowerInvariant() ||
               normalized == SimplifiedChinese ||
               normalized == TraditionalChinese ||
               normalized == Spanish.ToLowerInvariant();
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

