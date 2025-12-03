namespace Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;

/// <summary>
/// Interface for ConfigurationGAgent
/// New framework style - inherits from new IGAgent
/// </summary>
public interface IConfigurationGAgent : Aevatar.Agents.Abstractions.IGAgent
{
    string GetSystemLLM();
    bool GetStreamingModeEnabled();
    string GetPrompt();
    string GetUserProfilePrompt();
    Task UpdateSystemPromptAsync(string systemPrompt);
}
