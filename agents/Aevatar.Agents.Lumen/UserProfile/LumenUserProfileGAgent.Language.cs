using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.UserProfile;

public partial class LumenUserProfileGAgent
{
    #region Language Management

    public async Task<SetLanguageResult> SetLanguageAsync(string newLanguage)
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] SetLanguageAsync - UserId: {UserId}, Language: {Language}", 
                State.UserId, newLanguage);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return new SetLanguageResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            if (string.IsNullOrWhiteSpace(newLanguage))
            {
                return new SetLanguageResult
                {
                    Success = false,
                    Message = "Language cannot be empty"
                };
            }

            if (State.CurrentLanguage == newLanguage)
            {
                var remaining = CalculateRemainingChanges();
                return new SetLanguageResult
                {
                    Success = true,
                    Message = "Language is already set to " + newLanguage,
                    CurrentLanguage = State.CurrentLanguage,
                    RemainingChanges = remaining,
                    MaxChangesPerDay = MaxLanguageSwitchesPerDay
                };
            }

            var now = DateTime.UtcNow;
            var today = new DateValue { Year = now.Year, Month = now.Month, Day = now.Day };

            // Check if we need to reset today's count
            if (State.LastLanguageSwitchDate == null || 
                !IsSameDate(State.LastLanguageSwitchDate, today))
            {
                State.TodayLanguageSwitchCount = 0;
            }

            if (State.TodayLanguageSwitchCount >= MaxLanguageSwitchesPerDay)
            {
                return new SetLanguageResult
                {
                    Success = false,
                    Message = $"Daily language switch limit reached ({MaxLanguageSwitchesPerDay} per day)",
                    CurrentLanguage = State.CurrentLanguage,
                    RemainingChanges = 0,
                    MaxChangesPerDay = MaxLanguageSwitchesPerDay
                };
            }

            var previousLanguage = State.CurrentLanguage;
            var newCount = State.TodayLanguageSwitchCount + 1;

            RaiseEvent(new UserProfileLanguageSwitchedEvent
            {
                UserId = State.UserId,
                PreviousLanguage = previousLanguage ?? "",
                NewLanguage = newLanguage,
                SwitchedAt = Timestamp.FromDateTime(now),
                SwitchDate = today,
                TodayCount = newCount
            });

            await ConfirmEventsAsync();

            var remainingChanges = Math.Max(0, MaxLanguageSwitchesPerDay - newCount);

            return new SetLanguageResult
            {
                Success = true,
                Message = "Language updated successfully",
                CurrentLanguage = newLanguage,
                RemainingChanges = remainingChanges,
                MaxChangesPerDay = MaxLanguageSwitchesPerDay
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error setting language");
            return new SetLanguageResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetLanguageInfoResult> GetLanguageInfoAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetLanguageInfoResult
                {
                    Success = false,
                    Message = "User not found"
                });
            }

            var remaining = CalculateRemainingChanges();

            return Task.FromResult(new GetLanguageInfoResult
            {
                Success = true,
                Message = string.Empty,
                CurrentLanguage = State.CurrentLanguage ?? "en",
                RemainingChanges = remaining,
                MaxChangesPerDay = MaxLanguageSwitchesPerDay,
                LastSwitchDate = State.LastLanguageSwitchDate != null 
                    ? Timestamp.FromDateTime(new DateTime(
                        State.LastLanguageSwitchDate.Year, 
                        State.LastLanguageSwitchDate.Month, 
                        State.LastLanguageSwitchDate.Day, 0, 0, 0, DateTimeKind.Utc))
                    : null
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error getting language info");
            return Task.FromResult(new GetLanguageInfoResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public async Task InitializeLanguageAsync(string initialLanguage)
    {
        try
        {
            Logger.LogInformation("[LumenUserProfileGAgent] InitializeLanguageAsync - UserId: {UserId}, Language: {Language}", 
                State.UserId, initialLanguage);

            var now = DateTime.UtcNow;
            var today = new DateValue { Year = now.Year, Month = now.Month, Day = now.Day };

            // Does not count as a switch
            RaiseEvent(new UserProfileLanguageSwitchedEvent
            {
                UserId = State.UserId,
                PreviousLanguage = string.Empty,
                NewLanguage = initialLanguage,
                SwitchedAt = Timestamp.FromDateTime(now),
                SwitchDate = today,
                TodayCount = 0
            });

            await ConfirmEventsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error initializing language");
        }
    }
    
    public async Task<UpdateTimeZoneResult> UpdateTimeZoneAsync(UpdateTimeZoneRequest request)
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] UpdateTimeZoneAsync - UserId: {UserId}, TimeZone: {TimeZoneId}", 
                request.UserId, request.TimeZoneId);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = "User not found"
                };
            }

            if (State.UserId != request.UserId)
            {
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            // Validate timezone ID
            try
            {
                TimeZoneInfo.FindSystemTimeZoneById(request.TimeZoneId);
            }
            catch (TimeZoneNotFoundException)
            {
                return new UpdateTimeZoneResult
                {
                    Success = false,
                    Message = $"Invalid time zone ID: {request.TimeZoneId}. Please use IANA time zone format."
                };
            }

            var now = DateTime.UtcNow;

            RaiseEvent(new TimeZoneUpdatedEvent
            {
                UserId = request.UserId,
                TimeZoneId = request.TimeZoneId,
                UpdatedAt = Timestamp.FromDateTime(now)
            });

            await ConfirmEventsAsync();

            return new UpdateTimeZoneResult
            {
                Success = true,
                Message = string.Empty,
                TimeZoneId = request.TimeZoneId
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error updating timezone: {UserId}", request.UserId);
            return new UpdateTimeZoneResult
            {
                Success = false,
                Message = $"Failed to update timezone: {ex.Message}"
            };
        }
    }

    public async Task SaveInferredLatLongAsync(string latLongInferred, string birthCity)
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] SaveInferredLatLongAsync - UserId: {UserId}, City: {BirthCity}", 
                State.UserId, birthCity);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return;
            }

            // Only save if not already exists
            if (!string.IsNullOrEmpty(State.LatLongInferred))
            {
                return;
            }

            var now = DateTime.UtcNow;

            RaiseEvent(new UserProfileLatLongInferredEvent
            {
                UserId = State.UserId,
                LatLongInferred = latLongInferred,
                BirthCity = birthCity,
                InferredAt = Timestamp.FromDateTime(now)
            });

            await ConfirmEventsAsync();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error saving inferred latlong");
        }
    }
    
    private int CalculateRemainingChanges()
    {
        var today = DateTime.UtcNow;
        var todayDate = new DateValue { Year = today.Year, Month = today.Month, Day = today.Day };
        
        if (State.LastLanguageSwitchDate == null || !IsSameDate(State.LastLanguageSwitchDate, todayDate))
        {
            return MaxLanguageSwitchesPerDay;
        }
        
        return Math.Max(0, MaxLanguageSwitchesPerDay - State.TodayLanguageSwitchCount);
    }

    private static bool IsSameDate(DateValue? a, DateValue? b)
    {
        if (a == null || b == null) return false;
        return a.Year == b.Year && a.Month == b.Month && a.Day == b.Day;
    }

    #endregion
}


