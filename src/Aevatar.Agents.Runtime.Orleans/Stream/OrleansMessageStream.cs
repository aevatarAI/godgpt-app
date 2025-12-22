using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Orleans Stream implementation of IMessageStream.
/// Wraps Orleans IAsyncStream&lt;byte[]&gt; to provide IMessageStream interface.
/// </summary>
public class OrleansMessageStream : IMessageStream
{
    private readonly IAsyncStream<byte[]> _stream;
    private readonly ConcurrentDictionary<Guid, OrleansMessageStreamSubscription> _subscriptions = new();
    
    public string StreamId { get; }

    public OrleansMessageStream(string streamId, IAsyncStream<byte[]> stream)
    {
        StreamId = streamId;
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
    }

    /// <inheritdoc />
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        byte[] bytes;
        
        if (message is EventEnvelope envelope)
        {
            // Serialize EventEnvelope to byte[]
            bytes = envelope.ToByteArray();
        }
        else
        {
            // For other message types, pack into EventEnvelope
            var eventEnvelope = new EventEnvelope
            {
                Id = Guid.NewGuid().ToString(),
                Payload = Google.Protobuf.WellKnownTypes.Any.Pack(message),
                Timestamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.UtcNow),
                CorrelationId = Guid.NewGuid().ToString()
            };
            bytes = eventEnvelope.ToByteArray();
        }
        
        await _stream.OnNextAsync(bytes);
    }

    /// <inheritdoc />
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) where T : IMessage
    {
        return SubscribeAsync(handler, filter: null, ct);
    }
    
    /// <inheritdoc />
    public async Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        var subscriptionId = Guid.NewGuid();
        
        // Create Orleans stream observer
        var observer = new OrleansStreamObserver<T>(
            subscriptionId,
            StreamId,
            handler,
            filter,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        // Subscribe to Orleans stream
        var streamSubscriptionHandle = await _stream.SubscribeAsync(observer);
        
        // Create subscription wrapper with pause/resume control
        var subscription = new OrleansMessageStreamSubscription(
            subscriptionId,
            StreamId,
            streamSubscriptionHandle,
            onPause: () => observer.Pause(),
            onResume: () => observer.Resume(),
            onDisposed: () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        return subscription;
    }
}

/// <summary>
/// Orleans stream observer that handles deserialization and filtering
/// </summary>
internal class OrleansStreamObserver<T> : IAsyncObserver<byte[]> where T : IMessage
{
    private readonly Guid _subscriptionId;
    private readonly string _streamId;
    private readonly Func<T, Task> _handler;
    private readonly Func<T, bool>? _filter;
    private readonly Action _onDisposed;
    private bool _isActive = true;
    
    /// <summary>
    /// Whether the observer is currently processing messages
    /// </summary>
    public bool IsActive => _isActive;

    public OrleansStreamObserver(
        Guid subscriptionId,
        string streamId,
        Func<T, Task> handler,
        Func<T, bool>? filter,
        Action onDisposed)
    {
        _subscriptionId = subscriptionId;
        _streamId = streamId;
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _filter = filter;
        _onDisposed = onDisposed;
    }
    
    /// <summary>
    /// Pause message processing (messages will be ignored until Resume is called)
    /// </summary>
    public void Pause() => _isActive = false;
    
    /// <summary>
    /// Resume message processing
    /// </summary>
    public void Resume() => _isActive = true;

    public async Task OnNextAsync(byte[] item, StreamSequenceToken? token = null)
    {
        if (!_isActive)
        {
            return;
        }

        try
        {
            // Deserialize EventEnvelope
            var envelope = EventEnvelope.Parser.ParseFrom(item);
            
            // Handle EventEnvelope directly
            if (typeof(T) == typeof(EventEnvelope))
            {
                var typedMessage = (T)(object)envelope;
                if (_filter != null && !_filter(typedMessage))
                {
                    return;
                }
                await _handler(typedMessage);
                return;
            }
            
            // Extract payload from EventEnvelope
            if (envelope.Payload != null)
            {
                try
                {
                    // Try to unpack the payload
                    var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                        .GetMethod("Unpack", Type.EmptyTypes)
                        ?.MakeGenericMethod(typeof(T));

                    if (unpackMethod != null)
                    {
                        var message = (T)unpackMethod.Invoke(envelope.Payload, null)!;
                        if (_filter != null && !_filter(message))
                        {
                            return;
                        }
                        await _handler(message);
                    }
                }
                catch (Exception)
                {
                    // Ignore type mismatch - this observer might not be interested in this message type
                }
            }
        }
        catch (Exception ex)
        {
            // Log error but don't throw - Orleans stream will handle retries
            // TODO: Add logging if needed
            System.Diagnostics.Debug.WriteLine($"Error in OrleansStreamObserver: {ex.Message}");
        }
    }

    public Task OnCompletedAsync()
    {
        _isActive = false;
        _onDisposed?.Invoke();
        return Task.CompletedTask;
    }

    public Task OnErrorAsync(Exception ex)
    {
        _isActive = false;
        _onDisposed?.Invoke();
        return Task.CompletedTask;
    }
}
