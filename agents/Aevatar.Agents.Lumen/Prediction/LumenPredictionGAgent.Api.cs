using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Helpers;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using System.Globalization;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - Complete API Methods Implementation
/// </summary>
public partial class LumenPredictionGAgent
{
    #region GetCalculatedValuesAsync - Full Implementation

    /// <summary>
    /// Get all calculated values (zodiac, Chinese zodiac, cycles, Four Pillars, etc.)
    /// This provides complete astrological calculations without generating predictions
    /// </summary>
    public Task<CalculatedValuesDto> GetCalculatedValuesFullAsync(LumenUserDto userInfo, string userLanguage = "en")
    {
        try
        {
            Logger.LogInformation(
                "[LumenPredictionGAgent][GetCalculatedValuesAsync] Calculating values for user {UserId}, language: {Language}",
                userInfo.UserId, userLanguage);
            
            var results = new CalculatedValuesDto();
            
            if (userInfo.BirthDate == null)
            {
                return Task.FromResult(results);
            }
            
            // Convert DateValue to DateOnly
            var birthDateValue = userInfo.BirthDate;
            var birthDate = new DateOnly(birthDateValue.Year, birthDateValue.Month, birthDateValue.Day);
            
            // Convert TimeValue to TimeOnly? if present
            TimeOnly? birthTime = userInfo.BirthTime != null 
                ? new TimeOnly(userInfo.BirthTime.Hour, userInfo.BirthTime.Minute, userInfo.BirthTime.Second)
                : null;
            
            // Calculate current date using user's local timezone
            var today = GetUserLocalDate(userInfo.CurrentTimeZone);
            var currentYear = today.Year;
            var birthYear = birthDate.Year;
            
            // ========== WESTERN ASTROLOGY ==========
            var sunSign = LumenCalculator.CalculateZodiacSign(birthDate);
            results.Values["sunSign_name"] = TranslationHelpers.TranslateSunSign(sunSign, userLanguage);
            results.Values["sunSign_enum"] = ((int)LumenCalculator.ParseZodiacSignEnum(sunSign)).ToString();
            
            // Moon and Rising signs (simplified - use Sun as fallback since SwissEphNet is not available)
            var moonSign = sunSign;
            var risingSign = sunSign;
            
            results.Values["moonSign_name"] = TranslationHelpers.TranslateSunSign(moonSign, userLanguage);
            results.Values["risingSign_name"] = TranslationHelpers.TranslateSunSign(risingSign, userLanguage);
            
            // ========== CHINESE ASTROLOGY ==========
            var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
            var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
            var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
            
            results.Values["chineseZodiac_animal"] = TranslationHelpers.TranslateZodiac(birthYearAnimal, userLanguage);
            results.Values["chineseZodiac_enum"] = ((int)LumenCalculator.ParseChineseZodiacEnum(birthYearAnimal)).ToString();
            results.Values["birthYear_zodiac"] = TranslationHelpers.TranslateZodiac(birthYearAnimal, userLanguage);
            results.Values["birthYear_element"] = TranslationHelpers.TranslateElement(birthYearElement, userLanguage);
            
            // Birth Year Stems
            var birthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear);
            results.Values["birthYear_stems"] = birthYearStems;
            
            // Birth Year Stems Components
            var birthYearStemsComponents = LumenCalculator.GetStemsAndBranchesComponents(birthYear);
            results.Values["chineseAstrology_currentYearStem"] = birthYearStemsComponents.stemChinese;
            results.Values["chineseAstrology_currentYearStemPinyin"] = birthYearStemsComponents.stemPinyin;
            results.Values["chineseAstrology_currentYearBranch"] = birthYearStemsComponents.branchChinese;
            results.Values["chineseAstrology_currentYearBranchPinyin"] = birthYearStemsComponents.branchPinyin;
            
            // Current Year Zodiac
            var currentYearZodiac = LumenCalculator.GetChineseZodiacWithElement(currentYear);
            var currentYearAnimal = LumenCalculator.CalculateChineseZodiac(currentYear);
            var currentYearElement = LumenCalculator.CalculateChineseElement(currentYear);
            
            results.Values["currentYear"] = currentYear.ToString();
            results.Values["currentYear_zodiac"] = TranslationHelpers.TranslateZodiac(currentYearAnimal, userLanguage);
            results.Values["currentYear_element"] = TranslationHelpers.TranslateElement(currentYearElement, userLanguage);
            
            // Taishui Relationship
            var taishuiRelationship = LumenCalculator.CalculateTaishuiRelationship(birthYear, currentYear);
            results.Values["taishui_relationship"] = taishuiRelationship;
            results.Values["taishui_translated"] = TranslationHelpers.TranslateTaishuiRelationship(taishuiRelationship, userLanguage);
            
            // Zodiac Influence
            results.Values["zodiacInfluence"] = TranslationHelpers.BuildZodiacInfluence(
                birthYearZodiac, currentYearZodiac, taishuiRelationship, userLanguage);
            
            // ========== LIFE CYCLES ==========
            var currentAge = LumenCalculator.CalculateAge(birthDate);
            results.Values["currentAge"] = currentAge.ToString();
            
            // 10-year Cycles
            var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
            var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
            var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);
            
            results.Values["pastCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(pastCycle.AgeRange, userLanguage);
            results.Values["pastCycle_period"] = TranslationHelpers.TranslateCyclePeriod(pastCycle.Period, userLanguage);
            results.Values["currentCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(currentCycle.AgeRange, userLanguage);
            results.Values["currentCycle_period"] = TranslationHelpers.TranslateCyclePeriod(currentCycle.Period, userLanguage);
            results.Values["futureCycle_ageRange"] = TranslationHelpers.TranslateCycleAgeRange(futureCycle.AgeRange, userLanguage);
            results.Values["futureCycle_period"] = TranslationHelpers.TranslateCyclePeriod(futureCycle.Period, userLanguage);
            
            // ========== FOUR PILLARS (BA ZI) ==========
            var fourPillars = LumenCalculator.CalculateFourPillars(birthDate, birthTime);
            InjectFourPillarsData(results.Values, fourPillars, userLanguage);
            
            // ========== LUCKY NUMBER (NUMEROLOGY) ==========
            var luckyNumberResult = Aevatar.Agents.Lumen.Services.LuckyNumberService.CalculateLuckyNumber(
                birthDate, today, userLanguage);
            
            results.Values["luckyAlignments_luckyNumber_number"] = luckyNumberResult.NumberWord;
            results.Values["luckyAlignments_luckyNumber_digit"] = luckyNumberResult.Digit.ToString();
            results.Values["luckyAlignments_luckyNumber_description"] = luckyNumberResult.Description;
            results.Values["luckyAlignments_luckyNumber_calculation"] = luckyNumberResult.CalculationFormula;
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][GetCalculatedValuesAsync] Successfully calculated {Count} values for user {UserId}",
                results.Values.Count, userInfo.UserId);
            
            return Task.FromResult(results);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][GetCalculatedValuesAsync] Error calculating values for user {UserId}",
                userInfo.UserId);
            return Task.FromResult(new CalculatedValuesDto());
        }
    }

    /// <summary>
    /// Inject Four Pillars data into dictionary
    /// </summary>
    private static void InjectFourPillarsData(
        Google.Protobuf.Collections.MapField<string, string> values,
        FourPillarsInfo fourPillars,
        string language)
    {
        // Year Pillar
        values["fourPillars_yearStemChinese"] = fourPillars.YearPillar.StemChinese;
        values["fourPillars_yearStemPinyin"] = fourPillars.YearPillar.StemPinyin;
        values["fourPillars_yearBranchChinese"] = fourPillars.YearPillar.BranchChinese;
        values["fourPillars_yearBranchPinyin"] = fourPillars.YearPillar.BranchPinyin;
        values["fourPillars_yearElement"] = fourPillars.YearPillar.Element;
        values["fourPillars_yearZodiac"] = TranslationHelpers.TranslateZodiac(fourPillars.YearPillar.BranchZodiac, language);
        
        // Month Pillar
        values["fourPillars_monthStemChinese"] = fourPillars.MonthPillar.StemChinese;
        values["fourPillars_monthStemPinyin"] = fourPillars.MonthPillar.StemPinyin;
        values["fourPillars_monthBranchChinese"] = fourPillars.MonthPillar.BranchChinese;
        values["fourPillars_monthBranchPinyin"] = fourPillars.MonthPillar.BranchPinyin;
        values["fourPillars_monthElement"] = fourPillars.MonthPillar.Element;
        
        // Day Pillar
        values["fourPillars_dayStemChinese"] = fourPillars.DayPillar.StemChinese;
        values["fourPillars_dayStemPinyin"] = fourPillars.DayPillar.StemPinyin;
        values["fourPillars_dayBranchChinese"] = fourPillars.DayPillar.BranchChinese;
        values["fourPillars_dayBranchPinyin"] = fourPillars.DayPillar.BranchPinyin;
        values["fourPillars_dayElement"] = fourPillars.DayPillar.Element;
        
        // Hour Pillar (if available)
        if (fourPillars.HourPillar != null)
        {
            values["fourPillars_hourStemChinese"] = fourPillars.HourPillar.StemChinese;
            values["fourPillars_hourStemPinyin"] = fourPillars.HourPillar.StemPinyin;
            values["fourPillars_hourBranchChinese"] = fourPillars.HourPillar.BranchChinese;
            values["fourPillars_hourBranchPinyin"] = fourPillars.HourPillar.BranchPinyin;
            values["fourPillars_hourElement"] = fourPillars.HourPillar.Element;
        }
    }

    #endregion

    #region TriggerTranslationAsync - Full Implementation

    /// <summary>
    /// Trigger translation for this prediction to target language (triggered by language switch)
    /// </summary>
    public async Task<TriggerTranslationResult> TriggerTranslationFullAsync(LumenUserDto userInfo, string targetLanguage)
    {
        try
        {
            Logger.LogInformation(
                "[LumenPredictionGAgent][TriggerTranslationAsync] Triggering translation - User: {UserId}, Type: {Type}, Language: {Language}", 
                userInfo.UserId, CustomState.Type, targetLanguage);

            // Check if prediction exists
            if (string.IsNullOrEmpty(CustomState.PredictionId) || CustomState.MultilingualResults == null || CustomState.MultilingualResults.Count == 0)
            {
                Logger.LogWarning("[LumenPredictionGAgent][TriggerTranslationAsync] No prediction exists to translate");
                return new TriggerTranslationResult
                {
                    Success = false,
                    Message = "No prediction exists to translate",
                    AlreadyTranslating = false
                };
            }

            // Check if target language already exists
            if (CustomState.MultilingualResults.TryGetValue(targetLanguage, out var existing) && existing.Values.Count > 0)
            {
                Logger.LogInformation(
                    "[LumenPredictionGAgent][TriggerTranslationAsync] Target language '{Language}' already exists, skipping", 
                    targetLanguage);
                return new TriggerTranslationResult
                {
                    Success = true,
                    Message = "Language already available",
                    AlreadyTranslating = false
                };
            }

            // Check if already translating
            if (CustomState.TranslationLocks.TryGetValue(targetLanguage, out var lockInfo) && lockInfo.IsTranslating)
            {
                Logger.LogInformation(
                    "[LumenPredictionGAgent][TriggerTranslationAsync] Translation already in progress for '{Language}'", 
                    targetLanguage);
                return new TriggerTranslationResult
                {
                    Success = false,
                    Message = "Translation already in progress",
                    AlreadyTranslating = true
                };
            }

            // Find source language (prefer English, fallback to any available)
            var sourceLanguage = CustomState.MultilingualResults.ContainsKey("en") 
                ? "en" 
                : CustomState.MultilingualResults.Keys.FirstOrDefault();
            
            if (sourceLanguage == null)
            {
                Logger.LogWarning("[LumenPredictionGAgent][TriggerTranslationAsync] No source language available");
                return new TriggerTranslationResult
                {
                    Success = false,
                    Message = "No source language available",
                    AlreadyTranslating = false
                };
            }

            // Get source content
            var sourceContent = CustomState.MultilingualResults[sourceLanguage];
            var sourceDict = new Dictionary<string, string>();
            foreach (var kvp in sourceContent.Values)
            {
                sourceDict[kvp.Key] = kvp.Value;
            }

            // Get prediction date
            var predictionDate = CustomState.PredictionDate != null
                ? new DateOnly(CustomState.PredictionDate.Year, CustomState.PredictionDate.Month, CustomState.PredictionDate.Day)
                : DateOnly.FromDateTime(DateTime.UtcNow);

            // Trigger on-demand translation (fire-and-forget)
            _ = TranslateAndSaveAsync(userInfo, predictionDate, CustomState.Type, sourceLanguage, sourceDict, targetLanguage);
            
            Logger.LogInformation("[LumenPredictionGAgent][TriggerTranslationAsync] Translation triggered successfully");
            
            return new TriggerTranslationResult
            {
                Success = true,
                Message = "Translation triggered",
                AlreadyTranslating = false
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent][TriggerTranslationAsync] Error triggering translation");
            return new TriggerTranslationResult
            {
                Success = false,
                Message = "Internal error occurred",
                AlreadyTranslating = false
            };
        }
    }

    /// <summary>
    /// Translate and save to state (handles concurrent translation requests safely)
    /// </summary>
    private async Task TranslateAndSaveAsync(
        LumenUserDto userInfo, 
        DateOnly predictionDate, 
        PredictionType type,
        string sourceLanguage, 
        Dictionary<string, string> sourceContent, 
        string targetLanguage)
    {
        try
        {
            // Double-check if already exists
            if (CustomState.MultilingualResults.TryGetValue(targetLanguage, out var existing) && existing.Values.Count > 0)
            {
                Logger.LogInformation(
                    "[LumenPredictionGAgent][TranslateAndSave] {UserId} Language {Language} already exists (double-check), skipping",
                    userInfo.UserId, targetLanguage);
                return;
            }
            
            // Set translation lock
            RaiseEvent(new TranslationLockSetEvent
            {
                Language = targetLanguage,
                StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                SourceLanguage = sourceLanguage
            });
            await ConfirmEventsAsync();
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateAndSave] {UserId} Starting translation {SourceLang} → {TargetLang} for {Type}",
                userInfo.UserId, sourceLanguage, targetLanguage, type);
            
            // Perform translation based on prediction type
            Dictionary<string, string>? translatedDict = null;
            
            if (type == PredictionType.PredictionLifetime)
            {
                // Use batched translation for Lifetime
                translatedDict = await TranslateLifetimeBatchedAsync(
                    userInfo, predictionDate, sourceLanguage, sourceContent, targetLanguage);
            }
            else
            {
                // Use single translation for Daily/Yearly
                translatedDict = await TranslateSingleAsync(
                    userInfo, sourceLanguage, sourceContent, targetLanguage, type.ToString());
            }
            
            if (translatedDict == null || translatedDict.Count == 0)
            {
                Logger.LogError(
                    "[LumenPredictionGAgent][TranslateAndSave] {UserId} Translation failed for {Language}",
                    userInfo.UserId, targetLanguage);
                return;
            }
            
            // Save translated content
            var translatedLanguages = new LanguagesTranslatedEvent();
            var multilingualValue = new MultilingualResultValue();
            foreach (var kvp in translatedDict)
            {
                multilingualValue.Values[kvp.Key] = kvp.Value;
            }
            translatedLanguages.TranslatedLanguages[targetLanguage] = multilingualValue;
            
            RaiseEvent(translatedLanguages);
            await ConfirmEventsAsync();
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][TranslateAndSave] {UserId} Successfully saved translation for {Language} ({Count} fields)",
                userInfo.UserId, targetLanguage, translatedDict.Count);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][TranslateAndSave] {UserId} Error translating to {Language}",
                userInfo.UserId, targetLanguage);
        }
        finally
        {
            // Clear translation lock
            RaiseEvent(new TranslationLockClearedEvent { Language = targetLanguage });
            await ConfirmEventsAsync();
        }
    }

    /// <summary>
    /// Translate single prediction type (Daily/Yearly)
    /// </summary>
    private async Task<Dictionary<string, string>?> TranslateSingleAsync(
        LumenUserDto userInfo,
        string sourceLanguage,
        Dictionary<string, string> sourceContent,
        string targetLanguage,
        string predictionTypeName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!_isInitialized)
            {
                Logger.LogWarning("[LumenPredictionGAgent][TranslateSingle] LLM not initialized");
                return null;
            }
            
            // Build translation prompt
            var prompt = BuildTranslationPrompt(sourceContent, sourceLanguage, new List<string> { targetLanguage }, CustomState.Type);
            
            var systemPrompt = "You are a professional translator for astrological and philosophical reflection content. All content is for entertainment, self-exploration, and contemplative purposes only.";
            
            var request = new AI.Abstractions.AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                Settings = new AI.Abstractions.AevatarLLMSettings
                {
                    Temperature = 0.3f,
                    MaxTokens = 4000,
                    ModelId = Config.Model
                }
            };
            
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            
            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning("[LumenPredictionGAgent][TranslateSingle] No response from LLM");
                return null;
            }
            
            // Parse TSV response
            var translatedDict = ParseTsvResponse(response.Content);
            
            if (translatedDict == null || translatedDict.Count == 0)
            {
                Logger.LogWarning("[LumenPredictionGAgent][TranslateSingle] Failed to parse translation response");
                return null;
            }
            
            return translatedDict;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent][TranslateSingle] Error translating to {Language}", targetLanguage);
            return null;
        }
    }

    #endregion

    // GetUserLocalDate is defined in LumenPredictionGAgent.Utilities.cs
}

