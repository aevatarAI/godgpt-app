using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Lumen.Calculators;
using Aevatar.Agents.Lumen.Prediction.Services;
using Aevatar.Agents.Lumen.Protos;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// Lifetime prediction batch type for parallel LLM calls
/// </summary>
public enum LifetimeBatchType
{
    /// <summary>Western Astrology: Sun/Moon/Rising archetypes, combined essence, zodiac whisper</summary>
    WesternAstrology = 0,
    
    /// <summary>Chinese Astrology: Four Pillars, Chinese traits, zodiac cycle</summary>
    ChineseAstrology = 1,
    
    /// <summary>Life Traits: Strengths, challenges, destiny paths</summary>
    LifeTraits = 2,
    
    /// <summary>Timeline Plot: 10-year cycles, life narrative</summary>
    TimelinePlot = 3
}

/// <summary>
/// Context data for Lifetime batch generation (pre-calculated values shared across batches)
/// </summary>
public class LifetimeBatchContext
{
    public string SunSign { get; set; } = string.Empty;
    public string SunSignTranslated { get; set; } = string.Empty;
    public int BirthYear { get; set; }
    public string BirthYearZodiac { get; set; } = string.Empty;
    public string BirthYearAnimal { get; set; } = string.Empty;
    public string BirthYearAnimalTranslated { get; set; } = string.Empty;
    public string BirthYearElement { get; set; } = string.Empty;
    public string BirthYearStems { get; set; } = string.Empty;
    public int CurrentAge { get; set; }
    public string PastCycleAgeRange { get; set; } = string.Empty;
    public string CurrentCycleAgeRange { get; set; } = string.Empty;
    public string FutureCycleAgeRange { get; set; } = string.Empty;
    public string CurrentYearStemsFormatted { get; set; } = string.Empty;
}

/// <summary>
/// LumenPredictionGAgent - Lifetime Batched Generation
/// Handles parallel batch generation for Lifetime predictions
/// </summary>
public partial class LumenPredictionGAgent
{
    #region Lifetime Batch Generation

    /// <summary>
    /// Generate Lifetime prediction using parallel batched LLM calls
    /// Returns updated moonSign/risingSign if recalculated during pre-batch LatLong inference
    /// </summary>
    private async Task<(Dictionary<string, string>? Results, Dictionary<string, Dictionary<string, string>>? MultilingualResults, string? MoonSign, string? RisingSign)> 
        GenerateLifetimeBatchedAsync(
            LumenUserDto userInfo, 
            DateOnly predictionDate,
            string targetLanguage,
            string? moonSign,
            string? risingSign,
            LifetimeBatchContext context,
            CancellationToken cancellationToken = default)
    {
        var totalStopwatch = Stopwatch.StartNew();
        Logger.LogInformation(
            "[LumenPredictionGAgent][GenerateLifetimeBatched] Starting parallel batch generation for user {UserId}, Language: {Language}",
            userInfo.UserId, targetLanguage);

        try
        {
            // PRE-BATCH: Infer LatLong first if needed (to calculate accurate Moon/Rising signs)
            var needLatLongInference = !string.IsNullOrWhiteSpace(userInfo.BirthCity) 
                && string.IsNullOrWhiteSpace(userInfo.LatLong) 
                && string.IsNullOrWhiteSpace(userInfo.LatLongInferred);
            
            if (needLatLongInference && userInfo.BirthTime != null)
            {
                Logger.LogInformation(
                    "[LumenPredictionGAgent][GenerateLifetimeBatched] {UserId} Inferring LatLong BEFORE batch generation",
                    userInfo.UserId);
                
                var latLongInferred = await InferLatLongAsync(userInfo, predictionDate, targetLanguage, cancellationToken);
                
                if (!string.IsNullOrWhiteSpace(latLongInferred))
                {
                    Logger.LogInformation(
                        "[LumenPredictionGAgent][GenerateLifetimeBatched] {UserId} Successfully inferred LatLong: {LatLong}",
                        userInfo.UserId, latLongInferred);
                    
                    // Note: Moon/Rising sign calculation would require SwissEphNet
                    // For now, we'll use the Sun sign as fallback
                    moonSign ??= context.SunSign;
                    risingSign ??= context.SunSign;
                }
            }

            // Create batch tasks for parallel execution
            var batchTasks = new Dictionary<LifetimeBatchType, Task<Dictionary<string, string>?>>
            {
                [LifetimeBatchType.WesternAstrology] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign, 
                    LifetimeBatchType.WesternAstrology, context, cancellationToken),
                    
                [LifetimeBatchType.ChineseAstrology] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.ChineseAstrology, context, cancellationToken),
                    
                [LifetimeBatchType.LifeTraits] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.LifeTraits, context, cancellationToken),
                    
                [LifetimeBatchType.TimelinePlot] = GenerateLifetimeBatchAsync(
                    userInfo, predictionDate, targetLanguage, moonSign, risingSign,
                    LifetimeBatchType.TimelinePlot, context, cancellationToken)
            };

            // Wait for all batches to complete
            await Task.WhenAll(batchTasks.Values);
            
            totalStopwatch.Stop();
            Logger.LogInformation(
                "[PERF][LumenPredictionGAgent][Batched] {UserId} All_Batches: {ElapsedMs}ms",
                userInfo.UserId, totalStopwatch.ElapsedMilliseconds);

            // Merge results from all batches
            var mergedResults = new Dictionary<string, string>();
            var successCount = 0;
            var failedBatches = new List<LifetimeBatchType>();

            foreach (var (batchType, task) in batchTasks)
            {
                var batchResult = await task;
                if (batchResult != null && batchResult.Count > 0)
                {
                    foreach (var (key, value) in batchResult)
                    {
                        mergedResults[key] = value;
                    }
                    successCount++;
                    Logger.LogDebug(
                        "[LumenPredictionGAgent][GenerateLifetimeBatched] Batch {BatchType} returned {Count} fields",
                        batchType, batchResult.Count);
                }
                else
                {
                    failedBatches.Add(batchType);
                    Logger.LogWarning(
                        "[LumenPredictionGAgent][GenerateLifetimeBatched] Batch {BatchType} failed or returned empty",
                        batchType);
                }
            }

            // Check if enough batches succeeded (at least 3 of 4)
            if (successCount < 3)
            {
                Logger.LogError(
                    "[LumenPredictionGAgent][GenerateLifetimeBatched] Too many batches failed ({FailedCount}/4): {FailedBatches}",
                    failedBatches.Count, string.Join(", ", failedBatches));
                return (null, null, moonSign, risingSign);
            }

            Logger.LogInformation(
                "[LumenPredictionGAgent][GenerateLifetimeBatched] Successfully merged {FieldCount} fields from {SuccessCount}/4 batches",
                mergedResults.Count, successCount);

            // Build multilingual results
            var multilingualResults = new Dictionary<string, Dictionary<string, string>>
            {
                [targetLanguage] = mergedResults
            };

            return (mergedResults, multilingualResults, moonSign, risingSign);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][GenerateLifetimeBatched] Error during batch generation for user {UserId}",
                userInfo.UserId);
            return (null, null, moonSign, risingSign);
        }
    }

    /// <summary>
    /// Generate a single batch of Lifetime prediction fields
    /// </summary>
    private async Task<Dictionary<string, string>?> GenerateLifetimeBatchAsync(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchType batchType,
        LifetimeBatchContext context,
        CancellationToken cancellationToken = default)
    {
        var batchStopwatch = Stopwatch.StartNew();
        var batchName = batchType.ToString();
        
        try
        {
            // Build batch-specific prompt
            var prompt = BuildLifetimeBatchPrompt(userInfo, predictionDate, targetLanguage, moonSign, risingSign, batchType, context);
            
            Logger.LogDebug(
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} prompt: {Length} chars",
                batchType, prompt.Length);

            // Build system prompt for this batch
            var systemPrompt = BuildLifetimeBatchSystemPrompt(targetLanguage, batchType);

            // Check if LLM provider is initialized
            if (!_isInitialized)
            {
                Logger.LogWarning(
                    "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} - LLM not initialized",
                    batchType);
                return null;
            }

            // Make LLM call
            var llmStopwatch = Stopwatch.StartNew();
            var request = new AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.7f,
                    MaxTokens = 2000,
                    ModelId = Config.Model
                }
            };
            
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            llmStopwatch.Stop();

            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning(
                    "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} - No response from LLM",
                    batchType);
                return null;
            }

            var aiResponse = response.Content;
            Logger.LogInformation(
                "[PERF][LumenPredictionGAgent][Batch] {UserId} {BatchType}: {ElapsedMs}ms, {ResponseLength} chars",
                userInfo.UserId, batchType, llmStopwatch.ElapsedMilliseconds, aiResponse.Length);

            // Parse TSV response
            var parsedResults = ParseTsvResponse(aiResponse);
            
            if (parsedResults == null || parsedResults.Count == 0)
            {
                Logger.LogWarning(
                    "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} - Failed to parse response",
                    batchType);
                return null;
            }

            batchStopwatch.Stop();
            Logger.LogDebug(
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} completed: {FieldCount} fields in {ElapsedMs}ms",
                batchType, parsedResults.Count, batchStopwatch.ElapsedMilliseconds);

            return parsedResults;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                "[LumenPredictionGAgent][GenerateLifetimeBatch] {BatchType} failed for user {UserId}",
                batchType, userInfo.UserId);
            return null;
        }
    }

    #endregion

    #region LatLong Inference

    /// <summary>
    /// Infer latitude/longitude from birth city using LLM
    /// Falls back gracefully if unable to infer (returns null without throwing)
    /// </summary>
    private async Task<string?> InferLatLongAsync(
        LumenUserDto userInfo, 
        DateOnly predictionDate, 
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userInfo.BirthCity))
            {
                return null;
            }

            // Relaxed prompt - prioritizes best-effort inference over strict validation
            var prompt = $@"Task: Provide the approximate latitude and longitude for the following location.

Location: {userInfo.BirthCity}

Instructions:
- If you recognize the location, provide its approximate coordinates
- For ambiguous names (e.g., ""Springfield""), choose the most well-known one
- If you're uncertain but have a reasonable guess, provide it
- Only return UNKNOWN if the location is completely unrecognizable or nonsensical
- Format: latitude,longitude (decimal degrees, e.g., 34.0522,-118.2437)

Output format (TSV):
location_latlong	latitude,longitude

Examples:
location_latlong	34.0522,-118.2437
location_latlong	UNKNOWN";

            var systemPrompt = "You are a helpful geography assistant. Make your best effort to provide coordinates, even if uncertain. Only refuse if the location is completely unrecognizable.";

            // Check if LLM provider is initialized
            if (!_isInitialized)
            {
                Logger.LogWarning("[LumenPredictionGAgent][LatLongInference] LLM not initialized");
                return null;
            }

            var llmStopwatch = Stopwatch.StartNew();
            var request = new AevatarLLMRequest
            {
                SystemPrompt = systemPrompt,
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.3f,
                    MaxTokens = 100,
                    ModelId = Config.Model
                }
            };
            
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            llmStopwatch.Stop();
            
            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning(
                    "[LumenPredictionGAgent][LatLongInference] {UserId} No response from LLM for city: {BirthCity}",
                    userInfo.UserId, userInfo.BirthCity);
                return null;
            }
            
            var aiResponse = response.Content;
            Logger.LogInformation(
                "[PERF][LumenPredictionGAgent][LatLongInference] {UserId} LLM_Call: {ElapsedMs}ms, City: {BirthCity}",
                userInfo.UserId, llmStopwatch.ElapsedMilliseconds, userInfo.BirthCity);
            
            // Parse TSV response
            var tsvResult = ParseTsvResponse(aiResponse);
            if (tsvResult != null && tsvResult.TryGetValue("location_latlong", out var latLong) && 
                !string.IsNullOrWhiteSpace(latLong) && latLong.ToUpperInvariant() != "UNKNOWN")
            {
                // Validate format (basic check for lat,lon pattern)
                var parts = latLong.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length == 2 && 
                    double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _) &&
                    double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                {
                    Logger.LogInformation(
                        "[LumenPredictionGAgent][LatLongInference] {UserId} Successfully inferred LatLong: {LatLong} from city: {BirthCity}",
                        userInfo.UserId, latLong, userInfo.BirthCity);
                    return latLong;
                }
                
                Logger.LogWarning(
                    "[LumenPredictionGAgent][LatLongInference] {UserId} Invalid LatLong format: {LatLong}",
                    userInfo.UserId, latLong);
                return null;
            }
            
            Logger.LogInformation(
                "[LumenPredictionGAgent][LatLongInference] {UserId} LLM returned UNKNOWN or empty for city: {BirthCity}",
                userInfo.UserId, userInfo.BirthCity);
            return null;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, 
                "[LumenPredictionGAgent][LatLongInference] Error inferring LatLong for user {UserId}, city: {BirthCity}",
                userInfo.UserId, userInfo.BirthCity);
            return null;
        }
    }

    #endregion

    #region Batch Context

    /// <summary>
    /// Create batch context with pre-calculated values
    /// </summary>
    private LifetimeBatchContext CreateLifetimeBatchContext(
        LumenUserDto userInfo, 
        DateOnly predictionDate,
        string targetLanguage)
    {
        var birthDateValue = userInfo.BirthDate;
        if (birthDateValue == null)
        {
            return new LifetimeBatchContext();
        }
        
        // Convert DateValue to DateOnly
        var birthDate = new DateOnly(birthDateValue.Year, birthDateValue.Month, birthDateValue.Day);
        
        var birthYear = birthDate.Year;
        var currentYear = predictionDate.Year;
        var sunSign = LumenCalculator.CalculateZodiacSign(birthDate);
        var birthYearZodiac = LumenCalculator.GetChineseZodiacWithElement(birthYear);
        var birthYearAnimal = LumenCalculator.CalculateChineseZodiac(birthYear);
        var birthYearElement = LumenCalculator.CalculateChineseElement(birthYear);
        
        var pastCycle = LumenCalculator.CalculateTenYearCycle(birthYear, -1);
        var currentCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 0);
        var futureCycle = LumenCalculator.CalculateTenYearCycle(birthYear, 1);

        return new LifetimeBatchContext
        {
            SunSign = sunSign,
            SunSignTranslated = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage),
            BirthYear = birthYear,
            BirthYearZodiac = birthYearZodiac,
            BirthYearAnimal = birthYearAnimal,
            BirthYearAnimalTranslated = TranslationHelpers.TranslateZodiac(birthYearAnimal, targetLanguage),
            BirthYearElement = birthYearElement,
            BirthYearStems = LumenCalculator.CalculateStemsAndBranches(birthYear),
            CurrentAge = LumenCalculator.CalculateAge(birthDate),
            PastCycleAgeRange = pastCycle.AgeRange,
            CurrentCycleAgeRange = currentCycle.AgeRange,
            FutureCycleAgeRange = futureCycle.AgeRange,
            CurrentYearStemsFormatted = LumenCalculator.CalculateStemsAndBranches(currentYear)
        };
    }

    #endregion

    #region Batch System Prompts

    /// <summary>
    /// Build system prompt for a Lifetime batch
    /// </summary>
    private static string BuildLifetimeBatchSystemPrompt(string targetLanguage, LifetimeBatchType batchType)
    {
        var languageMap = new Dictionary<string, string>
        {
            { "en", "English" },
            { "zh-tw", "繁體中文" },
            { "zh", "简体中文" },
            { "es", "Español" }
        };
        var languageName = languageMap.GetValueOrDefault(targetLanguage, "English");

        var batchFocus = batchType switch
        {
            LifetimeBatchType.WesternAstrology => "Western Astrology archetypes, zodiac insights, and combined essence",
            LifetimeBatchType.ChineseAstrology => "Chinese Astrology, Four Pillars (BaZi), and zodiac cycles",
            LifetimeBatchType.LifeTraits => "Personal strengths, challenges, and destiny paths",
            LifetimeBatchType.TimelinePlot => "Life timeline, 10-year luck cycles, and narrative plot",
            _ => "astrological content"
        };

        return $@"You are a creative astrology guide creating {batchFocus} content.

===== LANGUAGE REQUIREMENT (CRITICAL) =====
Target Language: {languageName}

RULES:
1. Write ALL field values in {languageName} ONLY
2. Field names remain in English (lowercase with underscores)
3. Do NOT mix languages in field values
4. If {languageName} is not English, translate ALL descriptive text
============================================

CONTENT STYLE:
- Be SPECIFIC and DESCRIPTIVE - avoid vague, mystical language
- Provide ACTIONABLE insights users can apply
- Create PERSONALIZED content that addresses the user directly
- Write naturally with quality over strict length requirements
- Focus on empowerment, awareness, and personal growth

FORMAT:
- Return raw TSV (Tab-Separated Values)
- Use ACTUAL TAB CHARACTER between field name and value
- NO JSON, NO markdown, NO extra text
- Start immediately with the first field";
    }

    #endregion

    #region Batch Prompt Building

    /// <summary>
    /// Build prompt for a specific Lifetime batch
    /// </summary>
    private string BuildLifetimeBatchPrompt(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchType batchType,
        LifetimeBatchContext ctx)
    {
        return batchType switch
        {
            LifetimeBatchType.WesternAstrology => BuildWesternAstrologyBatchPrompt(userInfo, targetLanguage, moonSign, risingSign, ctx),
            LifetimeBatchType.ChineseAstrology => BuildChineseAstrologyBatchPrompt(userInfo, targetLanguage, ctx),
            LifetimeBatchType.LifeTraits => BuildLifeTraitsBatchPrompt(userInfo, targetLanguage, ctx),
            LifetimeBatchType.TimelinePlot => BuildTimelinePlotBatchPrompt(userInfo, targetLanguage, ctx),
            _ => throw new ArgumentException($"Unknown batch type: {batchType}")
        };
    }

    /// <summary>
    /// Batch 1: Western Astrology - Sun/Moon/Rising archetypes, combined essence, zodiac whisper
    /// </summary>
    private string BuildWesternAstrologyBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        string? moonSign,
        string? risingSign,
        LifetimeBatchContext ctx)
    {
        var sunSign = ctx.SunSign;
        moonSign ??= sunSign;
        risingSign ??= sunSign;
        
        var sunSignTranslated = TranslationHelpers.TranslateSunSign(sunSign, targetLanguage);
        var moonSignTranslated = TranslationHelpers.TranslateSunSign(moonSign, targetLanguage);
        var risingSignTranslated = TranslationHelpers.TranslateSunSign(risingSign, targetLanguage);

        var languageReminder = GetLanguageReminder(targetLanguage);

        return $@"{languageReminder}

User Profile:
- Sun Sign: {sunSignTranslated} ({sunSign})
- Moon Sign: {moonSignTranslated} ({moonSign})
- Rising Sign: {risingSignTranslated} ({risingSign})
- Birth Year Animal: {ctx.BirthYearAnimalTranslated}

Generate the following fields (TSV format):

combined_essence	Describe how your Sun, Moon, and Rising signs work together. (60-80 words)
zodiac_whisper	Start with '{ctx.BirthYearAnimalTranslated}' and offer specific advice. (40-60 words)
sun_archetype	Your Sun sign archetype and core identity. (30-50 words)
moon_archetype	Your Moon sign emotional nature. (30-50 words)
rising_archetype	Your Rising sign external expression. (30-50 words)";
    }

    /// <summary>
    /// Batch 2: Chinese Astrology - Four Pillars, zodiac cycle
    /// </summary>
    private string BuildChineseAstrologyBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        var languageReminder = GetLanguageReminder(targetLanguage);

        return $@"{languageReminder}

User Profile:
- Birth Year: {ctx.BirthYear}
- Chinese Zodiac: {ctx.BirthYearAnimalTranslated} ({ctx.BirthYearAnimal})
- Element: {ctx.BirthYearElement}
- Heavenly Stems & Earthly Branches: {ctx.BirthYearStems}
- Current Age: {ctx.CurrentAge}

Generate the following fields (TSV format):

zodiac_cycle_intro	Start with 'Your zodiac is {ctx.BirthYearAnimalTranslated}...' and describe how 20-year cycles affect life stages. (50-70 words)
four_pillars_summary	Brief summary of your Four Pillars (BaZi) destiny. (40-60 words)
chinese_element_influence	How your birth element ({ctx.BirthYearElement}) influences your personality. (30-50 words)
zodiac_compatibility	Your zodiac compatibility insights. (30-50 words)";
    }

    /// <summary>
    /// Batch 3: Life Traits - Strengths, challenges, destiny paths
    /// </summary>
    private string BuildLifeTraitsBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        var languageReminder = GetLanguageReminder(targetLanguage);

        return $@"{languageReminder}

User Profile:
- Sun Sign: {ctx.SunSignTranslated}
- Chinese Zodiac: {ctx.BirthYearAnimalTranslated}
- Element: {ctx.BirthYearElement}
- Current Age: {ctx.CurrentAge}

Generate the following fields (TSV format):

core_strengths	Your top 3-5 innate strengths with specific examples. (50-70 words)
growth_challenges	Areas for personal development with actionable advice. (50-70 words)
destiny_paths	Career and life path suggestions based on your chart. (50-70 words)
soul_purpose	Your deeper life purpose and spiritual calling. (40-60 words)
relationship_style	How you approach relationships and connections. (40-60 words)";
    }

    /// <summary>
    /// Batch 4: Timeline Plot - 10-year cycles, life narrative
    /// </summary>
    private string BuildTimelinePlotBatchPrompt(
        LumenUserDto userInfo,
        string targetLanguage,
        LifetimeBatchContext ctx)
    {
        var languageReminder = GetLanguageReminder(targetLanguage);

        return $@"{languageReminder}

User Profile:
- Birth Year: {ctx.BirthYear}
- Current Age: {ctx.CurrentAge}
- Past Cycle: {ctx.PastCycleAgeRange}
- Current Cycle: {ctx.CurrentCycleAgeRange}
- Future Cycle: {ctx.FutureCycleAgeRange}

Generate the following fields (TSV format):

past_cycle_summary	Review of the past 10-year cycle ({ctx.PastCycleAgeRange}). (40-60 words)
current_cycle_focus	Focus areas for current 10-year cycle ({ctx.CurrentCycleAgeRange}). (50-70 words)
future_cycle_preview	Preview of next 10-year cycle ({ctx.FutureCycleAgeRange}). (40-60 words)
life_plot_summary	Your overall life narrative arc. (50-70 words)
current_year_advice	Specific advice for this year. (40-60 words)";
    }

    /// <summary>
    /// Get language reminder text
    /// </summary>
    private static string GetLanguageReminder(string targetLanguage)
    {
        return targetLanguage switch
        {
            "zh" => "⚠️ 重要：所有字段内容必须使用简体中文，不要出现英文！",
            "zh-tw" => "⚠️ 重要：所有欄位內容必須使用繁體中文，不要出現英文！",
            "es" => "⚠️ IMPORTANTE: Todo el contenido debe estar en español, ¡sin inglés!",
            _ => "⚠️ IMPORTANT: All content must be in English."
        };
    }

    #endregion
}

