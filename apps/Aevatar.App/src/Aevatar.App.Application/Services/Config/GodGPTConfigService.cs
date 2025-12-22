using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Quantum;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;

namespace Aevatar.App.Application.Services.Config;

/// <summary>
/// Service implementation for managing GodGPT system configuration.
/// Handles system prompt retrieval and updates through the ConfigurationGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTConfigService : ApplicationService, IGodGPTConfigService
{
    private readonly IGAgentFactory _agentFactory;

    public GodGPTConfigService(IGAgentFactory agentFactory)
    {
        _agentFactory = agentFactory;
    }

    /// <inheritdoc />
    public Task<string> GetSystemPromptAsync()
    {
        var configurationAgent =
            _agentFactory.CreateGAgent<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        return Task.FromResult(configurationAgent.GetPrompt());
    }

    /// <inheritdoc />
    public Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto)
    {
        var configurationAgent =
            _agentFactory.CreateGAgent<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        return configurationAgent.UpdateSystemPromptAsync(godGptConfigurationDto.SystemPrompt);
    }
}
