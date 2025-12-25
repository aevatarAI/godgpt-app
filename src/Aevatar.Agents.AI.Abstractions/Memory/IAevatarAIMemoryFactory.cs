namespace Aevatar.Agents.AI.Abstractions;

/// <summary>
/// Factory for creating per-agent AI memory instances.
///
/// WHY:
/// - <see cref="IAevatarAIMemory"/> is intentionally minimal and does not carry agent/session identifiers.
/// - In real systems, memory must be isolated by agent (and optionally session).
/// - The factory lets the runtime create a memory instance bound to a specific agent id.
/// </summary>
// ReSharper disable once InconsistentNaming
public interface IAevatarAIMemoryFactory
{
    /// <summary>
    /// Create an AI memory instance bound to the given agent id.
    /// </summary>
    /// <param name="agentId">
    /// Agent id (string). Format may vary by runtime (e.g., Orleans may use a composite key).
    /// </param>
    /// <param name="sessionId">
    /// Optional session identifier. If provided, implementations may scope history/search to that session.
    /// </param>
    IAevatarAIMemory Create(string agentId, string? sessionId = null);
}

