using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.GodChat;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.Awakening.Helpers;
using GodGPT.GAgents.SpeechChat;
using Microsoft.Extensions.Logging;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.GodGPT.AIAgentStatusProxy.Protos;
using Aevatar.GAgents.AIGAgent.Dtos;
using Google.Protobuf.WellKnownTypes;

// Protobuf types aliases
using AwakeningStatusProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningStatusProto;
using LockGenerationTimestampEvent = Aevatar.Agents.GodGPT.Protos.Awakening.LockGenerationTimestampEvent;
using UpdateAwakeningStatusEvent = Aevatar.Agents.GodGPT.Protos.Awakening.UpdateAwakeningStatusEvent;
using ResetAwakeningContentEvent = Aevatar.Agents.GodGPT.Protos.Awakening.ResetAwakeningContentEvent;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// Awakening GAgent - Generation and LLM calling methods
/// </summary>
public partial class AwakeningGAgent
{
    #region Generation Methods

    /// <summary>
    /// Call LLM with retry logic
    /// </summary>
    private async Task<AwakeningResultDto> CallLLMWithRetry(string prompt, VoiceLanguageEnum language, string? region)
    {
        var maxAttempts = _options.CurrentValue.MaxRetryAttempts;
        var timeout = TimeSpan.FromSeconds(_options.CurrentValue.TimeoutSeconds);
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                
                // Get current user ID - use ExtractRawId to handle AgentType/Guid format
                var rawId = AgentId.ExtractRawId(Id);
                var userId = Guid.Parse(rawId);
                
                // Get IGodChat instance
                var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(rawId);
                var godChat = godChatActor.As<IGodChat>();
                var chatId = Guid.NewGuid().ToString();
                
                // Build Protobuf input with custom Temperature from configuration
                var protoInput = new ChatWithHistoryInputProto 
                { 
                    Prompt = prompt 
                };
                
                // Set prompt settings using Protobuf type with custom Temperature
                protoInput.PromptSettings = new ExecutionPromptSettingsProto
                {
                    Temperature = _options.CurrentValue.Temperature.ToString()
                };
                
                // Create context for the request
                protoInput.Context = new AIChatContextProto
                {
                    RequestId = userId.ToString(),
                    SessionId = userId.ToString(),
                    UserId = userId.ToString(),
                    ChatId = chatId
                };
                
                // Call Protobuf method directly to preserve Temperature configuration
                var response = await godChat.ChatWithoutHistoryProtoAsync(protoInput, true, region);
                
                string responseContent;
                if (response == null || response.Messages.Count == 0)
                {
                    responseContent = string.Empty;
                }
                else
                {
                    responseContent = response.Messages.FirstOrDefault()?.Content ?? string.Empty;
                }
                
                if (!string.IsNullOrWhiteSpace(responseContent))
                {
                    var result = AwakeningParserHelper.ParseAwakeningResponse(responseContent, language, GetTodayTimestamp(), _logger);
                    if (result.IsSuccess)
                    {
                        return result;
                    }
                }
                
                _logger.LogWarning("Attempt {Attempt} failed: No valid response from LLM", attempt);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception on attempt {Attempt}", attempt);
            }
            
            if (attempt < maxAttempts)
            {
                await Task.Delay(1000 * attempt); // Incremental delay
            }
        }
        
        // All retries failed, return failure result
        return new AwakeningResultDto 
        { 
            IsSuccess = false, 
            ErrorMessage = "Failed to generate awakening content after all retries" 
        };
    }

    /// <summary>
    /// Try to lock today's generation slot
    /// </summary>
    private async Task<bool> TryLockTodayGenerationAsync(VoiceLanguageEnum language)
    {
        var todayTimestamp = GetTodayTimestamp();
        
        // If it's already today's timestamp, it means it's locked or generated
        if (IsToday(State.LastGeneratedTimestamp))
        {
            return false; // Already locked, no need to regenerate
        }
        
        // Atomically update timestamp to today and reset content
        RaiseEvent(new LockGenerationTimestampEvent 
        { 
            Timestamp = todayTimestamp 
        });
        
        // Reset awakening content for new day
        RaiseEvent(new ResetAwakeningContentEvent
        {
            Timestamp = todayTimestamp,
            Language = (int)language
        });
        
        // Set status to generating
        RaiseEvent(new UpdateAwakeningStatusEvent
        {
            Status = AwakeningStatusProto.AwakeningStatusGenerating
        });
        
        await ConfirmEventsAsync();
        return true; // Successfully locked and initialized
    }

    /// <summary>
    /// Set awakening status
    /// </summary>
    private async Task SetStatusAsync(AwakeningStatus status)
    {
        RaiseEvent(new UpdateAwakeningStatusEvent
        {
            Status = ToProto(status)
        });
        
        await ConfirmEventsAsync();
    }

    /// <summary>
    /// Complete generation and update status
    /// </summary>
    private async Task CompleteGenerationAsync(bool isSuccess)
    {
        // Always set status to completed regardless of success/failure
        await SetStatusAsync(AwakeningStatus.Completed);
        
        if (!isSuccess)
        {
            _logger.LogWarning("Awakening generation completed with failure for user {UserId}", Id);
        }
    }

    #endregion
}

