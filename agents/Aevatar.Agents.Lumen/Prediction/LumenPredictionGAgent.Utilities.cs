using System.Text.Json;
using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// Utility methods for LumenPredictionGAgent
/// Includes date/time utilities, response parsing, and data transformation
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Date/Time Utilities

    /// <summary>
    /// Get user's local date based on their timezone (for Daily prediction date calculation)
    /// </summary>
    private DateOnly GetUserLocalDate(string? timeZoneId)
    {
        if (string.IsNullOrEmpty(timeZoneId))
        {
            // Fallback to UTC if timezone not provided (backward compatibility)
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }

        try
        {
            var userTimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            var userNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, userTimeZone);
            return DateOnly.FromDateTime(userNow);
        }
        catch (TimeZoneNotFoundException ex)
        {
            Logger.LogWarning(ex, "[Lumen] Invalid timezone: {TimeZoneId}, falling back to UTC", timeZoneId);
            return DateOnly.FromDateTime(DateTime.UtcNow);
        }
    }

    /// <summary>
    /// Calculate current life phase based on birth date
    /// </summary>
    private string CalculateCurrentPhase(DateOnly birthDate, string? timeZoneId = null)
    {
        var today = GetUserLocalDate(timeZoneId);
        var age = today.Year - birthDate.Year;

        // Adjust if birthday hasn't occurred this year
        if (today < birthDate.AddYears(age))
        {
            age--;
        }

        if (age <= 20) return "phase1";
        if (age <= 35) return "phase2";
        return "phase3";
    }

    #endregion

    #region Array Field Conversion

    /// <summary>
    /// Define all array field names (using full frontend keys)
    /// </summary>
    private static readonly HashSet<string> ArrayFieldNames = new()
    {
        // Daily prediction array fields
        "twistOfFate_favorable",
        "twistOfFate_avoid",

        // Yearly prediction array fields  
        "divineInfluence_career_bestMoves",
        "divineInfluence_career_avoid",
        "divineInfluence_love_bestMoves",
        "divineInfluence_love_avoid",
        "divineInfluence_wealth_bestMoves",
        "divineInfluence_wealth_avoid",
        "divineInfluence_health_bestMoves",
        "divineInfluence_health_avoid"
        // Lifetime prediction has no array fields
    };

    /// <summary>
    /// Convert array fields from pipe-separated strings to JSON array strings for frontend
    /// </summary>
    private static Dictionary<string, string> ConvertArrayFieldsToJson(Dictionary<string, string> data)
    {
        if (data == null || data.Count == 0)
            return data;

        var result = new Dictionary<string, string>(data);

        foreach (var fieldName in ArrayFieldNames)
        {
            if (result.TryGetValue(fieldName, out var value) && !string.IsNullOrEmpty(value) && value.Contains('|'))
            {
                // Split pipe-separated string and convert to JSON array
                var items = value.Split('|', StringSplitOptions.RemoveEmptyEntries)
                    .Select(item => item.Trim())
                    .ToList();
                result[fieldName] = JsonSerializer.Serialize(items);
            }
        }

        return result;
    }

    /// <summary>
    /// Add quotes to affirmation text based on language
    /// Chinese: 「」, Others: ""
    /// </summary>
    private static Dictionary<string, string> AddQuotesToAffirmation(Dictionary<string, string> data, string language)
    {
        if (data == null || data.Count == 0)
            return data;

        const string affirmationField = "luckyAlignments_luckySpell_description";

        if (!data.TryGetValue(affirmationField, out var affirmationText) || string.IsNullOrEmpty(affirmationText))
            return data;

        var result = new Dictionary<string, string>(data);

        // Remove existing quotes if any
        affirmationText = affirmationText.Trim().Trim('"', '「', '」', '"', '"');

        // Add appropriate quotes based on language
        bool isChinese = language.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        result[affirmationField] = isChinese
            ? $"「{affirmationText}」" // Chinese quotes
            : $"\"{affirmationText}\""; // English quotes

        return result;
    }

    #endregion

    #region Key Mapping

    /// <summary>
    /// Map shortened TSV keys to full field names expected by frontend
    /// </summary>
    private static readonly Dictionary<string, string> ShortKeyMapping = new()
    {
        // ===== DAILY PREDICTION MAPPINGS =====
        ["daily_theme_title"] = "dayTitle",

        // Tarot Card (tarot_*) - Semantic keys for prompt
        ["tarot_card_name"] = "todaysReading_tarotCard_name",
        ["tarot_card_essence"] = "todaysReading_tarotCard_represents",
        ["tarot_card_orientation"] = "todaysReading_tarotCard_orientation",

        // Path (path_*)
        ["path_adjective"] = "todaysReading_pathType",
        ["path_greeting"] = "todaysReading_pathDescription",
        ["path_wisdom"] = "todaysReading_pathDescriptionExpanded",

        // Life Areas (reflection_*)
        ["reflection_career"] = "todaysReading_careerAndWork",
        ["reflection_relationships"] = "todaysReading_loveAndRelationships",
        ["reflection_wealth"] = "todaysReading_wealthAndFinance",
        ["reflection_wellbeing"] = "todaysReading_healthAndWellness",

        // Takeaway
        ["daily_takeaway"] = "todaysTakeaway",

        // Lucky Stone (crystal_*)
        ["crystal_stone_id"] = "luckyAlignments_luckyStone",
        ["crystal_power"] = "luckyAlignments_luckyStone_description",
        ["crystal_usage"] = "luckyAlignments_luckyStone_guidance",

        // Affirmation (affirmation_*)
        ["affirmation_poetic"] = "luckyAlignments_luckySpell",
        ["affirmation_text"] = "luckyAlignments_luckySpell_description",
        ["affirmation_intent"] = "luckyAlignments_luckySpell_intent",

        // Guidance (guidance_*)
        ["guidance_metaphor"] = "twistOfFate_title",
        ["guidance_suggestions"] = "twistOfFate_favorable",
        ["guidance_mindful_of"] = "twistOfFate_avoid",
        ["guidance_tip"] = "twistOfFate_todaysRecommendation",

        // ===== YEARLY PREDICTION MAPPINGS =====
        ["astro_overlay"] = "westernAstroOverlay",
        ["theme_title"] = "yearlyTheme_overallTheme",
        ["theme_glance"] = "yearlyTheme_atAGlance",
        ["theme_detail"] = "yearlyTheme_expanded",
        ["career_score"] = "divineInfluence_career_score",
        ["career_tag"] = "divineInfluence_career_tagline",
        ["career_do"] = "divineInfluence_career_bestMoves",
        ["career_avoid"] = "divineInfluence_career_avoid",
        ["career_detail"] = "divineInfluence_career_inANutshell",
        ["love_score"] = "divineInfluence_love_score",
        ["love_tag"] = "divineInfluence_love_tagline",
        ["love_do"] = "divineInfluence_love_bestMoves",
        ["love_avoid"] = "divineInfluence_love_avoid",
        ["love_detail"] = "divineInfluence_love_inANutshell",
        ["prosperity_score"] = "divineInfluence_wealth_score",
        ["prosperity_tag"] = "divineInfluence_wealth_tagline",
        ["prosperity_do"] = "divineInfluence_wealth_bestMoves",
        ["prosperity_avoid"] = "divineInfluence_wealth_avoid",
        ["prosperity_detail"] = "divineInfluence_wealth_inANutshell",
        ["wellness_score"] = "divineInfluence_health_score",
        ["wellness_tag"] = "divineInfluence_health_tagline",
        ["wellness_do"] = "divineInfluence_health_bestMoves",
        ["wellness_avoid"] = "divineInfluence_health_avoid",
        ["wellness_detail"] = "divineInfluence_health_inANutshell",
        ["mantra"] = "embodimentMantra",

        // ===== LIFETIME PREDICTION MAPPINGS =====
        ["pillars_id"] = "fourPillars_coreIdentity",
        ["pillars_detail"] = "fourPillars_coreIdentity_expanded",
        ["cn_trait1"] = "chineseAstrology_trait1",
        ["cn_trait2"] = "chineseAstrology_trait2",
        ["cn_trait3"] = "chineseAstrology_trait3",
        ["cn_trait4"] = "chineseAstrology_trait4",
        ["whisper"] = "zodiacWhisper",
        ["sun_tag"] = "sunSign_tagline",
        ["sun_arch_name"] = "westernOverview_sunArchetypeName",
        ["sun_desc"] = "westernOverview_sunDescription",
        ["moon_arch_name"] = "westernOverview_moonArchetypeName",
        ["moon_desc"] = "westernOverview_moonDescription",
        ["rising_arch_name"] = "westernOverview_risingArchetypeName",
        ["rising_desc"] = "westernOverview_risingDescription",
        ["combined_essence"] = "westernOverview_combinedEssenceStatement",
        ["str_intro"] = "strengths_overview",
        ["str1_title"] = "strengths_item1_title",
        ["str1_desc"] = "strengths_item1_description",
        ["str2_title"] = "strengths_item2_title",
        ["str2_desc"] = "strengths_item2_description",
        ["str3_title"] = "strengths_item3_title",
        ["str3_desc"] = "strengths_item3_description",
        ["chal_intro"] = "challenges_overview",
        ["chal1_title"] = "challenges_item1_title",
        ["chal1_desc"] = "challenges_item1_description",
        ["chal2_title"] = "challenges_item2_title",
        ["chal2_desc"] = "challenges_item2_description",
        ["chal3_title"] = "challenges_item3_title",
        ["chal3_desc"] = "challenges_item3_description",
        ["destiny_intro"] = "destiny_overview",
        ["path1_title"] = "destiny_path1_title",
        ["path1_desc"] = "destiny_path1_description",
        ["path2_title"] = "destiny_path2_title",
        ["path2_desc"] = "destiny_path2_description",
        ["path3_title"] = "destiny_path3_title",
        ["path3_desc"] = "destiny_path3_description",
        ["cn_essence"] = "chineseZodiac_essence",
        ["cycle_year_range"] = "zodiacCycle_yearRange",
        ["cycle_name_zh"] = "zodiacCycle_cycleNameChinese",
        ["cycle_name"] = "zodiacCycle_cycleName",
        ["cycle_intro"] = "zodiacCycle_overview",
        ["cycle_pt1"] = "zodiacCycle_dayMasterPoint1",
        ["cycle_pt2"] = "zodiacCycle_dayMasterPoint2",
        ["cycle_pt3"] = "zodiacCycle_dayMasterPoint3",
        ["cycle_pt4"] = "zodiacCycle_dayMasterPoint4",
        ["ten_intro"] = "tenYearCycles_description",
        ["past_summary"] = "pastCycle_influenceSummary",
        ["past_detail"] = "pastCycle_meaning",
        ["curr_summary"] = "currentCycle_influenceSummary",
        ["curr_detail"] = "currentCycle_meaning",
        ["future_summary"] = "futureCycle_influenceSummary",
        ["future_detail"] = "futureCycle_meaning",
        ["plot_title"] = "lifePlot_title",
        ["plot_chapter"] = "lifePlot_chapter",
        ["plot_pt1"] = "lifePlot_point1",
        ["plot_pt2"] = "lifePlot_point2",
        ["plot_pt3"] = "lifePlot_point3",
        ["plot_pt4"] = "lifePlot_point4",
        ["act1_title"] = "activationSteps_step1_title",
        ["act1_desc"] = "activationSteps_step1_description",
        ["act2_title"] = "activationSteps_step2_title",
        ["act2_desc"] = "activationSteps_step2_description",
        ["act3_title"] = "activationSteps_step3_title",
        ["act3_desc"] = "activationSteps_step3_description",
        ["act4_title"] = "activationSteps_step4_title",
        ["act4_desc"] = "activationSteps_step4_description",
        ["mantra_title"] = "mantra_title",
        ["mantra_pt1"] = "mantra_point1",
        ["mantra_pt2"] = "mantra_point2",
        ["mantra_pt3"] = "mantra_point3"
    };

    /// <summary>
    /// Map shortened TSV keys to full field names expected by frontend
    /// </summary>
    private static Dictionary<string, string> MapShortKeysToFullKeys(Dictionary<string, string> shortKeyData)
    {
        var mappedData = new Dictionary<string, string>();

        foreach (var kvp in shortKeyData)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            // Map short key to full key if mapping exists, otherwise keep original key
            var fullKey = ShortKeyMapping.TryGetValue(key, out var mapped) ? mapped : key;
            mappedData[fullKey] = value;
        }

        return mappedData;
    }

    #endregion

    #region Response Parsing

    /// <summary>
    /// Parse TSV (Tab-Separated Values) response from LLM
    /// Format: fieldName	value (one per line)
    /// Arrays: fieldName	item1|item2|item3
    /// </summary>
    private Dictionary<string, string>? ParseTsvResponse(string aiResponse)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                Logger.LogWarning("[LumenPredictionGAgent] ParseTsvResponse: Empty response");
                return null;
            }

            var result = new Dictionary<string, string>();
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Skip header line if present
                if (line.TrimStart().StartsWith("field", StringComparison.OrdinalIgnoreCase))
                    continue;

                // Split by tab
                var parts = line.Split('\t', 2);
                if (parts.Length != 2)
                    continue;

                var fieldName = parts[0].Trim();
                var value = parts[1].Trim();

                // Skip empty values
                if (string.IsNullOrWhiteSpace(fieldName))
                    continue;

                result[fieldName] = value;
            }

            if (result.Count == 0)
            {
                Logger.LogWarning("[LumenPredictionGAgent] ParseTsvResponse: No valid key-value pairs found");
                return null;
            }

            Logger.LogDebug("[LumenPredictionGAgent] ParseTsvResponse: Parsed {Count} fields", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] ParseTsvResponse: Failed to parse TSV response");
            return null;
        }
    }

    /// <summary>
    /// Parse plain text response (key: value format)
    /// </summary>
    private Dictionary<string, string>? ParsePlainTextResponse(string aiResponse)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(aiResponse))
            {
                Logger.LogWarning("[LumenPredictionGAgent] ParsePlainTextResponse: Empty response");
                return null;
            }

            var result = new Dictionary<string, string>();
            var lines = aiResponse.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                // Try to find key: value pattern
                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                    continue;

                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();

                // Remove quotes if present
                if (value.StartsWith("\"") && value.EndsWith("\""))
                    value = value.Substring(1, value.Length - 2);

                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            if (result.Count == 0)
            {
                Logger.LogWarning("[LumenPredictionGAgent] ParsePlainTextResponse: No valid key-value pairs found");
                return null;
            }

            Logger.LogDebug("[LumenPredictionGAgent] ParsePlainTextResponse: Parsed {Count} fields", result.Count);
            return result;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] ParsePlainTextResponse: Failed to parse response");
            return null;
        }
    }

    /// <summary>
    /// Flatten dictionary (remove nesting)
    /// </summary>
    private static Dictionary<string, string> FlattenDictionary(Dictionary<string, string> source)
    {
        // For string dictionaries, there's no nesting to flatten
        return new Dictionary<string, string>(source);
    }

    #endregion

    #region Four Pillars Data Injection

    /// <summary>
    /// Inject Four Pillars (Ba Zi) data into prediction dictionary with language-specific formatting
    /// </summary>
    private static void InjectFourPillarsData(
        Dictionary<string, string> prediction,
        FourPillarsInfo fourPillars,
        string language)
    {
        // Year Pillar - Standardized field naming: separate stem and branch attributes
        prediction["fourPillars_yearPillar"] = fourPillars.YearPillar.GetFormattedString(language);
        // Stem attributes
        prediction["fourPillars_yearPillar_stemChinese"] = fourPillars.YearPillar.StemChinese;
        prediction["fourPillars_yearPillar_stemPinyin"] = fourPillars.YearPillar.StemPinyin;
        prediction["fourPillars_yearPillar_stemYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.YearPillar.YinYang, language);
        prediction["fourPillars_yearPillar_stemElement"] =
            TranslationHelpers.TranslateElement(fourPillars.YearPillar.Element, language);
        prediction["fourPillars_yearPillar_stemDirection"] =
            TranslationHelpers.TranslateDirection(fourPillars.YearPillar.Direction, language);
        // Branch attributes
        prediction["fourPillars_yearPillar_branchChinese"] = fourPillars.YearPillar.BranchChinese;
        prediction["fourPillars_yearPillar_branchPinyin"] = fourPillars.YearPillar.BranchPinyin;
        prediction["fourPillars_yearPillar_branchYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.YearPillar.BranchYinYang, language);
        prediction["fourPillars_yearPillar_branchElement"] =
            TranslationHelpers.TranslateElement(fourPillars.YearPillar.BranchElement, language);
        prediction["fourPillars_yearPillar_branchZodiac"] =
            TranslationHelpers.TranslateZodiac(fourPillars.YearPillar.BranchZodiac, language);

        // Month Pillar
        prediction["fourPillars_monthPillar"] = fourPillars.MonthPillar.GetFormattedString(language);
        prediction["fourPillars_monthPillar_stemChinese"] = fourPillars.MonthPillar.StemChinese;
        prediction["fourPillars_monthPillar_stemPinyin"] = fourPillars.MonthPillar.StemPinyin;
        prediction["fourPillars_monthPillar_stemYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.MonthPillar.YinYang, language);
        prediction["fourPillars_monthPillar_stemElement"] =
            TranslationHelpers.TranslateElement(fourPillars.MonthPillar.Element, language);
        prediction["fourPillars_monthPillar_stemDirection"] =
            TranslationHelpers.TranslateDirection(fourPillars.MonthPillar.Direction, language);
        prediction["fourPillars_monthPillar_branchChinese"] = fourPillars.MonthPillar.BranchChinese;
        prediction["fourPillars_monthPillar_branchPinyin"] = fourPillars.MonthPillar.BranchPinyin;
        prediction["fourPillars_monthPillar_branchYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.MonthPillar.BranchYinYang, language);
        prediction["fourPillars_monthPillar_branchElement"] =
            TranslationHelpers.TranslateElement(fourPillars.MonthPillar.BranchElement, language);
        prediction["fourPillars_monthPillar_branchZodiac"] =
            TranslationHelpers.TranslateZodiac(fourPillars.MonthPillar.BranchZodiac, language);

        // Day Pillar
        prediction["fourPillars_dayPillar"] = fourPillars.DayPillar.GetFormattedString(language);
        prediction["fourPillars_dayPillar_stemChinese"] = fourPillars.DayPillar.StemChinese;
        prediction["fourPillars_dayPillar_stemPinyin"] = fourPillars.DayPillar.StemPinyin;
        prediction["fourPillars_dayPillar_stemYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.DayPillar.YinYang, language);
        prediction["fourPillars_dayPillar_stemElement"] =
            TranslationHelpers.TranslateElement(fourPillars.DayPillar.Element, language);
        prediction["fourPillars_dayPillar_stemDirection"] =
            TranslationHelpers.TranslateDirection(fourPillars.DayPillar.Direction, language);
        prediction["fourPillars_dayPillar_branchChinese"] = fourPillars.DayPillar.BranchChinese;
        prediction["fourPillars_dayPillar_branchPinyin"] = fourPillars.DayPillar.BranchPinyin;
        prediction["fourPillars_dayPillar_branchYinYang"] =
            TranslationHelpers.TranslateYinYang(fourPillars.DayPillar.BranchYinYang, language);
        prediction["fourPillars_dayPillar_branchElement"] =
            TranslationHelpers.TranslateElement(fourPillars.DayPillar.BranchElement, language);
        prediction["fourPillars_dayPillar_branchZodiac"] =
            TranslationHelpers.TranslateZodiac(fourPillars.DayPillar.BranchZodiac, language);

        // Hour Pillar (always include fields, empty if birth time not provided)
        if (fourPillars.HourPillar != null)
        {
            prediction["fourPillars_hourPillar"] = fourPillars.HourPillar.GetFormattedString(language);
            prediction["fourPillars_hourPillar_stemChinese"] = fourPillars.HourPillar.StemChinese;
            prediction["fourPillars_hourPillar_stemPinyin"] = fourPillars.HourPillar.StemPinyin;
            prediction["fourPillars_hourPillar_stemYinYang"] =
                TranslationHelpers.TranslateYinYang(fourPillars.HourPillar.YinYang, language);
            prediction["fourPillars_hourPillar_stemElement"] =
                TranslationHelpers.TranslateElement(fourPillars.HourPillar.Element, language);
            prediction["fourPillars_hourPillar_stemDirection"] =
                TranslationHelpers.TranslateDirection(fourPillars.HourPillar.Direction, language);
            prediction["fourPillars_hourPillar_branchChinese"] = fourPillars.HourPillar.BranchChinese;
            prediction["fourPillars_hourPillar_branchPinyin"] = fourPillars.HourPillar.BranchPinyin;
            prediction["fourPillars_hourPillar_branchYinYang"] =
                TranslationHelpers.TranslateYinYang(fourPillars.HourPillar.BranchYinYang, language);
            prediction["fourPillars_hourPillar_branchElement"] =
                TranslationHelpers.TranslateElement(fourPillars.HourPillar.BranchElement, language);
            prediction["fourPillars_hourPillar_branchZodiac"] =
                TranslationHelpers.TranslateZodiac(fourPillars.HourPillar.BranchZodiac, language);
        }
        else
        {
            // Birth time not provided - fill with empty strings
            prediction["fourPillars_hourPillar"] = "";
            prediction["fourPillars_hourPillar_stemChinese"] = "";
            prediction["fourPillars_hourPillar_stemPinyin"] = "";
            prediction["fourPillars_hourPillar_stemYinYang"] = "";
            prediction["fourPillars_hourPillar_stemElement"] = "";
            prediction["fourPillars_hourPillar_stemDirection"] = "";
            prediction["fourPillars_hourPillar_branchChinese"] = "";
            prediction["fourPillars_hourPillar_branchPinyin"] = "";
            prediction["fourPillars_hourPillar_branchYinYang"] = "";
            prediction["fourPillars_hourPillar_branchElement"] = "";
            prediction["fourPillars_hourPillar_branchZodiac"] = "";
        }
    }

    #endregion
}

