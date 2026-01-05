using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - Yearly and Lifetime prompt building
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Yearly Prompt

    private string BuildYearlyPrompt(
        string prefix,
        string userInfoLine,
        string displayName,
        string sunSign,
        int birthYear,
        string birthYearZodiac,
        DateOnly predictionDate,
        string targetLanguage)
    {
        var yearlyYear = predictionDate.Year;
        var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
        var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
        
        // Translate zodiac signs for prompt
        var sunSignTranslated = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
        var birthYearZodiacTranslated = TranslationHelpers.TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
        var yearlyTaishuiTranslated = TranslationHelpers.TranslateTaishuiRelationship(yearlyTaishui, targetLanguage);
        
        var descriptions = GetYearlyDescriptions(targetLanguage);
        
        return prefix + $@"Create a yearly astrological insight for {yearlyYear}.
User: {userInfoLine}

========== CONTEXT VALUES ==========
Sun Sign: {sunSignTranslated}
Birth Year Zodiac: {birthYearZodiacTranslated}
Yearly Year ({yearlyYear}): {yearlyYearZodiac}
Taishui Relationship: {yearlyTaishuiTranslated}

FORMAT (TSV - Tab-Separated Values):
Each field on ONE line: key	value

Output format (TSV):
astro_overlay	{sunSign} Sun · [2-3 word archetype] — {yearlyYear} [Key themes]
theme_title	{descriptions["themeTitle"]}
theme_glance	{descriptions["themeGlance"]}
theme_detail	{descriptions["themeDetail"]}

# Career & Purpose
career_score	[1-5]
career_tag	{descriptions["tag"]}
career_do	{descriptions["doAction"]}
career_avoid	{descriptions["avoid"]}
career_detail	{descriptions["detail"]}

# Relationships & Love
love_score	[1-5]
love_tag	{descriptions["tag"]}
love_do	{descriptions["doAction"]}
love_avoid	{descriptions["avoid"]}
love_detail	{descriptions["detail"]}

# Wealth & Prosperity
prosperity_score	[1-5]
prosperity_tag	{descriptions["tag"]}
prosperity_do	{descriptions["doAction"]}
prosperity_avoid	{descriptions["avoid"]}
prosperity_detail	{descriptions["detail"]}

# Wellness & Balance
wellness_score	[1-5]
wellness_tag	{descriptions["tag"]}
wellness_do	{descriptions["doAction"]}
wellness_avoid	{descriptions["avoid"]}
wellness_detail	{descriptions["detail"]}

# Annual Mantra
mantra	{descriptions["mantra"]}

FORMAT REQUIREMENTS:
- Return TSV format: one field per line with TAB between field name and value
- Array values: use | separator
- Scores: integer 1-5
- Return only the data (no markdown wrappers)
";
    }

    private static Dictionary<string, string> GetYearlyDescriptions(string lang)
    {
        return new Dictionary<string, string>
        {
            ["themeTitle"] = lang switch
            {
                "zh" => "4-7字，年度主题",
                "zh-tw" => "4-7字，年度主題",
                "es" => "4-7 palabras, tema anual",
                _ => "4-7 words yearly theme"
            },
            ["themeGlance"] = lang switch
            {
                "zh" => "15-20字，年度运势综述",
                "zh-tw" => "15-20字，年度運勢綜述",
                "es" => "15-20 palabras, resumen",
                _ => "15-20 words summary"
            },
            ["themeDetail"] = lang switch
            {
                "zh" => "60-80字，分三部分",
                "zh-tw" => "60-80字，分三部分",
                "es" => "60-80 palabras en 3 partes",
                _ => "60-80 words in 3 parts"
            },
            ["tag"] = lang switch
            {
                "zh" => "10-15字，反思性标语",
                "zh-tw" => "10-15字，反思性標語",
                "es" => "10-15 palabras",
                _ => "10-15 words tagline"
            },
            ["doAction"] = lang switch
            {
                "zh" => "2条完整行动句 (竖线分隔)",
                "zh-tw" => "2條完整行動句 (豎線分隔)",
                "es" => "2 oraciones de acción (separado por |)",
                _ => "2 action sentences (separated by |)"
            },
            ["avoid"] = lang switch
            {
                "zh" => "2条注意事项 (竖线分隔)",
                "zh-tw" => "2條注意事項 (豎線分隔)",
                "es" => "2 precauciones (separado por |)",
                _ => "2 cautions (separated by |)"
            },
            ["detail"] = lang switch
            {
                "zh" => "50-70字，三部分",
                "zh-tw" => "50-70字，三部分",
                "es" => "50-70 palabras",
                _ => "50-70 words"
            },
            ["mantra"] = lang switch
            {
                "zh" => "18-25字，第一人称真言",
                "zh-tw" => "18-25字，第一人稱真言",
                "es" => "18-25 palabras en primera persona",
                _ => "18-25 words first-person mantra"
            }
        };
    }

    #endregion

    #region Lifetime Prompt

    private string BuildLifetimePrompt(
        string prefix,
        string userInfoLine,
        string displayName,
        string sunSign,
        string moonSign,
        string risingSign,
        int birthYear,
        string birthYearZodiac,
        string birthYearAnimal,
        string birthYearElement,
        DateOnly predictionDate,
        string targetLanguage)
    {
        var currentYear = DateTime.UtcNow.Year;
        
        // Translate zodiac signs
        var sunSignTranslated = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
        var moonSignTranslated = TranslationHelpers.TranslateSunSign(moonSign, targetLanguage);
        var risingSignTranslated = TranslationHelpers.TranslateSunSign(risingSign, targetLanguage);
        var birthYearAnimalTranslated = TranslationHelpers.TranslateChineseZodiacAnimal(birthYearZodiac, targetLanguage);
        
        // Get 10-year cycles
        var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
        
        var currentYearZodiac = LumenCalculator.GetChineseZodiacWithElement(currentYear);
        var currentYearStemsFormatted = LumenCalculator.CalculateStemsAndBranches(currentYear);
        
        var descriptions = GetLifetimeDescriptions(targetLanguage);
        
        return prefix + $@"Create lifetime astrological narrative.
User: {userInfoLine}, Current Year: {currentYear}

CONTEXT (Use these exact values):
Sun: {sunSignTranslated} | Moon: {moonSignTranslated} | Rising: {risingSignTranslated}
Birth Year: {birthYearZodiac} ({birthYearAnimalTranslated}, {birthYearElement})
Current Year: {currentYearZodiac} ({currentYearStemsFormatted})
Cycles: Past {pastCycle.AgeRange}, Current {currentCycle.AgeRange}, Future {futureCycle.AgeRange}

Generate meaningful astrological content in TSV format (tab-separated).
Write naturally - quality over strict length.

Required fields (one per line, format: key	value):
pillars_id	{descriptions["text"]}
pillars_detail	{descriptions["text"]}
cn_trait1	{descriptions["simple"]}
cn_trait2	{descriptions["simple"]}
cn_trait3	{descriptions["simple"]}
cn_trait4	{descriptions["simple"]}
whisper	{descriptions["whisper"]}
sun_tag	{descriptions["text"]}
sun_arch_name	{descriptions["simple"]}
sun_desc	{descriptions["text"]}
moon_arch_name	{descriptions["simple"]}
moon_desc	{descriptions["text"]}
rising_arch_name	{descriptions["simple"]}
rising_desc	{descriptions["text"]}
combined_essence	{descriptions["combinedEssence"]}
str_intro	{descriptions["text"]}
str1_title	{descriptions["title"]}
str1_desc	{descriptions["text"]}
str2_title	{descriptions["title"]}
str2_desc	{descriptions["text"]}
str3_title	{descriptions["title"]}
str3_desc	{descriptions["text"]}
chal_intro	{descriptions["text"]}
chal1_title	{descriptions["title"]}
chal1_desc	{descriptions["text"]}
chal2_title	{descriptions["title"]}
chal2_desc	{descriptions["text"]}
chal3_title	{descriptions["title"]}
chal3_desc	{descriptions["text"]}
destiny_intro	{descriptions["text"]}
path1_title	{descriptions["title"]}
path1_desc	{descriptions["text"]}
path2_title	{descriptions["title"]}
path2_desc	{descriptions["text"]}
path3_title	{descriptions["title"]}
path3_desc	{descriptions["text"]}
cn_essence	{descriptions["cnEssence"]}
cycle_year_range	YYYY-YYYY
cycle_name_zh	[Simplified Chinese name]
cycle_name	{descriptions["cycleName"]}
cycle_intro	{descriptions["text"]}
cycle_pt1	{descriptions["simple"]}
cycle_pt2	{descriptions["simple"]}
cycle_pt3	{descriptions["simple"]}
cycle_pt4	{descriptions["simple"]}
ten_intro	{descriptions["text"]}
past_summary	{descriptions["simple"]}
past_detail	{descriptions["text"]}
curr_summary	{descriptions["simple"]}
curr_detail	{descriptions["text"]}
future_summary	{descriptions["simple"]}
future_detail	{descriptions["text"]}
plot_title	{descriptions["text"]}
plot_chapter	{descriptions["text"]}
plot_pt1	{descriptions["simple"]}
plot_pt2	{descriptions["simple"]}
plot_pt3	{descriptions["simple"]}
plot_pt4	{descriptions["simple"]}
act1_title	{descriptions["title"]}
act1_desc	{descriptions["text"]}
act2_title	{descriptions["title"]}
act2_desc	{descriptions["text"]}
act3_title	{descriptions["title"]}
act3_desc	{descriptions["text"]}
act4_title	{descriptions["title"]}
act4_desc	{descriptions["text"]}
mantra_title	{descriptions["title"]}
mantra_pt1	{descriptions["text"]}
mantra_pt2	{descriptions["text"]}
mantra_pt3	{descriptions["text"]}

Start output now with first field
";
    }

    private Dictionary<string, string> GetLifetimeDescriptions(string lang)
    {
        return new Dictionary<string, string>
        {
            ["simple"] = lang switch
            {
                "zh" => "[内容]",
                "zh-tw" => "[內容]",
                "es" => "[contenido]",
                _ => "[content]"
            },
            ["title"] = lang switch
            {
                "zh" => "[标题]",
                "zh-tw" => "[標題]",
                "es" => "[título]",
                _ => "[title]"
            },
            ["text"] = lang switch
            {
                "zh" => "[描述]",
                "zh-tw" => "[描述]",
                "es" => "[descripción]",
                _ => "[description]"
            },
            ["combinedEssence"] = lang switch
            {
                "zh" => "如：你像[太阳星座]一样思考...",
                "zh-tw" => "如：你像[太陽星座]一樣思考...",
                "es" => "Ej: Piensas como [sun]...",
                _ => "E.g.: You think like [sun]..."
            },
            ["whisper"] = lang switch
            {
                "zh" => "以生肖开头的消息",
                "zh-tw" => "以生肖開頭的訊息",
                "es" => "Mensaje con zodiaco",
                _ => "Message starting with zodiac"
            },
            ["cnEssence"] = lang switch
            {
                "zh" => "与五行相关的本质",
                "zh-tw" => "與五行相關的本質",
                "es" => "Esencia del elemento",
                _ => "Element essence"
            },
            ["cycleName"] = lang switch
            {
                "zh" => "[同 cycle_name_zh]",
                "zh-tw" => "[繁體中文名稱]",
                "es" => "[nombre en español]",
                _ => "[English name]"
            }
        };
    }

    #endregion

    #region Translation Prompt

    /// <summary>
    /// Build translation prompt for remaining languages
    /// </summary>
    private string BuildTranslationPrompt(
        Dictionary<string, string> sourceContent,
        string sourceLanguage,
        List<string> targetLanguages,
        PredictionType type)
    {
        var sourceLangName = LanguageMap.GetValueOrDefault(sourceLanguage, "English");
        var targetLangNames = string.Join(", ",
            targetLanguages.Select(lang => LanguageMap.GetValueOrDefault(lang, lang)));
        
        // Convert source content to TSV format
        var sourceTsv = string.Join("\n", sourceContent.Select(kvp => $"{kvp.Key}\t{kvp.Value}"));
        
        return $@"TASK: Translate the following {type} astrological reflection from {sourceLangName} into {targetLangNames}.

TRANSLATION RULES:
- TRANSLATE content, do NOT regenerate or reinterpret
- Keep exact same meaning and structure
- Maintain natural, fluent expression in each target language
- For Chinese: Adapt English grammar naturally

OUTPUT FORMAT:
[LANGUAGE_CODE]
fieldName	translatedValue
...

Example:
[zh-tw]
dayTitle	祥龍之日
path_title	今日的道路 - 寧靜之路

SOURCE CONTENT ({sourceLangName}):
{sourceTsv}

Translate now:";
    }

    #endregion
}

