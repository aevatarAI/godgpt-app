using Aevatar.Agents.Abstractions.Context;

namespace Aevatar.Application.Grains.Agents.ChatManager.Common;

/// <summary>
/// GodGPT language enumeration for chat sessions
/// </summary>
public enum GodGPTLanguage
{
    /// <summary>
    /// English language
    /// </summary>
    English = 0,

    /// <summary>
    /// Traditional Chinese language
    /// </summary>
    TraditionalChinese = 1,

    /// <summary>
    /// Spanish language
    /// </summary>
    Spanish = 2,
    CN = 3
}

/// <summary>
/// Helper class for retrieving GodGPT language from agent context.
/// Provides both IAgentContext-based methods (preferred) and legacy RequestContext methods.
/// </summary>
public static class GodGPTLanguageHelper
{
    private const string GodGPTLanguageKey = "GodGPTLanguage";
    private const string IsCNKey = "IsCN";

    #region IAgentContext-based methods (preferred)

    /// <summary>
    /// Check if client is in CN from IAgentContext.
    /// </summary>
    /// <param name="context">Agent context</param>
    /// <returns>True if client is in CN</returns>
    public static bool CheckClientIsCN(IAgentContext? context)
    {
        return context?.Get(AgentContextKeys.IsCN) ?? false;
    }

    /// <summary>
    /// Gets the GodGPT language from IAgentContext.
    /// </summary>
    /// <param name="context">Agent context</param>
    /// <returns>GodGPTLanguage enum value, defaults to English</returns>
    public static GodGPTLanguage GetGodGPTLanguage(IAgentContext? context)
    {
        var languageString = context?.Get(GodGPTContextKeys.GodGPTLanguage);
        if (!string.IsNullOrEmpty(languageString) &&
            Enum.TryParse<GodGPTLanguage>(languageString, true, out var language))
        {
            return language;
        }

        return GodGPTLanguage.English;
    }

    /// <summary>
    /// Sets the GodGPT language in IAgentContext.
    /// </summary>
    /// <param name="context">Agent context</param>
    /// <param name="language">Language to set</param>
    public static void SetGodGPTLanguage(IAgentContext? context, GodGPTLanguage language)
    {
        context?.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
    }

    /// <summary>
    /// Sets IsCN flag in IAgentContext.
    /// </summary>
    /// <param name="context">Agent context</param>
    /// <param name="isCN">IsCN value</param>
    public static void SetIsCN(IAgentContext? context, bool isCN)
    {
        context?.Set(AgentContextKeys.IsCN, isCN);
    }

    #endregion

    #region Legacy RequestContext methods (for backward compatibility)

    /// <summary>
    /// [Deprecated] Check if client is in CN from Orleans RequestContext.
    /// Use CheckClientIsCN(IAgentContext) instead.
    /// </summary>
    [Obsolete("Use CheckClientIsCN(IAgentContext) instead")]
    public static bool CheckClientIsCNFromContext()
    {
        try
        {
            var context = RequestContext.Get(IsCNKey);
            if (context != null && context is bool isCN)
            {
                return isCN;
            }
        }
        catch (Exception)
        {
            // Log error if needed, but return default
            return false;
        }

        return false;
    }

    /// <summary>
    /// [Deprecated] Gets the GodGPT language from Orleans RequestContext.
    /// Use GetGodGPTLanguage(IAgentContext) instead.
    /// </summary>
    [Obsolete("Use GetGodGPTLanguage(IAgentContext) instead")]
    public static GodGPTLanguage GetGodGPTLanguageFromContext()
    {
        try
        {
            var context = RequestContext.Get(GodGPTLanguageKey);
            if (context != null && context is string languageString)
            {
                if (Enum.TryParse<GodGPTLanguage>(languageString, true, out var language))
                {
                    return language;
                }
            }
        }
        catch (Exception)
        {
            // Log error if needed, but return default English
        }

        return GodGPTLanguage.English;
    }

    /// <summary>
    /// [Deprecated] Sets the GodGPT language in Orleans RequestContext.
    /// Use SetGodGPTLanguage(IAgentContext, GodGPTLanguage) instead.
    /// </summary>
    [Obsolete("Use SetGodGPTLanguage(IAgentContext, GodGPTLanguage) instead")]
    public static void SetGodgptLanguageInContext(GodGPTLanguage language)
    {
        try
        {
            RequestContext.Set(GodGPTLanguageKey, language.ToString());
        }
        catch (Exception)
        {
            // Handle exception if needed
        }
    }

    #endregion
    public static string AppendLanguagePrompt(this string message, GodGPTLanguage language)
    {
        var promptMsg = message;

        /*promptMsg += language switch
        {
            GodGPTLanguage.English => ".Requirement: Please reply in English.",
            GodGPTLanguage.TraditionalChinese => ".Requirement: Please reply in Chinese.",
            GodGPTLanguage.Spanish => ".Requirement: Please reply in Spanish.",
            _ => ".Requirement: Please reply in English."
        };*/

        return promptMsg;
    }
    
    /// <summary>
    /// Gets the English name for the specified GodGPT language
    /// </summary>
    /// <param name="language">The GodGPT language enum value</param>
    /// <returns>English name of the language</returns>
    public static string GetLanguageEnglishName(GodGPTLanguage language)
    {
        return language switch
        {
            GodGPTLanguage.English => "English",
            GodGPTLanguage.TraditionalChinese => "Traditional Chinese",
            GodGPTLanguage.Spanish => "Spanish",
            GodGPTLanguage.CN => "Chinese",
            _ => "English"
        };
    }
} 