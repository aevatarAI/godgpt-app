using System.Text;
using GodGPT.GAgents.Common.Constants;
using GodGPT.GAgents.SpeechChat;

namespace Aevatar.Application.Grains.Common.Helpers;

/// <summary>
/// Contains pure utility functions for text processing in chat functionality.
/// These methods are stateless and can be used across different agents.
/// </summary>
public static class TextProcessingUtils
{
    #region Meaningful Content Detection

    /// <summary>
    /// Checks if the text contains meaningful content (letters or Chinese characters)
    /// </summary>
    /// <param name="text">Text to check</param>
    /// <returns>True if text contains meaningful content, false otherwise</returns>
    public static bool HasMeaningfulContent(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        // Remove all punctuation and check if there's actual content
        var cleanText = ChatRegexPatterns.NonWordChars.Replace(text, "");
        return cleanText.Length > 0; // At least one letter or Chinese character
    }

    #endregion

    #region Text Cleaning for Speech Synthesis

    /// <summary>
    /// Cleans text for speech synthesis by removing markdown syntax and emojis
    /// </summary>
    /// <param name="text">Text to clean</param>
    /// <param name="language">Language for text replacement</param>
    /// <returns>Clean text suitable for speech synthesis</returns>
    public static string CleanTextForSpeech(string text, VoiceLanguageEnum language = VoiceLanguageEnum.English)
    {
        if (string.IsNullOrEmpty(text))
            return text;

        var cleanText = text;

        // Remove markdown links but keep link text
        cleanText = ChatRegexPatterns.MarkdownLink.Replace(cleanText, "$1");

        // Remove bold and italic formatting
        cleanText = ChatRegexPatterns.MarkdownBold.Replace(cleanText, "$1");
        cleanText = ChatRegexPatterns.MarkdownItalic.Replace(cleanText, "$1");

        // Remove strikethrough formatting
        cleanText = ChatRegexPatterns.MarkdownStrikethrough.Replace(cleanText, "$1");

        // Remove header formatting
        cleanText = ChatRegexPatterns.MarkdownHeader.Replace(cleanText, "$1");

        // Replace code blocks with speech-friendly text
        cleanText = ChatRegexPatterns.MarkdownCodeBlock.Replace(cleanText,
            language == VoiceLanguageEnum.Chinese ? "代码块" : "code block");

        // Remove inline code formatting but keep content
        cleanText = ChatRegexPatterns.MarkdownInlineCode.Replace(cleanText, "$1");

        // Remove table formatting
        cleanText = ChatRegexPatterns.MarkdownTable.Replace(cleanText, " ");

        // Remove emojis completely (they don't speech-synthesize well)
        cleanText = ChatRegexPatterns.Emoji.Replace(cleanText, "");

        // Remove multiple spaces and special markdown symbols
        cleanText = cleanText.Replace("**", "")
            .Replace("__", "")
            .Replace("~~", "")
            .Replace("---", "")
            .Replace("***", "")
            .Replace("===", "")
            .Replace("```", "")
            .Replace(">>>", "")
            .Replace("<<<", "");

        // Remove excessive whitespace
        cleanText = ChatRegexPatterns.WhitespaceNormalize.Replace(cleanText, " ").Trim();

        return cleanText;
    }

    #endregion

    #region Sentence Extraction

    /// <summary>
    /// Extracts complete sentences from accumulated text and removes them from the accumulator
    /// </summary>
    /// <param name="accumulatedText">The full accumulated text</param>
    /// <param name="textAccumulator">The accumulator to update</param>
    /// <param name="isLastChunk">Whether this is the last chunk of the stream</param>
    /// <returns>Complete sentence if found, otherwise null</returns>
    public static string ExtractCompleteSentence(string accumulatedText, StringBuilder textAccumulator, bool isLastChunk = false)
    {
        if (string.IsNullOrEmpty(accumulatedText))
        {
            return null;
        }

        var hasMeaningfulContent = HasMeaningfulContent(accumulatedText);

        // Enhanced logic: return any non-empty text when isLastChunk = true
        if (isLastChunk)
        {
            var trimmedText = accumulatedText.Trim();
            if (!string.IsNullOrEmpty(trimmedText))
            {
                textAccumulator.Clear();
                return trimmedText;
            }
        }

        // Special handling for short text: return directly if meaningful and <= 6 characters
        if (accumulatedText.Length <= 6 && hasMeaningfulContent)
        {
            var shortText = accumulatedText.Trim();
            textAccumulator.Clear();
            return shortText;
        }

        var extractIndex = -1;

        // Look for complete sentence endings
        for (var i = accumulatedText.Length - 1; i >= 0; i--)
        {
            if (VoiceChatConstants.SentenceEnders.Contains(accumulatedText[i]))
            {
                // Only check if there's meaningful content, no length restriction
                var potentialSentence = accumulatedText.Substring(0, i + 1);
                if (HasMeaningfulContent(potentialSentence))
                {
                    extractIndex = i;
                    break;
                }
            }
        }

        if (extractIndex == -1)
            return null;

        // Extract complete sentence
        var completeSentence = accumulatedText.Substring(0, extractIndex + 1).Trim();
        if (string.IsNullOrEmpty(completeSentence))
            return null;

        // Remove processed text from accumulator
        var remainingText = accumulatedText.Substring(extractIndex + 1);
        textAccumulator.Clear();
        textAccumulator.Append(remainingText);

        return completeSentence;
    }

    #endregion

    #region Numbered Item Extraction

    /// <summary>
    /// Extract numbered items from text (e.g., "1. item", "2. item", etc.)
    /// </summary>
    /// <param name="text">Text containing numbered items</param>
    /// <returns>List of extracted items</returns>
    public static List<string> ExtractNumberedItems(string text)
    {
        var items = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return items;
        }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();
            // Match numbered items like "1. content" or "1) content" using precompiled regex
            var match = ChatRegexPatterns.NumberedItem.Match(trimmedLine);
            if (match.Success)
            {
                var item = match.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(item))
                {
                    items.Add(item);
                }
            }
        }

        return items;
    }

    #endregion
}

