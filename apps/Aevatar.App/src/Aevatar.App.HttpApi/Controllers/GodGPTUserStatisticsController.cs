using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.Application.Grains.UserStatistics.Dtos;
using Aevatar.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

[RemoteService]
[ControllerName("GodGPTUserStatistics")]
[Route("api")]
[Authorize]
public class GodGPTUserStatisticsController : AevatarController
{
    private readonly IUserStatisticsService _userStatisticsService;
    private readonly ILogger<GodGPTUserStatisticsController> _logger;

    public GodGPTUserStatisticsController(
        IUserStatisticsService userStatisticsService,
        ILogger<GodGPTUserStatisticsController> logger)
    {
        _userStatisticsService = userStatisticsService;
        _logger = logger;
    }

    [HttpPost("godgpt/user-statistics/app-rating")]
    public async Task<AppRatingRecordDto> RecordAppRatingAsync(RecordAppRatingInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var response = await _userStatisticsService.RecordAppRatingAsync(currentUserId, input);

        _logger.LogDebug("[GodGPTUserStatisticsController][RecordAppRatingAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return response;
    }

    [HttpGet("godgpt/user-statistics/can-rate")]
    public async Task<bool> CanUserRateAppAsync(CanUserRateAppInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var response = await _userStatisticsService.CanUserRateAppAsync(currentUserId, input);

        _logger.LogDebug("[GodGPTUserStatisticsController][CanUserRateAppAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return response;
    }
}


