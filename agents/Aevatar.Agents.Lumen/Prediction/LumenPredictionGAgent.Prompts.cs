using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.Agents.Lumen.Services;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - Prompt building methods for LLM generation
/// Supports Daily, Yearly, and Lifetime prediction types
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Language Configuration

    private static readonly Dictionary<string, string> LanguageMap = new()
    {
        { "en", "English" },
        { "zh-tw", "繁體中文" },
        { "zh", "简体中文" },
        { "es", "Español" }
    };

    #endregion

    #region Prompt Building

    /// <summary>
    /// Build prediction prompt for AI (single language generation)
    /// </summary>
    private string BuildPredictionPrompt(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        PredictionType type,
        string targetLanguage = "en",
        string? moonSign = null,
        string? risingSign = null)
    {
        // Build user info line dynamically
        var userInfoParts = new List<string>();
        
        // Birth date
        if (userInfo.BirthDate != null)
        {
            var birthDateStr = $"Birth: {userInfo.BirthDate.Year}-{userInfo.BirthDate.Month:00}-{userInfo.BirthDate.Day:00}";
            // Default value is CALENDAR_SOLAR = 0
            var calendarType = userInfo.CalendarType == CalendarTypeEnum.CalendarLunar ? "Lunar" : "Solar";
            birthDateStr += $" ({calendarType} calendar)";
            userInfoParts.Add(birthDateStr);
        }
        
        // Gender
        var genderStr = userInfo.Gender switch
        {
            GenderEnum.GenderMale => "Male",
            GenderEnum.GenderFemale => "Female",
            _ => "Unknown"
        };
        userInfoParts.Add($"Gender: {genderStr}");
        
        // Occupation (optional)
        if (!string.IsNullOrWhiteSpace(userInfo.Occupation))
        {
            userInfoParts.Add($"Occupation: {userInfo.Occupation}");
        }
        
        var userInfoLine = string.Join(", ", userInfoParts);
        
        // Calculate display name
        var displayName = GetDisplayName(userInfo.FullName, targetLanguage);
        
        // Pre-calculate astrological values
        var currentYear = DateTime.UtcNow.Year;
        var birthYear = userInfo.BirthDate?.Year ?? currentYear;
        var birthDate = userInfo.BirthDate != null 
            ? new DateOnly(userInfo.BirthDate.Year, userInfo.BirthDate.Month, userInfo.BirthDate.Day)
            : DateOnly.FromDateTime(DateTime.UtcNow);
        
        // Western Zodiac
        string sunSign = LumenCalculator.CalculateZodiacSign(birthDate);
        moonSign ??= sunSign;
        risingSign ??= sunSign;
        
        // Chinese Zodiac & Element
        var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
        
        // Get language name
        var languageName = LanguageMap.GetValueOrDefault(targetLanguage, "English");
        
        // Build language instruction
        var languageInstruction = BuildLanguageInstruction(targetLanguage, languageName);
        var singleLanguagePrefix = BuildSingleLanguagePrefix(languageInstruction, displayName);
        
        return type switch
        {
            PredictionType.PredictionDaily => BuildDailyPrompt(
                singleLanguagePrefix, userInfoLine, displayName, sunSign, birthYearZodiac,
                predictionDate, targetLanguage, birthDate),
            PredictionType.PredictionYearly => BuildYearlyPrompt(
                singleLanguagePrefix, userInfoLine, displayName, sunSign, birthYear, birthYearZodiac,
                predictionDate, targetLanguage),
            PredictionType.PredictionLifetime => BuildLifetimePrompt(
                singleLanguagePrefix, userInfoLine, displayName, sunSign, moonSign, risingSign,
                birthYear, birthYearZodiac, birthYearAnimal, birthYearElement,
                predictionDate, targetLanguage),
            _ => throw new ArgumentException($"Unsupported prediction type: {type}")
        };
    }

    /// <summary>
    /// Build language instruction based on target language
    /// </summary>
    private static string BuildLanguageInstruction(string targetLanguage, string languageName)
    {
        return targetLanguage switch
        {
            "zh" => @"===== 语言要求 =====
⚠️ 重要：所有字段值必须用简体中文书写（字段名保持英文）。
✓ 正确示例：dayTitle	反思与和谐之日 | career	专注于团队协作
✗ 错误示例：dayTitle	Day of Reflection | career	Focus on teamwork

⚠️ 例外：以下字段使用英文标准名称（便于后端解析）：
- card_name: 使用英文塔罗牌名称（如 ""The Fool"", ""The Moon"", ""The Star""）
- card_orient: 使用英文（""Upright"" 或 ""Reversed""）
- lucky_stone: 使用英文宝石名称（如 ""Amethyst"", ""Rose Quartz""）
===================",
            "zh-tw" => @"===== 語言要求 =====
⚠️ 重要：所有字段值必須用繁體中文書寫（字段名保持英文）。
✓ 正確示例：dayTitle	反思與和諧之日 | career	專注於團隊協作
✗ 錯誤示例：dayTitle	Day of Reflection | career	Focus on teamwork

⚠️ 例外：以下字段使用英文標準名稱（便於後端解析）：
- card_name: 使用英文塔羅牌名稱（如 ""The Fool"", ""The Moon"", ""The Star""）
- card_orient: 使用英文（""Upright"" 或 ""Reversed""）
- lucky_stone: 使用英文寶石名稱（如 ""Amethyst"", ""Rose Quartz""）
===================",
            "es" => @"===== REQUISITO DE IDIOMA =====
Todos los valores de campo deben estar en ESPAÑOL (los nombres de campo permanecen en inglés).

⚠️ Excepciones: Los siguientes campos usan nombres estándar en INGLÉS:
- card_name: Use nombres de tarot en inglés (ej. ""The Fool"", ""The Moon"")
- card_orient: Use inglés (""Upright"" o ""Reversed"")
- lucky_stone: Use nombres de gemas en inglés (ej. ""Amethyst"")
================================",
            _ => $@"===== LANGUAGE REQUIREMENT =====
All field VALUES must be in {languageName} (field names stay in English).

⚠️ Exception: The following fields use standard ENGLISH names (for backend parsing):
- card_name: Use English tarot card names (e.g. ""The Fool"", ""The Moon"")
- card_orient: Use English (""Upright"" or ""Reversed"")
- lucky_stone: Use English gem names (e.g. ""Amethyst"", ""Rose Quartz"")
================================"
        };
    }

    /// <summary>
    /// Build single language prefix with format requirements
    /// </summary>
    private static string BuildSingleLanguagePrefix(string languageInstruction, string displayName)
    {
        return $@"{languageInstruction}

Guidelines:
- When addressing the user, use the provided ""Display Name"" (if given)
- Avoid making up names - use only the provided Display Name or second-person pronouns

FORMAT REQUIREMENT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER (\t) between field name and value
- Arrays: item1|item2|item3 (pipe separator)
- NO JSON, NO markdown, NO extra text
- Start immediately with the data

";
    }

    /// <summary>
    /// Get display name based on language
    /// </summary>
    private static string GetDisplayName(string fullName, string language)
    {
        if (string.IsNullOrWhiteSpace(fullName))
            return "";
        
        return LumenCalculator.GetDisplayName(fullName, language);
    }

    #endregion

    #region Daily Prompt

    private string BuildDailyPrompt(
        string prefix,
        string userInfoLine,
        string displayName,
        string sunSign,
        string birthYearZodiac,
        DateOnly predictionDate,
        string targetLanguage,
        DateOnly birthDate)
    {
        // Determine zodiac element
        var zodiacElement = sunSign switch
        {
            "Aries" or "Leo" or "Sagittarius" => "Fire",
            "Taurus" or "Virgo" or "Capricorn" => "Earth",
            "Gemini" or "Libra" or "Aquarius" => "Air",
            "Cancer" or "Scorpio" or "Pisces" => "Water",
            _ => "Fire"
        };
        
        // Calculate lucky number for daily variation
        var dailyEnergyNumber = LuckyNumberService.CalculateLuckyNumber(birthDate, predictionDate, targetLanguage);
        
        // Get language-specific descriptions
        var descriptions = GetDailyDescriptions(targetLanguage, displayName);
        
        return prefix + $@"Generate a daily reflection entry.
Date: {predictionDate:yyyy-MM-dd}
User: {userInfoLine}

========== CONTEXT VALUES ==========
Display Name: {displayName}
Sun Sign: {sunSign}
Element: {zodiacElement}
Birth Year Zodiac: {birthYearZodiac}
Today's Numerology Energy: {dailyEnergyNumber.Digit}

========== OUTPUT FORMAT (TSV) ==========

=== 1. THEME ===
daily_theme_title	{descriptions["dayTitle"]}

=== 2. INSIGHTS ===
tarot_card_name	{descriptions["cardName"]}
tarot_card_essence	{descriptions["cardEssence"]}
tarot_card_orientation	{descriptions["cardOrient"]}

path_adjective	{descriptions["pathType"]}
path_greeting	{descriptions["pathIntro"]}
path_wisdom	{descriptions["pathDetail"]}

reflection_career	{descriptions["career"]}
reflection_relationships	{descriptions["love"]}
reflection_wealth	{descriptions["prosperity"]}
reflection_wellbeing	{descriptions["wellness"]}

daily_takeaway	{descriptions["takeaway"]}

=== 3. RESONANCE ===
crystal_stone_id	{descriptions["stone"]}
crystal_power	{descriptions["stonePower"]}
crystal_usage	{descriptions["stoneUse"]}

affirmation_poetic	{descriptions["spell"]}
affirmation_text	{descriptions["spellWords"]}
affirmation_intent	{descriptions["spellIntent"]}

=== 4. GUIDANCE ===
guidance_metaphor	{descriptions["fortuneTitle"]}
guidance_suggestions	{descriptions["fortuneDo"]}
guidance_mindful_of	{descriptions["fortuneAvoid"]}
guidance_tip	{descriptions["fortuneTip"]}

IMPORTANT:
- Output strictly valid TSV.
- Start immediately with `daily_theme_title`.
- Array values: use | separator.
";
    }

    /// <summary>
    /// Get language-specific descriptions for Daily prompt
    /// </summary>
    private static Dictionary<string, string> GetDailyDescriptions(string lang, string displayName)
    {
        return new Dictionary<string, string>
        {
            ["dayTitle"] = lang switch
            {
                "zh" => "今日主题 (如：水与深度之日)",
                "zh-tw" => "今日主題 (如：水與深度之日)",
                "es" => "El Día de [palabra1] y [palabra2]",
                _ => "The Day of [word1] and [word2]"
            },
            ["cardName"] = lang switch
            {
                "zh" or "zh-tw" => "[保留英文原名] (如 \"The Fool\")",
                "es" => "[Usar nombre en INGLÉS]",
                _ => "[Use ENGLISH Name]"
            },
            ["cardEssence"] = lang switch
            {
                "zh" or "zh-tw" => "1-2个中文关键词",
                "es" => "1-2 palabras clave",
                _ => "1-2 words essence"
            },
            ["cardOrient"] = lang switch
            {
                "zh" or "zh-tw" => "[保留英文: \"Upright\" 或 \"Reversed\"]",
                _ => "[\"Upright\" or \"Reversed\"]"
            },
            ["pathType"] = lang switch
            {
                "zh" or "zh-tw" => "1个形容词",
                "es" => "1 adjetivo",
                _ => "1 adjective"
            },
            ["pathIntro"] = lang switch
            {
                "zh" or "zh-tw" => $"你好 {displayName} (15-25字)",
                "es" => $"Hola {displayName} (15-25 palabras)",
                _ => $"Hi {displayName} (15-25 words)"
            },
            ["pathDetail"] = lang switch
            {
                "zh" or "zh-tw" => "30-40字，反思与智慧指引",
                "es" => "30-40 palabras reflexivas",
                _ => "30-40 words of reflective wisdom"
            },
            ["career"] = lang switch { "zh" or "zh-tw" => "10-20字", "es" => "10-20 palabras", _ => "10-20 words" },
            ["love"] = lang switch { "zh" or "zh-tw" => "10-20字", "es" => "10-20 palabras", _ => "10-20 words" },
            ["prosperity"] = lang switch { "zh" or "zh-tw" => "10-20字", "es" => "10-20 palabras", _ => "10-20 words" },
            ["wellness"] = lang switch { "zh" or "zh-tw" => "10-15字", "es" => "10-15 palabras", _ => "10-15 words" },
            ["takeaway"] = lang switch
            {
                "zh" or "zh-tw" => $"15-25字，{displayName}，你的...",
                "es" => $"15-25 palabras, {displayName}, tu...",
                _ => $"15-25 words, {displayName}, your..."
            },
            ["stone"] = lang switch
            {
                "zh" or "zh-tw" => "[保留英文ID] (如 \"Amethyst\")",
                "es" => "[Usar INGLÉS]",
                _ => "[ENGLISH Name]"
            },
            ["stonePower"] = lang switch { "zh" or "zh-tw" => "15-20字", "es" => "15-20 palabras", _ => "15-20 words" },
            ["stoneUse"] = lang switch { "zh" or "zh-tw" => "20-30字", "es" => "20-30 palabras", _ => "20-30 words" },
            ["spell"] = lang switch { "zh" or "zh-tw" => "2个字", "es" => "2 palabras", _ => "2 words" },
            ["spellWords"] = lang switch { "zh" or "zh-tw" => "20-30字", "es" => "20-30 palabras", _ => "20-30 words" },
            ["spellIntent"] = lang switch { "zh" or "zh-tw" => "10-12字", "es" => "10-12 palabras", _ => "10-12 words" },
            ["fortuneTitle"] = lang switch { "zh" or "zh-tw" => "4-8字", "es" => "4-8 palabras", _ => "4-8 words" },
            ["fortuneDo"] = lang switch
            {
                "zh" or "zh-tw" => "5条具体行动短语 (竖线分隔)",
                "es" => "5 frases de acción (separado por |)",
                _ => "5 action phrases (separated by |)"
            },
            ["fortuneAvoid"] = lang switch
            {
                "zh" or "zh-tw" => "5条具体注意事项 (竖线分隔)",
                "es" => "5 cosas a evitar (separado por |)",
                _ => "5 things to avoid (separated by |)"
            },
            ["fortuneTip"] = lang switch { "zh" or "zh-tw" => "10-15字", "es" => "10-15 palabras", _ => "10-15 words" }
        };
    }

    #endregion
}

