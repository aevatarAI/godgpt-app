using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.AI.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.Prediction;

/// <summary>
/// Lumen Prediction GAgent - manages lumen prediction generation
/// 
/// New Framework: Inherits from AIGAgentBase, uses Protobuf State + Event Sourcing
/// With built-in LLM capabilities via LLMProvider
/// </summary>
public partial class LumenPredictionGAgent : AIGAgentBase<LumenPredictionState>, ILumenPredictionGAgent
{
    // Configuration constants
    private const int DefaultMaxRetryCount = 3;
    private const int GenerationTimeoutMinutes = 5;
    
    // LLM Provider name (configurable)
    private string _llmProviderName = "default";
    
    public LumenPredictionGAgent() : base() { }

    public override Task<string> GetDescriptionAsync()
    {
        var dateStr = CustomState.PredictionDate != null 
            ? $"{CustomState.PredictionDate.Year}-{CustomState.PredictionDate.Month:00}-{CustomState.PredictionDate.Day:00}" 
            : "none";
        return Task.FromResult($"Lumen prediction - Type: {CustomState.Type}, Date: {dateStr}, User: {CustomState.UserId}");
    }
    
    #region Initialization
    
    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Initialize LLM provider if factory is available
        try
        {
            if (LLMProviderFactory != null)
            {
                await InitializeAsync(_llmProviderName, cancellationToken: ct);
                Logger.LogInformation("[LumenPredictionGAgent] LLM provider initialized: {Provider}", _llmProviderName);
            }
            else
            {
                Logger.LogWarning("[LumenPredictionGAgent] LLMProviderFactory not available, LLM features will be limited");
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[LumenPredictionGAgent] Failed to initialize LLM provider, will retry on demand");
        }
    }
    
    /// <summary>
    /// Configure the LLM provider name
    /// </summary>
    public async Task ConfigureLlmProviderAsync(string providerName, CancellationToken ct = default)
    {
        _llmProviderName = providerName;
        
        if (LLMProviderFactory != null)
        {
            await InitializeAsync(providerName, cancellationToken: ct);
        }
    }
    
    #endregion

    #region State Transition (Pure Functional - required for Event Sourcing)

    /// <summary>
    /// Override from AIGAgentBase to handle custom state transitions
    /// </summary>
    protected override void TransitionState(LumenPredictionState state, IMessage evt)
    {
        switch (evt)
        {
            case PredictionGeneratedEvent genEvt:
                ApplyPredictionGenerated(state, genEvt);
                break;
            case PredictionClearedEvent _:
                ApplyPredictionCleared(state);
                break;
            case LanguagesTranslatedEvent transEvt:
                ApplyLanguagesTranslated(state, transEvt);
                break;
            case GenerationLockSetEvent lockSetEvt:
                ApplyGenerationLockSet(state, lockSetEvt);
                break;
            case GenerationLockClearedEvent lockClearEvt:
                ApplyGenerationLockCleared(state, lockClearEvt);
                break;
            case TranslationLockSetEvent transLockSetEvt:
                ApplyTranslationLockSet(state, transLockSetEvt);
                break;
            case TranslationLockClearedEvent transLockClearEvt:
                ApplyTranslationLockCleared(state, transLockClearEvt);
                break;
            case DailyReminderUpdatedEvent reminderEvt:
                state.IsDailyReminderEnabled = reminderEvt.IsEnabled;
                state.DailyReminderTargetId = reminderEvt.TargetId;
                break;
            case UserActivityUpdatedEvent activityEvt:
                state.LastActiveDate = activityEvt.LastActiveDate;
                break;
        }
    }

    private static void ApplyPredictionGenerated(LumenPredictionState state, PredictionGeneratedEvent evt)
    {
        state.PredictionId = evt.PredictionId;
        state.UserId = evt.UserId;
        state.PredictionDate = evt.PredictionDate;
        state.CreatedAt = evt.CreatedAt;
        state.Type = evt.Type;
        state.ProfileUpdatedAt = evt.ProfileUpdatedAt;
        state.PromptVersion = evt.PromptVersion;
        
        state.Results.Clear();
        foreach (var kvp in evt.Results)
        {
            state.Results[kvp.Key] = kvp.Value;
        }
        
        if (!state.GeneratedLanguages.Contains(evt.Language))
        {
            state.GeneratedLanguages.Add(evt.Language);
        }
        
        state.LastGeneratedDate = evt.PredictionDate;
        
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var todayDateValue = new DateValue { Year = today.Year, Month = today.Month, Day = today.Day };
        
        if (state.TodayProcessDate == null || !IsSameDate(state.TodayProcessDate, todayDateValue))
        {
            state.TodayProcessDate = todayDateValue;
            state.TodayProcessedLanguages.Clear();
        }
        
        if (!state.TodayProcessedLanguages.Contains(evt.Language))
        {
            state.TodayProcessedLanguages.Add(evt.Language);
        }
    }

    private static void ApplyPredictionCleared(LumenPredictionState state)
    {
        state.PredictionId = string.Empty;
        state.Results.Clear();
        state.MultilingualResults.Clear();
        state.GeneratedLanguages.Clear();
        state.GenerationLocks.Clear();
        state.TranslationLocks.Clear();
        state.LastGeneratedDate = null;
        state.TodayProcessDate = null;
        state.TodayProcessedLanguages.Clear();
    }

    private static void ApplyLanguagesTranslated(LumenPredictionState state, LanguagesTranslatedEvent evt)
    {
        foreach (var kvp in evt.TranslatedLanguages)
        {
            if (!state.MultilingualResults.ContainsKey(kvp.Key))
            {
                state.MultilingualResults[kvp.Key] = new MultilingualResultValue();
            }
            
            foreach (var v in kvp.Value.Values)
            {
                state.MultilingualResults[kvp.Key].Values[v.Key] = v.Value;
            }
        }
        
        foreach (var lang in evt.AllGeneratedLanguages)
        {
            if (!state.GeneratedLanguages.Contains(lang))
            {
                state.GeneratedLanguages.Add(lang);
            }
        }
    }

    private static void ApplyGenerationLockSet(LumenPredictionState state, GenerationLockSetEvent evt)
    {
        var typeKey = (int)evt.Type;
        if (!state.GenerationLocks.ContainsKey(typeKey))
        {
            state.GenerationLocks[typeKey] = new GenerationLockInfo();
        }
        
        state.GenerationLocks[typeKey].IsGenerating = true;
        state.GenerationLocks[typeKey].StartedAt = evt.StartedAt;
        state.GenerationLocks[typeKey].RetryCount = evt.RetryCount;
    }

    private static void ApplyGenerationLockCleared(LumenPredictionState state, GenerationLockClearedEvent evt)
    {
        var typeKey = (int)evt.Type;
        if (state.GenerationLocks.ContainsKey(typeKey))
        {
            state.GenerationLocks[typeKey].IsGenerating = false;
            state.GenerationLocks[typeKey].StartedAt = null;
        }
    }

    private static void ApplyTranslationLockSet(LumenPredictionState state, TranslationLockSetEvent evt)
    {
        if (!state.TranslationLocks.ContainsKey(evt.Language))
        {
            state.TranslationLocks[evt.Language] = new TranslationLockInfo();
        }
        
        state.TranslationLocks[evt.Language].IsTranslating = true;
        state.TranslationLocks[evt.Language].StartedAt = evt.StartedAt;
        state.TranslationLocks[evt.Language].SourceLanguage = evt.SourceLanguage;
    }

    private static void ApplyTranslationLockCleared(LumenPredictionState state, TranslationLockClearedEvent evt)
    {
        if (state.TranslationLocks.ContainsKey(evt.Language))
        {
            state.TranslationLocks[evt.Language].IsTranslating = false;
            state.TranslationLocks[evt.Language].StartedAt = null;
        }
    }

    #endregion

    #region Event Handlers (for receiving events from stream)

    [EventHandler]
    public Task HandlePredictionGenerated(PredictionGeneratedEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandlePredictionCleared(PredictionClearedEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleLanguagesTranslated(LanguagesTranslatedEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleGenerationLockSet(GenerationLockSetEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleGenerationLockCleared(GenerationLockClearedEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleTranslationLockSet(TranslationLockSetEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleTranslationLockCleared(TranslationLockClearedEvent evt)
    {
        TransitionState(CustomState, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleDailyReminderUpdated(DailyReminderUpdatedEvent evt)
    {
        CustomState.IsDailyReminderEnabled = evt.IsEnabled;
        CustomState.DailyReminderTargetId = evt.TargetId;
        
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserActivityUpdated(UserActivityUpdatedEvent evt)
    {
        CustomState.LastActiveDate = evt.LastActiveDate;
        
        return Task.CompletedTask;
    }

    #endregion

    #region Public API Methods

    public async Task<GetTodayPredictionResult> GetOrGeneratePredictionAsync(
        LumenUserDto userInfo,
        PredictionType type = PredictionType.PredictionDaily,
        string userLanguage = "en",
        DateValue? predictionDate = null)
    {
        try
        {
            Logger.LogDebug("[LumenPredictionGAgent] GetOrGeneratePredictionAsync - User: {UserId}, Type: {Type}, Language: {Language}",
                userInfo.UserId, type, userLanguage);

            // Check if currently generating
            var typeKey = (int)type;
            if (CustomState.GenerationLocks.TryGetValue(typeKey, out var lockInfo) && lockInfo.IsGenerating)
            {
                var startedAt = lockInfo.StartedAt?.ToDateTime() ?? DateTime.UtcNow;
                if ((DateTime.UtcNow - startedAt).TotalMinutes < GenerationTimeoutMinutes)
                {
                    return new GetTodayPredictionResult
                    {
                        Success = false,
                        Message = "Prediction is being generated, please wait...",
                        IsGenerating = true,
                        GeneratingLanguage = userLanguage
                    };
                }
                // Generation timed out, clear lock
                RaiseEvent(new GenerationLockClearedEvent { Type = type });
            }

            // Check if prediction already exists for today
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var requestDate = predictionDate != null 
                ? new DateOnly(predictionDate.Year, predictionDate.Month, predictionDate.Day)
                : today;

            if (HasValidPrediction(requestDate, type, userLanguage))
            {
                return new GetTodayPredictionResult
                {
                    Success = true,
                    Message = string.Empty,
                    Prediction = BuildPredictionResult(userLanguage),
                    IsGenerating = false
                };
            }

            // Set generation lock
            RaiseEvent(new GenerationLockSetEvent
            {
                Type = type,
                StartedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                RetryCount = 0
            });

            await ConfirmEventsAsync();

            // Generate prediction using LLM
            var results = await GeneratePredictionAsync(userInfo, type, userLanguage, requestDate);
            
            if (results == null || results.Count == 0)
            {
                RaiseEvent(new GenerationLockClearedEvent { Type = type });
                await ConfirmEventsAsync();
                
                return new GetTodayPredictionResult
                {
                    Success = false,
                    Message = "Failed to generate prediction",
                    IsGenerating = false
                };
            }

            // Save generated prediction
            RaiseEvent(new PredictionGeneratedEvent
            {
                PredictionId = Guid.NewGuid().ToString("N"),
                UserId = userInfo.UserId,
                PredictionDate = new DateValue { Year = requestDate.Year, Month = requestDate.Month, Day = requestDate.Day },
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow),
                Type = type,
                Results = { results },
                Language = userLanguage,
                PromptVersion = 1
            });

            // Clear generation lock
            RaiseEvent(new GenerationLockClearedEvent { Type = type });

            await ConfirmEventsAsync();

            return new GetTodayPredictionResult
            {
                Success = true,
                Message = string.Empty,
                Prediction = BuildPredictionResult(userLanguage),
                IsGenerating = false
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error generating prediction");
            
            // Clear lock on error
            RaiseEvent(new GenerationLockClearedEvent { Type = type });
            await ConfirmEventsAsync();
            
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = "Internal error occurred",
                IsGenerating = false
            };
        }
    }

    public Task<PredictionResultDto?> GetPredictionAsync(string userLanguage = "en")
    {
        try
        {
            if (string.IsNullOrEmpty(CustomState.PredictionId))
            {
                return Task.FromResult<PredictionResultDto?>(null);
            }

            return Task.FromResult<PredictionResultDto?>(BuildPredictionResult(userLanguage));
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error getting prediction");
            return Task.FromResult<PredictionResultDto?>(null);
        }
    }

    public Task<PredictionStatusDto?> GetPredictionStatusAsync(DateTime? profileUpdatedAt = null, string? userTimeZone = null)
    {
        try
        {
            var typeKey = (int)CustomState.Type;
            var isGenerating = CustomState.GenerationLocks.TryGetValue(typeKey, out var lockInfo) && lockInfo.IsGenerating;

            var needsRegeneration = false;
            if (profileUpdatedAt.HasValue && CustomState.ProfileUpdatedAt != null)
            {
                needsRegeneration = profileUpdatedAt.Value > CustomState.ProfileUpdatedAt.ToDateTime();
            }

            return Task.FromResult<PredictionStatusDto?>(new PredictionStatusDto
            {
                HasPrediction = !string.IsNullOrEmpty(CustomState.PredictionId),
                IsGenerating = isGenerating,
                NeedsRegeneration = needsRegeneration,
                PredictionDate = CustomState.PredictionDate,
                LastGeneratedDate = CustomState.LastGeneratedDate,
                PromptVersion = CustomState.PromptVersion,
                AvailableLanguages = { CustomState.GeneratedLanguages },
                ProfileUpdatedAt = CustomState.ProfileUpdatedAt
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error getting prediction status");
            return Task.FromResult<PredictionStatusDto?>(null);
        }
    }

    public async Task ClearCurrentPredictionAsync()
    {
        try
        {
            Logger.LogDebug("[LumenPredictionGAgent] ClearCurrentPredictionAsync");

            RaiseEvent(new PredictionClearedEvent
            {
                ClearedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await ConfirmEventsAsync();

            Logger.LogInformation("[LumenPredictionGAgent] Prediction cleared successfully");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error clearing prediction");
        }
    }

    public Task<CalculatedValuesDto> GetCalculatedValuesAsync(LumenUserDto userInfo, string userLanguage = "en")
    {
        try
        {
            var values = new CalculatedValuesDto();
            
            // Calculate zodiac sign
            if (userInfo.BirthDate != null)
            {
                var zodiacSign = CalculateZodiacSign(userInfo.BirthDate);
                values.Values["zodiacSign"] = zodiacSign;
                
                // Calculate Chinese zodiac
                var chineseZodiac = CalculateChineseZodiac(userInfo.BirthDate.Year);
                values.Values["chineseZodiac"] = chineseZodiac;
            }
            
            return Task.FromResult(values);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error calculating values");
            return Task.FromResult(new CalculatedValuesDto());
        }
    }

    public async Task<TriggerTranslationResult> TriggerTranslationAsync(LumenUserDto userInfo, string targetLanguage)
    {
        try
        {
            Logger.LogDebug("[LumenPredictionGAgent] TriggerTranslationAsync - Language: {Language}", targetLanguage);

            // Check if already translating
            if (CustomState.TranslationLocks.TryGetValue(targetLanguage, out var lockInfo) && lockInfo.IsTranslating)
            {
                return new TriggerTranslationResult
                {
                    Success = false,
                    Message = "Translation already in progress",
                    AlreadyTranslating = true
                };
            }

            // Check if already translated
            if (CustomState.GeneratedLanguages.Contains(targetLanguage))
            {
                return new TriggerTranslationResult
                {
                    Success = true,
                    Message = "Language already available",
                    AlreadyTranslating = false
                };
            }

            // TODO: Implement actual translation logic using LLM
            // For now, just return success
            return new TriggerTranslationResult
            {
                Success = true,
                Message = "Translation triggered",
                AlreadyTranslating = false
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error triggering translation");
            return new TriggerTranslationResult
            {
                Success = false,
                Message = "Internal error occurred",
                AlreadyTranslating = false
            };
        }
    }

    public async Task<UpdateTimeZoneReminderResult> UpdateTimeZoneReminderAsync(string timeZoneId)
    {
        try
        {
            Logger.LogDebug("[LumenPredictionGAgent] UpdateTimeZoneReminderAsync - TimeZone: {TimeZone}", timeZoneId);

            // TODO: Implement reminder update logic
            // For now, just update state
            RaiseEvent(new UserActivityUpdatedEvent
            {
                LastActiveDate = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await ConfirmEventsAsync();

            return new UpdateTimeZoneReminderResult
            {
                Success = true,
                Message = "Reminder updated"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error updating timezone reminder");
            return new UpdateTimeZoneReminderResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public async Task UpdateUserActivityAsync(string? userTimeZone = null)
    {
        try
        {
            RaiseEvent(new UserActivityUpdatedEvent
            {
                LastActiveDate = Timestamp.FromDateTime(DateTime.UtcNow),
                UserTimeZone = userTimeZone
            });

            await ConfirmEventsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenPredictionGAgent] Error updating user activity");
        }
    }

    #endregion

    #region Private Helpers

    private bool HasValidPrediction(DateOnly requestDate, PredictionType type, string language)
    {
        if (string.IsNullOrEmpty(CustomState.PredictionId))
            return false;
        
        if (CustomState.Type != type)
            return false;
        
        if (CustomState.PredictionDate == null)
            return false;
        
        var stateDate = new DateOnly(CustomState.PredictionDate.Year, CustomState.PredictionDate.Month, CustomState.PredictionDate.Day);
        if (stateDate != requestDate)
            return false;
        
        if (!CustomState.GeneratedLanguages.Contains(language))
            return false;
        
        return true;
    }

    private PredictionResultDto BuildPredictionResult(string language)
    {
        var result = new PredictionResultDto
        {
            PredictionId = CustomState.PredictionId,
            Type = CustomState.Type,
            PredictionDate = CustomState.PredictionDate,
            CreatedAt = CustomState.CreatedAt,
            Language = language,
            AvailableLanguages = { CustomState.GeneratedLanguages }
        };

        // Try to get language-specific results
        if (CustomState.MultilingualResults.TryGetValue(language, out var langResults))
        {
            foreach (var kvp in langResults.Values)
            {
                result.Content[kvp.Key] = kvp.Value;
            }
        }
        else
        {
            // Fall back to base results
            foreach (var kvp in CustomState.Results)
            {
                result.Content[kvp.Key] = kvp.Value;
            }
        }

        return result;
    }

    /// <summary>
    /// Generate prediction using LLM
    /// </summary>
    private async Task<Dictionary<string, string>?> GeneratePredictionAsync(
        LumenUserDto userInfo, 
        PredictionType type, 
        string language,
        DateOnly predictionDate)
    {
        // Build the prediction prompt
        var prompt = BuildPredictionPrompt(userInfo, predictionDate, type, language);
        
        // Call LLM if provider is initialized
        if (_isInitialized)
        {
            try
            {
                var response = await CallLlmAsync(prompt, type, default);
                if (!string.IsNullOrEmpty(response))
                {
                    var parsedResults = ParseTsvResponse(response);
                    if (parsedResults.Count > 0)
                    {
                        // Apply post-processing
                        parsedResults = MapShortKeysToFullKeys(parsedResults);
                        parsedResults = ConvertArrayFieldsToJson(parsedResults);
                        AddQuotesToAffirmation(parsedResults, language);
                        return parsedResults;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "[LumenPredictionGAgent] LLM generation failed, falling back to placeholder");
            }
        }
        
        // Fallback: return sample data when LLM is not available
        Logger.LogWarning("[LumenPredictionGAgent] Using placeholder prediction data");
        var results = new Dictionary<string, string>
        {
            ["career"] = $"Today's career outlook for {userInfo.FullName} is positive.",
            ["love"] = "Focus on communication in relationships.",
            ["wellness"] = "Take time for self-care and rest.",
            ["prosperity"] = "A good day for financial planning.",
            ["overall"] = "A balanced day with opportunities for growth."
        };

        return results;
    }

    private static string CalculateZodiacSign(DateValue? birthDate)
    {
        if (birthDate == null) return "Unknown";
        
        var month = birthDate.Month;
        var day = birthDate.Day;
        
        return (month, day) switch
        {
            (3, >= 21) or (4, <= 19) => "Aries",
            (4, >= 20) or (5, <= 20) => "Taurus",
            (5, >= 21) or (6, <= 20) => "Gemini",
            (6, >= 21) or (7, <= 22) => "Cancer",
            (7, >= 23) or (8, <= 22) => "Leo",
            (8, >= 23) or (9, <= 22) => "Virgo",
            (9, >= 23) or (10, <= 22) => "Libra",
            (10, >= 23) or (11, <= 21) => "Scorpio",
            (11, >= 22) or (12, <= 21) => "Sagittarius",
            (12, >= 22) or (1, <= 19) => "Capricorn",
            (1, >= 20) or (2, <= 18) => "Aquarius",
            _ => "Pisces"
        };
    }

    private static string CalculateChineseZodiac(int year)
    {
        var zodiacAnimals = new[] 
        { 
            "Rat", "Ox", "Tiger", "Rabbit", "Dragon", "Snake",
            "Horse", "Goat", "Monkey", "Rooster", "Dog", "Pig"
        };
        return zodiacAnimals[(year - 4) % 12];
    }

    private static bool IsSameDate(DateValue? a, DateValue? b)
    {
        if (a == null || b == null) return false;
        return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day;
    }

    #endregion
}

