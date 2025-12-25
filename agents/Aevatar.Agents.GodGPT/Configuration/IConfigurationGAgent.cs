namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Interface for ConfigurationGAgent
/// New framework style - inherits from new IGAgent
/// </summary>
public interface IConfigurationGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    Task<string> GetSystemLLMAsync();
    Task<bool> GetStreamingModeEnabledAsync();
    Task<string> GetPromptAsync();
    Task<string> GetUserProfilePromptAsync();
    Task UpdateSystemPromptAsync(string systemPrompt);
}
