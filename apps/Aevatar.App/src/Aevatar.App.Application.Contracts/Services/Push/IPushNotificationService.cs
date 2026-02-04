using System;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Dtos.Push;

namespace Aevatar.App.Application.Contracts.Services.Push;

/// <summary>
/// Service for sending push notifications.
/// </summary>
public interface IPushNotificationService
{
    /// <summary>
    /// Send push notifications to all users in a specific timezone.
    /// </summary>
    Task<PushResult> SendByTimezoneAsync(SendPushByTimezoneInput input, CancellationToken ct = default);
    
    /// <summary>
    /// Send push notification to a specific user.
    /// </summary>
    Task<bool> SendToUserAsync(Guid userId, string title, string body, CancellationToken ct = default);
}
