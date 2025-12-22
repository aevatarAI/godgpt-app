using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Factory;

/// <summary>
/// Simple auto-discovery factory provider
/// Automatically creates factories for all Agents without manual registration
/// </summary>
public class DefaultGAgentActorFactoryProvider : IGAgentActorFactoryProvider
{
    private readonly ILogger<DefaultGAgentActorFactoryProvider>? _logger;

    private readonly ConcurrentDictionary<Type, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>>
        _factories;

    public DefaultGAgentActorFactoryProvider(IServiceProvider serviceProvider)
    {
        _logger = serviceProvider.GetService<ILogger<DefaultGAgentActorFactoryProvider>>();
        _factories =
            new ConcurrentDictionary<Type, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>>();
    }

    public void RegisterFactory<TAgent>(Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
        where TAgent : IGAgent
    {
        _factories[typeof(TAgent)] = factory;
    }

    public void RegisterFactory(Type agentType,
        Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
    {
        _factories[agentType] = factory;
    }

    public Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType)
    {
        return _factories.GetValueOrDefault(agentType);
    }
}