using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aevatar.Agents.Core;
using Aevatar.Application.Grains.Common;
using Aevatar.GAgents.AI.Common;
using Aevatar.GAgents.AI.Options;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using GodGPT.GAgents.SpeechChat;
using GodGPT.GAgents.Awakening.Dtos;
using GodGPT.GAgents.Awakening.Options;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent;
using Aevatar.Application.Grains.Agents.ChatManager.ProxyAgent.Dtos;
using Aevatar.GAgents.AIGAgent.Dtos;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Orleans;

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
/// Awakening system grain implementation
/// </summary>
public class AwakeningGAgent : GAgentBase<AwakeningStateProto>, IAwakeningGAgent
{
    private const string NovaChimeGuider = "Nova·Chime";
    
    private readonly IClusterClient _clusterClient;
    private readonly IOptionsMonitor<AwakeningOptions> _options;
    private readonly ILogger<AwakeningGAgent> _logger;

    public AwakeningGAgent(
        IClusterClient clusterClient,
        IOptionsMonitor<AwakeningOptions> options,
        ILogger<AwakeningGAgent> logger)
    {
        _clusterClient = clusterClient;
        _options = options;
        _logger = logger;
    }
    
    // === Helper: Convert between C# enum and Proto enum ===
    private static AwakeningStatusProto ToProto(AwakeningStatus status) => status switch
    {
        AwakeningStatus.NotStarted => AwakeningStatusProto.AwakeningStatusNotStarted,
        AwakeningStatus.Generating => AwakeningStatusProto.AwakeningStatusGenerating,
        AwakeningStatus.Completed => AwakeningStatusProto.AwakeningStatusCompleted,
        _ => AwakeningStatusProto.AwakeningStatusUnspecified
    };
    
    private static AwakeningStatus FromProto(AwakeningStatusProto status) => status switch
    {
        AwakeningStatusProto.AwakeningStatusNotStarted => AwakeningStatus.NotStarted,
        AwakeningStatusProto.AwakeningStatusGenerating => AwakeningStatus.Generating,
        AwakeningStatusProto.AwakeningStatusCompleted => AwakeningStatus.Completed,
        _ => AwakeningStatus.NotStarted
    };

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Personalized Awakening System GAgent");
    }

    public async Task<List<SessionContentDto>> GetLatestNonEmptySessionAsync()
    {
        try
        {
            // Get current user ID through Grain's Primary Key
            var userId = Id;
            
            // Get ChatManagerGAgent for this user
            var chatManager = _clusterClient.GetGrain<IChatManagerGAgent>(userId);
            var sessionList = await chatManager.GetSessionListAsync();
            
            if (sessionList == null || sessionList.Count == 0)
            {
                return new List<SessionContentDto>();
            }
            
            SessionContentDto? firstNonEmptySession = null;
            SessionContentDto? novaChimeSession = null;
            
            // Single pass: Find both sessions in one traversal with optimizations
            for (int i = sessionList.Count - 1; i >= 0; i--)
            {
                var session = sessionList[i];
                
                // Optimization: Skip sessions with empty titles as they likely have no messages
                if (string.IsNullOrEmpty(session.Title))
                {
                    continue;
                }
                
                var messages = await chatManager.GetSessionMessageListAsync(session.SessionId);
                
                if (messages != null && messages.Count > 0)
                {
                    var sessionContent = new SessionContentDto
                    {
                        SessionId = session.SessionId,
                        Title = session.Title ?? string.Empty,
                        Messages = messages,
                        LastActivityTime = session.CreateAt,
                        ExtractedContent = ExtractCoreContent(messages)
                    };
                    
                    // Check if this is the first non-empty session we found
                    if (firstNonEmptySession == null)
                    {
                        firstNonEmptySession = sessionContent;
                    }
                    
                    // Check if this is a Nova·Chime session
                    if (session.Guider == NovaChimeGuider && novaChimeSession == null)
                    {
                        novaChimeSession = sessionContent;
                    }
                    
                    // Early exit: If we've found both types, no need to continue
                    if (firstNonEmptySession != null && novaChimeSession != null)
                    {
                        break;
                    }
                }
            }
            
            // Return logic based on findings
            var result = new List<SessionContentDto>();
            
            if (firstNonEmptySession != null && novaChimeSession != null)
            {
                // If both found and they're the same session (Nova·Chime is the first non-empty)
                if (firstNonEmptySession.SessionId == novaChimeSession.SessionId)
                {
                    result.Add(firstNonEmptySession);
                }
                else
                {
                    // Different sessions: add regular first, Nova·Chime second
                    result.Add(firstNonEmptySession);
                    result.Add(novaChimeSession);
                }
            }
            else if (firstNonEmptySession != null)
            {
                // Only found regular non-empty session
                result.Add(firstNonEmptySession);
            }
            else if (novaChimeSession != null)
            {
                // Only found Nova·Chime session
                result.Add(novaChimeSession);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get latest non-empty sessions for user {UserId}", Id);
            return new List<SessionContentDto>();
        }
    }

    public async Task<AwakeningResultDto> GenerateAwakeningContentAsync(List<SessionContentDto> sessionContents,
        VoiceLanguageEnum language, string? region = "")
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

            var prompt = AwakeningPromptHelper.BuildPrompt(sessionContents, language);
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

    public async Task<AwakeningContentDto?> GetTodayAwakeningAsync(VoiceLanguageEnum language, string? region)
    {
        try
        {
            // Check if already generated today
            if (IsToday(State.LastGeneratedTimestamp))
            {
                return BuildAwakeningContentDto();
            }

            // Try to lock today's generation
            var lockSuccessful = await TryLockTodayGenerationAsync(language);
            if (!lockSuccessful)
            {
                return BuildAwakeningContentDto();
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
                
                return new AwakeningContentDto
                {
                    AwakeningLevel = 0,
                    AwakeningMessage = string.Empty,
                    Status = AwakeningStatus.Completed
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

            // Return null with generating status - client should poll for completion
            return new AwakeningContentDto
            {
                AwakeningLevel = 0,
                AwakeningMessage = string.Empty,
                Status = AwakeningStatus.Generating
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get today awakening");
            return null;
        }
    }

    #region Private Helper Methods

    private string ExtractCoreContent(List<ChatMessage> messages)
    {
        // Extract user messages and assistant replies' key content
        var userMessages = messages
            .Where(m => m.ChatRole == ChatRole.User)
            .Select(m => m.Content)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToList();
            
        var summary = string.Join(" | ", userMessages.Take(3));
        if (summary.Length > 500)
        {
            summary = summary.Substring(0, 500) + "...";
        }
        
        return summary;
    }

    private async Task<AwakeningResultDto> CallLLMWithRetry(string prompt, VoiceLanguageEnum language, string? region)
    {
        var maxAttempts = _options.CurrentValue.MaxRetryAttempts;
        var timeout = TimeSpan.FromSeconds(_options.CurrentValue.TimeoutSeconds);
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var cts = new CancellationTokenSource(timeout);
                
                // Get current user ID
                var userId = Id;
                
                // Get IGodChat instance for current user
                var godChat = _clusterClient.GetGrain<IGodChat>(userId);
                var chatId = Guid.NewGuid().ToString();
                //var sessionId = Guid.NewGuid(); // Create a new session for awakening generation
                
                var settings = new ExecutionPromptSettings
                {
                    Temperature = _options.CurrentValue.Temperature.ToString()
                };
                
                // Call IGodChat.ChatWithHistory with our prompt
                var response = await godChat.ChatWithoutHistoryAsync(userId, string.Empty, prompt, chatId, settings, true, region);
                
                string responseContent;
                if (response.IsNullOrEmpty())
                {
                    responseContent = string.Empty;
                }
                else
                {
                    responseContent = response.FirstOrDefault()?.Content ?? string.Empty;
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

    private AwakeningResultDto ParseAwakeningFromText(string responseContent, VoiceLanguageEnum language)
    {
        try
        {
            // Extract level (look for numbers 1-10)
            var levelMatch = Regex.Match(responseContent, @"\b([1-9]|10)\b");
            if (!levelMatch.Success)
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening level found in LLM response"
                };
            }
            
            var level = int.Parse(levelMatch.Value);

            // Extract message (take the longest meaningful sentence)
            var sentences = responseContent.Split(new[] { '.', '!', '?', '。', '！', '？' }, StringSplitOptions.RemoveEmptyEntries);
            var message = sentences
                .Where(s => !string.IsNullOrWhiteSpace(s) && s.Length > 10)
                .OrderByDescending(s => s.Length)
                .FirstOrDefault()?.Trim();

            // If no valid message found, return failure (following "empty is empty" principle)
            if (string.IsNullOrWhiteSpace(message))
            {
                return new AwakeningResultDto
                {
                    IsSuccess = false,
                    ErrorMessage = "No valid awakening message found in LLM response"
                };
            }

            return new AwakeningResultDto
            {
                IsSuccess = true,
                AwakeningLevel = level,
                AwakeningMessage = message,
                Timestamp = GetTodayTimestamp()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse awakening response from text");
            return new AwakeningResultDto
            {
                IsSuccess = false,
                ErrorMessage = "Failed to parse awakening content from LLM response"
            };
        }
    }

    private bool IsToday(long timestamp)
    {
        if (timestamp == 0) return false;
        
        var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;
        var today = DateTime.UtcNow.Date;
        
        return dateTime.Date == today;
    }

    private long GetTodayTimestamp()
    {
        return ((DateTimeOffset)DateTime.UtcNow.Date).ToUnixTimeSeconds();
    }

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

    private async Task SetStatusAsync(AwakeningStatus status)
    {
        RaiseEvent(new UpdateAwakeningStatusEvent
        {
            Status = ToProto(status)
        });
        
        await ConfirmEventsAsync();
    }

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

    private async Task CompleteGenerationAsync(bool isSuccess)
    {
        // Always set status to completed regardless of success/failure
        await SetStatusAsync(AwakeningStatus.Completed);
        
        if (!isSuccess)
        {
            _logger.LogWarning("Awakening generation completed with failure for user {UserId}", Id);
        }
    }

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
