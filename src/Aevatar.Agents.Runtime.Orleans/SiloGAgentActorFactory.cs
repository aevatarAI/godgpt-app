using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Silo-side IGAgentActorFactory implementation (for OrleansGrain internal use).
/// Creates <see cref="SiloGAgentActor"/> which delegates to <see cref="IGAgentGrain"/> via <see cref="IGrainFactory"/>.
/// </summary>
internal sealed class SiloGAgentActorFactory : IGAgentActorFactory
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<SiloGAgentActorFactory> _logger;

    public SiloGAgentActorFactory(IGrainFactory grainFactory, ILogger<SiloGAgentActorFactory> logger)
    {
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(string? id = null, CancellationToken ct = default)
        where TAgent : IGAgent
    {
        var agentType = typeof(TAgent);
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();

        // Normalize to unified ActorId = "ShortName:RawId"
        var actorId = AgentId.Normalize(agentType, inputId);
        var rawId = AgentId.ExtractRawId(actorId);
        var agentTypeName = agentType.AssemblyQualifiedName ?? agentType.FullName ?? agentType.Name;

        _logger.LogDebug("Creating SiloGAgentActor for {AgentType}, inputId={InputId}, actorId={ActorId}",
            agentType.Name, inputId, actorId);

        var actor = new SiloGAgentActor(rawId, agentTypeName, _grainFactory, _logger);
        await actor.ActivateAsync(ct);
        return actor;
    }
}


