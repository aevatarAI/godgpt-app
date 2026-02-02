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

/// <summary>
/// User Info Collection Agent interface - manages user information collection during onboarding.
/// 
/// Note: This is NOT an Orleans Grain interface. Agent runs inside OrleansGAgentGrain.
/// Use IGAgentActorManager to manage Agent lifecycle.
/// All RPC-exposed methods use Protobuf types for cross-runtime compatibility.
/// </summary>
public interface IUserInfoCollectionGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    /// <summary>
    /// Update user information collection (uses Protobuf type for RPC)
    /// </summary>
    Task<UserInfoCollectionResponseProto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionRequestProto request);
    
    /// <summary>
    /// Get user information collection (uses Protobuf type for RPC)
    /// </summary>
    Task<UserInfoCollectionProto> GetUserInfoCollectionAsync();
    
    /// <summary>
    /// Get user info display data (uses Protobuf type for RPC)
    /// </summary>
    Task<UserInfoDisplayProto> GetUserInfoDisplayAsync();
    
    /// <summary>
    /// Clear all user info collection data
    /// </summary>
    Task ClearAllAsync();
    
    /// <summary>
    /// Get user info options (uses Protobuf type for RPC)
    /// </summary>
    Task<UserInfoOptionsResponseProto> GetUserInfoOptionsAsync();
    
    /// <summary>
    /// Generate user info prompt for AI (uses Protobuf type for RPC)
    /// </summary>
    Task<GenerateUserInfoPromptResponseProto> GenerateUserInfoPromptAsync(GenerateUserInfoPromptRequestProto request);
}

public class UserInfoCollectionGAgent : GAgentBase<UserInfoCollectionState>, IUserInfoCollectionGAgent
{
    // Parameterless constructor required for Orleans activation
    public UserInfoCollectionGAgent() : base()
    {
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

    public async Task<UserInfoCollectionResponseProto> UpdateUserInfoCollectionAsync(UpdateUserInfoCollectionRequestProto request)
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][UpdateUserInfoCollectionAsync] Updating user info collection userId:{userId}", request.UserId);
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);

        // Validate optional fields if they are being updated
        if (request.NameInfo != null)
        {
            // Gender is optional, but if provided must be valid (1 or 2)
            if (request.NameInfo.HasGender && 
                request.NameInfo.Gender != 1 && request.NameInfo.Gender != 2)
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Gender is invalid",
                    Data = ConvertStateToProto()
                };
            }
        }

        // BirthDateInfo is optional - validate only if values are provided
        if (request.BirthDateInfo != null)
        {
            if (!request.BirthDateInfo.HasDay || !request.BirthDateInfo.HasMonth || !request.BirthDateInfo.HasYear)
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Day, Month, and Year are required",
                    Data = ConvertStateToProto()
                };
            }
            
            if (request.BirthDateInfo.Day <= 0 || request.BirthDateInfo.Month <= 0 || request.BirthDateInfo.Year <= 0)
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Valid Day, Month, and Year are required",
                    Data = ConvertStateToProto()
                };
            }
            
            if (request.BirthDateInfo.Day > 31 || request.BirthDateInfo.Month > 12 || 
                request.BirthDateInfo.Year < 1900 || request.BirthDateInfo.Year > DateTime.Now.Year)
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Invalid birthDate values",
                    Data = ConvertStateToProto()
                };
            }
        }
        
        if (request.BirthTimeInfo != null)
        {
            if (request.BirthTimeInfo.HasHour && (request.BirthTimeInfo.Hour < 0 || request.BirthTimeInfo.Hour > 23))
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Hour must be between 0 and 23",
                    Data = ConvertStateToProto()
                };
            }
            
            if (request.BirthTimeInfo.HasMinute && (request.BirthTimeInfo.Minute < 0 || request.BirthTimeInfo.Minute > 59))
            {
                return new UserInfoCollectionResponseProto
                {
                    Success = false,
                    Message = "Minute must be between 0 and 59",
                    Data = ConvertStateToProto()
                };
            }
        }
        
        if (request.SeekingInterests.Count == 0)
        {
            return new UserInfoCollectionResponseProto
            {
                Success = false,
                Message = "At least one seeking interest is required",
                Data = ConvertStateToProto()
            };
        }
        
        if (request.SourceChannels.Count == 0)
        {
            return new UserInfoCollectionResponseProto
            {
                Success = false,
                Message = "At least one source channel is required",
                Data = ConvertStateToProto()
            };
        }

        // Validate enum values
        var invalidSeekingInterests = request.SeekingInterests.Where(x => !System.Enum.IsDefined(typeof(SeekingInterestEnum), x)).ToList();
        if (invalidSeekingInterests.Count > 0)
        {
            return new UserInfoCollectionResponseProto
            {
                Success = false,
                Message = "Invalid seeking interests",
                Data = ConvertStateToProto()
            };
        }
        
        var invalidSourceChannels = request.SourceChannels.Where(x => !System.Enum.IsDefined(typeof(SourceChannelEnum), x)).ToList();
        if (invalidSourceChannels.Count > 0)
        {
            return new UserInfoCollectionResponseProto
            {
                Success = false,
                Message = "Invalid source channels",
                Data = ConvertStateToProto()
            };
        }

        List<int> seekingInterestsCode = null;
        List<string> seekingInterests = null;
        
        if (request.SeekingInterests.Count > 0)
        {
            seekingInterestsCode = request.SeekingInterests.Distinct().OrderBy(x => x).ToList();
            seekingInterests = request.SeekingInterests
                .Select(interest => UserInfoLocalizationHelper.GetSeekingInterestText((SeekingInterestEnum)interest, language))
                .ToList();
        }
        
        List<int> sourceChannelsCode = null;
        List<string> sourceChannels = null;
        
        if (request.SourceChannels.Count > 0)
        {
            sourceChannelsCode = request.SourceChannels.Distinct().OrderBy(x => x).ToList();
            sourceChannels = request.SourceChannels
                .Select(channel => UserInfoLocalizationHelper.GetSourceChannelText((SourceChannelEnum)channel, language).Item1)
                .ToList();
        }
        
        var now = DateTime.UtcNow;
        
        var evt = new UpdateUserInfoCollectionEvent
        {
            UserId = request.UserId,
            FirstName = request.NameInfo != null ? request.NameInfo.FirstName : string.Empty,
            LastName = request.NameInfo != null ? request.NameInfo.LastName : string.Empty,
            Country = request.LocationInfo != null ? request.LocationInfo.Country : string.Empty,
            City = request.LocationInfo != null ? request.LocationInfo.City : string.Empty,
            UpdatedAt = Timestamp.FromDateTime(now)
        };
        
        if (request.NameInfo != null && request.NameInfo.Gender != 0)
            evt.Gender = request.NameInfo.Gender;
        if (request.BirthDateInfo != null && request.BirthDateInfo.HasDay)
            evt.Day = request.BirthDateInfo.Day;
        if (request.BirthDateInfo != null && request.BirthDateInfo.HasMonth)
            evt.Month = request.BirthDateInfo.Month;
        if (request.BirthDateInfo != null && request.BirthDateInfo.HasYear)
            evt.Year = request.BirthDateInfo.Year;
        if (request.BirthTimeInfo != null && request.BirthTimeInfo.HasHour)
            evt.Hour = request.BirthTimeInfo.Hour;
        if (request.BirthTimeInfo != null && request.BirthTimeInfo.HasMinute)
            evt.Minute = request.BirthTimeInfo.Minute;
        
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
        
        return new UserInfoCollectionResponseProto
        {
            Success = true,
            Message = "User info collection updated successfully",
            Data = ConvertStateToProto()
        };
    }

    public Task<UserInfoCollectionProto> GetUserInfoCollectionAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] Getting user info collection");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoCollectionAsync] User info collection not initialized");
            return Task.FromResult<UserInfoCollectionProto>(null);
        }
        
        return Task.FromResult(ConvertStateToProto());
    }

    public Task<UserInfoDisplayProto> GetUserInfoDisplayAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] Getting user info display data");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GetUserInfoDisplayAsync] User info collection not initialized");
            return Task.FromResult<UserInfoDisplayProto>(null);
        }
        
        var result = new UserInfoDisplayProto
        {
            FirstName = State.FirstName,
            LastName = State.LastName,
            Gender = State.Gender,
            Day = State.Day,
            Month = State.Month,
            Year = State.Year,
            Country = State.Country,
            City = State.City
        };
        
        if (State.HasHour)
            result.Hour = State.Hour;
        if (State.HasMinute)
            result.Minute = State.Minute;
        
        result.SeekingInterests.AddRange(State.SeekingInterests);
        result.SourceChannels.AddRange(State.SourceChannels);
        
        return Task.FromResult(result);
    }

    public async Task ClearAllAsync()
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Clearing all user info collection data");
        
        RaiseEvent(new ClearUserInfoCollectionEvent());
        await ConfirmEventsAsync();
        
        Logger.LogInformation("[UserInfoCollectionGAgent][ClearAllAsync] Successfully cleared all user info collection data");
    }
    
    public Task<UserInfoOptionsResponseProto> GetUserInfoOptionsAsync()
    {
        Logger.LogDebug("[UserInfoCollectionGAgent][GetUserInfoOptionsAsync] Getting user info options");
        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);

        var seekingInterestOptions = UserInfoLocalizationHelper.GetSeekingInterestEnumOptions(language);
        var sourceChannelOptions = UserInfoLocalizationHelper.GetSourceChannelEnumOptions(language);
        
        var result = new UserInfoOptionsResponseProto
        {
            Success = true,
            Message = "Options retrieved successfully"
        };
        
        foreach (var option in seekingInterestOptions)
        {
            result.SeekingInterestOptions.Add(new SeekingInterestOptionProto
            {
                Code = option.Code,
                Text = option.Text
            });
        }
        
        foreach (var option in sourceChannelOptions)
        {
            result.SourceChannelOptions.Add(new SourceChannelOptionProto
            {
                Code = option.Code,
                Text = option.Text,
                Desc = option.Desc
            });
        }
        
        return Task.FromResult(result);
    }

    public Task<GenerateUserInfoPromptResponseProto> GenerateUserInfoPromptAsync(GenerateUserInfoPromptRequestProto request)
    {
        Logger.LogInformation("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] Generating user info prompt");
        
        if (!State.IsInitialized)
        {
            Logger.LogWarning("[UserInfoCollectionGAgent][GenerateUserInfoPromptAsync] User info collection not initialized");
            return Task.FromResult(new GenerateUserInfoPromptResponseProto
            {
                FullName = string.Empty,
                Prompt = string.Empty
            });
        }

        var language = GodGPTLanguageHelper.GetGodGPTLanguage(Context);
        var currentTime = request.UserLocalTime != null
            ? request.UserLocalTime.ToDateTime() 
            : DateTime.UtcNow;
        
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
        
        return Task.FromResult(new GenerateUserInfoPromptResponseProto
        {
            FullName = fullName,
            Prompt = prompt
        });
    }

    private UserInfoCollectionProto ConvertStateToProto()
    {
        var result = new UserInfoCollectionProto
        {
            UserId = State.UserId,
            IsInitialized = State.IsInitialized,
            IsCompleted = IsCollectionCompleted()
        };
        
        if (State.CreatedAt != null)
            result.CreatedAt = State.CreatedAt;
        if (State.LastUpdated != null)
            result.UpdatedAt = State.LastUpdated;
        
        if (!string.IsNullOrWhiteSpace(State.FirstName) || !string.IsNullOrWhiteSpace(State.LastName) || State.Gender > 0)
        {
            result.NameInfo = new UserNameInfoProto
            {
                Gender = State.Gender,
                FirstName = State.FirstName,
                LastName = State.LastName
            };
        }
        
        if (!string.IsNullOrWhiteSpace(State.Country) || !string.IsNullOrWhiteSpace(State.City))
        {
            result.LocationInfo = new UserLocationInfoProto
            {
                Country = State.Country,
                City = State.City
            };
        }
        
        if (State.Day > 0 && State.Month > 0 && State.Year > 0)
        {
            result.BirthDateInfo = new UserBirthDateInfoProto
            {
                Day = State.Day,
                Month = State.Month,
                Year = State.Year
            };
        }
        
        if (State.HasHour || State.HasMinute)
        {
            result.BirthTimeInfo = new UserBirthTimeInfoProto();
            if (State.HasHour)
                result.BirthTimeInfo.Hour = State.Hour;
            if (State.HasMinute)
                result.BirthTimeInfo.Minute = State.Minute;
        }
        
        result.SeekingInterests.AddRange(State.SeekingInterests);
        result.SourceChannels.AddRange(State.SourceChannels);
        result.SeekingInterestsCode.AddRange(State.SeekingInterestsCode);
        result.SourceChannelsCode.AddRange(State.SourceChannelsCode);
        
        return result;
    }
    
    private bool IsCollectionCompleted()
    {
        return State.SeekingInterests.Count > 0 &&
               State.SourceChannels.Count > 0;
    }
    
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
