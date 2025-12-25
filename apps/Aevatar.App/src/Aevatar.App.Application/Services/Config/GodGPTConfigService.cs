using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Extensions;
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
    public async Task<string> GetSystemPromptAsync()
    {
        var configurationActor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId().ToString());
        var configurationAgent = configurationActor.As<IConfigurationGAgent>();
        return await configurationAgent.GetPromptAsync();
    }

    /// <inheritdoc />
    public async Task UpdateSystemPromptAsync(GodGPTConfigurationDto godGptConfigurationDto)
    {
        var configurationActor = await _actorFactory.CreateGAgentActorAsync<ConfigurationGAgent>(CommonHelper.GetSessionManagerConfigurationId().ToString());
        var configurationAgent = configurationActor.As<IConfigurationGAgent>();
        await configurationAgent.UpdateSystemPromptAsync(godGptConfigurationDto.SystemPrompt);
    }
}
