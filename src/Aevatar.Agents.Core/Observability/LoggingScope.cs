using Microsoft.Extensions.Logging;

namespace Aevatar.Agents.Core.Observability;

/// <summary>
/// Logging scope helper class
/// Provides structured logging support
/// </summary>
public static class LoggingScope
{
    /// <summary>
    /// Create logging scope for Agent operation
    /// </summary>
    public static IDisposable CreateAgentScope(
        ILogger logger,
        string agentId,
        string operation,
        Dictionary<string, object>? additionalData = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["Operation"] = operation
        };

        if (additionalData != null)
        {
            foreach (var kvp in additionalData)
            {
                scopeData[kvp.Key] = kvp.Value;
            }
        }

        return logger.BeginScope(scopeData) ?? new NoOpDisposable();
    }

    /// <summary>
    /// Create logging scope for event handling
    /// </summary>
    public static IDisposable CreateEventHandlingScope(
        ILogger logger,
        string agentId,
        string eventId,
        string eventType,
        string? correlationId = null)
    {
        var scopeData = new Dictionary<string, object>
        {
            ["AgentId"] = agentId,
            ["EventId"] = eventId,
            ["EventType"] = eventType
        };

        if (correlationId != null)
        {
            scopeData["CorrelationId"] = correlationId;
        }

        return logger.BeginScope(scopeData) ?? new NoOpDisposable();
    }

    /// <summary>
    /// Empty Disposable (used when BeginScope returns null)
    /// </summary>
    private class NoOpDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}