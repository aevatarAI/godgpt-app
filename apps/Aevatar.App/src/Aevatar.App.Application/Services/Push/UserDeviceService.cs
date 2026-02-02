using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.UserDevice;
using Aevatar.App.Application.Contracts.Services.Push;
using Aevatar.Application.Grains.Agents.UserDevice;
using Aevatar.Dtos.Push;
using Microsoft.Extensions.Logging;
using Volo.Abp.ObjectMapping;

namespace Aevatar.App.Application.Services.Push;

/// <summary>
/// Service for managing user devices for push notifications.
/// </summary>
public class UserDeviceService : IUserDeviceService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IObjectMapper _objectMapper;
    private readonly ILogger<UserDeviceService> _logger;

    public UserDeviceService(
        IGAgentActorFactory actorFactory,
        IObjectMapper objectMapper,
        ILogger<UserDeviceService> logger)
    {
        _actorFactory = actorFactory;
        _objectMapper = objectMapper;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<RegisterDeviceResult> RegisterOrUpdateDeviceAsync(Guid userId, RegisterDeviceInput input)
    {
        try
        {
            _logger.LogInformation(
                "[UserDeviceService][RegisterOrUpdateDevice] UserId: {UserId}, DeviceId: {DeviceId}, Platform: {Platform}, TimeZone: {TimeZone}",
                userId, input.DeviceId, input.Platform, input.TimeZoneId);

            // Simple timezone validation - let it throw if invalid
            if (!string.IsNullOrEmpty(input.TimeZoneId))
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);
                }
                catch (TimeZoneNotFoundException ex)
                {
                    throw new ArgumentException($"Invalid timezone ID: {input.TimeZoneId}", ex);
                }
                catch (InvalidTimeZoneException ex)
                {
                    throw new ArgumentException($"Invalid timezone format: {input.TimeZoneId}", ex);
                }
            }
            
            var actor = await _actorFactory.CreateGAgentActorAsync<UserDeviceGAgent>(userId.ToString());
            var agent = actor.As<IUserDeviceGAgent>();
            
            var isNewDevice = await agent.RegisterOrUpdateDeviceAsync(
                input.DeviceId,
                input.PushToken,
                input.TimeZoneId,
                input.PushEnabled,
                input.Platform ?? string.Empty,
                input.AppVersion ?? string.Empty,
                string.Empty);
            
            return new RegisterDeviceResult
            {
                Success = true,
                IsNewDevice = isNewDevice
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserDeviceService][RegisterOrUpdateDevice] Failed for UserId: {UserId}", userId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<DeviceInfoDto?> GetDeviceAsync(Guid userId)
    {
        try
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<UserDeviceGAgent>(userId.ToString());
            var agent = actor.As<IUserDeviceGAgent>();
            var deviceInfo = await agent.GetDeviceAsync();
            
            if (deviceInfo == null)
            {
                return null;
            }
            
            return _objectMapper.Map<DeviceInfo, DeviceInfoDto>(deviceInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserDeviceService][GetDevice] Failed for UserId: {UserId}", userId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task ClearDeviceAsync(Guid userId)
    {
        try
        {
            _logger.LogInformation("[UserDeviceService][ClearDevice] UserId: {UserId}", userId);
            
            var actor = await _actorFactory.CreateGAgentActorAsync<UserDeviceGAgent>(userId.ToString());
            var agent = actor.As<IUserDeviceGAgent>();
            await agent.ClearDeviceAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UserDeviceService][ClearDevice] Failed for UserId: {UserId}", userId);
            throw;
        }
    }
}
