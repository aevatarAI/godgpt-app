using System;
using System.Diagnostics;
using System.Security;
using System.Threading.Tasks;
using Aevatar.App.Application.Services;
using Aevatar.App.Application.Contracts.Services.Admin;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.Application.Grains.FreeTrialCode.Dtos;
using Aevatar.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Aevatar.Controllers;

/// <summary>
/// Management controller for admin operations.
/// Handles batch info queries and other administrative functions.
/// </summary>
[ApiController]
[Route("api/godgpt/management")]
[Authorize]
public class GodGPTManagementController : AevatarController
{
    private readonly ILogger<GodGPTManagementController> _logger;
    private readonly IGodGPTAdminService _adminService;
    private readonly IInvitationService _invitationService;

    public GodGPTManagementController(
        ILogger<GodGPTManagementController> logger,
        IGodGPTAdminService adminService,
        IInvitationService invitationService)
    {
        _logger = logger;
        _adminService = adminService;
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
        if (!await _adminService.CheckIsManagerAsync(currentUserId))
        {
            _logger.LogInformation($"User is not manager {currentUserId}");
            throw new SecurityException($"User is not manager {currentUserId}");
        }
    }
}