using Aevatar.Core.Abstractions;
using Orleans;
using Orleans.Concurrency;

namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Interface for ConfigurationGAgent
/// 
/// Migration Phase 1: Uses Protobuf State but still Orleans Grain pattern
/// Inherits legacy IGAgent (simple interface) + Orleans IGrainWithGuidKey
/// TODO: After all GAgents are migrated, switch to new framework's IGAgent
/// </summary>
public interface IConfigurationGAgent : IGAgent, IGrainWithGuidKey
{
    [ReadOnly]
    Task<string> GetSystemLLM();
    
    [ReadOnly]
    Task<bool> GetStreamingModeEnabled();
    
    [ReadOnly]
    Task<string> GetPrompt();
        
    Task UpdateSystemPromptAsync(string systemPrompt);
    
    [ReadOnly]
    Task<string> GetUserProfilePromptAsync();
}
