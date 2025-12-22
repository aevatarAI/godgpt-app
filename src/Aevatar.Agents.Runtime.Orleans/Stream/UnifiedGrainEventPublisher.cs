using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Unified Event Publisher for Orleans Grain.
/// Uses IMessageStream interface, agnostic to underlying implementation (Orleans or MassTransit).
/// </summary>
internal class UnifiedGrainEventPublisher : IEventPublisher
{
    private readonly IMessageStream? _stream;
    private readonly Func<string> _getGrainId;
    private readonly ILogger _logger;
    private readonly IGrainFactory _grainFactory;

    public UnifiedGrainEventPublisher(
        IMessageStream? stream,
        Func<string> getGrainId,
        ILogger logger,
        IGrainFactory grainFactory)
    {
        _stream = stream;
        _getGrainId = getGrainId;
        _logger = logger;
        _grainFactory = grainFactory;
    }

    public async Task<string> PublishEventAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var grainId = _getGrainId();

        // Create EventEnvelope
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = grainId, // Always set for internal Grain publishing
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = direction,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString()
        };

        _logger.LogDebug("Grain {GrainId} publishing event {EventId} with direction {Direction}",
            grainId, envelope.Id, direction);

        if (_stream != null)
        {
            await _stream.ProduceAsync(envelope, ct);
        }
        else
        {
            _logger.LogWarning("Stream not available for Grain {GrainId}, event {EventId} not published",
                grainId, envelope.Id);
        }

        return envelope.Id;
    }

    public async Task<string> SendToAsync<TEvent>(
        string targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        var grainId = _getGrainId();

        // Create EventEnvelope for P2P
        var envelope = new EventEnvelope
        {
            Id = Guid.NewGuid().ToString(),
            PublisherId = grainId,
            Payload = Google.Protobuf.WellKnownTypes.Any.Pack(evt),
            Direction = onArrivalDirection,
            Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
            CorrelationId = Guid.NewGuid().ToString(),
            TargetAgentId = targetAgentId,
            OnArrivalDirection = onArrivalDirection
        };

        _logger.LogDebug(
            "Grain {GrainId} sending P2P event {EventId} to {TargetAgentId}, onArrival={OnArrivalDirection}",
            grainId, envelope.Id, targetAgentId, onArrivalDirection);

        // Serialize envelope
        using var memStream = new MemoryStream();
        using var codedOutput = new CodedOutputStream(memStream);
        envelope.WriteTo(codedOutput);
        codedOutput.Flush();
        var envelopeBytes = memStream.ToArray();

        // Direct RPC to target Grain (no stream broadcast)
        var targetGrain = _grainFactory.GetGrain<IGAgentGrain>(targetAgentId);
        await targetGrain.HandleEventAsync(envelopeBytes);

        return envelope.Id;
    }
}

