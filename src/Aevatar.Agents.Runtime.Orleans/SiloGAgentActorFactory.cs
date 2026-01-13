using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Silo-side IGAgentActorFactory implementation (for OrleansGrain internal use).
/// Creates <see cref="SiloGAgentActor"/> which delegates to <see cref="IGAgentGrain"/> via <see cref="IGrainFactory"/>.
/// Caches actor instances to avoid repeated IsInitializedAsync RPC calls.
/// </summary>
public sealed class SiloGAgentActorFactory : IGAgentActorFactory
{
    private readonly IGrainFactory _grainFactory;
    private readonly ILogger<SiloGAgentActorFactory> _logger;
    
    /// <summary>
    /// Cache for actor instances. Key = "AgentTypeShortName:RawId"
    /// This avoids repeated IsInitializedAsync/InitializeAgentAsync RPC calls.
    /// </summary>
    private readonly ConcurrentDictionary<string, IGAgentActor> _actorCache = new();

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
        
        // Build cache key
        var agentTypeShortName = AgentId.GetAgentTypeShortName(agentTypeName);
        var cacheKey = $"{agentTypeShortName}:{rawId}";
        
        // Check cache first - fast path without any RPC
        if (_actorCache.TryGetValue(cacheKey, out var cachedActor))
        {
            _logger.LogDebug("[SiloActorCache] ✅ HIT cacheKey={CacheKey}", cacheKey);
            return cachedActor;
        }

        _logger.LogDebug("[SiloActorCache] ❌ MISS cacheKey={CacheKey} - Creating SiloGAgentActor for {AgentType}",
            cacheKey, agentType.Name);

        var actor = new SiloGAgentActor(rawId, agentTypeName, _grainFactory, _logger);
        await actor.ActivateAsync(ct);
        
        // Cache the actor
        _actorCache.TryAdd(cacheKey, actor);
        
        return actor;
    }
}


