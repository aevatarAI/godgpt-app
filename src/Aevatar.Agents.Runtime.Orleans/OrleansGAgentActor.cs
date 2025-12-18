using System.Reflection;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Internal;
using Aevatar.Agents.Rpc;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans;

/// <summary>
/// Orleans Agent Actor - Lightweight Grain Proxy
/// 
/// This actor acts as a proxy to the OrleansGAgentGrain.
/// All business logic is executed in the Grain (Silo) side.
/// 
/// Responsibilities:
/// 1. Forward events to Grain via RPC
/// 2. Manage local stream subscriptions for hierarchy navigation
/// 3. Provide IGAgentActor interface for HttpApi layer
/// </summary>
public class OrleansGAgentActor : IGAgentActor, IActorHierarchyOperations
{
    private readonly IGrainFactory _grainFactory;
    private readonly IStreamProvider? _orleansStreamProvider;
    private readonly StreamingOptions _streamingOptions;
    private readonly IMessageStreamProvider? _externalStreamProvider;
    private readonly MessageStreamProviderOptions _providerOptions;
    
    // Cached Grain reference
    private IGAgentGrain? _grain;
    
    // Agent metadata (from Grain)
    private Guid _id;
    private string _agentTypeName;
    
    // Logger
    protected ILogger Logger { get; }

    public Guid Id => _id;

    public OrleansGAgentActor(
        Guid id,
        string agentTypeName,
        IGrainFactory grainFactory,
        IStreamProvider? orleansStreamProvider,
        StreamingOptions streamingOptions,
        ILogger<OrleansGAgentActor> logger,
        IMessageStreamProvider? externalStreamProvider = null,
        IOptions<MessageStreamProviderOptions>? providerOptions = null)
    {
        _id = id;
        _agentTypeName = agentTypeName;
        _grainFactory = grainFactory ?? throw new ArgumentNullException(nameof(grainFactory));
        _orleansStreamProvider = orleansStreamProvider;
        _streamingOptions = streamingOptions ?? new StreamingOptions();
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _externalStreamProvider = externalStreamProvider;
        _providerOptions = providerOptions?.Value ?? new MessageStreamProviderOptions();
    }

    #region IGAgentActor Implementation

    /// <summary>
    /// Activate actor - Initialize Grain with Agent type
    /// Business State is stored via IStateStore (per-type collections)
    /// Orleans Grain State only stores metadata (AgentId, ParentId, Children)
    /// </summary>
    public async Task ActivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Activating Orleans Actor proxy {ActorId}, AgentType: {AgentType}", _id, _agentTypeName);

        // Get non-generic Grain reference
        // Grain ID = AgentTypeShortName:AgentId
        //
        // IMPORTANT:
        // AgentId is NOT guaranteed to be globally unique across different Agent types in this framework.
        // Using only agentId as the grain key would cause type collisions (wrong Agent instance reused).
        var agentTypeShortName = GetAgentTypeShortName(_agentTypeName);
        var grainId = $"{agentTypeShortName}:{_id}";
        _grain = _grainFactory.GetGrain<IGAgentGrain>(grainId);

        // Initialize Agent in Grain (Silo side)
        // Agent ID is derived from Grain's PrimaryKey, no need to pass separately
        var success = await _grain.InitializeAgentAsync(_agentTypeName);
        if (!success)
        {
            throw new InvalidOperationException(
                $"Failed to initialize Agent {_agentTypeName} in Grain {grainId}");
        }

        Logger.LogInformation("✅ Orleans Actor proxy {GrainId} activated, Agent running in Silo", grainId);
    }

    /// <summary>
    /// Deactivate actor
    /// </summary>
    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogInformation("Deactivating Orleans Actor proxy {ActorId}", _id);

        if (_grain != null)
        {
            await _grain.DeactivateAsync();
        }
    }

    /// <summary>
    /// Publish event - Forward to Grain (broadcast mode)
    /// </summary>
    /// <param name="isInternalCall">If true, keeps PublisherId; if false (default), clears it for external calls</param>
    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt, 
        EventDirection direction = EventDirection.Down, 
        CancellationToken ct = default,
        bool isInternalCall = false) 
        where TEvent : IMessage
    {
        EnsureGrain();

        // Create EventEnvelope
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            // External calls: empty PublisherId allows Agent to handle the event
            // Internal calls: set to actor ID for self-handling check
            PublisherId = isInternalCall ? _id.ToString() : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = direction,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString()
        };

        // Serialize and forward to Grain
        using var stream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(stream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();

        await _grain!.HandleEventAsync(stream.ToArray());

        return envelope.Id;
    }

    /// <summary>
    /// Point-to-point send - Direct delivery to target Grain
    /// </summary>
    /// <param name="isInternalCall">If true, keeps PublisherId; if false (default), clears it for external calls</param>
    public async Task<string> SendToAsync<TEvent>(
        Guid targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        // Create EventEnvelope for P2P
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = isInternalCall ? _id.ToString() : "",
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId.ToString(),
            OnArrivalDirection = onArrivalDirection
        };

        Logger.LogDebug(
            "Actor {ActorId} sending P2P event {EventId} to {TargetAgentId}, onArrival={OnArrivalDirection}",
            _id, envelope.Id, targetAgentId, onArrivalDirection);

        // Serialize envelope
        using var stream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(stream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();

        // Direct RPC to target Grain (no broadcast)
        // NOTE:
        // We intentionally route by {CurrentAgentType}:{TargetAgentId} to avoid cross-type collisions.
        // If you need to send to a different Agent type, create the corresponding actor and call SendToAsync on it.
        var targetAgentTypeShortName = GetAgentTypeShortName(_agentTypeName);
        var targetGrainId = $"{targetAgentTypeShortName}:{targetAgentId}";
        var targetGrain = _grainFactory.GetGrain<IGAgentGrain>(targetGrainId);
        await targetGrain.HandleEventAsync(stream.ToArray());

        return envelope.Id;
    }

    /// <summary>
    /// Handle event - Forward to Grain for processing in Silo
    /// </summary>
    public async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        EnsureGrain();

        Logger.LogDebug("Forwarding event {EventId} to Grain {GrainId}", envelope.Id, _id);

        // Serialize and forward to Grain
        using var stream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(stream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();

        await _grain!.HandleEventAsync(stream.ToArray());
    }

    /// <summary>
    /// Get agent description from Grain
    /// </summary>
    public async Task<string> GetDescriptionAsync()
    {
        EnsureGrain();
        return await _grain!.GetDescriptionAsync();
    }

    #endregion

    #region Hierarchy Management

    public async Task SetParentAsync(Guid parentId)
    {
        EnsureGrain();
        await _grain!.SetParentAsync(parentId);
    }

    public async Task SetParentAsync(Guid parentId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.SetParentAsync(parentId);
    }

    public async Task ClearParentAsync()
    {
        EnsureGrain();
        await _grain!.ClearParentAsync();
    }

    public async Task ClearParentAsync(CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.ClearParentAsync();
    }

    public async Task AddChildAsync(Guid childId)
    {
        EnsureGrain();
        await _grain!.AddChildAsync(childId);
    }

    public async Task AddChildAsync(Guid childId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.AddChildAsync(childId);
    }

    public async Task RemoveChildAsync(Guid childId)
    {
        EnsureGrain();
        await _grain!.RemoveChildAsync(childId);
    }

    public async Task RemoveChildAsync(Guid childId, CancellationToken ct)
    {
        EnsureGrain();
        await _grain!.RemoveChildAsync(childId);
    }

    public async Task<Guid?> GetParentAsync()
    {
        EnsureGrain();
        return await _grain!.GetParentAsync();
    }

    public async Task<IReadOnlyList<Guid>> GetChildrenAsync()
    {
        EnsureGrain();
        return await _grain!.GetChildrenAsync();
    }

    #endregion

    #region Helper Methods

    private void EnsureGrain()
    {
        if (_grain == null)
        {
            var agentTypeShortName = GetAgentTypeShortName(_agentTypeName);
            var grainId = $"{agentTypeShortName}:{_id}";
            _grain = _grainFactory.GetGrain<IGAgentGrain>(grainId);
        }
    }

    /// <summary>
    /// Extract short type name from assembly qualified name
    /// Example: "Aevatar.App.Agents.UserQuotaGAgent, Aevatar.App.Agents" -> "UserQuotaGAgent"
    /// </summary>
    private static string GetAgentTypeShortName(string agentTypeName)
    {
        // Extract class name from assembly qualified name
        var fullName = agentTypeName.Split(',')[0].Trim();
        var lastDot = fullName.LastIndexOf('.');
        return lastDot >= 0 ? fullName.Substring(lastDot + 1) : fullName;
    }

    /// <summary>
    /// Get Agent instance - Not available in Orleans mode
    /// Agent runs in Silo, not in Client
    /// </summary>
    public IGAgent GetAgent()
    {
        throw new NotSupportedException(
            "Agent instance is not available on client side. " +
            "In Orleans mode, Agent runs in Silo (Grain). " +
            "Use Grain RPC methods instead.");
    }

    /// <summary>
    /// Invoke RPC method on Agent via Protobuf
    /// </summary>
    /// <param name="requestBytes">RpcRequest serialized bytes</param>
    /// <returns>RpcResponse serialized bytes</returns>
    public async Task<byte[]> InvokeRpcAsync(byte[] requestBytes)
    {
        EnsureGrain();
        return await _grain!.InvokeRpcAsync(requestBytes);
    }

    #endregion
}
