using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Interface for handling MassTransit events and routing them to the correct actor.
/// 
/// Implementations should ensure the event is processed in the correct actor context
/// (e.g., Grain turn for Orleans, Actor mailbox for ProtoActor).
/// </summary>
public interface IMassTransitEventHandler
{
    /// <summary>
    /// Handle an incoming event for a specific agent.
    /// </summary>
    /// <param name="agentId">The target agent's ID (format varies by runtime)</param>
    /// <param name="envelope">The event envelope containing the message</param>
    /// <returns>True if the event was handled, false if this handler cannot route to the agent</returns>
    Task<bool> HandleEventAsync(string agentId, EventEnvelope envelope);
}

