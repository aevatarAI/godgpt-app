using Aevatar.Agents.Abstractions.EventRouting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.EventRouting;

/// <summary>
/// EventRouter factory with DI support
/// Creates EventRouter instances with dependencies injected from DI container
/// </summary>
public class EventRouterFactory
{
    private readonly IServiceProvider? _serviceProvider;

    public EventRouterFactory(IServiceProvider? serviceProvider = null)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Create EventRouter with injected store from service provider
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="sendToActorAsync">Send to actor delegate</param>
    /// <param name="sendToSelfAsync">Send to self delegate</param>
    /// <param name="logger">Logger instance</param>
    /// <returns>EventRouter instance with injected store</returns>
    public EventRouter CreateEventRouter(
        string agentId,
        Func<string, EventEnvelope, CancellationToken, Task> sendToActorAsync,
        Func<EventEnvelope, CancellationToken, Task> sendToSelfAsync,
        ILogger? logger)
    {
        // Try to get IEventRouterStore from service container
        var store = _serviceProvider?.GetService<IEventRouterStore>();
        
        // Create EventRouter with or without store
        return new EventRouter(
            agentId,
            sendToActorAsync,
            sendToSelfAsync,
            logger,
            store);
    }
}
