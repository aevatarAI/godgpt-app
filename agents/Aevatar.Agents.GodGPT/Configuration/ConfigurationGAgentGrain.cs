using Aevatar.Agents.GodGPT.Protos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;

namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Orleans Grain wrapper for ConfigurationGAgent
/// 
/// This allows legacy code to call via GrainFactory.GetGrain() 
/// while the actual logic lives in the new framework's ConfigurationGAgent
/// 
/// TODO: Remove this wrapper after all callers are migrated to new framework
/// </summary>
[StorageProvider(ProviderName = "PubSubStore")]
public class ConfigurationGAgentGrain : Grain, IConfigurationGAgentGrain
{
    private ConfigurationGAgent _agent = null!;
    private ILogger<ConfigurationGAgentGrain> _logger = null!;

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        _logger = ServiceProvider.GetRequiredService<ILogger<ConfigurationGAgentGrain>>();
        
        // Create the new framework agent
        _agent = new ConfigurationGAgent();
        
        // Inject dependencies
        var agentLogger = ServiceProvider.GetRequiredService<ILogger<ConfigurationGAgent>>();
        var loggerProperty = typeof(Aevatar.Agents.Core.GAgentBase).GetProperty("Logger", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        loggerProperty?.SetValue(_agent, agentLogger);
        
        // Set ID
        var idProperty = typeof(Aevatar.Agents.Core.GAgentBase).GetProperty("Id", 
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (idProperty?.CanWrite == true)
        {
            idProperty.SetValue(_agent, this.GetPrimaryKey());
        }
        
        // Activate agent
        await _agent.ActivateAsync(ct);
        
        await base.OnActivateAsync(ct);
        _logger.LogDebug("ConfigurationGAgentGrain {Id} activated", this.GetPrimaryKey());
    }

    public Task<string> GetDescriptionAsync() => _agent.GetDescriptionAsync();

    public Task<string> GetSystemLLM() => Task.FromResult(_agent.GetSystemLLM());

    public Task<bool> GetStreamingModeEnabled() => Task.FromResult(_agent.GetStreamingModeEnabled());

    public Task<string> GetPrompt() => Task.FromResult(_agent.GetPrompt());

    public Task<string> GetUserProfilePromptAsync() => Task.FromResult(_agent.GetUserProfilePrompt());

    public Task UpdateSystemPromptAsync(string systemPrompt) => _agent.UpdateSystemPromptAsync(systemPrompt);
}

/// <summary>
/// Orleans Grain interface for ConfigurationGAgent
/// Used by legacy code via GrainFactory.GetGrain()
/// </summary>
public interface IConfigurationGAgentGrain : IGrainWithGuidKey
{
    Task<string> GetDescriptionAsync();
    Task<string> GetSystemLLM();
    Task<bool> GetStreamingModeEnabled();
    Task<string> GetPrompt();
    Task<string> GetUserProfilePromptAsync();
    Task UpdateSystemPromptAsync(string systemPrompt);
}

