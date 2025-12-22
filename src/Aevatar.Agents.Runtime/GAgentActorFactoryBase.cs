using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Core.Helpers;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// GAgent Actor 工厂的抽象基类，包含通用逻辑
/// </summary>
public abstract class GAgentActorFactoryBase : IGAgentActorFactory
{
    protected readonly IServiceProvider _serviceProvider;
    protected readonly ILogger _logger;
    protected readonly IGAgentActorFactoryProvider? _factoryProvider;
    protected readonly IGAgentFactory? _agentFactory;

    protected GAgentActorFactoryBase(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _factoryProvider = serviceProvider.GetService<IGAgentActorFactoryProvider>();
        _agentFactory = serviceProvider.GetService<IGAgentFactory>();
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(string? id = null, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        if (string.IsNullOrEmpty(id))
        {
            id = Guid.NewGuid().ToString();
        }

        var agentType = typeof(TAgent);

        if (_factoryProvider != null)
        {
            var customFactory = _factoryProvider.GetFactory(agentType);
            if (customFactory != null)
            {
                _logger.LogDebug("Using custom factory for type {AgentType} with id {Id}",
                    agentType.Name, id);
                return await customFactory(this, id, ct);
            }
        }

        _logger.LogDebug("Using default creation process for type {AgentType} with id {Id}",
            agentType.Name, id);

        if (_agentFactory == null)
        {
            throw new InvalidOperationException(
                $"No IGAgentFactory registered. Cannot create agent of type {agentType.Name}");
        }

        var agent = _agentFactory.CreateGAgent(id, agentType, ct);

        await agent.ActivateAsync(ct);

        // Template Method Pattern:
        // 1. Create uninitialized actor instance (implemented by subclasses)
        var actor = await CreateActorInstanceAsync(agent, id, ct);

        // 2. Inject dependencies (Logger, EventRouterFactory, StateProjector)
        LoggerInjector.InjectLogger(actor, _serviceProvider);
        EventRouterFactoryInjector.InjectEventRouterFactory(actor, _serviceProvider);
        StateProjectorInjector.InjectStateProjector(agent, _serviceProvider);

        // 3. Activate actor (starts streams, loads state, etc.)
        await actor.ActivateAsync(ct);

        return actor;
    }

    /// <summary>
    /// Create an uninitialized Actor instance for the Agent
    /// Subclasses should only create the instance, dependency injection and activation are handled by the base class
    /// </summary>
    protected abstract Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id,
        CancellationToken ct = default);
}