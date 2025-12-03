using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Core;
using Aevatar.Agents.GodGPT.Protos;
using Google.Protobuf;
using Microsoft.Extensions.Logging;

namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Configuration GAgent - manages chat agent configuration
/// 
/// New Framework: Inherits from GAgentBase, NOT Grain
/// Uses Protobuf State + Event Sourcing pattern
/// </summary>
public class ConfigurationGAgent : GAgentBase<ConfigurationState>
{
    private const string DefaultSystemLLM = "OpenAI";
    private const string DefaultUserProfilePrompt = @"
        I'm {Gender} 
        My Birth date is {BirthDate} and my birth place is {BirthPlace} 
        Please tell me my fate. 
        Remember: respond in the same language the user used when filling in the location. 
    ";

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await base.OnActivateAsync(ct);

        Logger.LogDebug("ConfigurationGAgent {Id} activating", Id);

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

        await ConfirmEventsAsync(ct);
    }

    // ============================================================================
    // Event Handlers (new framework style)
    // ============================================================================

    [EventHandler]
    public async Task HandleSetSystemLLMAsync(SetSystemLLMEvent evt)
    {
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    [EventHandler]
    public async Task HandleSetPromptAsync(SetPromptEvent evt)
    {
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    [EventHandler]
    public async Task HandleSetStreamingModeAsync(SetStreamingModeEnabledEvent evt)
    {
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    [EventHandler]
    public async Task HandleSetUserProfilePromptAsync(SetUserProfilePromptEvent evt)
    {
        RaiseEvent(evt);
        await ConfirmEventsAsync();
    }

    // ============================================================================
    // State Transition (Pure Functional)
    // ============================================================================

    protected override void TransitionState(ConfigurationState state, IMessage evt)
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
    // Public Methods
    // ============================================================================

    public override Task<string> GetDescriptionAsync()
    {
        return Task.FromResult($"Configuration GAgent - LLM: {State.SystemLlm}, Streaming: {State.StreamingModeEnabled}");
    }

    public string GetSystemLLM() => State.SystemLlm ?? DefaultSystemLLM;

    public bool GetStreamingModeEnabled() => State.StreamingModeEnabled;

    public string GetPrompt() => State.Prompt ?? string.Empty;

    public string GetUserProfilePrompt() => State.UserProfilePrompt ?? DefaultUserProfilePrompt;

    public async Task UpdateSystemPromptAsync(string systemPrompt)
    {
        Logger.LogDebug("[ConfigurationGAgent] Updating prompt to '{NewPrompt}'", systemPrompt);
        RaiseEvent(new SetPromptEvent { Prompt = systemPrompt });
        await ConfirmEventsAsync();
    }
}
