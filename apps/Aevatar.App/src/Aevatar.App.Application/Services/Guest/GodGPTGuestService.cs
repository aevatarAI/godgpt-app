using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Anonymous;
using Aevatar.Application.Grains.Agents.Anonymous;
using Aevatar.Application.Grains.Common;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.App.Application.Contracts.Services.Guest;

namespace Aevatar.App.Application.Services.Guest;

/// <summary>
/// Service implementation for managing anonymous/guest user sessions and chat functionality.
/// Handles guest session creation, chat execution, and limit checking through AnonymousUserGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTGuestService : ApplicationService, IGodGPTGuestService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<GodGPTGuestService> _logger;

    public GodGPTGuestService(
        IGAgentActorFactory actorFactory,
        ILogger<GodGPTGuestService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<CreateGuestSessionResponseDto> CreateGuestSessionAsync(string clientIp, string? guider = null)
    {
        var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
        var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
        var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
        
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
        var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
        var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
        var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
        await anonymousUserGrain.GuestChatAsync(content, chatId);
    }

    /// <inheritdoc />
    public async Task<GuestChatLimitsResponseDto> GetGuestChatLimitsAsync(string clientIp)
    { 
        var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
        var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
        var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
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
        var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
        var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
        var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
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
            var agentId = CommonHelper.GetAnonymousUserGAgentId("127.0.0.1");
            var configActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
            var configGrain = configActor.As<IAnonymousUserGAgent>();
            return await configGrain.GetMaxChatCountAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get max chat count from configuration, using default: 3");
            return 3;
        }
    }
}
