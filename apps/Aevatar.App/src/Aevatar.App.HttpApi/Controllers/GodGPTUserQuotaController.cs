using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.BlobStorings;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.Application.Grains.ChatManager.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Dtos;
using Aevatar.GodGPT.Dtos;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;
using Volo.Abp;

namespace Aevatar.Controllers;

[RemoteService]
[ControllerName("GodGPTUserQuota")]
[Route("api")]
[Authorize]
public class GodGPTUserQuotaController : AevatarController
{
    private readonly IUserQuotaService _userQuotaService;
    private readonly ILogger<GodGPTUserQuotaController> _logger;

    public GodGPTUserQuotaController(
        IUserQuotaService userQuotaService,
        ILogger<GodGPTUserQuotaController> logger)
    {
        _userQuotaService = userQuotaService;
        _logger = logger;
    }

    [HttpPost("godgpt/account/show-toast")]
    public async Task<Guid> UpdateShowToastAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        await _userQuotaService.SetShownCreditsToastAsync(currentUserId, true);

        _logger.LogDebug("[GodGPTUserQuotaController][UpdateShowToastAsync] userId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return currentUserId;
    }

    [HttpPost("godgpt/account/credits")]
    public async Task<GrainResultDto<int>> UpdateUserCreditsAsync(UpdateUserCreditsInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var result = await _userQuotaService.UpdateUserCreditsAsync(currentUserId, input);

        _logger.LogDebug("[GodGPTUserQuotaController][UpdateUserCreditsAsync] operatorUserId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return result;
    }

    [HttpPost("godgpt/account/subscription")]
    public async Task<GrainResultDto<List<SubscriptionInfoDto>>> UpdateUserSubscriptionAsync(UpdateUserSubscriptionsInput input)
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        var result = await _userQuotaService.UpdateUserSubscriptionAsync(currentUserId, input);

        _logger.LogDebug("[GodGPTUserQuotaController][UpdateUserSubscriptionAsync] operatorUserId: {UserId}, duration: {Duration}ms",
            currentUserId, stopwatch.ElapsedMilliseconds);

        return result;
    }

    [HttpGet("godgpt/can-upload-image")]
    public async Task<CanUploadImageResponseDto> CanUploadImageAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;

        // Preserve language context for agent localization
        var language = HttpContext.GetGodGPTLanguage();
        RequestContext.Set("GodGPTLanguage", language.ToString());

        var response = await _userQuotaService.CanUploadImageAsync(currentUserId);

        var result = new CanUploadImageResponseDto
        {
            CanUpload = response.Success,
            Reason = response.Message
        };

        _logger.LogDebug("[GodGPTUserQuotaController][CanUploadImageAsync] userId: {UserId}, canUpload: {CanUpload}, duration: {Duration}ms",
            currentUserId, result.CanUpload, stopwatch.ElapsedMilliseconds);

        return result;
    }
}


