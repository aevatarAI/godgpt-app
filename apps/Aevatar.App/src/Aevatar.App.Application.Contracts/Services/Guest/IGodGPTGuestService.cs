using System.Threading.Tasks;
using Aevatar.Anonymous;

namespace Aevatar.App.Application.Contracts.Services.Guest;

/// <summary>
/// Service interface for managing anonymous/guest user sessions and chat functionality.
/// All operations are based on client IP identification.
/// </summary>
public interface IGodGPTGuestService
{
    /// <summary>
    /// Creates a guest session for an anonymous user.
    /// </summary>
    /// <param name="clientIp">The client IP address</param>
    /// <param name="guider">Optional guider parameter</param>
    /// <returns>Guest session response with remaining chat limits</returns>
    Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string clientIp, string? guider = null);

    /// <summary>
    /// Executes a guest chat message.
    /// </summary>
    /// <param name="clientIp">The client IP address</param>
    /// <param name="content">The chat message content</param>
    /// <param name="chatId">The chat identifier</param>
    Task GuestChatAsync(string clientIp, string content, string chatId);

    /// <summary>
    /// Gets the chat limits for an anonymous user.
    /// </summary>
    /// <param name="clientIp">The client IP address</param>
    /// <returns>Guest chat limits information</returns>
    Task<GuestChatLimitsResponseDto> GetGuestChatLimitsAsync(string clientIp);

    /// <summary>
    /// Checks if an anonymous user can still chat.
    /// </summary>
    /// <param name="clientIp">The client IP address</param>
    /// <returns>True if the user can chat, false otherwise</returns>
    Task<bool> CanGuestChatAsync(string clientIp);
}
