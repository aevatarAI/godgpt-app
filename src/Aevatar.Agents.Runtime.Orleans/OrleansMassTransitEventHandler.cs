using System;
using System.IO;
using System.Threading.Tasks;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans implementation of IMassTransitEventHandler.
/// Routes MassTransit events to Orleans Grains via RPC, ensuring callbacks run on Grain turn.
/// 
/// Since AgentId is now a string that directly serves as the GrainKey,
/// no cache mapping is needed - agentId IS the grainKey.
/// </summary>
public class OrleansMassTransitEventHandler : IMassTransitEventHandler
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<OrleansMassTransitEventHandler> _logger;

    public OrleansMassTransitEventHandler(
        IGrainFactory grainFactory,
        ILogger<OrleansMassTransitEventHandler> logger)
    {
        _grainFactory = grainFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> HandleEventAsync(string agentId, EventEnvelope envelope)
    {
        if (string.IsNullOrEmpty(agentId))
        {
            _logger.LogDebug("Empty agentId received, skipping");
            return false;
        }

        try
        {
            _logger.LogDebug("Routing event {EventId} to Grain {GrainKey} via RPC", envelope.Id, agentId);
            
            // Serialize envelope to bytes
            using var stream = new MemoryStream();
            envelope.WriteTo(stream);
            var envelopeBytes = stream.ToArray();
            
            // AgentId is the GrainKey - direct mapping after Guid→string migration
            var grain = _grainFactory.GetGrain<IGAgentGrain>(agentId);
            await grain.HandleEventAsync(envelopeBytes);
            
            _logger.LogDebug("Successfully handled event {EventId} for Grain {GrainKey}", envelope.Id, agentId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route event {EventId} to Grain {GrainKey}", envelope.Id, agentId);
            throw;
        }
    }
}

