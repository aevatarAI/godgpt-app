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

    public async Task HandleStreamNotFoundAsync(Guid streamId)
    {
        try
        {
            _logger.LogDebug("Attempting to activate Orleans Grain for Agent {AgentId}", streamId);
            
            // Get the Grain using the Agent ID
            // Using IGAgentGrain which is the standard grain interface for agents
            var grain = _grainFactory.GetGrain<IGAgentGrain>(streamId.ToString());
            
            // Invoke a method to force activation
            // GetDescriptionAsync will trigger grain activation if not already activated
            await grain.GetDescriptionAsync();
            
            _logger.LogDebug("Successfully triggered activation for Orleans Grain {AgentId}", streamId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to activate Orleans Grain for Agent {AgentId}", streamId);
            throw; // Re-throw to let Dispatcher know activation failed
        }
    }
}

