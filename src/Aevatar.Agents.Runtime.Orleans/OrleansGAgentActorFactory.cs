using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.Context;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor Factory
/// 
/// Creates lightweight actor proxies that forward to Grains.
/// Agent instances are created and executed in the Grain (Silo) side.
/// 
/// Actor proxies are cached to avoid repeated RPC calls to InitializeAgentAsync.
/// </summary>
public class OrleansGAgentActorFactory : IGAgentActorFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<OrleansGAgentActorFactory> _logger;
    private readonly IStreamProvider? _streamProvider;
    private readonly StreamingOptions _streamingOptions;
    private readonly IMessageStreamProvider? _messageStreamProvider;
    private readonly IOptions<MessageStreamProviderOptions>? _providerOptions;
    
    /// <summary>
    /// Cache for actor proxies. Key = "AgentTypeShortName:RawId"
    /// This avoids repeated InitializeAgentAsync RPC calls for the same agent.
    /// </summary>
    private readonly ConcurrentDictionary<string, IGAgentActor> _actorCache = new();
    
    /// <summary>
    /// Unique identifier for this factory instance (for debugging singleton behavior)
    /// </summary>
    private readonly string _factoryInstanceId = Guid.NewGuid().ToString("N")[..8];

    public OrleansGAgentActorFactory(
        IServiceProvider serviceProvider,
        IClusterClient clusterClient,
        ILogger<OrleansGAgentActorFactory> logger,
        IMessageStreamProvider? messageStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
    {
        _serviceProvider = serviceProvider;
        _clusterClient = clusterClient;
        _logger = logger;
        _messageStreamProvider = messageStreamProvider;
        _providerOptions = providerOptions;
        
        _logger.LogInformation("[ActorFactory] Created new instance: {FactoryId}", _factoryInstanceId);

        // Get StreamingOptions from configuration
        _streamingOptions = serviceProvider.GetService<IOptions<StreamingOptions>>()?.Value
                            ?? new StreamingOptions();

        // Orleans Stream Provider
        var streamProviderName = _streamingOptions.StreamProviderName;
        try 
        {
            _streamProvider = clusterClient.GetStreamProvider(streamProviderName);
        }
        catch (Exception ex)
        {
            if (messageStreamProvider == null || providerOptions?.Value.Provider != "MassTransit")
            {
                _logger.LogWarning(ex, "Stream provider '{StreamProviderName}' not found", streamProviderName);
            }
        }
    }

    /// <summary>
    /// Create or get cached agent actor by type.
    /// Actor proxies are cached to avoid repeated InitializeAgentAsync RPC calls.
    /// </summary>
    public async Task<IGAgentActor> CreateGAgentActorAsync(
        Type agentType, 
        string? id = null, 
        CancellationToken ct = default)
    {
        // Orleans 下 ActorId 必须包含类型前缀（避免跨类型 id 冲突）：
        // ActorId = "AgentTypeShortName:RawId"
        var inputId = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("D") : id.Trim();
        var actorId = AgentId.Normalize(agentType, inputId);
        var rawId = AgentId.ExtractRawId(actorId);
        var agentTypeName = agentType.AssemblyQualifiedName ?? agentType.FullName ?? agentType.Name;
        
        // Build cache key: "AgentTypeShortName:RawId"
        var agentTypeShortName = AgentId.GetAgentTypeShortName(agentTypeName);
        var cacheKey = $"{agentTypeShortName}:{rawId}";
        
        // Check cache first - fast path without RPC
        if (_actorCache.TryGetValue(cacheKey, out var cachedActor))
        {
            _logger.LogInformation("[ActorCache] ✅ HIT factoryId={FactoryId}, cacheKey={CacheKey}, cacheSize={CacheSize}", 
                _factoryInstanceId, cacheKey, _actorCache.Count);
            return cachedActor;
        }

        _logger.LogInformation(
            "[ActorCache] ❌ MISS factoryId={FactoryId}, cacheKey={CacheKey}, cacheSize={CacheSize} - Creating new Actor proxy for {AgentType}",
            _factoryInstanceId, cacheKey, _actorCache.Count, agentType.Name);

        // Create lightweight actor proxy (Agent will be created in Grain/Silo)
        var contextPropagator = _serviceProvider.GetService<AgentContextPropagator>();
        var actor = new OrleansGAgentActor(
            rawId,
            agentTypeName,
            _clusterClient,
            _streamProvider,
            _streamingOptions,
            _serviceProvider.GetRequiredService<ILogger<OrleansGAgentActor>>(),
            _messageStreamProvider,
            _providerOptions,
            contextPropagator);

        // Activate - This will initialize Agent in the Grain (Silo side)
        await actor.ActivateAsync(ct);
        
        // Cache the actor proxy
        var added = _actorCache.TryAdd(cacheKey, actor);

        _logger.LogInformation("[ActorCache] ✅ ADDED factoryId={FactoryId}, cacheKey={CacheKey}, added={Added}, newCacheSize={CacheSize}", 
            _factoryInstanceId, cacheKey, added, _actorCache.Count);

        return actor;
    }

    /// <summary>
    /// Create agent actor by generic type
    /// </summary>
    public Task<IGAgentActor> CreateGAgentActorAsync<TAgent>(
        string? id = null, 
        CancellationToken ct = default) 
        where TAgent : IGAgent
    {
        return CreateGAgentActorAsync(typeof(TAgent), id, ct);
    }
}
