using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Contracts.Services.Push;
using Aevatar.App.HttpApi.Controllers;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.Application.Constants;
using Aevatar.Dtos.Push;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Volo.Abp;

namespace Aevatar.Controllers;

/// <summary>
/// Controller for managing user devices for push notifications.
/// </summary>
[RemoteService]
[ControllerName("PushDevice")]
[Route("api/push/device")]
[Authorize]
public class PushDeviceController : AevatarController
{
    private readonly IUserDeviceService _userDeviceService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<PushDeviceController> _logger;

    public PushDeviceController(
        IUserDeviceService userDeviceService,
        ILocalizationService localizationService,
        ILogger<PushDeviceController> logger)
    {
        _userDeviceService = userDeviceService;
        _logger = logger;
        _localizationService = localizationService;
    }

    /// <summary>
    /// Register or update device for push notifications.
    /// Called on app launch/login.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RegisterDeviceAsync([FromBody] RegisterDeviceInput input)
    {
        try
        {
            var stopwatch = Stopwatch.StartNew();
            var currentUserId = (Guid)CurrentUser.Id!;
            var language = HttpContext.GetGodGPTLanguage();

            var result = await _userDeviceService.RegisterOrUpdateDeviceAsync(currentUserId, language, input);
            
            _logger.LogDebug(
                "[PushDeviceController][RegisterDevice] UserId: {UserId}, DeviceId: {DeviceId}, Duration: {Duration}ms",
                currentUserId, input.DeviceId, stopwatch.ElapsedMilliseconds);

            // Follow GodGPT pattern: direct return with result data
            return Ok(new
            {
                result = result.Success,
                isNewRegistration = result.IsNewDevice
            });
        }
        catch (ArgumentException ex) when (ex.Message.Contains("timezone"))
        {
            var language = HttpContext.GetGodGPTLanguage();
            var localizedMessage =
                _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidTimezone, language);
            _logger.LogWarning(ex, "Invalid timezone for device registration: {DeviceId}", input.DeviceId);
            return BadRequest(new
            {
                error = new { code = 1, message = localizedMessage },
                result = false
            });
        }
        catch (ArgumentException ex)
        {
            var language = HttpContext.GetGodGPTLanguage();
            var localizedMessage =
                _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidRequest, language);
            _logger.LogWarning(ex, "Invalid request for device registration: {DeviceId}", input.DeviceId);
            return BadRequest(new
            {
                error = new { code = 1, message = localizedMessage },
                result = false
            });
        }
        catch (Exception ex)
        {
            var language = HttpContext.GetGodGPTLanguage();
            var localizedMessage =
                _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language);
            _logger.LogError(ex, "Failed to register/update device {DeviceId}", input.DeviceId);
            return StatusCode(500, new { error = localizedMessage });
        }
    }
    
    /// <summary>
    /// Query device status (timezone, push settings)
    /// Used for debugging and settings verification
    /// </summary>
    [HttpGet("{deviceId}")]
    public virtual async Task<IActionResult> GetDeviceStatusAsync(string deviceId)
    {
        try
        {
            var currentUserId = (Guid)CurrentUser.Id!;
        
            var result = await _userDeviceService.GetDeviceAsync(currentUserId);

            if (result == null || result.DeviceId != deviceId)
            {
                // Return empty object with pushEnabled=true for non-existent devices
                return Ok(new
                {
                    result = true,
                    deviceId = deviceId,
                    timeZoneId = "",
                    pushEnabled = true,
                    pushLanguage = "",
                    pushToken = ""
                });
            }
            return Ok(new
            {
                DeviceId = result.DeviceId,
                TimeZoneId = result.TimeZoneId,
                PushEnabled = result.PushEnabled,
                PushLanguage = result.Language,
                PushToken = result.PushToken
            });
        }
        catch (Exception ex)
        {
            var language = HttpContext.GetGodGPTLanguage();
            var localizedMessage =
                _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language);
            _logger.LogError(ex, "Failed to get device status for device {DeviceId}", deviceId);
            return StatusCode(500, new { error = localizedMessage });
        }
    }
}
