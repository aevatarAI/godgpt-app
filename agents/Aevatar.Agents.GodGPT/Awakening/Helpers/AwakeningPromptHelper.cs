using System.Text;
using Aevatar.Application.Grains.Common;
using Aevatar.GAgents.AI.Common;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.SpeechChat;

namespace GodGPT.GAgents.Awakening.Helpers;

/// <summary>
/// Helper class for building prompts for awakening content generation.
/// Contains pure functions for prompt construction and token calculation.
/// </summary>
public static class AwakeningPromptHelper
{
    /// <summary>
    /// Build the complete prompt for awakening generation
    /// </summary>
    /// <param name="sessionContents">Session content list</param>
    /// <param name="language">Target language</param>
    /// <param name="options">Awakening options</param>
    /// <returns>Complete prompt string</returns>
    public static string BuildPrompt(
        List<SessionContentDto> sessionContents, 
        VoiceLanguageEnum language,
        AwakeningOptions options)
    {
        var template = "@" + options.PromptTemplate + "Format your response as JSON: {{\"level\": number, \"message\": \"string\"}}";
        var basePrompt = template.Replace("{USER_CONTEXT}", BuildUserContext(sessionContents, language, options));
        
        // Check multi-language switch
        if (options.EnableLanguageSpecificPrompt)
        {
            var languageInstructions = options.LanguageInstructions;
            if (languageInstructions.TryGetValue(language, out var instruction))
            {
                basePrompt += $"\n\n{instruction}";
            }
        }
        
        return basePrompt;
    }

    /// <summary>
    /// Build user context from session contents with token limiting
    /// </summary>
    /// <param name="sessionContents">Session content list</param>
    /// <param name="language">Target language</param>
    /// <param name="options">Awakening options</param>
    /// <returns>User context string</returns>
    public static string BuildUserContext(
        List<SessionContentDto> sessionContents, 
        VoiceLanguageEnum language,
        AwakeningOptions options)
    {
        if (sessionContents.IsNullOrEmpty())
        {
            return "No recent chat messages available.";
        }

        var context = new StringBuilder();

        foreach (var sessionContent in sessionContents)
        {
            foreach (var message in sessionContent.Messages)
            {
                var role = message.ChatRole == ChatRole.User ? "User" : "Assistant";
                context.AppendLine($"{role}: {message.Content}");
            }
        }

        // Calculate reserved tokens for fixed content
        int reservedTokens = CalculateReservedTokens(language, options);
        
        return TokenHelper.TruncateMessageIfExceedsLimit(context.ToString(), reservedTokens);
    }

    /// <summary>
    /// Calculate reserved tokens for prompt template and language instructions
    /// </summary>
    /// <param name="language">Target language</param>
    /// <param name="options">Awakening options</param>
    /// <returns>Reserved token count</returns>
    public static int CalculateReservedTokens(VoiceLanguageEnum language, AwakeningOptions options)
    {
        // Calculate tokens for prompt template and fixed content
        var templateContent = "@" + options.PromptTemplate + "Format your response as JSON: {{\"level\": number, \"message\": \"string\"}}";
        int templateTokens = TokenHelper.EstimateTokenCount(templateContent);
        
        // Calculate tokens for language-specific instructions if enabled
        int languageTokens = 0;
        if (options.EnableLanguageSpecificPrompt)
        {
            var languageInstructions = options.LanguageInstructions;
            if (languageInstructions.TryGetValue(language, out var instruction))
            {
                languageTokens = TokenHelper.EstimateTokenCount($"\n\n{instruction}");
            }
        }
        
        // Add some safety margin (50% of calculated tokens)
        int totalReserved = templateTokens + languageTokens;
        return (int)(totalReserved * 1.5);
    }
}

