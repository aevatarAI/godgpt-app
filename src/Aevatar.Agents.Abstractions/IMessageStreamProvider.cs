namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream provider interface.
/// Responsible for providing message streams for agents.
/// </summary>
public interface IMessageStreamProvider
{
    /// <summary>
    /// Gets the message stream for the specified agent.
    /// </summary>
    /// <param name="agentId">The agent identifier (format varies by runtime).</param>
    /// <param name="category">The optional category (e.g., Agent Type) used for routing to specific topics/queues.</param>
    /// <returns>The message stream instance.</returns>
    IMessageStream GetStream(string agentId, string? category = null);
}
