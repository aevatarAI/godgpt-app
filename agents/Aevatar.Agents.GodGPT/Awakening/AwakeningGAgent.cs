using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Application.Grains.Common;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.Awakening.Options;
using GodGPT.GAgents.Awakening.Helpers;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

// Protobuf types aliases
using AwakeningStateProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningStateProto;
using AwakeningStatusProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningStatusProto;
using AwakeningContentDtoProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningContentDtoProto;
using GenerateAwakeningEvent = Aevatar.Agents.GodGPT.Protos.Awakening.GenerateAwakeningEvent;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// Awakening system grain implementation - Main entry point
/// </summary>
public partial class AwakeningGAgent : GAgentBase<AwakeningStateProto>, IAwakeningGAgent
{
    private const string NovaChimeGuider = "Nova·Chime";
    
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IOptionsMonitor<AwakeningOptions> _options;
    private readonly ILogger<AwakeningGAgent> _logger;

    public AwakeningGAgent(
        IGAgentActorFactory actorFactory,
        IOptionsMonitor<AwakeningOptions> options,
        ILogger<AwakeningGAgent> logger)
    {
        _actorFactory = actorFactory;
        _options = options;
        _logger = logger;
    }
    
    #region Enum Converters
    
    /// <summary>
    /// Convert C# enum to Proto enum
    /// </summary>
    private static AwakeningStatusProto ToProto(AwakeningStatus status) => status switch
    {
        AwakeningStatus.NotStarted => AwakeningStatusProto.AwakeningStatusNotStarted,
        AwakeningStatus.Generating => AwakeningStatusProto.AwakeningStatusGenerating,
        AwakeningStatus.Completed => AwakeningStatusProto.AwakeningStatusCompleted,
        _ => AwakeningStatusProto.AwakeningStatusUnspecified
    };
    
    /// <summary>
    /// Convert Proto enum to C# enum
    /// </summary>
    private static AwakeningStatus FromProto(AwakeningStatusProto status) => status switch
    {
        AwakeningStatusProto.AwakeningStatusNotStarted => AwakeningStatus.NotStarted,
        AwakeningStatusProto.AwakeningStatusGenerating => AwakeningStatus.Generating,
        AwakeningStatusProto.AwakeningStatusCompleted => AwakeningStatus.Completed,
        _ => AwakeningStatus.NotStarted
    };
    
    #endregion

    #region Public Interface Methods

    /// <summary>
    /// Get agent description
    /// </summary>
    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Personalized Awakening System GAgent");
    }

    /// <summary>
    /// Generate awakening content based on session contents
    /// </summary>
    public async Task<AwakeningResultDto> GenerateAwakeningContentAsync(
        List<SessionContentDto> sessionContents,
        VoiceLanguageEnum language, 
        string? region = "")
    {
        try
        {
            if (sessionContents.IsNullOrEmpty())
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "Session content is null"
                };
            }

            var prompt = AwakeningPromptHelper.BuildPrompt(sessionContents, language, _options.CurrentValue);
            var result = await CallLLMWithRetry(prompt, language, region);
            
            if (result.IsSuccess)
            {
                // Save successful generation
                RaiseEvent(new GenerateAwakeningEvent
                {
                    Timestamp = result.Timestamp,
                    AwakeningLevel = result.AwakeningLevel,
                    AwakeningMessage = result.AwakeningMessage,
                    Language = (int)language,
                    SessionId = sessionContents.First().SessionId.ToString(),
                    IsSuccess = true,
                    AttemptCount = State.GenerationAttempts + 1
                });

                await ConfirmEventsAsync();
            }
            else
            {
                // Log failure (only in memory log, not persisted event)
                _logger.LogWarning("Awakening generation failed for user {UserId}: {ErrorMessage}", 
                    Id, result.ErrorMessage);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate awakening content");
            return new AwakeningResultDto
            {
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
        }
    }

    /// <summary>
    /// Get today's awakening content, trigger async generation if not exists
    /// Returns Protobuf type for RPC compatibility
    /// </summary>
    public async Task<AwakeningContentDtoProto> GetTodayAwakeningAsync(VoiceLanguageEnum language, string? region)
    {
        try
        {
            // Check if already generated today
            if (IsToday(State.LastGeneratedTimestamp))
            {
                return BuildAwakeningContentProto();
            }

            // Try to lock today's generation
            var lockSuccessful = await TryLockTodayGenerationAsync(language);
            if (!lockSuccessful)
            {
                return BuildAwakeningContentProto();
            }

            // Get latest session content
            var sessionContentList = await GetLatestNonEmptySessionAsync();
            if (sessionContentList == null || sessionContentList.Count == 0)
            {
                // No session content, generate empty result immediately and return completed
                RaiseEvent(new GenerateAwakeningEvent
                {
                    Timestamp = GetTodayTimestamp(),
                    AwakeningLevel = 0,
                    AwakeningMessage = string.Empty,
                    Language = (int)language,
                    SessionId = string.Empty,
                    IsSuccess = true,
                    AttemptCount = 1
                });

                await SetStatusAsync(AwakeningStatus.Completed);
                await ConfirmEventsAsync();
                
                return new AwakeningContentDtoProto
                {
                    AwakeningLevel = 0,
                    AwakeningMessage = string.Empty,
                    Status = AwakeningStatusProto.AwakeningStatusCompleted
                };
            }

            // Has session content, start asynchronous generation using the first (priority) session
            _ = Task.Run(async () =>
            {
                try
                {
                    var result = await GenerateAwakeningContentAsync(sessionContentList, language, region);
                    await CompleteGenerationAsync(result.IsSuccess);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Background awakening generation failed");
                    await CompleteGenerationAsync(false);
                }
            });

            // Return with generating status - client should poll for completion
            return new AwakeningContentDtoProto
            {
                AwakeningLevel = 0,
                AwakeningMessage = string.Empty,
                Status = AwakeningStatusProto.AwakeningStatusGenerating
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get today awakening");
            // Return empty proto instead of null for RPC compatibility
            return new AwakeningContentDtoProto
            {
                AwakeningLevel = 0,
                AwakeningMessage = string.Empty,
                Status = AwakeningStatusProto.AwakeningStatusNotStarted
            };
        }
    }

    #endregion
}
