using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.App.Application.Common;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Constants;
using Aevatar.GodGPT.Dtos;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Runtime;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.GAgents.AI.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager.Dtos;
using Aevatar.App.Domain.Shared;
using Aevatar.Quantum;
using Aevatar.App.Application.Contracts.Services.Share;

namespace Aevatar.App.Application.Services.Share;

/// <summary>
/// Service implementation for managing conversation sharing functionality.
/// Handles share link generation, shared content retrieval, and AI-powered keyword generation.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTShareService : ApplicationService, IGodGPTShareService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<GodGPTShareService> _logger;
    private readonly ILocalizationService _localizationService;

    public GodGPTShareService(
        IGAgentActorFactory actorFactory,
        IClusterClient clusterClient,
        ILogger<GodGPTShareService> logger,
        ILocalizationService localizationService)
    {
        _actorFactory = actorFactory;
        _clusterClient = clusterClient;
        _logger = logger;
        _localizationService = localizationService;
    }

    /// <inheritdoc />
    public async Task<CreateShareIdResponse> GenerateShareContentAsync(Guid currentUserId, CreateShareIdRequest request, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        try
        {
            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(currentUserId);
            var manager = (IChatManagerGAgent)managerActor.GetAgent();
            RequestContext.Set("GodGPTLanguage", language.ToString());
            var shareId = await manager.GenerateChatShareContentAsync(request.SessionId);
            return new CreateShareIdResponse
            {
                ShareId = GuidCompressor.CompressGuids(currentUserId, request.SessionId, shareId)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"[GodGPTShareService][GenerateShareContentAsync] userId:{currentUserId}, sessionId:{request.SessionId}, error: {ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<List<ChatMessage>> GetShareMessageListAsync(string shareString, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        if (shareString.IsNullOrWhiteSpace())
        {
            throw new UserFriendlyException("Invalid Share string");
        }

        Guid userId;
        Guid sessionId;
        Guid shareId;
        try
        {
            (userId, sessionId, shareId) = GuidCompressor.DecompressGuids(shareString);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Invalid Share string. {0}", shareString);
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidShare, language);
            throw new UserFriendlyException(localizedMessage);
        }

        try
        {
            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId);
            var manager = (IChatManagerGAgent)managerActor.GetAgent();
            RequestContext.Set("GodGPTLanguage", language.ToString());
            var shareLinkDto = await manager.GetChatShareContentAsync(sessionId, shareId);
            return shareLinkDto.Messages;
        }
        catch (Exception ex)
        {
            _logger.LogError($"[GodGPTShareService][GetShareMessageListAsync] exception userId:{userId}, shareId:{shareId}, error:{ex.Message}");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<QuantumShareResponseDto> GetShareKeyWordWithAIAsync(Guid sessionId, string? content, string? region, SessionType sessionType, GodGPTChatLanguage language = GodGPTChatLanguage.English)
    {
        _logger.LogDebug($"[GodGPTShareService][GetShareKeyWordWithAIAsync] start: sessionId={sessionId}, sessionType={sessionType}");
        var responseContent = "";
        try
        {
            var godChat = _clusterClient.GetGrain<IGodChat>(sessionId);
            var chatId = Guid.NewGuid().ToString();
            var response = await godChat.ChatWithHistory(sessionId, string.Empty, content,
                chatId, null, true, region);
            responseContent = response.IsNullOrEmpty() ? sessionType.GetDefaultContent(language) : response.FirstOrDefault().Content;
            _logger.LogDebug(
                $"[GodGPTShareService][GetShareKeyWordWithAIAsync] completed for sessionId={sessionId}, responseContent:{responseContent}");
        }
        catch (Exception ex)
        {
            responseContent = sessionType.GetDefaultContent(language);
            _logger.LogError(ex, $"[GodGPTShareService][GetShareKeyWordWithAIAsync] error for sessionId={sessionId}, sessionType={sessionType}");
        }

        return new QuantumShareResponseDto
        {
            Success = true,
            Content = responseContent,
        };
    }
}
