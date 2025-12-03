using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.Application.Grains.UserInfo.Enums;
using Aevatar.Application.Grains.UserInfo.Helpers;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.UserInfo;

public interface IUserInfoCollectionGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionDto updateDto);
    Task<UserInfoCollectionDto> GetUserInfoCollectionAsync();
    Task<UserInfoDisplayDto> GetUserInfoDisplayAsync();
    Task ClearAllAsync();
    Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync();
    Task<Tuple<string, string>> GenerateUserInfoPromptAsync(DateTime? userLocalTime = null);
}

[GAgent(nameof(UserInfoCollectionGAgent))]
public class UserInfoCollectionGAgent : GAgentBase<UserInfoCollectionState>, IUserInfoCollectionGAgent
{
    public UserInfoCollectionGAgent(Guid id) : base(id)
    {
        Logger.LogDebug("[UserInfoCollectionGAgent] Activating agent for user {UserId}", id);
    }
    
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"UserInfoCollectionGAgent for user {Id}, IsInitialized: {State.IsInitialized}");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        
        // Check and initialize first access status if needed
        if (State.FixState == 0)
        {
            Logger.LogDebug("[UserInfoCollectionGAgent][OnActivateAsync] Modify data sync status {0}", Id);
            RaiseEvent(new UpdateFixStateEvent { FixState = 1 });
            await ConfirmEventsAsync();
        }
    }

    public async Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionDto updateDto)
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Updating user info collection userId:{userId}", Id);
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();

        // Validate required fields if they are being updated
        if (updateDto.NameInfo != null)
        {
            if ((updateDto.NameInfo.Gender != 1 && updateDto.NameInfo.Gender != 2) || 
                string.IsNullOrWhiteSpace(updateDto.NameInfo.FirstName) || 
                string.IsNullOrWhiteSpace(updateDto.NameInfo.LastName))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Gender, FirstName, and LastName are required",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.LocationInfo != null)
        {
            if (string.IsNullOrWhiteSpace(updateDto.LocationInfo.Country) || 
                string.IsNullOrWhiteSpace(updateDto.LocationInfo.City))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Country and City are required",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.BirthDateInfo != null)
        {
            if (!updateDto.BirthDateInfo.Day.HasValue || !updateDto.BirthDateInfo.Month.HasValue || !updateDto.BirthDateInfo.Year.HasValue)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Day, Month, and Year are required",
                    Data = ConvertStateToDto()
                };
            }
            
            if (updateDto.BirthDateInfo.Day.Value <= 0 || updateDto.BirthDateInfo.Month.Value <= 0 || updateDto.BirthDateInfo.Year.Value <= 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Valid Day, Month, and Year are required",
                    Data = ConvertStateToDto()
                };
            }
            
            if (updateDto.BirthDateInfo.Day.Value > 31 || updateDto.BirthDateInfo.Month.Value > 12 || 
                updateDto.BirthDateInfo.Year.Value < 1900 || updateDto.BirthDateInfo.Year.Value > DateTime.Now.Year)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid birthDate values",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.BirthTimeInfo != null)
        {
            if (updateDto.BirthTimeInfo.Hour.HasValue && (updateDto.BirthTimeInfo.Hour < 0 || updateDto.BirthTimeInfo.Hour > 23))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Hour must be between 0 and 23",
                    Data = ConvertStateToDto()
                };
            }
            
            if (updateDto.BirthTimeInfo.Minute.HasValue && (updateDto.BirthTimeInfo.Minute < 0 || updateDto.BirthTimeInfo.Minute > 59))
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Minute must be between 0 and 59",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count == 0)
        {
            return new UserInfoCollectionResponseDto
            {
                Success = false,
                Message = "At least one seeking interest is required",
                Data = ConvertStateToDto()
            };
        }
        
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count == 0)
        {
            return new UserInfoCollectionResponseDto
            {
                Success = false,
                Message = "At least one source channel is required",
                Data = ConvertStateToDto()
            };
        }

        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count > 0)
        {
            var invalidSeekingInterests = updateDto.SeekingInterests.Where(x => !System.Enum.IsDefined(typeof(SeekingInterestEnum), x)).ToList();
            if (invalidSeekingInterests.Count > 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid seeking interests",
                    Data = ConvertStateToDto()
                };
            }
        }
        
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count > 0)
        {
            var invalidSourceChannels = updateDto.SourceChannels.Where(x => !System.Enum.IsDefined(typeof(SourceChannelEnum), x)).ToList();
            if (invalidSourceChannels.Count > 0)
            {
                return new UserInfoCollectionResponseDto
                {
                    Success = false,
                    Message = "Invalid source channels",
                    Data = ConvertStateToDto()
                };
            }
        }

        List<int> seekingInterestsCode = null;
        List<string> seekingInterests = null;
        
        if (updateDto.SeekingInterests != null && updateDto.SeekingInterests.Count > 0)
        {
            seekingInterestsCode = updateDto.SeekingInterests.Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            seekingInterests = updateDto.SeekingInterests
                .Select(interest => UserInfoLocalizationHelper.GetSeekingInterestText(interest, language))
                .ToList();
        }
        
        List<int> sourceChannelsCode = null;
        List<string> sourceChannels = null;
        
        if (updateDto.SourceChannels != null && updateDto.SourceChannels.Count > 0)
        {
            sourceChannelsCode = updateDto.SourceChannels.Select(x => (int)x).Distinct().OrderBy(x => x).ToList();
            sourceChannels = updateDto.SourceChannels
                .Select(channel => UserInfoLocalizationHelper.GetSourceChannelText(channel, language).Item1)
                .ToList();
        }
        
        var now = DateTime.UtcNow;
        
        var evt = new UpdateUserInfoCollectionEvent
        {
            UserId = Id.ToString(),
            FirstName = updateDto.NameInfo?.FirstName ?? string.Empty,
            LastName = updateDto.NameInfo?.LastName ?? string.Empty,
            Country = updateDto.LocationInfo?.Country ?? string.Empty,
            City = updateDto.LocationInfo?.City ?? string.Empty,
            UpdatedAt = Timestamp.FromDateTime(now)
        };
        
        if (updateDto.NameInfo?.Gender != null)
            evt.Gender = updateDto.NameInfo.Gender;
        if (updateDto.BirthDateInfo?.Day != null)
            evt.Day = updateDto.BirthDateInfo.Day.Value;
        if (updateDto.BirthDateInfo?.Month != null)
            evt.Month = updateDto.BirthDateInfo.Month.Value;
        if (updateDto.BirthDateInfo?.Year != null)
            evt.Year = updateDto.BirthDateInfo.Year.Value;
        if (updateDto.BirthTimeInfo?.Hour != null)
            evt.Hour = updateDto.BirthTimeInfo.Hour.Value;
        if (updateDto.BirthTimeInfo?.Minute != null)
            evt.Minute = updateDto.BirthTimeInfo.Minute.Value;
        
        if (seekingInterests != null)
            evt.SeekingInterests.AddRange(seekingInterests);
        if (sourceChannels != null)
            evt.SourceChannels.AddRange(sourceChannels);
        if (seekingInterestsCode != null)
            evt.SeekingInterestsCode.AddRange(seekingInterestsCode);
        if (sourceChannelsCode != null)
            evt.SourceChannelsCode.AddRange(sourceChannelsCode);
        
        RaiseEvent(evt);
        await ConfirmEventsAsync();
        
        Logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Successfully updated user info collection");
        
        return new UserInfoCollectionResponseDto
        {
            Success = true,
            Message = "User info collection updated successfully",
            Data = ConvertStateToDto()
        };
    }

    public Task<UserInfoCollectionDto> GetUserInfoCollectionAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Getting user info collection");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] User info collection not initialized");
            return Task.FromResult<UserInfoCollectionDto>(null);
        }
        
        return Task.FromResult(ConvertStateToDto());
    }

    public Task<UserInfoDisplayDto> GetUserInfoDisplayAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] Getting user info display data");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] User info collection not initialized");
            return Task.FromResult<UserInfoDisplayDto>(null);
        }
        
        return Task.FromResult(new UserInfoDisplayDto
        {
            FirstName = State.FirstName,
            LastName = State.LastName,
            Gender = State.Gender,
            Day = State.Day,
            Month = State.Month,
            Year = State.Year,
            Hour = State.HasHour ? State.Hour : null,
            Minute = State.HasMinute ? State.Minute : null,
            Country = State.Country,
            City = State.City,
            SeekingInterests = State.SeekingInterests.ToList(),
            SourceChannels = State.SourceChannels.ToList()
        });
    }

    public async Task ClearAllAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Clearing all user info collection data");
        
        RaiseEvent(new ClearUserInfoCollectionEvent());
        await ConfirmEventsAsync();
        
        Logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Successfully cleared all user info collection data");
    }
    
    public Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync()
    {
        Logger.LogDebug("[UserInfoCollectionGAgent][GetUserInfoOptionsAsync] Getting user info options");
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();

        var seekingInterestOptions = UserInfoLocalizationHelper.GetSeekingInterestEnumOptions(language);
        var sourceChannelOptions = UserInfoLocalizationHelper.GetSourceChannelEnumOptions(language);
        
        return Task.FromResult(new UserInfoOptionsResponseDto
        {
            Success = true,
            Message = "Options retrieved successfully",
            SeekingInterestOptions = seekingInterestOptions,
            SourceChannelOptions = sourceChannelOptions
        });
    }

    public Task<Tuple<string, string>> GenerateUserInfoPromptAsync(DateTime? userLocalTime = null)
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Generating user info prompt");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] User info collection not initialized");
            return Task.FromResult(new Tuple<string, string>(string.Empty, string.Empty));
        }

        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        var currentTime = userLocalTime ?? DateTime.UtcNow;
        
        var fullName = $"{State.FirstName} {State.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            fullName = "Unknown";
        }
        
        var location = $"{State.City}, {State.Country}".Trim(' ', ',');
        if (string.IsNullOrWhiteSpace(location))
        {
            location = "Unknown";
        }
        
        var genderText = State.Gender switch
        {
            1 => "Male",
            2 => "Female", 
            _ => "Unknown"
        };
        
        var age = "Unknown";
        if (State.Year > 0 && State.Month > 0 && State.Day > 0)
        {
            try
            {
                var birthDate = new DateTime(State.Year, State.Month, State.Day);
                var calculatedAge = currentTime.Year - birthDate.Year;
                if (currentTime < birthDate.AddYears(calculatedAge))
                {
                    calculatedAge--;
                }
                age = calculatedAge.ToString();
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Invalid birth date: {Year}-{Month}-{Day}", State.Year, State.Month, State.Day);
                age = "Unknown";
            }
        }
        
        var languageText = language switch
        {
            GodGPTLanguage.English => "English",
            GodGPTLanguage.TraditionalChinese => "Traditional Chinese",
            GodGPTLanguage.Spanish => "Spanish",
            GodGPTLanguage.CN => "Chinese",
            _ => "English"
        };
        
        var timeText = currentTime.ToString("yyyy-MM-dd HH:mm:ss");
        
        var prompt = $@"Generate a personalized ""Today's Dos and Don'ts"" for the user based on their information and cosmological theories.
User Name: {fullName}
User Location: {location}
User Message Time: {timeText}
User Gender: {genderText}
User Age: {age}
User Language: {languageText}";

        Logger.LogDebug("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Generated prompt for user {UserId}", State.UserId);
        
        return Task.FromResult(new Tuple<string, string>(fullName, prompt));
    }

    private UserInfoCollectionDto ConvertStateToDto()
    {
        var userId = string.IsNullOrEmpty(State.UserId) ? Guid.Empty : Guid.Parse(State.UserId);
        
        return new UserInfoCollectionDto
        {
            UserId = userId,
            NameInfo = !string.IsNullOrWhiteSpace(State.FirstName) ? new UserNameInfoDto
            {
                Gender = State.Gender,
                FirstName = State.FirstName,
                LastName = State.LastName
            } : null,
            LocationInfo = !string.IsNullOrWhiteSpace(State.Country) ? new UserLocationInfoDto
            {
                Country = State.Country,
                City = State.City
            } : null,
            BirthDateInfo = State.Day > 0 && State.Month > 0 && State.Year > 0 ? new UserBirthDateInfoDto
            {
                Day = State.Day,
                Month = State.Month,
                Year = State.Year
            } : null,
            BirthTimeInfo = State.HasHour || State.HasMinute ? new UserBirthTimeInfoDto
            {
                Hour = State.HasHour ? State.Hour : null,
                Minute = State.HasMinute ? State.Minute : null
            } : null,
            SeekingInterests = State.SeekingInterests.ToList(),
            SourceChannels = State.SourceChannels.ToList(),
            CreatedAt = State.CreatedAt?.ToDateTime() ?? DateTime.MinValue,
            UpdatedAt = State.LastUpdated?.ToDateTime() ?? DateTime.MinValue,
            IsInitialized = State.IsInitialized,
            SeekingInterestsCode = State.SeekingInterestsCode.ToList(),
            SourceChannelsCode = State.SourceChannelsCode.ToList(),
            IsCompleted = IsCollectionCompleted()
        };
    }
    
    private bool IsCollectionCompleted()
    {
        return State.Gender != 0 &&
               !string.IsNullOrWhiteSpace(State.FirstName) &&
               !string.IsNullOrWhiteSpace(State.LastName) &&
               !string.IsNullOrWhiteSpace(State.Country) &&
               !string.IsNullOrWhiteSpace(State.City) &&
               State.Day > 0 && State.Month > 0 && State.Year > 0 &&
               State.SeekingInterests.Count > 0 &&
               State.SourceChannels.Count > 0;
    }

    #region EventHandlers

    [EventHandler]
    public void HandleInitializeUserInfoCollectionEvent(InitializeUserInfoCollectionEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleUpdateUserInfoCollectionEvent(UpdateUserInfoCollectionEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleClearUserInfoCollectionEvent(ClearUserInfoCollectionEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleUpdateFixStateEvent(UpdateFixStateEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion
    
    protected override void TransitionState(UserInfoCollectionState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeUserInfoCollectionEvent initializeEvent:
                state.UserId = initializeEvent.UserId;
                state.IsInitialized = true;
                state.CreatedAt = initializeEvent.CreatedAt;
                state.LastUpdated = initializeEvent.CreatedAt;
                Logger.LogDebug("[UserInfoCollectionGAgent][TransitionState] Initialized user info collection for user {UserId}", initializeEvent.UserId);
                break;
                
            case UpdateUserInfoCollectionEvent updateEvent:
                var isFirstUpdate = !state.IsInitialized;
                
                if (isFirstUpdate)
                {
                    state.IsInitialized = true;
                    state.UserId = updateEvent.UserId;
                    state.CreatedAt = updateEvent.UpdatedAt;
                }

                if (string.IsNullOrEmpty(state.UserId))
                {
                    state.UserId = updateEvent.UserId;
                }

                state.LastUpdated = updateEvent.UpdatedAt;
                
                if (updateEvent.HasGender && updateEvent.Gender != 0) state.Gender = updateEvent.Gender;
                if (!string.IsNullOrWhiteSpace(updateEvent.FirstName)) state.FirstName = updateEvent.FirstName;
                if (!string.IsNullOrWhiteSpace(updateEvent.LastName)) state.LastName = updateEvent.LastName;
                
                if (!string.IsNullOrWhiteSpace(updateEvent.Country)) state.Country = updateEvent.Country;
                if (!string.IsNullOrWhiteSpace(updateEvent.City)) state.City = updateEvent.City;
                
                if (updateEvent.HasDay && updateEvent.Day > 0) state.Day = updateEvent.Day;
                if (updateEvent.HasMonth && updateEvent.Month > 0) state.Month = updateEvent.Month;
                if (updateEvent.HasYear && updateEvent.Year > 0) state.Year = updateEvent.Year;
                
                if (updateEvent.HasHour && updateEvent.Hour >= 0 && updateEvent.Hour <= 23) 
                    state.Hour = updateEvent.Hour;
                if (updateEvent.HasMinute && updateEvent.Minute >= 0 && updateEvent.Minute <= 59) 
                    state.Minute = updateEvent.Minute;
                
                if (updateEvent.SeekingInterests.Count > 0)
                {
                    state.SeekingInterests.Clear();
                    state.SeekingInterests.AddRange(updateEvent.SeekingInterests);
                }
                
                if (updateEvent.SourceChannels.Count > 0)
                {
                    state.SourceChannels.Clear();
                    state.SourceChannels.AddRange(updateEvent.SourceChannels);
                }
                
                if (updateEvent.SeekingInterestsCode.Count > 0)
                {
                    state.SeekingInterestsCode.Clear();
                    state.SeekingInterestsCode.AddRange(updateEvent.SeekingInterestsCode);
                }
                
                if (updateEvent.SourceChannelsCode.Count > 0)
                {
                    state.SourceChannelsCode.Clear();
                    state.SourceChannelsCode.AddRange(updateEvent.SourceChannelsCode);
                }
                
                Logger.LogDebug("[UserInfoCollectionGAgent][TransitionState] Updated user info collection, userId:{userId} isFirstUpdate: {IsFirstUpdate}", updateEvent.UserId, isFirstUpdate);
                break;
                
            case ClearUserInfoCollectionEvent:
                state.UserId = string.Empty;
                state.IsInitialized = false;
                state.CreatedAt = null;
                state.LastUpdated = null;
                state.Gender = 0;
                state.FirstName = string.Empty;
                state.LastName = string.Empty;
                state.Country = string.Empty;
                state.City = string.Empty;
                state.Day = 0;
                state.Month = 0;
                state.Year = 0;
                state.ClearHour();
                state.ClearMinute();
                state.SeekingInterests.Clear();
                state.SourceChannels.Clear();
                state.SeekingInterestsCode.Clear();
                state.SourceChannelsCode.Clear();
                Logger.LogDebug("[UserInfoCollectionGAgent][TransitionState] Cleared all user info collection data");
                break;
                
            case UpdateFixStateEvent updateFixState:
                state.FixState = updateFixState.FixState;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}
