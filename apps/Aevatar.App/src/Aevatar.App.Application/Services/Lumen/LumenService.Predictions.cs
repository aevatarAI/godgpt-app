using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aevatar.Agents.Lumen.Protos;
using Aevatar.App.Lumen.Dtos;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Services.Lumen;

public partial class LumenService
{
    #region Predictions

    /// <inheritdoc />
    public async Task<GetTodayPredictionResult> GetLifetimePredictionAsync(string userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][GetLifetimePredictionAsync] Getting lifetime prediction: {UserId}", userId);

            var agent = await GetPredictionAgentAsync(userId, PredictionType.PredictionLifetime);
            var prediction = await agent.GetPredictionAsync(userLanguage);
            
            return new GetTodayPredictionResult
            {
                Success = prediction != null,
                Prediction = prediction
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetLifetimePredictionAsync] Error: {UserId}", userId);
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"Error getting lifetime prediction: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetTodayPredictionResult> GetYearlyPredictionAsync(string userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][GetYearlyPredictionAsync] Getting yearly prediction: {UserId}", userId);

            var agent = await GetPredictionAgentAsync(userId, PredictionType.PredictionYearly);
            var prediction = await agent.GetPredictionAsync(userLanguage);
            
            return new GetTodayPredictionResult
            {
                Success = prediction != null,
                Prediction = prediction
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetYearlyPredictionAsync] Error: {UserId}", userId);
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"Error getting yearly prediction: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetTodayPredictionResult> GetTodayPredictionAsync(string userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][GetTodayPredictionAsync] Getting today's prediction: {UserId}", userId);

            var agent = await GetPredictionAgentAsync(userId, PredictionType.PredictionDaily);
            var prediction = await agent.GetPredictionAsync(userLanguage);
            
            return new GetTodayPredictionResult
            {
                Success = prediction != null,
                Prediction = prediction
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetTodayPredictionAsync] Error: {UserId}", userId);
            return new GetTodayPredictionResult
            {
                Success = false,
                Message = $"Error getting today's prediction: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task TriggerPredictionGenerationAsync(string userId, List<PredictionType> types)
    {
        try
        {
            _logger.LogDebug("[LumenService][TriggerPredictionGenerationAsync] Triggering generation: {UserId}, Types: {Types}", 
                userId, string.Join(",", types));

            // Get user profile
            var profileAgent = await GetUserProfileAgentAsync(userId);
            var profileResult = await profileAgent.GetUserProfileAsync(userId, "en");
            
            if (!profileResult.Success || profileResult.UserProfile == null)
            {
                _logger.LogWarning("[LumenService][TriggerPredictionGenerationAsync] User profile not found: {UserId}", userId);
                return;
            }

            var userDto = BuildUserDto(profileResult.UserProfile);
            var languageInfo = await profileAgent.GetLanguageInfoAsync();
            var userLanguage = languageInfo.Success ? languageInfo.CurrentLanguage : "en";

            // Trigger generation for each type (fire and forget)
            foreach (var type in types)
            {
                try
                {
                    var agent = await GetPredictionAgentAsync(userId, type);
                    _ = agent.GetOrGeneratePredictionAsync(userDto, type, userLanguage);
                    _logger.LogInformation("[LumenService][TriggerPredictionGenerationAsync] Triggered {Type} for {UserId}", type, userId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[LumenService][TriggerPredictionGenerationAsync] Error triggering {Type} for {UserId}", type, userId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][TriggerPredictionGenerationAsync] Error: {UserId}", userId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CheckUserProfileExistsAsync(string userId)
    {
        try
        {
            var agent = await GetUserProfileAgentAsync(userId);
            var profile = await agent.GetUserProfileAsync(userId, "en");
            return profile.Success && profile.UserProfile != null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][CheckUserProfileExistsAsync] Error: {UserId}", userId);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<GetPredictionStatusResult> GetPredictionStatusAsync(string userId)
    {
        try
        {
            _logger.LogDebug("[LumenService][GetPredictionStatusAsync] Getting status: {UserId}", userId);

            var result = new GetPredictionStatusResult
            {
                Success = true,
                GeneratingTypes = new List<string>()
            };

            // Check each prediction type
            foreach (var type in new[] { PredictionType.PredictionDaily, PredictionType.PredictionYearly, PredictionType.PredictionLifetime })
            {
                try
                {
                    var agent = await GetPredictionAgentAsync(userId, type);
                    var status = await agent.GetPredictionStatusAsync();
                    
                    if (status != null)
                    {
                        var statusInfo = new PredictionStatusInfo
                        {
                            Exists = status.HasPrediction,
                            IsGenerating = status.IsGenerating,
                            AvailableLanguages = new List<string>(status.AvailableLanguages)
                        };

                        switch (type)
                        {
                            case PredictionType.PredictionDaily:
                                result.Daily = statusInfo;
                                break;
                            case PredictionType.PredictionYearly:
                                result.Yearly = statusInfo;
                                break;
                            case PredictionType.PredictionLifetime:
                                result.Lifetime = statusInfo;
                                break;
                        }

                        if (status.IsGenerating)
                        {
                            result.IsGenerating = true;
                            result.GeneratingTypes.Add(type.ToString());
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[LumenService][GetPredictionStatusAsync] Error getting status for type: {Type}", type);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetPredictionStatusAsync] Error: {UserId}", userId);
            return new GetPredictionStatusResult
            {
                Success = false,
                Message = $"Error getting prediction status: {ex.Message}"
            };
        }
    }

    /// <inheritdoc />
    public async Task<GetCalculatedValuesResult> GetCalculatedValuesAsync(string userId, string userLanguage = "en")
    {
        try
        {
            _logger.LogDebug("[LumenService][GetCalculatedValuesAsync] Getting calculated values: {UserId}", userId);

            // Get user profile
            var profileAgent = await GetUserProfileAgentAsync(userId);
            var profileResult = await profileAgent.GetUserProfileAsync(userId, userLanguage);
            
            if (!profileResult.Success || profileResult.UserProfile == null)
            {
                return new GetCalculatedValuesResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            var userDto = BuildUserDto(profileResult.UserProfile);
            
            // Use daily prediction agent for calculated values
            var predictionAgent = await GetPredictionAgentAsync(userId, PredictionType.PredictionDaily);
            var calculatedValues = await predictionAgent.GetCalculatedValuesAsync(userDto, userLanguage);
            
            // Map CalculatedValuesDto to GetCalculatedValuesResult
            var result = new GetCalculatedValuesResult
            {
                Success = true
            };
            
            // Extract values from the map-based CalculatedValuesDto
            if (calculatedValues?.Values != null)
            {
                calculatedValues.Values.TryGetValue("sun_sign", out var sunSign);
                calculatedValues.Values.TryGetValue("moon_sign", out var moonSign);
                calculatedValues.Values.TryGetValue("rising_sign", out var risingSign);
                calculatedValues.Values.TryGetValue("chinese_zodiac", out var chineseZodiac);
                calculatedValues.Values.TryGetValue("element", out var element);
                calculatedValues.Values.TryGetValue("yin_yang", out var yinYang);
                calculatedValues.Values.TryGetValue("year_pillar", out var yearPillar);
                calculatedValues.Values.TryGetValue("month_pillar", out var monthPillar);
                calculatedValues.Values.TryGetValue("day_pillar", out var dayPillar);
                calculatedValues.Values.TryGetValue("hour_pillar", out var hourPillar);
                calculatedValues.Values.TryGetValue("lucky_number", out var luckyNumber);
                calculatedValues.Values.TryGetValue("lucky_color", out var luckyColor);
                calculatedValues.Values.TryGetValue("lucky_direction", out var luckyDirection);
                
                result.SunSign = sunSign;
                result.MoonSign = moonSign;
                result.RisingSign = risingSign;
                result.ChineseZodiac = chineseZodiac;
                result.Element = element;
                result.YinYang = yinYang;
                result.YearPillar = yearPillar;
                result.MonthPillar = monthPillar;
                result.DayPillar = dayPillar;
                result.HourPillar = hourPillar;
                
                if (int.TryParse(luckyNumber, out var ln))
                {
                    result.LuckyNumber = ln;
                }
                result.LuckyColor = luckyColor;
                result.LuckyDirection = luckyDirection;
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LumenService][GetCalculatedValuesAsync] Error: {UserId}", userId);
            return new GetCalculatedValuesResult
            {
                Success = false,
                Message = $"Error getting calculated values: {ex.Message}"
            };
        }
    }

    #endregion
}
