using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using MassTransit;
using MassTransit.KafkaIntegration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Provider for MassTransit message streams.
/// </summary>
public class MassTransitMessageStreamProvider : IMessageStreamProvider
{
    private readonly IBus _bus;
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<MassTransitStreamOptions> _options;
    private readonly ConcurrentDictionary<string, MassTransitMessageStream> _streams = new();
    private bool _isWarmedUp;

    public MassTransitMessageStreamProvider(
        IBus bus,
        IServiceProvider serviceProvider,
        IOptions<MassTransitStreamOptions> options)
    {
        _bus = bus;
        _serviceProvider = serviceProvider;
        _options = options;
    }

    /// <summary>
    /// Warm up the Kafka producer connection to avoid cold-start latency.
    /// Call this after MassTransit services are started to pre-establish connections.
    /// </summary>
    public async Task WarmupAsync(CancellationToken ct = default)
    {
        if (_isWarmedUp) return;
        
        var logger = _serviceProvider.GetService<ILogger<MassTransitMessageStreamProvider>>();
        
        try
        {
            if (_options.Value.TransportType == MassTransitTransportType.Kafka)
            {
                // Trigger producer metadata fetch by getting producer instance
                var producerProvider = _serviceProvider.GetService<ITopicProducerProvider>();
                if (producerProvider != null)
                {
                    var topic = _options.Value.TopicPrefix;
                    var producer = producerProvider.GetProducer<string, ByteArrayMessage>(new Uri($"topic:{topic}"));
                    
                    // Send a warmup message (will be filtered out by consumers)
                    var warmupMsg = new ByteArrayMessage
                    {
                        StreamId = string.Empty,  // Special marker for warmup
                        Data = Array.Empty<byte>()
                    };
                    
                    await producer.Produce(string.Empty, warmupMsg, ct);
                    logger?.LogDebug("MassTransit Kafka producer warmed up successfully");
                }
            }
            
            _isWarmedUp = true;
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to warm up MassTransit producer (non-fatal)");
        }
    }

    /// <inheritdoc />
    public IMessageStream GetStream(string agentId)
    {
        return GetStream(agentId, null);
    }

    /// <inheritdoc />
    public IMessageStream GetStream(string agentId, string? category = null)
    {
        // We use GetOrAdd, but we need to make sure if the stream exists, its category is updated or compatible?
        // Actually, StreamId (AgentId) is unique. The category is mainly used for Producing.
        // A stream instance is tied to an AgentId. 
        // If we create it with a category, that category determines where it publishes TO.
        
        return _streams.GetOrAdd(agentId, id => 
            new MassTransitMessageStream(id, category, _bus, _serviceProvider, _options));
    }

    /// <summary>
    /// Internal method to retrieve a stream if it exists locally.
    /// Used by StreamMessageDispatcher.
    /// </summary>
    internal MassTransitMessageStream? GetStreamInternal(string streamId)
    {
        _streams.TryGetValue(streamId, out var stream);
        return stream;
    }

    /// <summary>
    /// Gets all registered stream IDs (for debugging).
    /// </summary>
    internal IEnumerable<string> GetAllStreamIds() => _streams.Keys;
}
