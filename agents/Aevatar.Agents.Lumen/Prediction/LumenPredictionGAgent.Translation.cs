using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - Translation batch processing
/// Handles parallel batch translation for Lifetime predictions
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Translation Batch Processing

    /// <summary>
    /// Translate Lifetime prediction using batched parallel LLM calls
    /// </summary>
    private async Task<Dictionary<string, string>?> TranslateLifetimeBatchedAsync(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        string sourceLanguage,
        Dictionary<string, string> sourceContent,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation(
            "[LumenPredictionGAgent][TranslateLifetimeBatched] Starting parallel batch translation for user {UserId}, {SourceLang} → {TargetLang}",
            userInfo.UserId, sourceLanguage, targetLanguage);

        try
        {
            // Filter fields that don't need translation
            var filteredForTranslation = FilterFieldsForTranslation(sourceContent);
            var skippedFields = sourceContent
                .Where(kvp => !filteredForTranslation.ContainsKey(kvp.Key))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateLifetimeBatched] {UserId} Filtered {SkippedCount} fields, {TranslateCount} to translate",
                userInfo.UserId, skippedFields.Count, filteredForTranslation.Count);
            
            // Split filtered fields into 4 batches based on field name prefixes
            var batch1Fields = filteredForTranslation.Where(kvp => 
                kvp.Key.StartsWith("westernOverview_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("sun_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("moon_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("rising_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("combined_essence", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.Equals("whisper", StringComparison.OrdinalIgnoreCase)
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            var batch2Fields = filteredForTranslation.Where(kvp => 
                kvp.Key.StartsWith("pillars_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("cn_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("cycle_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("chineseAstrology_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("zodiacCycle_", StringComparison.OrdinalIgnoreCase)
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            var batch3Fields = filteredForTranslation.Where(kvp => 
                kvp.Key.StartsWith("str_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("str1_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("str2_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("str3_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("chal_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("chal1_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("chal2_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("chal3_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("destiny_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("path1_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("path2_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("path3_", StringComparison.OrdinalIgnoreCase)
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            var batch4Fields = filteredForTranslation.Where(kvp => 
                kvp.Key.StartsWith("ten_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("past_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("curr_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("future_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("plot_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("act1_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("act2_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("act3_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("act4_", StringComparison.OrdinalIgnoreCase) ||
                kvp.Key.StartsWith("mantra_", StringComparison.OrdinalIgnoreCase)
            ).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateLifetimeBatched] {UserId} Split into batches: B1={B1}, B2={B2}, B3={B3}, B4={B4}",
                userInfo.UserId, batch1Fields.Count, batch2Fields.Count, batch3Fields.Count, batch4Fields.Count);
            
            // Create batch tasks for parallel execution
            var batchTasks = new List<Task<Dictionary<string, string>?>>();
            
            if (batch1Fields.Count > 0)
                batchTasks.Add(TranslateLifetimeBatchAsync(userInfo, sourceLanguage, batch1Fields, targetLanguage, "Western Astrology", cancellationToken));
            if (batch2Fields.Count > 0)
                batchTasks.Add(TranslateLifetimeBatchAsync(userInfo, sourceLanguage, batch2Fields, targetLanguage, "Chinese Astrology", cancellationToken));
            if (batch3Fields.Count > 0)
                batchTasks.Add(TranslateLifetimeBatchAsync(userInfo, sourceLanguage, batch3Fields, targetLanguage, "Life Traits", cancellationToken));
            if (batch4Fields.Count > 0)
                batchTasks.Add(TranslateLifetimeBatchAsync(userInfo, sourceLanguage, batch4Fields, targetLanguage, "Timeline & Plot", cancellationToken));
            
            // Wait for all batches to complete
            await Task.WhenAll(batchTasks);
            
            totalStopwatch.Stop();
            Logger.LogInformation(
                "[PERF][LumenPredictionGAgent][TranslateLifetimeBatched] {UserId} All_Translation_Batches: {ElapsedMs}ms",
                userInfo.UserId, totalStopwatch.ElapsedMilliseconds);
            
            // Merge results from all batches
            var mergedResults = new Dictionary<string, string>();
            var successCount = 0;
            
            foreach (var task in batchTasks)
            {
                var batchResult = await task;
                if (batchResult != null && batchResult.Count > 0)
                {
                    foreach (var (key, value) in batchResult)
                    {
                        mergedResults[key] = value;
                    }
                    successCount++;
                }
            }
            
            // Identify backend-calculated fields (to be re-injected)
            var backendCalculatedPatterns = GetBackendCalculatedPatterns();
            var backendCalculatedFields = new Dictionary<string, string>();
            var otherSkippedFields = new Dictionary<string, string>();
            
            foreach (var skipped in skippedFields)
            {
                if (IsBackendCalculatedField(skipped.Key, backendCalculatedPatterns))
                {
                    backendCalculatedFields[skipped.Key] = skipped.Value;
                }
                else
                {
                    otherSkippedFields[skipped.Key] = skipped.Value;
                }
            }
            
            // Merge back non-backend-calculated skipped fields
            foreach (var skipped in otherSkippedFields)
            {
                mergedResults[skipped.Key] = skipped.Value;
            }
            
            // Re-inject backend-calculated fields for target language
            InjectBackendFieldsForLanguage(mergedResults, userInfo, predictionDate, PredictionType.PredictionLifetime, targetLanguage);
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateLifetimeBatched] {UserId} {TargetLang} Merged {TranslatedCount} translated + {SkippedCount} skipped + {BackendCount} backend = {TotalCount} total",
                userInfo.UserId, targetLanguage, 
                mergedResults.Count - otherSkippedFields.Count - backendCalculatedFields.Count, 
                otherSkippedFields.Count, backendCalculatedFields.Count, mergedResults.Count);
            
            return mergedResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][TranslateLifetimeBatched] Error during batch translation for user {UserId} {SourceLang} → {TargetLang}",
                userInfo.UserId, sourceLanguage, targetLanguage);
            return null;
        }
    }

    /// <summary>
    /// Translate a single batch of Lifetime fields
    /// </summary>
    private async Task<Dictionary<string, string>?> TranslateLifetimeBatchAsync(
        LumenUserDto userInfo,
        string sourceLanguage,
        Dictionary<string, string> batchFields,
        string targetLanguage,
        string batchName,
        CancellationToken cancellationToken = default)
    {
        var batchStopwatch = Stopwatch.StartNew();
        
        try
        {
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] {UserId} Translating {Count} fields {SourceLang} → {TargetLang}",
                batchName, userInfo.UserId, batchFields.Count, sourceLanguage, targetLanguage);
            
            // Build translation prompt for this batch
            var translationPrompt = BuildLifetimeTranslationBatchPrompt(batchFields, sourceLanguage, targetLanguage, batchName);
            
            // Check if LLM provider is initialized
            if (!_isInitialized)
            {
                Logger.LogWarning("[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] LLM not initialized", batchName);
                return null;
            }
            
            var systemPrompt = "You are a professional translator for astrological and philosophical reflection content. All content is for entertainment, self-exploration, and contemplative purposes only.";
            
            var request = new AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = translationPrompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.3f,
                    MaxTokens = 3000,
                    ModelId = Config.Model
                }
            };
            
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            batchStopwatch.Stop();
            
            Logger.LogInformation(
                "[PERF][LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] {UserId} LLM_Call: {ElapsedMs}ms",
                batchName, userInfo.UserId, batchStopwatch.ElapsedMilliseconds);
            
            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning(
                    "[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] {UserId} No response from LLM",
                    batchName, userInfo.UserId);
                return null;
            }
            
            var aiResponse = response.Content;
            
            // Parse TSV response
            var contentDict = ParseTsvResponse(aiResponse);
            if (contentDict == null || contentDict.Count == 0)
            {
                Logger.LogError(
                    "[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] {UserId} TSV parse failed",
                    batchName, userInfo.UserId);
                return null;
            }
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] {UserId} COMPLETED - {Count} fields in {ElapsedMs}ms",
                batchName, userInfo.UserId, contentDict.Count, batchStopwatch.ElapsedMilliseconds);
            
            return contentDict;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][TranslateLifetimeBatch:{BatchName}] Error for user {UserId}",
                batchName, userInfo.UserId);
            return null;
        }
    }

    #endregion

    #region Translation Helpers

    /// <summary>
    /// Build translation prompt for a single Lifetime batch
    /// </summary>
    private static string BuildLifetimeTranslationBatchPrompt(
        Dictionary<string, string> batchFields,
        string sourceLanguage,
        string targetLanguage,
        string batchName)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        
        var sourceLangName = languageMap.GetValueOrDefault(sourceLanguage, "English");
        var targetLangName = languageMap.GetValueOrDefault(targetLanguage, targetLanguage);
        
        // Convert batch fields to TSV format
        var sourceTsv = new StringBuilder();
        foreach (var kvp in batchFields)
        {
            sourceTsv.AppendLine($"{kvp.Key}\t{kvp.Value}");
        }
        
        var languageReminder = targetLanguage switch
        {
            "zh" => "⚠️ 重要：请使用简体中文翻译所有内容，避免混入英文！",
            "zh-tw" => "⚠️ 重要：請使用繁體中文翻譯所有內容，避免混入英文！",
            "es" => "⚠️ IMPORTANTE: Por favor traduce todo el contenido al español, ¡evita mezclar inglés!",
            _ => "⚠️ IMPORTANT: Please translate all content to English only!"
        };
        
        return $@"TASK: Translate the following Lifetime prediction content ({batchName} section) from {sourceLangName} into {targetLangName}.

{languageReminder}

TRANSLATION GUIDELINES:
1. Translate content while keeping the exact same meaning and structure.
2. Keep user names unchanged (e.g., ""Sean"" stays ""Sean"")
3. Maintain natural, fluent expression in {targetLangName}.
4. Keep all field names unchanged.
5. Preserve numbers, dates, and proper nouns.
6. For Chinese translations: Adapt English grammar naturally
   - Remove or adapt articles (""The/A"") as needed
   - Adjust to natural Chinese word order

OUTPUT FORMAT (TSV - Tab-Separated Values):
- Each field on ONE line: fieldName	translatedValue
- Use TAB character (\t) as separator
- Avoid line breaks within field values
- Return TSV format only, no markdown or extra text

SOURCE CONTENT ({sourceLangName} - TSV Format):
{sourceTsv}

Start translation now:";
    }

    /// <summary>
    /// Filter fields that don't need translation (enums, numbers, backend-calculated fields)
    /// </summary>
    private static Dictionary<string, string> FilterFieldsForTranslation(Dictionary<string, string> sourceContent)
    {
        var filtered = new Dictionary<string, string>();
        var backendCalculatedPatterns = GetBackendCalculatedPatterns();
        
        foreach (var kvp in sourceContent)
        {
            // Skip enum fields (end with _enum)
            if (kvp.Key.EndsWith("_enum", StringComparison.OrdinalIgnoreCase))
                continue;
            
            // Skip backend-calculated fields
            if (IsBackendCalculatedField(kvp.Key, backendCalculatedPatterns))
                continue;
            
            // Skip numeric-only values
            if (double.TryParse(kvp.Value, out _))
                continue;
            
            filtered[kvp.Key] = kvp.Value;
        }
        
        return filtered;
    }

    /// <summary>
    /// Get set of backend-calculated field patterns
    /// </summary>
    private static HashSet<string> GetBackendCalculatedPatterns()
    {
        return new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "sunSign_name", "westernOverview_sunSign", "westernOverview_moonSign", "westernOverview_risingSign",
            "westernOverview_sunArchetype", "westernOverview_moonArchetype", "westernOverview_risingArchetype",
            "chineseZodiac_animal", "chineseZodiac_title",
            "chineseAstrology_currentYearStem", "chineseAstrology_currentYearStemPinyin",
            "chineseAstrology_currentYearBranch", "chineseAstrology_currentYearBranchPinyin",
            "chineseAstrology_taishuiRelationship", "chineseAstrology_currentYear",
            "pastCycle_ageRange", "pastCycle_period", "currentCycle_ageRange", "currentCycle_period",
            "futureCycle_ageRange", "futureCycle_period", "zodiacCycle_title", "zodiacCycle_cycleName",
            "zodiacInfluence", "currentPhase"
        };
    }

    /// <summary>
    /// Check if a field is backend-calculated
    /// </summary>
    private static bool IsBackendCalculatedField(string fieldName, HashSet<string> patterns)
    {
        if (patterns.Contains(fieldName))
            return true;
        
        // Four Pillars fields
        if (fieldName.StartsWith("fourPillars_year", StringComparison.OrdinalIgnoreCase) ||
            fieldName.StartsWith("fourPillars_month", StringComparison.OrdinalIgnoreCase) ||
            fieldName.StartsWith("fourPillars_day", StringComparison.OrdinalIgnoreCase) ||
            fieldName.StartsWith("fourPillars_hour", StringComparison.OrdinalIgnoreCase))
            return true;
        
        return false;
    }

    #endregion

    #region Backend Field Injection

    /// <summary>
    /// Re-inject backend-calculated fields for a specific language
    /// </summary>
    private void InjectBackendFieldsForLanguage(
        Dictionary<string, string> targetDict,
        LumenUserDto userInfo, 
        DateOnly predictionDate, 
        PredictionType type, 
        string targetLanguage)
    {
        try
        {
            var birthDateValue = userInfo.BirthDate;
            if (birthDateValue == null)
                return;
            
            // Convert DateValue to DateOnly
            var birthDate = new DateOnly(birthDateValue.Year, birthDateValue.Month, birthDateValue.Day);
            
            // Convert TimeValue to TimeOnly? if present
            TimeOnly? birthTime = userInfo.BirthTime != null 
                ? new TimeOnly(userInfo.BirthTime.Hour, userInfo.BirthTime.Minute, userInfo.BirthTime.Second)
                : null;
            
            var currentYear = DateTime.UtcNow.Year;
            var birthYear = birthDate.Year;
            
            var sunSign = LumenCalculator.CalculateZodiacSign(birthDate);
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var birthYearStemsComponents = LumenCalculator.GetStemsAndBranchesComponents(birthYear);
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            // Moon/Rising signs (simplified - use Sun as fallback since we don't have SwissEphNet)
            var moonSign = sunSign;
            var risingSign = sunSign;
            
            if (type == PredictionType.PredictionLifetime)
            {
                var fourPillars = LumenCalculator.CalculateFourPillars(birthDate, birthTime);
                
                targetDict["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                targetDict["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                targetDict["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                targetDict["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                targetDict["sunSign_name"] = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
                targetDict["westernOverview_sunSign"] = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
                targetDict["westernOverview_moonSign"] = TranslationHelpers.TranslateSunSign(moonSign, targetLanguage);
                targetDict["westernOverview_risingSign"] = TranslationHelpers.TranslateSunSign(risingSign, targetLanguage);
                targetDict["chineseZodiac_animal"] = TranslationHelpers.TranslateZodiac(birthYearAnimal, targetLanguage);
                targetDict["pastCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(pastCycle.AgeRange, targetLanguage);
                targetDict["pastCycle_period"] = TranslationHelpers.TranslateCyclePeriod(pastCycle.Period, targetLanguage);
                targetDict["currentCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(currentCycle.AgeRange, targetLanguage);
                targetDict["currentCycle_period"] = TranslationHelpers.TranslateCyclePeriod(currentCycle.Period, targetLanguage);
                targetDict["futureCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(futureCycle.AgeRange, targetLanguage);
                targetDict["futureCycle_period"] = TranslationHelpers.TranslateCyclePeriod(futureCycle.Period, targetLanguage);
                
                InjectFourPillarsData(targetDict, fourPillars, targetLanguage);
                
                Logger.LogDebug("[LumenPredictionGAgent][InjectBackendFields] Re-injected Lifetime backend fields for {Language}", targetLanguage);
            }
            else if (type == PredictionType.PredictionYearly)
            {
                var yearlyYear = predictionDate.Year;
                var yearlyYearZodiac = LumenCalculator.GetChineseZodiacWithElement(yearlyYear);
                var yearlyTaishui = LumenCalculator.CalculateTaishuiRelationship(birthYear, yearlyYear);
                
                targetDict["sunSign_name"] = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
                targetDict["chineseZodiac_animal"] = TranslationHelpers.TranslateZodiac(birthYearAnimal, targetLanguage);
                targetDict["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
                targetDict["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
                targetDict["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
                targetDict["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
                targetDict["chineseAstrology_taishuiRelationship"] = TranslationHelpers.TranslateTaishuiRelationship(yearlyTaishui, targetLanguage);
                targetDict["zodiacInfluence"] = TranslationHelpers.BuildZodiacInfluence(birthYearZodiac, yearlyYearZodiac, yearlyTaishui, targetLanguage);
                
                Logger.LogDebug("[LumenPredictionGAgent][InjectBackendFields] Re-injected Yearly backend fields for {Language}", targetLanguage);
            }
            else if (type == PredictionType.PredictionDaily)
            {
                // For Daily, handle path title construction
                if (targetDict.TryGetValue("todaysReading_pathType", out var pathAdjective) && !string.IsNullOrWhiteSpace(pathAdjective))
                {
                    // Use user's full name for path title, fallback to "You" if empty
                    var displayName = !string.IsNullOrEmpty(userInfo.FullName) ? userInfo.FullName : "You";
                    targetDict["todaysReading_pathTitle"] = TranslationHelpers.BuildPathTitle(displayName, pathAdjective, targetLanguage);
                }
                
                // Add lucky number for target language
                var luckyNumberResult = Aevatar.Agents.Lumen.Services.LuckyNumberService.CalculateLuckyNumber(birthDate, predictionDate, targetLanguage);
                targetDict["luckyAlignments_luckyNumber_number"] = luckyNumberResult.NumberWord;
                
                Logger.LogDebug("[LumenPredictionGAgent][InjectBackendFields] Re-injected Daily backend fields for {Language}", targetLanguage);
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[LumenPredictionGAgent][InjectBackendFields] Error injecting backend fields for {Language}", targetLanguage);
        }
    }

    // Note: InjectFourPillarsData is defined in LumenPredictionGAgent.Utilities.cs

    #endregion
}

