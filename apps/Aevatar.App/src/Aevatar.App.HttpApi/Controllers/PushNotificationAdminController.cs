using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services.Push;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.Permissions;
using Aevatar.Dtos.Push;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Admin controller for sending push notifications.
/// </summary>
[RemoteService]
[ControllerName("PushNotificationAdmin")]
[Route("api/admin/push")]
[Authorize(AppPermissions.PushNotification.Default)]
public class PushNotificationAdminController : AevatarController
{
    private readonly IPushNotificationService _pushNotificationService;
    private readonly ILogger<PushNotificationAdminController> _logger;

    public PushNotificationAdminController(
        IPushNotificationService pushNotificationService,
        ILogger<PushNotificationAdminController> logger)
    {
        _pushNotificationService = pushNotificationService;
        _logger = logger;
    }

    /// <summary>
    /// Send push notifications to all users in a specific timezone.
    /// </summary>
    [HttpPost("send-by-timezone")]
    [Authorize(AppPermissions.PushNotification.SendByTimezone)]
    public async Task<PushResult> SendByTimezoneAsync(
        [FromBody] SendPushByTimezoneInput input,
        CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "[PushNotificationAdminController][SendByTimezone] TimeZone: {TimeZone}, Title: {Title}",
            input.TimeZoneId, input.Title);
        
        var result = await _pushNotificationService.SendByTimezoneAsync(input, ct);
        
        _logger.LogInformation(
            "[PushNotificationAdminController][SendByTimezone] TimeZone: {TimeZone}, Targeted: {Targeted}, Success: {Success}, Failed: {Failed}, Duration: {Duration}ms",
            input.TimeZoneId, result.TotalTargeted, result.SuccessCount, result.FailureCount, stopwatch.ElapsedMilliseconds);
        
        return result;
    }
}
