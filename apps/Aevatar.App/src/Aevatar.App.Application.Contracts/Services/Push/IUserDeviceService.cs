using System;
using System.Threading.Tasks;
using Aevatar.App.Domain.Shared;
using Aevatar.Dtos.Push;

namespace Aevatar.App.Application.Contracts.Services.Push;

/// <summary>
/// Service for managing user devices for push notifications.
/// </summary>
public interface IUserDeviceService
{
    /// <summary>
    /// Register or update device information.
    /// Called on app launch/login.
    /// </summary>
    Task<RegisterDeviceResult> RegisterOrUpdateDeviceAsync(Guid userId, GodGPTChatLanguage language, RegisterDeviceInput input);
    
    /// <summary>
    /// Get device information for a user.
    /// </summary>
    Task<DeviceInfoDto?> GetDeviceAsync(Guid userId);
    
    /// <summary>
    /// Clear device information.
    /// Called on user logout.
    /// </summary>
    Task ClearDeviceAsync(Guid userId);
}
