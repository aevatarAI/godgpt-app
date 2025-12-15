using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.EventSourcing;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Helpers;
using Microsoft.Extensions.DependencyInjection;

namespace Aevatar.App;

/// <summary>
/// Test helper to create agents with InMemoryEventStore for unit testing.
/// Uses the framework's AgentEventStoreInjector for consistency.
/// Based on the Payment module's test pattern.
/// </summary>
public static class TestHelpers
{
    /// <summary>
    /// Create agent with InMemoryEventStore configured (for unit tests without Orleans)
    /// </summary>
    public static T CreateAgent<T>() where T : class, IGAgent, new()
    {
        var agent = new T();
        
        // Build a minimal service provider with InMemoryEventStore
        var services = new ServiceCollection();
        services.AddSingleton<IEventStore, InMemoryEventStore>();
        var serviceProvider = services.BuildServiceProvider();
        
        // Use framework's injector
        AgentEventStoreInjector.InjectEventStore(agent, serviceProvider);
        
        return agent;
    }
}

