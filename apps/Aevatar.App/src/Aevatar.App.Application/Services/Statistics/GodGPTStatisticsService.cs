using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Common;
using Aevatar.Application.Grains.UserStatistics;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Dtos;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.Application.Grains.Agents.ChatManager.Common;

namespace Aevatar.App.Application.Services.Statistics;

/// <summary>
/// Service implementation for managing user statistics and app rating operations.
/// Handles recording and checking app ratings through the UserStatisticsGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTStatisticsService : ApplicationService, IGodGPTStatisticsService
{
    private readonly IGAgentFactory _agentFactory;

    public GodGPTStatisticsService(IGAgentFactory agentFactory)
    {
        _agentFactory = agentFactory;
    }

    /// <inheritdoc />
    public async Task<AppRatingRecordDto> RecordAppRatingAsync(Guid currentUserId, RecordAppRatingInput input)
    {
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var userStatisticsGAgent = _agentFactory.CreateGAgent<UserStatisticsGAgent>(grainId);
        return await userStatisticsGAgent.RecordAppRatingAsync(currentUserId, input.Platform, input.DeviceId);
    }

    /// <inheritdoc />
    public async Task<bool> CanUserRateAppAsync(Guid currentUserId, CanUserRateAppInput input)
    {
        var grainId = CommonHelper.StringToGuid(input.DeviceId);
        var userStatisticsGAgent = _agentFactory.CreateGAgent<UserStatisticsGAgent>(grainId);
        return await userStatisticsGAgent.CanUserRateAppAsync(input.DeviceId);
    }
}
