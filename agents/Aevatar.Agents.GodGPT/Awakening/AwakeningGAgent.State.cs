using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.SpeechChat;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;

// Protobuf types aliases
using AwakeningStateProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningStateProto;
using AwakeningStatusProto = Aevatar.Agents.GodGPT.Protos.Awakening.AwakeningStatusProto;
using GenerateAwakeningEvent = Aevatar.Agents.GodGPT.Protos.Awakening.GenerateAwakeningEvent;
using LockGenerationTimestampEvent = Aevatar.Agents.GodGPT.Protos.Awakening.LockGenerationTimestampEvent;
using UpdateAwakeningStatusEvent = Aevatar.Agents.GodGPT.Protos.Awakening.UpdateAwakeningStatusEvent;
using ResetAwakeningContentEvent = Aevatar.Agents.GodGPT.Protos.Awakening.ResetAwakeningContentEvent;
using ResetAwakeningStateForTestingEvent = Aevatar.Agents.GodGPT.Protos.Awakening.ResetAwakeningStateForTestingEvent;
using ResetTodayContentEvent = Aevatar.Agents.GodGPT.Protos.Awakening.ResetTodayContentEvent;

namespace GodGPT.GAgents.Awakening;

/// <summary>
/// Awakening GAgent - State management and event transition methods
/// </summary>
public partial class AwakeningGAgent
{
    #region State Management Methods

    /// <summary>
    /// Check if timestamp is today
    /// </summary>
    private bool IsToday(long timestamp)
    {
        if (timestamp == 0) return false;
        
        var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
        var today = DateTime.UtcNow.Date;
        
        return dateTime.Date == today;
    }

    /// <summary>
    /// Get today's timestamp (start of day in UTC)
    /// </summary>
    private long GetTodayTimestamp()
    {
        return ((DateTimeOffset)DateTime.UtcNow.Date).ToUnixTimeSeconds();
    }

    /// <summary>
    /// Build awakening content DTO from current state
    /// </summary>
    private AwakeningContentDto? BuildAwakeningContentDto()
    {
        // If content hasn't been generated today, return null
        if (!IsToday(State.LastGeneratedTimestamp))
        {
            return null;
        }
        
        // Return current content with status
        return new AwakeningContentDto
        {
            AwakeningLevel = State.AwakeningLevel,
            AwakeningMessage = State.AwakeningMessage,
            Status = FromProto(State.Status)
        };
    }

    /// <summary>
    /// Reset awakening state for testing purposes
    /// </summary>
    public async Task<bool> ResetAwakeningStateForTestingAsync()
    {
        try
        {
            _logger.LogInformation("Resetting awakening state for testing for user {UserId}", Id);
            
            // Trigger reset event
            RaiseEvent(new ResetAwakeningStateForTestingEvent
            {
                ResetAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });
            
            await ConfirmEventsAsync();
            
            _logger.LogInformation("Successfully reset awakening state for testing for user {UserId}", Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset awakening state for testing for user {UserId}", Id);
            return false;
        }
    }

    /// <summary>
    /// Reset today's awakening content
    /// </summary>
    public async Task<bool> ResetTodayContentAsync()
    {
        try
        {
            // Simply reset level and message to empty values
            RaiseEvent(new ResetTodayContentEvent());
            
            await ConfirmEventsAsync();
            
            _logger.LogInformation("Successfully reset awakening content for user {UserId}", Id);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reset content for user {UserId}", Id);
            return false;
        }
    }

    #endregion

    #region Event Transition

    /// <summary>
    /// Handle state transitions for all events
    /// </summary>
    protected override void TransitionState(AwakeningStateProto state, IMessage @event)
    {
        switch (@event)
        {
            case ResetAwakeningContentEvent resetEvent:
                state.LastGeneratedTimestamp = resetEvent.Timestamp;
                state.Language = resetEvent.Language;
                state.AwakeningMessage = string.Empty;
                state.AwakeningLevel = 0;
                state.SessionId = string.Empty;
                state.GenerationAttempts = 0;
                state.Status = AwakeningStatusProto.AwakeningStatusNotStarted;
                break;

            case LockGenerationTimestampEvent lockEvent:
                state.LastGeneratedTimestamp = lockEvent.Timestamp;
                break;

            case UpdateAwakeningStatusEvent statusEvent:
                state.Status = statusEvent.Status;
                break;

            case GenerateAwakeningEvent generateEvent:
                state.LastGeneratedTimestamp = generateEvent.Timestamp;
                state.AwakeningLevel = generateEvent.AwakeningLevel;
                state.AwakeningMessage = generateEvent.AwakeningMessage;
                state.Language = generateEvent.Language;
                state.SessionId = generateEvent.SessionId;
                state.GenerationAttempts = generateEvent.AttemptCount;
                break;

            case ResetAwakeningStateForTestingEvent resetTestingEvent:
                state.LastGeneratedTimestamp = 0;
                state.AwakeningLevel = 0;
                state.AwakeningMessage = string.Empty;
                state.Language = (int)VoiceLanguageEnum.Unset;
                state.SessionId = string.Empty;
                state.GenerationAttempts = 0;
                state.Status = AwakeningStatusProto.AwakeningStatusNotStarted;
                break;
                
            case ResetTodayContentEvent resetTodayEvent:
                // Only reset level and message, preserve all other fields
                state.AwakeningLevel = 0;
                state.AwakeningMessage = string.Empty;
                break;
        }
    }

    #endregion
}

