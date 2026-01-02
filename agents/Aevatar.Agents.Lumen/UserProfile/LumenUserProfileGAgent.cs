using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.Lumen.Protos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Lumen.UserProfile;

/// <summary>
/// Lumen User Profile GAgent - manages user profile with FullName
/// </summary>
public partial class LumenUserProfileGAgent : GAgentBase<LumenUserProfileState>, ILumenUserProfileGAgent
{
    // Configuration constants (can be moved to options later)
    private const int MaxProfileUpdatesPerWeek = 100;
    private const int MaxIconUploadsPerDay = 1;
    private const int MaxLanguageSwitchesPerDay = 1;
    
    /// <summary>
    /// Valid lumen prediction actions
    /// </summary>
    private static readonly HashSet<string> ValidActions = new()
    {
        "forecast", "horoscope", "bazi", "ziwei", "constellation", 
        "numerology", "synastry", "chineseZodiac", "mayanTotem", 
        "humanFigure", "tarot", "zhengYu"
    };

    public LumenUserProfileGAgent() : base() { }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Lumen user profile - UserId: {State.UserId}, FullName: {State.FullName}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        if (string.IsNullOrEmpty(State.CurrentLanguage))
        {
            State.CurrentLanguage = "en";
        }
    }

    #region State Transition (Pure Functional - required for Event Sourcing)

    protected override void TransitionState(LumenUserProfileState state, Google.Protobuf.IMessage evt)
    {
        switch (evt)
        {
            case UserProfileUpdatedEvent updatedEvt:
                ApplyUserProfileUpdated(state, updatedEvt);
                break;
            case UserProfileActionsUpdatedEvent actionsEvt:
                state.Actions.Clear();
                state.Actions.AddRange(actionsEvt.Actions);
                state.UpdatedAt = actionsEvt.UpdatedAt;
                break;
            case UserProfileClearedEvent clearedEvt:
                ApplyUserProfileCleared(state, clearedEvt);
                break;
            case IconUpdatedEvent iconEvt:
                state.Icon = iconEvt.HasIconUrl ? iconEvt.IconUrl : "";
                state.UpdatedAt = iconEvt.UpdatedAt;
                state.IconUploadHistory.Add(iconEvt.UploadTimestamp);
                break;
            case UserProfileLanguageSwitchedEvent langEvt:
                state.CurrentLanguage = langEvt.NewLanguage;
                state.LastLanguageSwitchDate = langEvt.SwitchDate;
                state.TodayLanguageSwitchCount = langEvt.TodayCount;
                state.UpdatedAt = langEvt.SwitchedAt;
                break;
            case UserProfileLatLongInferredEvent inferredEvt:
                state.LatLongInferred = inferredEvt.LatLongInferred;
                state.InferredFromCity = inferredEvt.BirthCity;
                break;
            case TimeZoneUpdatedEvent tzEvt:
                state.CurrentTimeZone = tzEvt.TimeZoneId;
                state.UpdatedAt = tzEvt.UpdatedAt;
                break;
        }
    }

    private static void ApplyUserProfileUpdated(LumenUserProfileState state, UserProfileUpdatedEvent evt)
    {
        state.UserId = evt.UserId;
        state.FullName = evt.FullName;
        state.Gender = evt.Gender;
        state.BirthDate = evt.BirthDate;
        state.BirthTime = evt.BirthTime;
        state.BirthCity = evt.HasBirthCity ? evt.BirthCity : "";
        state.LatLong = evt.HasLatLong ? evt.LatLong : "";
        state.MbtiType = evt.HasMbtiType ? evt.MbtiType : MbtiTypeEnum.MbtiUnspecified;
        state.RelationshipStatus = evt.HasRelationshipStatus ? evt.RelationshipStatus : RelationshipStatusEnum.RelationshipUnspecified;
        state.Interests = evt.HasInterests ? evt.Interests : "";
        state.CalendarType = evt.HasCalendarType ? evt.CalendarType : CalendarTypeEnum.CalendarSolar;
        state.UpdatedAt = evt.UpdatedAt;
        state.CurrentResidence = evt.HasCurrentResidence ? evt.CurrentResidence : "";
        state.Email = evt.HasEmail ? evt.Email : "";
        state.Occupation = evt.HasOccupation ? evt.Occupation : "";
        state.Icon = evt.HasIcon ? evt.Icon : "";
        state.CurrentTimeZone = evt.HasCurrentTimeZone ? evt.CurrentTimeZone : "";
        state.IsDeleted = false;
        
        state.InterestsList.Clear();
        state.InterestsList.AddRange(evt.InterestsList);
        
        // Record update timestamp for rate limiting (only for actual updates)
        var isInitialRegistration = state.CreatedAt == null || state.CreatedAt.Seconds == 0;
        if (!isInitialRegistration)
        {
            state.UpdateHistory.Add(evt.UpdatedAt);
        }
        
        // Set CreatedAt on first registration
        if (isInitialRegistration)
        {
            state.CreatedAt = evt.UpdatedAt;
        }
    }

    private static void ApplyUserProfileCleared(LumenUserProfileState state, UserProfileClearedEvent evt)
    {
        state.UserId = string.Empty;
        state.FullName = string.Empty;
        state.Gender = GenderEnum.GenderUnspecified;
        state.BirthDate = null;
        state.BirthTime = null;
        state.BirthCity = "";
        state.LatLong = "";
        state.MbtiType = MbtiTypeEnum.MbtiUnspecified;
        state.RelationshipStatus = RelationshipStatusEnum.RelationshipUnspecified;
        state.Interests = "";
        state.CalendarType = CalendarTypeEnum.CalendarSolar;
        state.CurrentResidence = "";
        state.Email = "";
        state.Occupation = "";
        state.Icon = "";
        state.Actions.Clear();
        state.CreatedAt = null;
        state.UpdatedAt = evt.ClearedAt;
        state.UpdateHistory.Clear();
        state.IsDeleted = true;
    }

    #endregion

    #region Event Handlers (for receiving events from stream)

    [EventHandler]
    public Task HandleUserProfileUpdated(UserProfileUpdatedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserProfileActionsUpdated(UserProfileActionsUpdatedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserProfileCleared(UserProfileClearedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleIconUpdated(IconUpdatedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserProfileLanguageSwitched(UserProfileLanguageSwitchedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleUserProfileLatLongInferred(UserProfileLatLongInferredEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    [EventHandler]
    public Task HandleTimeZoneUpdatedEvent(TimeZoneUpdatedEvent evt)
    {
        TransitionState(State, evt);
        return Task.CompletedTask;
    }

    #endregion

    #region Public API Methods

    public async Task<UpdateUserProfileResult> UpdateUserProfileAsync(UpdateUserProfileRequest request)
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] UpdateUserProfileAsync - UserId: {UserId}", request.UserId);

            // Validate request
            var validationResult = ValidateProfileRequest(request);
            if (!validationResult.IsValid)
            {
                Logger.LogWarning("[LumenUserProfileGAgent] Validation failed: {Message}", validationResult.Message);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = validationResult.Message
                };
            }
            
            if (!string.IsNullOrWhiteSpace(State.UserId) && State.UserId != request.UserId)
            {
                Logger.LogWarning("[LumenUserProfileGAgent] User ID mismatch: {UserId}", request.UserId);
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = "User ID mismatch"
                };
            }

            // Check rate limit
            var now = DateTime.UtcNow;
            var oneWeekAgo = now.AddDays(-7);
            
            // Clean up old update history
            var recentUpdates = State.UpdateHistory
                .Where(ts => ts.ToDateTime() > oneWeekAgo)
                .ToList();
            State.UpdateHistory.Clear();
            foreach (var ts in recentUpdates)
            {
                State.UpdateHistory.Add(ts);
            }
            
            if (State.UpdateHistory.Count >= MaxProfileUpdatesPerWeek)
            {
                var oldestUpdate = State.UpdateHistory.Min();
                var nextAllowedUpdate = oldestUpdate.ToDateTime().AddDays(7);
                var remainingTime = nextAllowedUpdate - now;
                
                return new UpdateUserProfileResult
                {
                    Success = false,
                    Message = $"Profile update limit exceeded. Maximum {MaxProfileUpdatesPerWeek} updates per week. " +
                              $"Please try again in {remainingTime.Days} day(s) and {remainingTime.Hours} hour(s)."
                };
            }

            // Build event
            var evt = new UserProfileUpdatedEvent
            {
                UserId = request.UserId,
                FullName = request.FullName,
                Gender = request.Gender,
                BirthDate = request.BirthDate,
                UpdatedAt = Timestamp.FromDateTime(now)
            };
            
            // Set optional fields
            if (request.BirthTime != null) evt.BirthTime = request.BirthTime;
            if (request.HasBirthCity) evt.BirthCity = request.BirthCity;
            if (request.HasLatLong) evt.LatLong = request.LatLong;
            if (request.HasMbtiType) evt.MbtiType = request.MbtiType;
            if (request.HasRelationshipStatus) evt.RelationshipStatus = request.RelationshipStatus;
            if (request.HasInterests) evt.Interests = request.Interests;
            if (request.HasCalendarType) evt.CalendarType = request.CalendarType;
            if (request.HasCurrentResidence) evt.CurrentResidence = request.CurrentResidence;
            if (request.HasEmail) evt.Email = request.Email;
            if (request.HasOccupation) evt.Occupation = request.Occupation;
            if (request.HasIcon) evt.Icon = request.Icon;
            if (request.HasCurrentTimeZone) evt.CurrentTimeZone = request.CurrentTimeZone;
            evt.InterestsList.AddRange(request.InterestsList);

            RaiseEvent(evt);
            await ConfirmEventsAsync();

            Logger.LogInformation("[LumenUserProfileGAgent] User profile updated successfully: {UserId}", request.UserId);

            return new UpdateUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserId = request.UserId,
                CreatedAt = State.CreatedAt,
                UpdatedAt = State.UpdatedAt
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error updating user profile: {UserId}", request.UserId);
            return new UpdateUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    public Task<GetUserProfileResult> GetUserProfileAsync(string userId, string userLanguage = "en")
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] GetUserProfileAsync - Id: {Id}, Language: {Language}", Id, userLanguage);

            if (State.IsDeleted)
            {
                return Task.FromResult(new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                });
            }

            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult(new GetUserProfileResult
                {
                    Success = false,
                    Message = "User profile not found"
                });
            }

            // Calculate WelcomeNote using backend calculations
            var welcomeNote = GenerateWelcomeNote(State.BirthDate);
            var translatedWelcomeNote = TranslateWelcomeNote(welcomeNote, userLanguage);
            
            // Calculate zodiac sign and Chinese zodiac
            var zodiacSignEn = CalculateZodiacSign(State.BirthDate);
            var zodiacSign = TranslateZodiacSign(zodiacSignEn, userLanguage);
            var zodiacSignEnum = ParseZodiacSignEnum(zodiacSignEn);
            
            var chineseZodiacWithElementEn = GetChineseZodiacWithElement(State.BirthDate?.Year ?? 2000);
            var chineseZodiac = TranslateChineseZodiac(chineseZodiacWithElementEn, userLanguage);
            var chineseZodiacAnimal = CalculateChineseZodiac(State.BirthDate?.Year ?? 2000);
            var chineseZodiacEnum = ParseChineseZodiacEnum(chineseZodiacAnimal);

            var profileDto = new LumenUserProfileDto
            {
                UserId = State.UserId,
                FullName = State.FullName,
                Gender = State.Gender,
                BirthDate = State.BirthDate,
                BirthCity = State.BirthCity,
                LatLong = State.LatLong,
                CreatedAt = State.CreatedAt,
                CurrentResidence = State.CurrentResidence,
                UpdatedAt = State.UpdatedAt,
                ZodiacSign = zodiacSign,
                ZodiacSignEnum = zodiacSignEnum,
                ChineseZodiac = chineseZodiac,
                ChineseZodiacEnum = chineseZodiacEnum,
                CurrentLanguage = State.CurrentLanguage ?? "en",
                LatLongInferred = State.LatLongInferred,
                InferredFromCity = State.InferredFromCity
            };
            
            // Set optional fields
            if (State.BirthTime != null) profileDto.BirthTime = State.BirthTime;
            if (State.HasCalendarType) profileDto.CalendarType = State.CalendarType;
            if (State.HasOccupation) profileDto.Occupation = State.Occupation;
            if (State.HasMbtiType) profileDto.MbtiType = State.MbtiType;
            if (State.HasRelationshipStatus) profileDto.RelationshipStatus = State.RelationshipStatus;
            if (State.HasInterests) profileDto.Interests = State.Interests;
            if (State.HasEmail) profileDto.Email = State.Email;
            if (State.HasIcon) profileDto.Icon = State.Icon;
            if (State.HasCurrentTimeZone) profileDto.CurrentTimeZone = State.CurrentTimeZone;
            
            profileDto.InterestsList.AddRange(State.InterestsList);
            
            // Add welcome note
            foreach (var kvp in translatedWelcomeNote)
            {
                profileDto.WelcomeNote[kvp.Key] = kvp.Value;
            }

            return Task.FromResult(new GetUserProfileResult
            {
                Success = true,
                Message = string.Empty,
                UserProfile = profileDto
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error getting user profile");
            return Task.FromResult(new GetUserProfileResult
            {
                Success = false,
                Message = "Internal error occurred"
            });
        }
    }

    public Task<LumenUserProfileDto?> GetRawStateAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(State.UserId))
            {
                return Task.FromResult<LumenUserProfileDto?>(null);
            }

            var profileDto = new LumenUserProfileDto
            {
                UserId = State.UserId,
                FullName = State.FullName,
                Gender = State.Gender,
                BirthDate = State.BirthDate,
                BirthCity = State.BirthCity,
                LatLong = State.LatLong,
                CreatedAt = State.CreatedAt,
                CurrentResidence = State.CurrentResidence,
                UpdatedAt = State.UpdatedAt,
                CurrentLanguage = State.CurrentLanguage ?? "en",
                LatLongInferred = State.LatLongInferred,
                InferredFromCity = State.InferredFromCity
            };
            
            if (State.BirthTime != null) profileDto.BirthTime = State.BirthTime;
            if (State.HasCalendarType) profileDto.CalendarType = State.CalendarType;
            if (State.HasOccupation) profileDto.Occupation = State.Occupation;
            if (State.HasMbtiType) profileDto.MbtiType = State.MbtiType;
            if (State.HasRelationshipStatus) profileDto.RelationshipStatus = State.RelationshipStatus;
            if (State.HasInterests) profileDto.Interests = State.Interests;
            if (State.HasEmail) profileDto.Email = State.Email;
            if (State.HasIcon) profileDto.Icon = State.Icon;
            if (State.HasCurrentTimeZone) profileDto.CurrentTimeZone = State.CurrentTimeZone;
            
            profileDto.InterestsList.AddRange(State.InterestsList);

            return Task.FromResult<LumenUserProfileDto?>(profileDto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error getting raw state");
            return Task.FromResult<LumenUserProfileDto?>(null);
        }
    }

    public async Task<ClearUserResult> ClearUserAsync()
    {
        try
        {
            Logger.LogDebug("[LumenUserProfileGAgent] ClearUserAsync - Id: {Id}", Id);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return new ClearUserResult
                {
                    Success = false,
                    Message = "User profile not found"
                };
            }

            var now = DateTime.UtcNow;

            RaiseEvent(new UserProfileClearedEvent
            {
                ClearedAt = Timestamp.FromDateTime(now)
            });

            await ConfirmEventsAsync();

            Logger.LogInformation("[LumenUserProfileGAgent] User profile cleared successfully");

            return new ClearUserResult
            {
                Success = true,
                Message = "User profile cleared successfully"
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error clearing user profile");
            return new ClearUserResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }
    
    public Task<GetRemainingUpdatesResult> GetRemainingUpdatesAsync()
    {
        try
        {
            var now = DateTime.UtcNow;
            var oneWeekAgo = now.AddDays(-7);
            
            var recentUpdates = State.UpdateHistory
                .Where(ts => ts.ToDateTime() > oneWeekAgo)
                .ToList();
            
            var usedCount = recentUpdates.Count;
            var remainingCount = Math.Max(0, MaxProfileUpdatesPerWeek - usedCount);
            
            Timestamp? nextAvailableAt = null;
            if (remainingCount == 0 && recentUpdates.Count > 0)
            {
                var oldestUpdate = recentUpdates.Min();
                nextAvailableAt = Timestamp.FromDateTime(oldestUpdate.ToDateTime().AddDays(7));
            }
            
            return Task.FromResult(new GetRemainingUpdatesResult
            {
                Success = true,
                UsedCount = usedCount,
                MaxCount = MaxProfileUpdatesPerWeek,
                RemainingCount = remainingCount,
                NextAvailableAt = nextAvailableAt
            });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error getting remaining updates");
            return Task.FromResult(new GetRemainingUpdatesResult
            {
                Success = false,
                UsedCount = 0,
                MaxCount = MaxProfileUpdatesPerWeek,
                RemainingCount = 0,
                NextAvailableAt = null
            });
        }
    }
    
    public async Task<UpdateIconResult> UpdateIconAsync(string? iconUrl)
    {
        try
        {
            var action = string.IsNullOrWhiteSpace(iconUrl) ? "Removing" : "Updating";
            Logger.LogDebug("[LumenUserProfileGAgent] {Action} icon - UserId: {UserId}", action, State.UserId);

            if (string.IsNullOrEmpty(State.UserId))
            {
                return new UpdateIconResult
                {
                    Success = false,
                    Message = "User profile not found. Please create your profile first."
                };
            }

            var now = DateTime.UtcNow;
            var todayStart = now.Date;
            
            // Clean up old upload history (only keep today's records)
            var todayUploads = State.IconUploadHistory
                .Where(ts => ts.ToDateTime().Date == todayStart)
                .ToList();
            State.IconUploadHistory.Clear();
            foreach (var ts in todayUploads)
            {
                State.IconUploadHistory.Add(ts);
            }
            
            if (State.IconUploadHistory.Count >= MaxIconUploadsPerDay)
            {
                return new UpdateIconResult
                {
                    Success = false,
                    Message = $"Daily icon upload limit ({MaxIconUploadsPerDay}) exceeded. Please try again tomorrow.",
                    RemainingUploads = 0
                };
            }

            var evt = new IconUpdatedEvent
            {
                UserId = State.UserId,
                UpdatedAt = Timestamp.FromDateTime(now),
                UploadTimestamp = Timestamp.FromDateTime(now)
            };
            if (!string.IsNullOrEmpty(iconUrl))
            {
                evt.IconUrl = iconUrl;
            }

            RaiseEvent(evt);
            await ConfirmEventsAsync();

            var remainingUploads = Math.Max(0, MaxIconUploadsPerDay - State.IconUploadHistory.Count);

            return new UpdateIconResult
            {
                Success = true,
                Message = string.IsNullOrWhiteSpace(iconUrl) ? "Icon removed successfully" : "Icon updated successfully",
                IconUrl = iconUrl,
                RemainingUploads = remainingUploads
            };
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[LumenUserProfileGAgent] Error updating icon");
            return new UpdateIconResult
            {
                Success = false,
                Message = "Internal error occurred"
            };
        }
    }

    #endregion

    #region Private Helpers

    private (bool IsValid, string Message) ValidateProfileRequest(UpdateUserProfileRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.UserId) || request.UserId.Length < 3 || request.UserId.Length > 50)
        {
            return (false, "UserId must be between 3 and 50 characters");
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            return (false, "Full name is required");
        }

        if (request.BirthDate == null)
        {
            return (false, "Invalid birth date");
        }

        var birthDate = DateValueToDateOnly(request.BirthDate);
        if (birthDate > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            return (false, "Invalid birth date");
        }

        return (true, string.Empty);
    }

    private static DateOnly DateValueToDateOnly(DateValue? date)
    {
        if (date == null) return default;
        return new DateOnly(date.Year, date.Month, date.Day);
    }

    #endregion
}

