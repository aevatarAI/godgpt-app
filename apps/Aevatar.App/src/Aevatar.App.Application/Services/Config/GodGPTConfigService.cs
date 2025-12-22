using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Application.Grains.Agents.ChatManager.ConfigAgent;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Quantum;
using Volo.Abp;
using Volo.Abp.Application.Services;
using Volo.Abp.Auditing;
using Aevatar.App.Application.Contracts.Services.Config;

namespace Aevatar.App.Application.Services.Config;

/// <summary>
/// Service implementation for managing GodGPT system configuration.
/// Handles system prompt retrieval and updates through the ConfigurationGAgent.
/// </summary>
[RemoteService(IsEnabled = false)]
[DisableAuditing]
public class GodGPTConfigService : ApplicationService, IGodGPTConfigService
{
    private readonly IGAgentActorFactory _actorFactory;

    public GodGPTConfigService(IGAgentActorFactory actorFactory)
    {
        _actorFactory = actorFactory;
    }

    /// <inheritdoc />
    public Task<string> GetSystemPromptAsync()
    {
        var configurationActor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        var configurationAgent = (ConfigurationGAgent)configurationActor.GetAgent();
        return Task.FromResult(configurationAgent.GetPrompt());
    }

    /// <inheritdoc />
    public Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto)
    {
        var configurationActor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId());
        var configurationAgent = (ConfigurationGAgent)configurationActor.GetAgent();
        return configurationAgent.UpdateSystemPromptAsync(godGptConfigurationDto.SystemPrompt);
    }
}
