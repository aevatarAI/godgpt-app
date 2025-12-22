using System.Diagnostics;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents.GodGPT.Protos.Anonymous;
using Aevatar.Application.Grains.Agents.Anonymous.Options;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Options;
using Aevatar.Application.Grains.Common.Observability;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Json.Schema.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Application.Grains.Agents.Anonymous;

/// <summary>
/// Anonymous User GAgent - manages IP-based guest chat sessions with limited usage
/// Follows the same patterns as ChatManagerGAgent but simplified for anonymous users
/// </summary>
[Description("Anonymous user chat agent for guest access")]
[GAgent(nameof(AnonymousUserGAgent))]
public class AnonymousUserGAgent : GAgentBase<AnonymousUserState>, IAnonymousUserGAgent
{
    private readonly IGAgentFactory _agentFactory;
    private readonly IGAgentActorFactory _actorFactory;
    
    // Cached ConfigurationGAgent instance (new framework)
    private ConfigurationGAgent? _configurationAgent;

    public AnonymousUserGAgent(Guid id, IGAgentFactory agentFactory, IGAgentActorFactory actorFactory) : base(id)
    {
        _agentFactory = agentFactory;
        _actorFactory = actorFactory;
    }

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Anonymous User GAgent for guest chat sessions");
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);
        Logger.LogDebug("[AnonymousUserGAgent][OnActivateAsync] Activating anonymous user grain");
        await EnsureInitializedAsync();
    }

    public async Task<int> GetChatCountAsync()
    {
        await EnsureInitializedAsync();
        return State.ChatCount;
    }

    public async Task<bool> CanChatAsync()
    {
        await EnsureInitializedAsync();
        var maxCount = GetMaxChatCount();
        return State.ChatCount < maxCount;
    }

    public async Task<int> GetRemainingChatsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        await EnsureInitializedAsync();
        var maxCount = GetMaxChatCount();
        var remainingChats = Math.Max(0, maxCount - State.ChatCount);
        Logger.LogDebug("[AnonymousUserGAgent][GetRemainingChatsAsync] Total duration: {0}ms", stopwatch.ElapsedMilliseconds);
        return remainingChats;
    }

    public Task<int> GetMaxChatCountAsync()
    {
        return Task.FromResult(GetMaxChatCount());
    }

    public async Task<Guid> CreateGuestSessionAsync(string? guider = null)
    {
        var stopwatch = Stopwatch.StartNew();
        await EnsureInitializedAsync();
        
        // Check if user has exceeded chat limit
        if (!await CanChatAsync())
        {
            Logger.LogWarning($"[AnonymousUserGAgent][CreateGuestSessionAsync] Chat limit exceeded for user: {State.UserHashId}");
            throw new InvalidOperationException("Daily chat limit exceeded for guest users");
        }
        
        // Check if existing session can be reused (same guider and not yet used)
        if (!string.IsNullOrEmpty(State.CurrentSessionId) && !State.CurrentSessionUsed)
        {
            var existingGuider = State.CurrentGuider ?? string.Empty;
            var newGuider = guider ?? string.Empty;
            
            if (existingGuider.Equals(newGuider, StringComparison.OrdinalIgnoreCase))
            {
                var existingSessionId = Guid.Parse(State.CurrentSessionId);
                Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Reusing existing session: {existingSessionId} for user: {State.UserHashId}");
                return existingSessionId;
            }
        }

        var configuration = await GetConfigurationAsync();

        // Create new GodChat session (mimic ChatManagerGAgent.CreateSessionAsync)
        var newSessionId = Guid.NewGuid();
        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(newSessionId);
        var godChat = (IGodChat)godChatActor.GetAgent();

        // Get system prompt and append role prompt if provided (exact copy from ChatManagerGAgent)
        var sysMessage = configuration.GetPrompt();
        
        if (!string.IsNullOrEmpty(guider))
        {
            var rolePrompt = GetRolePrompt(guider);
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                sysMessage += $"You should follow the rules below. 1. {rolePrompt}. 2. {sysMessage}";
                Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Added role prompt for guider: {guider}");
            }
        }

        // Configure GodChat with same settings as regular users (exact copy from ChatManagerGAgent)
        var godChatConfig = new GodChatConfig()
        {
            Instructions = sysMessage, 
            MaxHistoryCount = 32,
            LlmSystemLlm = configuration.GetSystemLLM(),
            StreamingModeEnabled = true, 
            StreamingBufferingSize = 32
        };
        
        Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Config: {Newtonsoft.Json.JsonConvert.SerializeObject(godChatConfig)}");

        await godChat.ConfigAsync(godChatConfig);

        // Record session creation event
        RaiseEvent(new CreateGuestSessionEvent()
        {
            SessionId = newSessionId.ToString(),
            Guider = guider ?? string.Empty,
            CreateAt = Timestamp.FromDateTime(DateTime.UtcNow)
        });

        await ConfirmEventsAsync();

        stopwatch.Stop();
        Logger.LogDebug($"[AnonymousUserGAgent][CreateGuestSessionAsync] Session created: {newSessionId} for user: {State.UserHashId} Total duration: {stopwatch.ElapsedMilliseconds}ms");
        return newSessionId;
    }

    public async Task GuestChatAsync(string content, string chatId)
    {
        await EnsureInitializedAsync();
        
        // Get language from RequestContext with error handling
        var language = GodGPTLanguageHelper.GetGodGPTLanguageFromContext();
        Logger.LogDebug($"[AnonymousUserGAgent][GuestChatAsync] Language from context: {language}");
        
        // Check chat limits
        if (!await CanChatAsync())
        {
            Logger.LogWarning($"[AnonymousUserGAgent][GuestChatAsync] Chat limit exceeded for user: {State.UserHashId}");
            throw new InvalidOperationException("Daily chat limit exceeded for guest users");
        }

        // Validate current session
        if (string.IsNullOrEmpty(State.CurrentSessionId))
        {
            Logger.LogWarning($"[AnonymousUserGAgent][GuestChatAsync] No active guest session for user: {State.UserHashId}");
            throw new InvalidOperationException("No active guest session. Please create a session first.");
        }

        var sessionId = Guid.Parse(State.CurrentSessionId);
        var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(sessionId);
        var godChat = (IGodChat)godChatActor.GetAgent();
        var configuration = await GetConfigurationAsync();

        // Execute streaming chat (exact copy from ChatManagerGAgent.StreamChatWithSessionAsync)
        var stopwatch = Stopwatch.StartNew();
        await godChat.GodStreamChatAsync(
            sessionId,
            configuration.GetSystemLLM(), 
            configuration.GetStreamingModeEnabled(),
            content, 
            chatId,
            null, isHttpRequest: true);
        stopwatch.Stop();
        Logger.LogDebug($"[AnonymousUserGAgent][GuestChatAsync] Chat execution: {stopwatch.ElapsedMilliseconds}ms");

        // Record telemetry for this user's daily activity
        await RecordDailyActivityTelemetryAsync(State.ChatCount + 1);

        // Mark session as used and increment chat count
        RaiseEvent(new GuestChatEvent()
        {
            ChatCount = State.ChatCount + 1,
            ChatAt = Timestamp.FromDateTime(DateTime.UtcNow),
            SessionUsed = true // Mark session as used
        });

        await ConfirmEventsAsync();

        Logger.LogDebug($"[AnonymousUserGAgent][GuestChatAsync] Chat completed for user: {State.UserHashId}, new count: {State.ChatCount + 1}");
    }

    public async Task<GuestSessionInfoProto?> GetCurrentSessionAsync()
    {
        await EnsureInitializedAsync();
        
        if (string.IsNullOrEmpty(State.CurrentSessionId))
        {
            return null;
        }

        return new GuestSessionInfoProto
        {
            SessionId = State.CurrentSessionId,
            Guider = State.CurrentGuider,
            CreatedAt = State.CreatedAt ?? Timestamp.FromDateTime(DateTime.MinValue),
            ChatCount = State.ChatCount,
            RemainingChats = await GetRemainingChatsAsync(),
            SessionUsed = State.CurrentSessionUsed
        };
    }

    /// <summary>
    /// Get configuration agent (new framework style)
    /// </summary>
    private async Task<ConfigurationGAgent> GetConfigurationAsync()
    {
        if (_configurationAgent == null)
        {
            var actor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(
                CommonHelper.GetSessionManagerConfigurationId());
            _configurationAgent = (ConfigurationGAgent)actor.GetAgent();
            await _configurationAgent.ActivateAsync();
        }
        return _configurationAgent;
    }

    /// <summary>
    /// Get role-specific prompt from configuration (exact copy from ChatManagerGAgent)
    /// </summary>
    private string GetRolePrompt(string roleName)
    {
        try
        {
            var serviceProvider = _agentFactory as IServiceProvider ?? 
                                  throw new InvalidOperationException("Cannot get ServiceProvider from IGAgentFactory");
            var roleOptions = serviceProvider.GetService<IOptionsMonitor<RolePromptOptions>>()?.CurrentValue;
            var rolePrompt = roleOptions?.RolePrompts.GetValueOrDefault(roleName, string.Empty) ?? string.Empty;
            
            if (!string.IsNullOrEmpty(rolePrompt))
            {
                Logger.LogDebug($"[AnonymousUserGAgent][GetRolePrompt] Found role prompt for: {roleName}");
            }
            else
            {
                Logger.LogDebug($"[AnonymousUserGAgent][GetRolePrompt] No role prompt found for: {roleName}");
            }
            
            return rolePrompt;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[AnonymousUserGAgent][GetRolePrompt] Failed to get role prompt for role: {RoleName}", roleName);
            return string.Empty;
        }
    }

    /// <summary>
    /// Get maximum chat count from configuration
    /// </summary>
    private int GetMaxChatCount()
    {
        try
        {
            var serviceProvider = _agentFactory as IServiceProvider ?? 
                                  throw new InvalidOperationException("Cannot get ServiceProvider from IGAgentFactory");
            var options = serviceProvider.GetService<IOptionsMonitor<AnonymousGodGPTOptions>>()?.CurrentValue;
            return options?.MaxChatCount ?? 3;
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[AnonymousUserGAgent][GetMaxChatCount] Failed to get max chat count, using default: 3");
            return 3;
        }
    }

    /// <summary>
    /// Records daily activity telemetry for anonymous user
    /// This method ensures each user is counted only once per day
    /// </summary>
    /// <param name="chatCount">Chat count at time of report</param>
    private Task RecordDailyActivityTelemetryAsync(int chatCount)
    {
        var reportDate = DateTime.UtcNow.Date;
        var lastChatTime = State.LastChatTime?.ToDateTime() ?? DateTime.MinValue;
        if (lastChatTime != DateTime.MinValue && lastChatTime.Date == reportDate)
        {
            Logger.LogDebug(
                $"[AnonymousUserGAgent][RecordDailyActivityTelemetryAsync] already recorded: {State.UserHashId}, chatCount: {chatCount}");
            return Task.CompletedTask;
        }
        try
        {
            // Record the telemetry metric
            UserLifecycleTelemetryMetrics.RecordAnonymousUserActivity(
                chatCount,
                Logger);

            Logger.LogDebug(
                $"[AnonymousUserGAgent][RecordDailyActivityTelemetryAsync] Daily telemetry reported for user: {State.UserHashId}, chatCount: {chatCount}");
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, 
                "[AnonymousUserGAgent][RecordDailyActivityTelemetryAsync] Failed to record daily activity telemetry for user: {UserHashId}", 
                State.UserHashId);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Ensure the anonymous user record is initialized with hashed user identifier
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (string.IsNullOrEmpty(State.UserHashId))
        {
            var userHashId = Id.ToString("N")[..16]; // Use first 16 chars as hash ID
            
            RaiseEvent(new InitializeAnonymousUserEvent()
            {
                UserHashId = userHashId,
                CreatedAt = Timestamp.FromDateTime(DateTime.UtcNow)
            });

            await ConfirmEventsAsync();
            
            Logger.LogDebug($"[AnonymousUserGAgent][EnsureInitializedAsync] Initialized for user: {userHashId}");
        }
    }

    #region EventHandlers

    [EventHandler]
    public void HandleInitializeAnonymousUserEvent(InitializeAnonymousUserEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleCreateGuestSessionEvent(CreateGuestSessionEvent @event)
    {
        TransitionState(State, @event);
    }

    [EventHandler]
    public void HandleGuestChatEvent(GuestChatEvent @event)
    {
        TransitionState(State, @event);
    }

    #endregion

    /// <summary>
    /// Handle state transitions for events
    /// </summary>
    protected override void TransitionState(AnonymousUserState state, IMessage evt)
    {
        switch (evt)
        {
            case InitializeAnonymousUserEvent initEvent:
                state.UserHashId = initEvent.UserHashId;
                state.CreatedAt = initEvent.CreatedAt;
                state.ChatCount = 0;
                break;
                
            case CreateGuestSessionEvent createSessionEvent:
                state.CurrentSessionId = createSessionEvent.SessionId;
                state.CurrentGuider = createSessionEvent.Guider;
                state.CurrentSessionUsed = false; // Reset session used flag for new session
                // Note: Don't increment chat count on session creation, only on actual chat
                break;
                
            case GuestChatEvent chatEvent:
                state.ChatCount = chatEvent.ChatCount;
                state.LastChatTime = chatEvent.ChatAt;
                state.CurrentSessionUsed = chatEvent.SessionUsed;
                break;
                
            default:
                Logger.LogWarning("Unhandled event type {EventType}", evt.GetType().Name);
                break;
        }
    }
}
