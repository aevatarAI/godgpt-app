using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local Runtime Stream Not Found Handler
/// Handles cases where a message arrives for an agent that is not currently active in memory.
/// </summary>
public class LocalStreamNotFoundHandler : IStreamNotFoundHandler
{
    private readonly ILogger<LocalStreamNotFoundHandler> _logger;
    private readonly IGAgentActorManager _actorManager;

    public LocalStreamNotFoundHandler(
        IGAgentActorManager actorManager,
        ILogger<LocalStreamNotFoundHandler> logger)
    {
        _actorManager = actorManager;
        _logger = logger;
    }

    public async Task HandleStreamNotFoundAsync(string streamId)
    {
        // 1. Check if actor exists in manager (Double check)
        var exists = await _actorManager.ExistsAsync(streamId);
        
        if (exists)
        {
            // If it exists in manager, it might just be missing its stream initialization
            // Accessing it via GetActorAsync usually refreshes state
            var actor = await _actorManager.GetActorAsync(streamId);
            if (actor != null)
            {
                _logger.LogInformation("Agent {AgentId} found in Local Manager. Stream should be initialized.", streamId);
                return;
            }
        }

        // 2. Local Runtime Limitation:
        // We cannot auto-activate an agent from just a GUID because we don't know its Type.
        // In Orleans, the Grain Interface handles this. In Local, we need the concrete class.
        
        _logger.LogWarning(
            "Stream not found for Agent {AgentId} and Agent is not active in Local Runtime. " +
            "Auto-activation is NOT supported in Local Runtime without explicit creation. " +
            "Message may be retried or moved to DLQ depending on transport configuration.", 
            streamId);

        // We throw an exception to indicate failure to activate.
        // This will cause MassTransit to retry (and eventually DLQ).
        // If we simply returned, the Dispatcher would try to GetStream again, fail, and then throw anyway.
        // So we explicitly state the reason here.
        throw new InvalidOperationException($"Local Agent {streamId} not active and cannot be auto-activated.");
    }
}

