using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.AI.Abstractions;

namespace Aevatar.Agents.AI.Core.Helpers;

/// <summary>
/// Automatically injects per-agent <see cref="IAevatarAIMemory"/> into AI agents (AIGAgentBase).
///
/// Pattern:
/// - Resolve <see cref="IAevatarAIMemoryFactory"/> from DI (MongoDB-backed, etc.)
/// - Create a memory instance bound to current agent id
/// - Assign it to the protected property <c>AIMemory</c> on the agent instance
/// </summary>
public static class AIAgentAIMemoryInjector
{
    public static void InjectAIMemory(IGAgent agent, IServiceProvider serviceProvider)
    {
        if (agent == null || serviceProvider == null)
            return;

        var property = FindAIMemoryProperty(agent.GetType());
        if (property == null || !property.CanWrite)
            return;

        var factory = serviceProvider.GetService(typeof(IAevatarAIMemoryFactory)) as IAevatarAIMemoryFactory;
        if (factory == null)
            return;

        try
        {
            var memory = factory.Create(agent.Id);
            property.SetValue(agent, memory);
        }
        catch
        {
            // Best-effort injection; do not break agent creation pipeline.
        }
    }

    public static bool HasAIMemory(object target)
    {
        if (target == null)
            return false;

        return FindAIMemoryProperty(target.GetType()) != null;
    }

    private static PropertyInfo? FindAIMemoryProperty(Type type)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        foreach (var property in type.GetProperties(flags))
        {
            if (property.Name == "AIMemory" &&
                typeof(IAevatarAIMemory).IsAssignableFrom(property.PropertyType))
            {
                return property;
            }
        }

        return type.BaseType != null && type.BaseType != typeof(object)
            ? FindAIMemoryProperty(type.BaseType)
            : null;
    }
}

