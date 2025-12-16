using Aevatar.App.HttpApi.Controllers;
using System;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;
using Aevatar.App.Application.Services;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Dtos;
using Aevatar.Service;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp.AspNetCore.Mvc;

namespace Aevatar.Controllers;

/// <summary>
/// Weekly user feedback report controller
/// </summary>
[ApiController]
[Route("api/godgpt/management")]
[Authorize]
public class GodGPTManagementController : AbpControllerBase
{
    private readonly ILogger<GodGPTManagementController> _logger;
    private readonly IGodGPTService _godGptService;
    private readonly IInvitationService _invitationService;

    public GodGPTManagementController(
        ILogger<GodGPTManagementController> logger,
        IGodGPTService godGptService,
        IInvitationService invitationService)
    {
        _logger = logger;
        _godGptService = godGptService;
        _invitationService = invitationService;
    }

    [HttpGet("batch-info/{batchId}")]
    public async Task<BatchInfoDto> GetBatchInfoAsync(string batchId)
    {
        await CheckUserIsManager();
        
        var stopwatch = Stopwatch.StartNew();
        var currentUserId = (Guid)CurrentUser.Id!;
        var batchInfoDto = await _invitationService.GetBatchInfoAsync(batchId);
        _logger.LogDebug("[GodGPTInvitationController][GetBatchInfoAsync] userId: {0}, duration: {1}ms",
            currentUserId.ToString(), stopwatch.ElapsedMilliseconds);
        return batchInfoDto;
    }
    
    private async Task CheckUserIsManager()
    {
        var currentUserId = (Guid)CurrentUser.Id!;
        if (!await _godGptService.CheckIsManager(currentUserId))
        {
            _logger.LogInformation($"User is not manager {currentUserId}");
            throw new SecurityException($"User is not manager {currentUserId}");
        }
    }
}