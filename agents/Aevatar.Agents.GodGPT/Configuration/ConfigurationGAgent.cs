using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Google.Protobuf;
using Json.Schema.Generation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Configuration GAgent - manages chat agent configuration
/// 
/// Migration Phase 1: Uses Protobuf State + Event Sourcing pattern
/// Still inherits from Grain for Orleans compatibility during migration
/// TODO: After all callers are migrated, change to inherit from Aevatar.Agents.Core.GAgentBase
/// </summary>
[Description("manage chat agent")]
[StorageProvider(ProviderName = "PubSubStore")]
public class ConfigurationGAgent : Grain, IConfigurationGAgent
{
    private const string DefaultSystemLLM = "OpenAI";
    private const string DefaultUserProfilePrompt = @"
        I'm {Gender} 
        My Birth date is {BirthDate} and my birth place is {BirthPlace} 
        Please tell me my fate. 
        Remember: respond in the same language the user used when filling in the location. 
    ";

    // Protobuf State (new framework style)
    private ConfigurationState _state = new();
    protected ConfigurationState State => _state;

    // Event Sourcing
    private readonly List<IMessage> _pendingEvents = new();
    private long _eventVersion = 0;

    // Logger
    private ILogger<ConfigurationGAgent> _logger = null!;
    protected ILogger<ConfigurationGAgent> Logger => _logger;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _logger = ServiceProvider.GetRequiredService<ILogger<ConfigurationGAgent>>();

        _logger.LogDebug("ConfigurationGAgent OnActivateAsync");

        await base.OnActivateAsync(cancellationToken);

        // Initialize default values if not set
        if (string.IsNullOrEmpty(State.SystemLlm))
        {
            RaiseEvent(new SetSystemLLMEvent { SystemLlm = DefaultSystemLLM });
        }

        if (string.IsNullOrEmpty(State.UserProfilePrompt))
        {
            RaiseEvent(new SetUserProfilePromptEvent { UserProfilePrompt = DefaultUserProfilePrompt });
        }

        if (!State.StreamingModeEnabled)
        {
            RaiseEvent(new SetStreamingModeEnabledEvent { StreamingModeEnabled = true });
        }

        await ConfirmEventsAsync();
    }

    // ============================================================================
    // Event Sourcing Methods (new framework style)
    // ============================================================================

    /// <summary>
    /// Raise an event for state transition (Event Sourcing pattern)
    /// </summary>
    protected void RaiseEvent<TEvent>(TEvent evt) where TEvent : IMessage
    {
        _pendingEvents.Add(evt);
        TransitionState(_state, evt);
    }

    /// <summary>
    /// Confirm pending events
    /// </summary>
    protected Task ConfirmEventsAsync()
    {
        _eventVersion += _pendingEvents.Count;
        _pendingEvents.Clear();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Pure functional state transition
    /// </summary>
    protected void TransitionState(ConfigurationState state, IMessage evt)
    {
        switch (evt)
        {
            case SetSystemLLMEvent e:
                state.SystemLlm = e.SystemLlm;
                break;
            case SetPromptEvent e:
                state.Prompt = e.Prompt;
                break;
            case SetStreamingModeEnabledEvent e:
                state.StreamingModeEnabled = e.StreamingModeEnabled;
                break;
            case SetUserProfilePromptEvent e:
                state.UserProfilePrompt = e.UserProfilePrompt;
                break;
        }
    }

    // ============================================================================
    // IGAgent Implementation (legacy interface)
    // ============================================================================

    public Task<string> GetDescriptionAsync()
    {
        return Task.FromResult("Configuration GAgent - manages chat agent configuration");
    }

    // ============================================================================
    // IConfigurationGAgent Implementation
    // ============================================================================

    public Task<string> GetSystemLLM()
    {
        return Task.FromResult(State.SystemLlm ?? DefaultSystemLLM);
    }

    public Task<bool> GetStreamingModeEnabled()
    {
        return Task.FromResult(State.StreamingModeEnabled);
    }

    public Task<string> GetPrompt()
    {
        return Task.FromResult(State.Prompt ?? string.Empty);
    }

    public Task<string> GetUserProfilePromptAsync()
    {
        return Task.FromResult(State.UserProfilePrompt ?? DefaultUserProfilePrompt);
    }

    public async Task UpdateSystemPromptAsync(string systemPrompt)
    {
        Logger.LogDebug("[ConfigurationGAgent][UpdateSystemPrompt] Updating prompt to '{NewPrompt}'", systemPrompt);

        RaiseEvent(new SetPromptEvent { Prompt = systemPrompt });
        await ConfirmEventsAsync();

        Logger.LogDebug("[ConfigurationGAgent][UpdateSystemPrompt] Prompt updated successfully");
    }
}
