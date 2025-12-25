using Aevatar.Agents.Abstractions;
using Microsoft.Extensions.Logging;
using Orleans;
using System;
using System.Threading.Tasks;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Stream Not Found Handler
/// Activates the corresponding Grain when a stream is not found
/// </summary>
public class OrleansStreamNotFoundHandler : IStreamNotFoundHandler
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansStreamNotFoundHandler> _logger;

    public OrleansStreamNotFoundHandler(
        IGrainFactory grainFactory,
        ILogger<OrleansStreamNotFoundHandler> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    public async Task HandleStreamNotFoundAsync(string streamId)
    {
        try
        {
            _logger.LogDebug("Attempting to activate Orleans Grain for Agent {AgentId}", streamId);
            
            // Get the Grain using the Agent ID
            // Using IGAgentGrain which is the standard grain interface for agents
            var grain = _grainFactory.GetGrain<IGAgentGrain>(streamId);
            
            // Invoke a non-obsolete method to force activation.
            // NOTE:
            // - OnActivateAsync always initializes stream subscription.
            // - If AgentTypeName was persisted, the agent will be restored automatically.
            // - If not initialized, IsInitializedAsync simply returns false (but activation already happened).
            var initialized = await grain.IsInitializedAsync();
            
            _logger.LogDebug("Successfully triggered activation for Orleans Grain {AgentId} (initialized={Initialized})",
                streamId, initialized);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate Orleans Grain for Agent {AgentId}", streamId);
            throw; // Re-throw to let Dispatcher know activation failed
        }
    }
}

