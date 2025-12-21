using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Anonymous;
using Aevatar.Application.Grains.Agents.Anonymous;
using Aevatar.Application.Grains.Common;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.Application.Grains.Agents.ChatManager.Common;

namespace Aevatar.App.Application.Services.Guest;

/// <summary>
/// Service implementation for managing anonymous/guest user sessions and chat functionality.
/// Handles guest session creation, chat execution, and limit checking through AnonymousUserGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTGuestService : ApplicationService, IGodGPTGuestService
{
    private readonly IGAgentFactory _agentFactory;
    private readonly ILogger<GodGPTGuestService> _logger;

    public GodGPTGuestService(
        IGAgentFactory agentFactory,
        ILogger<GodGPTGuestService> logger)
    {
        _agentFactory = agentFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string clientIp, string? guider = null)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        
        // Check if user can still chat
        if (!await anonymousUserGrain.CanChatAsync())
        {
            var remainingChats = await anonymousUserGrain.GetRemainingChatsAsync();
            return new CreateGuestSessionResponseDto
            {
                RemainingChats = remainingChats,
                TotalAllowed = await GetMaxChatCountAsync()
            };
        }

        // Create new session (this will replace any existing session for the IP)
        await anonymousUserGrain.CreateGuestSessionAsync(guider);
        
        var remaining = await anonymousUserGrain.GetRemainingChatsAsync();
        return new CreateGuestSessionResponseDto
        {
            RemainingChats = remaining,
            TotalAllowed = await GetMaxChatCountAsync()
        };
    }

    /// <inheritdoc />
    public async Task GuestChatAsync(string clientIp, string content, string chatId)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        await anonymousUserGrain.GuestChatAsync(content, chatId);
    }

    /// <inheritdoc />
    public async Task<GuestChatLimitsResponseDto> GetGuestChatLimitsAsync(string clientIp)
    { 
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        var remaining = await anonymousUserGrain.GetRemainingChatsAsync();
        
        return new GuestChatLimitsResponseDto
        {
            RemainingChats = remaining,
            TotalAllowed = await GetMaxChatCountAsync()
        };
    }

    /// <inheritdoc />
    public async Task<bool> CanGuestChatAsync(string clientIp)
    {
        var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId(clientIp));
        var anonymousUserGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
        return await anonymousUserGrain.CanChatAsync();
    }

    /// <summary>
    /// Gets the maximum chat count from AnonymousUserGAgent configuration.
    /// </summary>
    /// <returns>Maximum chat count, defaults to 3 if unable to retrieve</returns>
    private async Task<int> GetMaxChatCountAsync()
    {
        try
        {
            // Use a dummy IP to get configuration from AnonymousUserGAgent
            var grainId = CommonHelper.StringToGuid(CommonHelper.GetAnonymousUserGAgentId("127.0.0.1"));
            var configGrain = _agentFactory.CreateGAgent<AnonymousUserGAgent>(grainId);
            return await configGrain.GetMaxChatCountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get max chat count from configuration, using default: 3");
            return 3;
        }
    }
}
