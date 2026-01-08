using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// LumenPredictionGAgent - LLM generation logic
/// Handles prediction generation orchestration and lock management
/// Uses AIGAgentBase's LLMProvider for actual LLM calls
/// </summary>
public partial class LumenPredictionGAgent
{
    // System prompt for prediction generation
    private const string PredictionSystemPrompt = @"You are a professional astrologer and spiritual guide specializing in personalized horoscope readings. 
Your predictions combine Eastern and Western astrology, Chinese metaphysics (Bazi, Five Elements), and spiritual wisdom.
Always provide practical, actionable advice while maintaining an uplifting and supportive tone.
Format your response as TSV (tab-separated values) with field names and values.";

    #region Generation Orchestration

    /// <summary>
    /// Generate prediction with LLM and store results
    /// </summary>
    private async Task<bool> GeneratePredictionCoreAsync(
        LumenUserDto userInfo,
        DateOnly predictionDate,
        PredictionType type,
        string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        // Check if generation is already in progress
        if (IsGenerationLocked(type))
        {
            Logger.LogWarning("Generation already in progress for type {Type}", type);
            return false;
        }
        
        // Acquire generation lock
        await SetGenerationLockAsync(type);
        
        try
        {
            // Build prompt
            var prompt = BuildPredictionPrompt(userInfo, predictionDate, type, targetLanguage);
            
            // Call LLM service
            var llmResponse = await CallLlmAsync(prompt, type, cancellationToken);
            
            if (string.IsNullOrEmpty(llmResponse))
            {
                Logger.LogError("LLM returned empty response for type {Type}", type);
                return false;
            }
            
            // Parse response
            var parsedResults = ParseTsvResponse(llmResponse);
            
            if (parsedResults.Count == 0)
            {
                Logger.LogError("Failed to parse LLM response for type {Type}", type);
                return false;
            }
            
            // Apply transformations
            parsedResults = MapShortKeysToFullKeys(parsedResults);
            parsedResults = ConvertArrayFieldsToJson(parsedResults);
            AddQuotesToAffirmation(parsedResults, targetLanguage);
            
            // Store results
            await StorePredictionResultsAsync(parsedResults, targetLanguage, type, predictionDate);
            
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error generating prediction for type {Type}", type);
            return false;
        }
        finally
        {
            // Release generation lock
            await ClearGenerationLockAsync(type);
        }
    }

    /// <summary>
    /// Store prediction results and emit event
    /// </summary>
    private async Task StorePredictionResultsAsync(
        Dictionary<string, string> results,
        string language,
        PredictionType type,
        DateOnly predictionDate)
    {
        // Create event
        var evt = new PredictionGeneratedEvent
        {
            PredictionId = CustomState.PredictionId,
            UserId = CustomState.UserId,
            CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            Language = language,
            Type = type,
            PredictionDate = new DateValue
            {
                Year = predictionDate.Year,
                Month = predictionDate.Month,
                Day = predictionDate.Day
            }
        };
        
        // Add results to event
        foreach (var kvp in results)
        {
            evt.Results.Add(kvp.Key, kvp.Value);
        }
        
        // Confirm event (triggers TransitionState)
        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        Logger.LogInformation(
            "Prediction generated for type {Type}, language {Language} with {Count} fields",
            type, language, results.Count);
    }

    #endregion

    #region Lock Management

    /// <summary>
    /// Check if generation is locked for the given type
    /// </summary>
    private bool IsGenerationLocked(PredictionType type)
    {
        var typeInt = (int)type;
        if (CustomState.GenerationLocks.TryGetValue(typeInt, out var lockInfo))
        {
            // Check if lock is still valid (not expired)
            if (lockInfo.IsGenerating && lockInfo.StartedAt != null)
            {
                var startedAt = lockInfo.StartedAt.ToDateTime();
                var lockDuration = DateTime.UtcNow - startedAt;
                
                // Lock expires after 5 minutes (safety net)
                if (lockDuration.TotalMinutes < 5)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Set generation lock for the given type
    /// </summary>
    private async Task SetGenerationLockAsync(PredictionType type)
    {
        RaiseEvent(new GenerationLockSetEvent
        {
            Type = type,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            RetryCount = 0
        });
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Clear generation lock for the given type
    /// </summary>
    private async Task ClearGenerationLockAsync(PredictionType type)
    {
        RaiseEvent(new GenerationLockClearedEvent { Type = type });
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Check if translation is locked for the given language
    /// </summary>
    private bool IsTranslationLocked(string language)
    {
        if (CustomState.TranslationLocks.TryGetValue(language, out var lockInfo))
        {
            if (lockInfo.IsTranslating && lockInfo.StartedAt != null)
            {
                var startedAt = lockInfo.StartedAt.ToDateTime();
                var lockDuration = DateTime.UtcNow - startedAt;
                
                // Lock expires after 3 minutes
                if (lockDuration.TotalMinutes < 3)
                {
                    return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Set translation lock for the given language
    /// </summary>
    private async Task SetTranslationLockAsync(string language, string sourceLanguage)
    {
        RaiseEvent(new TranslationLockSetEvent
        {
            Language = language,
            StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
            SourceLanguage = sourceLanguage
        });
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Clear translation lock for the given language
    /// </summary>
    private async Task ClearTranslationLockAsync(string language)
    {
        RaiseEvent(new TranslationLockClearedEvent { Language = language });
        await ConfirmEventsAsync();
    }

    #endregion

    #region LLM Integration

    /// <summary>
    /// Call LLM service to generate prediction using the inherited LLMProvider
    /// </summary>
    private async Task<string> CallLlmAsync(
        string prompt,
        PredictionType type,
        CancellationToken cancellationToken)
    {
        // Check if LLM provider is initialized
        if (!_isInitialized)
        {
            Logger.LogWarning("[LumenPredictionGAgent] LLM provider not initialized, returning empty response");
            return string.Empty;
        }
        
        try
        {
            // Build LLM request using the AI framework
            var request = new AevatarLLMRequest
            {
                SystemPrompt = PredictionSystemPrompt,
                UserPrompt = prompt,
                Settings = new AevatarLLMSettings
                {
                    Temperature = 0.7f,
                    MaxTokens = GetMaxTokensForType(type),
                    ModelId = Config.Model
                }
            };
            
            Logger.LogDebug("[LumenPredictionGAgent] Calling LLM for prediction type {Type}", type);
            
            // Call LLM using the inherited LLMProvider
            var response = await LLMProvider.GenerateAsync(request, cancellationToken);
            
            if (response == null || string.IsNullOrEmpty(response.Content))
            {
                Logger.LogWarning("[LumenPredictionGAgent] LLM returned empty response");
                return string.Empty;
            }
            
            // Log token usage
            if (response.Usage != null)
            {
                Logger.LogInformation(
                    "[LumenPredictionGAgent] LLM call completed - Prompt: {PromptTokens}, Completion: {CompletionTokens}, Total: {TotalTokens}",
                    response.Usage.PromptTokens,
                    response.Usage.CompletionTokens,
                    response.Usage.TotalTokens);
            }
            
            return response.Content;
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error calling LLM for prediction type {Type}", type);
            throw;
        }
    }
    
    /// <summary>
    /// Get max tokens based on prediction type
    /// </summary>
    private static int GetMaxTokensForType(PredictionType type)
    {
        return type switch
        {
            PredictionType.PredictionDaily => 2000,
            PredictionType.PredictionYearly => 4000,
            PredictionType.PredictionLifetime => 6000,
            _ => 2000
        };
    }

    #endregion

    #region Translation Generation

    /// <summary>
    /// Generate translations for remaining languages
    /// </summary>
    private async Task<bool> GenerateTranslationsAsync(
        string sourceLanguage,
        List<string> targetLanguages,
        PredictionType type,
        DateOnly predictionDate,
        CancellationToken cancellationToken = default)
    {
        if (!CustomState.Results.Any())
        {
            Logger.LogWarning("No source content to translate");
            return false;
        }
        
        // Get source content
        var sourceContent = CustomState.Results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        
        foreach (var targetLang in targetLanguages)
        {
            // Skip if already translated
            if (CustomState.GeneratedLanguages.Contains(targetLang))
            {
                continue;
            }
            
            // Skip if translation is locked
            if (IsTranslationLocked(targetLang))
            {
                Logger.LogDebug("Translation locked for language {Language}", targetLang);
                continue;
            }
            
            await SetTranslationLockAsync(targetLang, sourceLanguage);
            
            try
            {
                // Build translation prompt
                var prompt = BuildTranslationPrompt(sourceContent, sourceLanguage, new List<string> { targetLang }, type);
                
                // Call LLM
                var llmResponse = await CallLlmAsync(prompt, type, cancellationToken);
                
                if (!string.IsNullOrEmpty(llmResponse))
                {
                    // Parse translation response
                    var translations = ParseTranslationResponse(llmResponse, targetLang);
                    
                    if (translations.Any())
                    {
                        // Store translated results
                        await StoreTranslationResultsAsync(translations, targetLang, type, predictionDate);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error translating to language {Language}", targetLang);
            }
            finally
            {
                await ClearTranslationLockAsync(targetLang);
            }
        }
        
        return true;
    }

    /// <summary>
    /// Parse translation response from LLM
    /// </summary>
    private Dictionary<string, string> ParseTranslationResponse(string response, string targetLanguage)
    {
        // Find the section for target language
        var sectionMarker = $"[{targetLanguage}]";
        var sectionStart = response.IndexOf(sectionMarker, StringComparison.OrdinalIgnoreCase);
        
        if (sectionStart < 0)
        {
            // Try to parse as plain TSV if no language marker
            return ParseTsvResponse(response);
        }
        
        // Extract content after marker until next marker or end
        var contentStart = sectionStart + sectionMarker.Length;
        var nextMarker = response.IndexOf("[", contentStart, StringComparison.Ordinal);
        var content = nextMarker > 0
            ? response.Substring(contentStart, nextMarker - contentStart)
            : response.Substring(contentStart);
        
        return ParseTsvResponse(content);
    }

    /// <summary>
    /// Store translation results and emit event
    /// </summary>
    private async Task StoreTranslationResultsAsync(
        Dictionary<string, string> translations,
        string language,
        PredictionType type,
        DateOnly predictionDate)
    {
        var evt = new LanguagesTranslatedEvent
        {
            Type = type,
            PredictionDate = new DateValue
            {
                Year = predictionDate.Year,
                Month = predictionDate.Month,
                Day = predictionDate.Day
            }
        };
        
        // Add translations as multilingual result
        var multilingualResult = new MultilingualResultValue();
        foreach (var kvp in translations)
        {
            multilingualResult.Values.Add(kvp.Key, kvp.Value);
        }
        evt.TranslatedLanguages.Add(language, multilingualResult);
        
        // Update all generated languages
        var allLanguages = CustomState.GeneratedLanguages.ToList();
        if (!allLanguages.Contains(language))
        {
            allLanguages.Add(language);
        }
        evt.AllGeneratedLanguages.Add(allLanguages);
        
        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        Logger.LogInformation(
            "Translation completed for language {Language} with {Count} fields",
            language, translations.Count);
    }

    #endregion
}
