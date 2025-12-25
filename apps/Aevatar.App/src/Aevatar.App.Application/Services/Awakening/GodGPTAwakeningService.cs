using System;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.Protos.Awakening;
using Aevatar.GodGPT.Dtos;
using GodGPT.GAgents.Awakening;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.App.Application.Contracts.Services.Awakening;

namespace Aevatar.App.Application.Services.Awakening;

/// <summary>
/// Service implementation for managing user awakening content.
/// Handles daily awakening message retrieval and state management through the AwakeningGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTAwakeningService : ApplicationService, IGodGPTAwakeningService
{
    private readonly IGAgentActorFactory _actorFactory;
    private readonly ILogger<GodGPTAwakeningService> _logger;

    public GodGPTAwakeningService(
        IGAgentActorFactory actorFactory,
        ILogger<GodGPTAwakeningService> logger)
    {
        _actorFactory = actorFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AwakeningContentDto?> GetTodayAwakeningAsync(Guid currentUserId, VoiceLanguageEnum language, string? region)
    {
        _logger.LogInformation("[GodGPTAwakeningService][GetTodayAwakeningAsync] Starting for userId: {UserId}, language: {Language}, region: {Region}",
            currentUserId, language, region);
        
        try
        {
            var awakeningActor = await _actorFactory.CreateGAgentActorAsync<AwakeningGAgent>(currentUserId.ToString());
            var awakeningAgent = awakeningActor.As<IAwakeningGAgent>();
            var result = await awakeningAgent.GetTodayAwakeningAsync(language, region);
            
            _logger.LogInformation("[GodGPTAwakeningService][GetTodayAwakeningAsync] Completed for userId: {UserId}, Status: {Status}",
                currentUserId, result?.Status);
            
            // Convert Protobuf result to DTO
            return new AwakeningContentDto
            {
                AwakeningMessage = result?.AwakeningMessage ?? "",
                AwakeningLevel = result?.AwakeningLevel ?? 0,
                Status = ConvertProtoStatusToInt(result?.Status ?? AwakeningStatusProto.AwakeningStatusNotStarted)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTAwakeningService][GetTodayAwakeningAsync] Error for userId: {UserId}, language: {Language}, region: {Region}",
                currentUserId, language, region);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ResetAwakeningStateForTestingAsync(Guid userId)
    {
        _logger.LogInformation("[GodGPTAwakeningService][ResetAwakeningStateForTestingAsync] Starting for userId: {UserId}", userId);
        
        try
        {
            var awakeningActor = await _actorFactory.CreateGAgentActorAsync<AwakeningGAgent>(userId.ToString());
            var awakeningAgent = awakeningActor.As<IAwakeningGAgent>();
            bool resetSuccess = await awakeningAgent.ResetAwakeningStateForTestingAsync();
            
            _logger.LogInformation("[GodGPTAwakeningService][ResetAwakeningStateForTestingAsync] Completed for userId: {UserId}, success: {Success}",
                userId, resetSuccess);
            
            return resetSuccess;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GodGPTAwakeningService][ResetAwakeningStateForTestingAsync] Error resetting awakening state for userId: {UserId}", userId);
            return false;
        }
    }

    /// <summary>
    /// Convert Protobuf AwakeningStatusProto to int for DTO
    /// </summary>
    private static int ConvertProtoStatusToInt(AwakeningStatusProto status)
    {
        return status switch
        {
            AwakeningStatusProto.AwakeningStatusNotStarted => 0,
            AwakeningStatusProto.AwakeningStatusGenerating => 1,
            AwakeningStatusProto.AwakeningStatusCompleted => 2,
            _ => 0
        };
    }
}
