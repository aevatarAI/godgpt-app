namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Base interface for all GAgents
/// Defines the minimum abstraction with only identity
/// </summary>
public interface IGAgent
{
    /// <summary>
    /// Agent unique identifier.
    /// Format varies by runtime:
    /// - Orleans: "AgentType:Guid" (e.g., "ChatAgent:12345678-...")
    /// - Local/Proto: "Guid" (e.g., "12345678-...")
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Get Agent Category (used for Stream routing and grouping).
    /// Defaults to the Type Name.
    /// </summary>
    /// <returns>The category string.</returns>
    string GetAgentCategory();

    /// <summary>
    /// Get GAgent description.
    /// </summary>
    /// <returns>A descriptive string about this agent</returns>
    Task<string> GetDescriptionAsync();

    /// <summary>
    /// Get all subscribed events of current GAgent.
    /// </summary>
    /// <param name="includeAllEventHandler">Whether to include AllEventHandler subscribed events</param>
    /// <returns>List of event types this agent can handle</returns>
    Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false);

    /// <summary>
    /// Active current GAgent.
    /// </summary>
    /// <returns></returns>
    Task ActivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Deactive current GAgent.
    /// </summary>
    /// <returns></returns>
    Task DeactivateAsync(CancellationToken ct = default);

    /// <summary>
    /// Handle event envelope.
    /// </summary>
    /// <param name="envelope">Event envelope to handle</param>
    /// <param name="ct">Cancellation token</param>
    Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default);
}
