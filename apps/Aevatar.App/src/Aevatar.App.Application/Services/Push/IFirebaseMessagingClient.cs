using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aevatar.App.Application.Services.Push;

/// <summary>
/// Firebase Cloud Messaging client interface.
/// </summary>
public interface IFirebaseMessagingClient
{
    /// <summary>
    /// Send push notification to a single device.
    /// </summary>
    Task<SendResult> SendAsync(string token, PushMessage message, CancellationToken ct = default);
    
    /// <summary>
    /// Send push notifications to multiple devices.
    /// </summary>
    Task<BatchSendResult> SendBatchAsync(IEnumerable<string> tokens, PushMessage message, CancellationToken ct = default);
}

/// <summary>
/// Push notification message.
/// </summary>
public class PushMessage
{
    /// <summary>
    /// Notification title.
    /// </summary>
    public string Title { get; set; } = string.Empty;
    
    /// <summary>
    /// Notification body.
    /// </summary>
    public string Body { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional custom data payload.
    /// </summary>
    public Dictionary<string, string>? Data { get; set; }
}

/// <summary>
/// Result of sending a single push notification.
/// </summary>
public class SendResult
{
    /// <summary>
    /// Whether the send was successful.
    /// </summary>
    public bool Success { get; set; }
    
    /// <summary>
    /// Message ID if successful.
    /// </summary>
    public string? MessageId { get; set; }
    
    /// <summary>
    /// Error message if failed.
    /// </summary>
    public string? Error { get; set; }
    
    /// <summary>
    /// Whether the token is invalid and should be removed.
    /// </summary>
    public bool TokenInvalid { get; set; }
}

/// <summary>
/// Result of sending batch push notifications.
/// </summary>
public class BatchSendResult
{
    /// <summary>
    /// Number of successfully sent notifications.
    /// </summary>
    public int SuccessCount { get; set; }
    
    /// <summary>
    /// Number of failed notifications.
    /// </summary>
    public int FailureCount { get; set; }
    
    /// <summary>
    /// List of tokens that failed with their error messages.
    /// </summary>
    public List<(string Token, string? Error, bool TokenInvalid)> FailedTokens { get; set; } = new();
}
