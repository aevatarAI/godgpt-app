using System;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserInfoCollection;
using Aevatar.Application.Grains.UserInfo;
using Aevatar.Application.Grains.UserInfo.Dtos;
using Aevatar.App.Application.Contracts.Services;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services;

/// <summary>
/// User Info service implementation - manages user information collection during onboarding
/// Uses UserInfoCollectionGAgent architecture
/// </summary>
public class UserInfoService : IUserInfoService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UserInfoService> _logger;

    public UserInfoService(
        IGAgentActorFactory actorFactory,
        ILogger<UserInfoService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    private async Task<IUserInfoCollectionGAgent> GetAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserInfoCollectionGAgent>(userId.ToString());
        return actor.As<IUserInfoCollectionGAgent>();
    }

    public async Task<UserInfoCollectionResponseDto> UpdateUserInfoCollectionAsync(Guid userId, UpdateUserInfoCollectionDto updateDto)
    {
        _logger.LogInformation("[UserInfoService] Updating user info collection for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        
        // Convert DTO to Protobuf
        var protoRequest = new UpdateUserInfoCollectionRequestProto
        {
            UserId = userId.ToString()
        };
        
        if (updateDto.NameInfo != null)
        {
            protoRequest.NameInfo = new UserNameInfoProto
            {
                Gender = updateDto.NameInfo.Gender,
                FirstName = updateDto.NameInfo.FirstName ?? string.Empty,
                LastName = updateDto.NameInfo.LastName ?? string.Empty
            };
        }
        
        if (updateDto.LocationInfo != null)
        {
            protoRequest.LocationInfo = new UserLocationInfoProto
            {
                Country = updateDto.LocationInfo.Country ?? string.Empty,
                City = updateDto.LocationInfo.City ?? string.Empty
            };
        }
        
        if (updateDto.BirthDateInfo != null)
        {
            protoRequest.BirthDateInfo = new UserBirthDateInfoProto();
            if (updateDto.BirthDateInfo.Day.HasValue)
                protoRequest.BirthDateInfo.Day = updateDto.BirthDateInfo.Day.Value;
            if (updateDto.BirthDateInfo.Month.HasValue)
                protoRequest.BirthDateInfo.Month = updateDto.BirthDateInfo.Month.Value;
            if (updateDto.BirthDateInfo.Year.HasValue)
                protoRequest.BirthDateInfo.Year = updateDto.BirthDateInfo.Year.Value;
        }
        
        if (updateDto.BirthTimeInfo != null)
        {
            protoRequest.BirthTimeInfo = new UserBirthTimeInfoProto();
            if (updateDto.BirthTimeInfo.Hour.HasValue)
                protoRequest.BirthTimeInfo.Hour = updateDto.BirthTimeInfo.Hour.Value;
            if (updateDto.BirthTimeInfo.Minute.HasValue)
                protoRequest.BirthTimeInfo.Minute = updateDto.BirthTimeInfo.Minute.Value;
        }
        
        if (updateDto.SeekingInterests != null)
        {
            protoRequest.SeekingInterests.AddRange(updateDto.SeekingInterests.Select(s => (int)s));
        }
        
        if (updateDto.SourceChannels != null)
        {
            protoRequest.SourceChannels.AddRange(updateDto.SourceChannels.Select(s => (int)s));
        }
        
        var protoResult = await agent.UpdateUserInfoCollectionAsync(protoRequest);
        
        // Convert Protobuf to DTO
        return ConvertResponseFromProto(protoResult);
    }

    public async Task<UserInfoCollectionDto> GetUserInfoCollectionAsync(Guid userId)
    {
        _logger.LogDebug("[UserInfoService] Getting user info collection for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.GetUserInfoCollectionAsync();
        
        if (protoResult == null)
        {
            return null;
        }
        
        // Convert Protobuf to DTO
        // Use the known userId parameter directly (State.UserId stores full Agent Id, not raw Guid)
        return ConvertCollectionFromProto(protoResult, userId);
    }

    public async Task<UserInfoDisplayDto> GetUserInfoDisplayAsync(Guid userId)
    {
        _logger.LogDebug("[UserInfoService] Getting user info display for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.GetUserInfoDisplayAsync();
        
        if (protoResult == null)
        {
            return null;
        }
        
        // Convert Protobuf to DTO
        return new UserInfoDisplayDto
        {
            FirstName = protoResult.FirstName,
            LastName = protoResult.LastName,
            Gender = protoResult.Gender,
            Day = protoResult.Day,
            Month = protoResult.Month,
            Year = protoResult.Year,
            Hour = protoResult.HasHour ? protoResult.Hour : null,
            Minute = protoResult.HasMinute ? protoResult.Minute : null,
            Country = protoResult.Country,
            City = protoResult.City,
            SeekingInterests = protoResult.SeekingInterests.ToList(),
            SourceChannels = protoResult.SourceChannels.ToList()
        };
    }

    public async Task ClearAllAsync(Guid userId)
    {
        _logger.LogInformation("[UserInfoService] Clearing all user info collection for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        await agent.ClearAllAsync();
    }

    public async Task<UserInfoOptionsResponseDto> GetUserInfoOptionsAsync(Guid userId)
    {
        _logger.LogDebug("[UserInfoService] Getting user info options for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.GetUserInfoOptionsAsync();
        
        // Convert Protobuf to DTO
        return new UserInfoOptionsResponseDto
        {
            Success = protoResult.Success,
            Message = protoResult.Message,
            SeekingInterestOptions = protoResult.SeekingInterestOptions.Select(o => new SeekingInterestOptionDto
            {
                Code = o.Code,
                Text = o.Text
            }).ToList(),
            SourceChannelOptions = protoResult.SourceChannelOptions.Select(o => new SourceChannelOptionDto
            {
                Code = o.Code,
                Text = o.Text,
                Desc = o.Desc
            }).ToList()
        };
    }

    public async Task<Tuple<string, string>> GenerateUserInfoPromptAsync(Guid userId, DateTime? userLocalTime = null)
    {
        _logger.LogInformation("[UserInfoService] Generating user info prompt for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        
        var protoRequest = new GenerateUserInfoPromptRequestProto
        {
            UserId = userId.ToString()
        };
        
        if (userLocalTime.HasValue)
        {
            protoRequest.UserLocalTime = Timestamp.FromDateTime(DateTime.SpecifyKind(userLocalTime.Value, DateTimeKind.Utc));
        }
        
        var protoResult = await agent.GenerateUserInfoPromptAsync(protoRequest);
        
        return new Tuple<string, string>(protoResult.FullName, protoResult.Prompt);
    }

    private UserInfoCollectionResponseDto ConvertResponseFromProto(UserInfoCollectionResponseProto proto)
    {
        return new UserInfoCollectionResponseDto
        {
            Success = proto.Success,
            Message = proto.Message,
            Data = proto.Data != null ? ConvertCollectionFromProto(proto.Data, null) : null
        };
    }

    private UserInfoCollectionDto ConvertCollectionFromProto(UserInfoCollectionProto proto, Guid? knownUserId = null)
    {
        // Use knownUserId if provided (preferred - avoids parsing Agent Id)
        // Note: proto.UserId stores full Agent Id (format: "AgentType:Guid"), not raw Guid
        var dto = new UserInfoCollectionDto
        {
            UserId = knownUserId ?? Guid.Empty, // Use known userId, don't parse from proto.UserId
            CreatedAt = proto.CreatedAt?.ToDateTime() ?? DateTime.MinValue,
            UpdatedAt = proto.UpdatedAt?.ToDateTime() ?? DateTime.MinValue,
            IsCompleted = proto.IsCompleted,
            IsInitialized = proto.IsInitialized,
            SeekingInterests = proto.SeekingInterests.ToList(),
            SourceChannels = proto.SourceChannels.ToList(),
            SeekingInterestsCode = proto.SeekingInterestsCode.ToList(),
            SourceChannelsCode = proto.SourceChannelsCode.ToList()
        };
        
        if (proto.NameInfo != null)
        {
            dto.NameInfo = new UserNameInfoDto
            {
                Gender = proto.NameInfo.Gender,
                FirstName = proto.NameInfo.FirstName,
                LastName = proto.NameInfo.LastName
            };
        }
        
        if (proto.LocationInfo != null)
        {
            dto.LocationInfo = new UserLocationInfoDto
            {
                Country = proto.LocationInfo.Country,
                City = proto.LocationInfo.City
            };
        }
        
        if (proto.BirthDateInfo != null)
        {
            dto.BirthDateInfo = new UserBirthDateInfoDto
            {
                Day = proto.BirthDateInfo.HasDay ? proto.BirthDateInfo.Day : null,
                Month = proto.BirthDateInfo.HasMonth ? proto.BirthDateInfo.Month : null,
                Year = proto.BirthDateInfo.HasYear ? proto.BirthDateInfo.Year : null
            };
        }
        
        if (proto.BirthTimeInfo != null)
        {
            dto.BirthTimeInfo = new UserBirthTimeInfoDto
            {
                Hour = proto.BirthTimeInfo.HasHour ? proto.BirthTimeInfo.Hour : null,
                Minute = proto.BirthTimeInfo.HasMinute ? proto.BirthTimeInfo.Minute : null
            };
        }
        
        return dto;
    }
}

