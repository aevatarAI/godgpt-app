using System.Text.Json;
using System.Text.RegularExpressions;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;

namespace GodGPT.GAgents.Awakening.Helpers;

/// <summary>
/// Helper class for parsing LLM responses into awakening content.
/// Contains pure functions for response parsing and JSON extraction.
/// </summary>
public static class AwakeningParserHelper
{
    /// <summary>
    /// Parse awakening response from LLM output
    /// </summary>
    /// <param name="responseContent">Raw response content</param>
    /// <param name="language">Target language</param>
    /// <param name="timestamp">Timestamp for the result</param>
    /// <param name="logger">Optional logger for debugging</param>
    /// <returns>Parsed awakening result</returns>
    public static AwakeningResultDto ParseAwakeningResponse(
        string responseContent, 
        VoiceLanguageEnum language, 
        long timestamp,
        ILogger? logger = null)
    {
        try
        {
            // Clean up the response content - remove markdown code blocks
            var cleanedContent = CleanJsonContent(responseContent);
            
            // Log the cleaned content for debugging
            logger?.LogDebug("Original response: {OriginalContent}", responseContent);
            logger?.LogDebug("Cleaned content for JSON parsing: {CleanedContent}", cleanedContent);
            
            // Try to parse JSON response format
            var jsonResponse = JsonSerializer.Deserialize<Dictionary<string, object>>(cleanedContent);
            
            if (jsonResponse != null && 
                jsonResponse.TryGetValue("level", out var levelObj) && 
                jsonResponse.TryGetValue("message", out var messageObj))
            {
                // Handle JsonElement conversion properly
                var level = ExtractIntFromJsonElement(levelObj);
                var message = ExtractStringFromJsonElement(messageObj);

                // Validate that we have a meaningful message (following "empty is empty" principle)
                if (string.IsNullOrWhiteSpace(message))
                {
                    return new AwakeningResultDto
                    {
                        IsSuccess = false,
                        ErrorMessage = "Empty or invalid awakening message in JSON response"
                    };
                }

                // Validate level range
                if (level < 1 || level > 10)
                {
                    level = Math.Max(1, Math.Min(10, level));
                }

                return new AwakeningResultDto
                {
                    IsSuccess = true,
                    AwakeningLevel = level,
                    AwakeningMessage = message,
                    Timestamp = timestamp
                };
            }
        }
        catch (JsonException ex)
        {
            logger?.LogWarning("JSON parsing failed: {Error}", ex.Message);
            // If JSON parsing fails, try to extract level and message from text
        }

        // Fallback: try to parse from natural text response
        return ParseAwakeningFromText(responseContent, language, timestamp, logger);
    }

    /// <summary>
    /// Parse awakening content from natural text response
    /// </summary>
    /// <param name="responseContent">Raw response content</param>
    /// <param name="language">Target language</param>
    /// <param name="timestamp">Timestamp for the result</param>
    /// <param name="logger">Optional logger for debugging</param>
    /// <returns>Parsed awakening result</returns>
    public static AwakeningResultDto ParseAwakeningFromText(
        string responseContent, 
        VoiceLanguageEnum language, 
        long timestamp,
        ILogger? logger = null)
    {
        try
        {
            // Extract level (look for numbers 1-10)
            var levelMatch = Regex.Match(responseContent, @"\b([1-9]|10)\b");
            if (!levelMatch.Success)
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening level found in LLM response"
                };
            }
            
            var level = int.Parse(levelMatch.Value);

            // Extract message (take the longest meaningful sentence)
            var sentences = responseContent.Split(new[] { '.', '!', '?', '。', '！', '？' }, StringSplitOptions.RemoveEmptyEntries);
            var message = sentences
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 10)
                .OrderByDescending(s => s.Length)
                .FirstOrDefault()?.Trim();

            // If no valid message found, return failure (following "empty is empty" principle)
            if (string.IsNullOrWhiteSpace(message))
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening message found in LLM response"
                };
            }

            return new AwakeningResultDto
            {
                IsSuccess = true,
                AwakeningLevel = level,
                AwakeningMessage = message,
                Timestamp = timestamp
            };
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to parse awakening response from text");
            return new AwakeningResultDto
            {
                IsSuccess = false,
                ErrorMessage = "Failed to parse awakening content from LLM response"
            };
        }
    }

    /// <summary>
    /// Clean JSON content by removing markdown code block markers
    /// </summary>
    /// <param name="content">Raw content</param>
    /// <returns>Cleaned JSON content</returns>
    public static string CleanJsonContent(string content)
    {
         // Clean up the response content - remove markdown code blocks
        var cleanedContent = content.Trim();

        // Remove [SUGGESTIONS]...[/SUGGESTIONS] tags that may be appended by LLM
        cleanedContent = Regex.Replace(cleanedContent, @"\s*\[SUGGESTIONS\].*?\[/SUGGESTIONS\]\s*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        
        // Remove [SUGGESTIONS]...[/SUGGESTIONS] tags that may be appended by LLM
        cleanedContent = Regex.Replace(cleanedContent, @"\s*\[SUGGESTIONS\].*?\[/SUGGESTIONS\]\s*", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Remove markdown code block markers more robustly
        // Handle cases like "```json\n" or "```\n"
        if (cleanedContent.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
        {
            cleanedContent = cleanedContent.Substring(7); // Remove "```json"
        }
        else if (cleanedContent.StartsWith("```"))
        {
            cleanedContent = cleanedContent.Substring(3); // Remove "```"
        }
        
        // Remove trailing ``` and any whitespace/newlines
        if (cleanedContent.EndsWith("```"))
        {
            cleanedContent = cleanedContent.Substring(0, cleanedContent.Length - 3);
        }
        
        // Clean up any remaining whitespace and newlines
        return cleanedContent.Trim();
    }

    /// <summary>
    /// Extract integer value from JsonElement or object
    /// </summary>
    /// <param name="obj">Object to extract from</param>
    /// <returns>Extracted integer value, defaults to 1</returns>
    public static int ExtractIntFromJsonElement(object? obj)
    {
        if (obj is JsonElement element)
        {
            if (element.TryGetInt32(out var intValue))
            {
                return intValue;
            }
            // Try parsing as string in case it's a string number
            if (element.ValueKind == JsonValueKind.String && 
                int.TryParse(element.GetString(), out var parsedValue))
            {
                return parsedValue;
            }
        }
        else if (obj != null)
        {
            // Handle regular object conversion
            if (int.TryParse(obj.ToString(), out var convertedValue))
            {
                return convertedValue;
            }
        }
        
        // Default fallback
        return 1;
    }

    /// <summary>
    /// Extract string value from JsonElement or object
    /// </summary>
    /// <param name="obj">Object to extract from</param>
    /// <returns>Extracted string value</returns>
    public static string? ExtractStringFromJsonElement(object? obj)
    {
        if (obj is JsonElement element)
        {
            return element.ValueKind == JsonValueKind.String ? element.GetString() : element.ToString();
        }
        
        return obj?.ToString();
    }
}

