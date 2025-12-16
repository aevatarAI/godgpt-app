using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserStatistics;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.Dtos;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

namespace Aevatar.App.Application.Services;

/// <summary>
/// User Statistics service implementation - manages user behavior statistics including app ratings
/// Uses UserStatisticsGAgent architecture
/// </summary>
public class UserStatisticsService : IUserStatisticsService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<UserStatisticsService> _logger;

    public UserStatisticsService(
        IGAgentActorFactory actorFactory,
        ILogger<UserStatisticsService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    private async Task<IUserStatisticsGAgent> GetAgentAsync(Guid userId)
    {
        var actor = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(userId);
        return actor.As<IUserStatisticsGAgent>();
    }

    public async Task<AppRatingRecordDto> RecordAppRatingAsync(Guid userId, RecordAppRatingInput input)
    {
        _logger.LogInformation("[UserStatisticsService] Recording app rating for user {UserId}, platform {Platform}, device {DeviceId}", 
            userId, input.Platform, input.DeviceId);
        
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var actor = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(grainId);
        var agent = actor.As<IUserStatisticsGAgent>();
        
        var request = new Aevatar.Agents.GodGPT.Protos.UserStatistics.RecordAppRatingRequestProto
        {
            UserId = userId.ToString(),
            Platform = input.Platform,
            DeviceId = input.DeviceId
        };
        
        var protoResult = await agent.RecordAppRatingAsync(request);
        
        // Convert Protobuf to DTO
        return new AppRatingRecordDto
        {
            Platform = protoResult.Platform,
            DeviceId = protoResult.DeviceId,
            FirstRatingTime = protoResult.FirstRatingTime.ToDateTime(),
            LastRatingTime = protoResult.LastRatingTime.ToDateTime(),
            RatingCount = protoResult.RatingCount
        };
    }

    public async Task<bool> CanUserRateAppAsync(Guid userId, CanUserRateAppInput input)
    {
        _logger.LogDebug("[UserStatisticsService] Checking if user {UserId} can rate app for device {DeviceId}", 
            userId, input.DeviceId);
        
        // Use deviceId to get agent (agent ID is based on deviceId, not userId)
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var actor = await _actorFactory.CreateGAgentActorAsync<UserStatisticsGAgent>(grainId);
        var agent = actor.As<IUserStatisticsGAgent>();
        
        return await agent.CanUserRateAppAsync(input.DeviceId);
    }

    public async Task<UserStatisticsDto> GetUserStatisticsAsync(Guid userId)
    {
        _logger.LogDebug("[UserStatisticsService] Getting user statistics for user {UserId}", userId);
        
        var agent = await GetAgentAsync(userId);
        var protoResult = await agent.GetUserStatisticsAsync();
        
        // Convert Protobuf to DTO
        return new UserStatisticsDto
        {
            UserId = Guid.TryParse(protoResult.UserId, out var uid) ? uid : Guid.Empty,
            AppRatings = protoResult.AppRatings.Select(r => new AppRatingRecordDto
            {
                Platform = r.Platform,
                DeviceId = r.DeviceId,
                FirstRatingTime = r.FirstRatingTime.ToDateTime(),
                LastRatingTime = r.LastRatingTime.ToDateTime(),
                RatingCount = r.RatingCount
            }).ToList()
        };
    }

    public async Task<List<AppRatingRecordDto>> GetAppRatingRecordsAsync(Guid userId, string? deviceId = null)
    {
        _logger.LogDebug("[UserStatisticsService] Getting app rating records for user {UserId}, deviceId {DeviceId}", 
            userId, deviceId);
        
        var agent = await GetAgentAsync(userId);
        var request = new Aevatar.Agents.GodGPT.Protos.UserStatistics.GetAppRatingRecordsRequestProto
        {
            UserId = userId.ToString()
        };
        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            request.DeviceId = deviceId;
        }
        
        var protoResult = await agent.GetAppRatingRecordsAsync(request);
        
        // Convert Protobuf to DTO
        return protoResult.Records.Select(r => new AppRatingRecordDto
        {
            Platform = r.Platform,
            DeviceId = r.DeviceId,
            FirstRatingTime = r.FirstRatingTime.ToDateTime(),
            LastRatingTime = r.LastRatingTime.ToDateTime(),
            RatingCount = r.RatingCount
        }).ToList();
    }
}

