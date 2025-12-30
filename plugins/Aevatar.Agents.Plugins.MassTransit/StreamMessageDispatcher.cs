using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit consumer that dispatches incoming messages to agents.
/// 
/// Key design: Uses IMassTransitEventHandler to properly dispatch events to actors.
/// This ensures callbacks run in the correct actor context (e.g., Grain turn for Orleans).
/// 
/// Dispatch modes (configurable via Consumer.DispatchMode):
/// - GrainOnly: Only dispatch to Grain handlers (Silo, best performance)
/// - LocalStreamOnly: Only dispatch to local memory streams (HttpApi Client)
/// - Both: Dispatch to both (for debugging)
/// </summary>
public class StreamMessageDispatcher : IConsumer<ByteArrayMessage>
{
    private readonly IEnumerable<IMassTransitEventHandler> _eventHandlers;
    private readonly IEnumerable<IStreamNotFoundHandler> _notFoundHandlers;
    private readonly ILogger<StreamMessageDispatcher> _logger;
    private readonly IServiceProvider? _serviceProvider;
    private readonly DispatchHandler _dispatchHandler;

    public StreamMessageDispatcher(
        IEnumerable<IMassTransitEventHandler> eventHandlers,
        IEnumerable<IStreamNotFoundHandler> notFoundHandlers,
        ILogger<StreamMessageDispatcher> logger,
        IOptions<MassTransitStreamOptions>? options = null,
        IServiceProvider? serviceProvider = null)
    {
        _eventHandlers = eventHandlers;
        _notFoundHandlers = notFoundHandlers;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _dispatchHandler = options?.Value?.Consumer?.DispatchHandler ?? DispatchHandler.GrainHandler;
        
        _logger.LogInformation("StreamMessageDispatcher initialized with DispatchHandler: {DispatchHandler}", _dispatchHandler);
    }

    public async Task Consume(ConsumeContext<ByteArrayMessage> context)
    {
        var streamId = context.Message.StreamId;
        var data = context.Message.Data;
        
        // Skip warmup messages (used for pre-establishing Kafka connections)
        if (string.IsNullOrEmpty(streamId))
        {
            _logger.LogDebug("Skipping warmup message");
            return;
        }
        
        _logger.LogInformation("Received message for StreamId {StreamId}, DispatchHandler: {DispatchHandler}", streamId, _dispatchHandler);
        
        // Parse the envelope first
        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Parser.ParseFrom(data);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "Failed to parse EventEnvelope for StreamId {StreamId}", streamId);
            throw;
        }
        
        // ============================================================
        // Dispatch based on configured handler type
        // ============================================================
        if (_dispatchHandler == DispatchHandler.LocalHandler)
        {
            // LocalHandler: Dispatch to local memory stream subscribers (HttpApi Client)
            var dispatched = await TryDispatchToLocalStreamAsync(streamId, data, envelope);
            if (!dispatched)
            {
                _logger.LogDebug("LocalHandler: No local subscribers for StreamId {StreamId}", streamId);
            }
            return;
        }
        
        // GrainHandler: Dispatch to Grain handlers (Silo)
        var handled = await TryDispatchToGrainHandlersAsync(streamId, envelope);
        if (handled)
        {
            return;
        }
        
        // Try activation handlers and retry
        handled = await TryActivateAndRetryAsync(streamId, envelope);
        if (handled)
        {
            return;
        }
        
        // If still not handled, throw to trigger MassTransit retry
        _logger.LogWarning("GrainHandler: No handler for StreamId {StreamId}. Throwing to trigger retry.", streamId);
        throw new System.InvalidOperationException($"No handler for StreamId {streamId}. Actor might be failing to activate.");
    }
    
    /// <summary>
    /// Dispatch to local memory stream subscribers (e.g., ChatMiddleware in HttpApi)
    /// </summary>
    private async Task<bool> TryDispatchToLocalStreamAsync(string streamId, byte[] data, EventEnvelope envelope)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        if (_serviceProvider == null)
        {
            _logger.LogWarning("LocalHandler: ServiceProvider is null for StreamId {StreamId}", streamId);
            return false;
        }
        
        try
        {
            var streamProvider = _serviceProvider.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider == null)
            {
                _logger.LogWarning("LocalHandler: MassTransitMessageStreamProvider not found for StreamId {StreamId}", streamId);
                return false;
            }
            
            var getStreamMs = sw.ElapsedMilliseconds;
            var localStream = streamProvider.GetStreamInternal(streamId);
            if (localStream != null)
            {
                var handlerCount = localStream.GetHandlerCount();
                _logger.LogInformation("LocalHandler: Found local stream for StreamId {StreamId} with {HandlerCount} handlers, GetStreamMs={GetStreamMs}ms", 
                    streamId, handlerCount, getStreamMs);
                    
                if (handlerCount == 0)
                {
                    _logger.LogWarning("LocalHandler: Local stream found but no handlers registered for StreamId {StreamId}", streamId);
                    return false;
                }
                
                var dispatchStartMs = sw.ElapsedMilliseconds;
                await localStream.DispatchAsync(data);
                var dispatchEndMs = sw.ElapsedMilliseconds;
                _logger.LogInformation("Event {EventId} dispatched to local stream subscribers for StreamId {StreamId}, DispatchMs={DispatchMs}ms, TotalMs={TotalMs}ms", 
                    envelope.Id, streamId, dispatchEndMs - dispatchStartMs, dispatchEndMs);
                return true;
            }
            else
            {
                // Log all registered stream IDs to help diagnose mismatch
                var registeredIds = streamProvider.GetAllStreamIds();
                _logger.LogWarning("LocalHandler: No local stream found for StreamId '{StreamId}'. Registered streams: [{RegisteredIds}]", 
                    streamId, string.Join(", ", registeredIds));
            }
        }
        catch (System.Exception ex)
        {
            _logger.LogWarning(ex, "Failed to dispatch to local stream for StreamId {StreamId}", streamId);
        }
        
        return false;
    }
    
    /// <summary>
    /// Dispatch to Grain handlers (Orleans/ProtoActor actors)
    /// </summary>
    private async Task<bool> TryDispatchToGrainHandlersAsync(string streamId, EventEnvelope envelope)
    {
        foreach (var handler in _eventHandlers)
        {
            try
            {
                var handled = await handler.HandleEventAsync(streamId, envelope);
                if (handled)
                {
                    _logger.LogDebug("Event {EventId} handled by {HandlerType} for StreamId {StreamId}", 
                        envelope.Id, handler.GetType().Name, streamId);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Event handler {HandlerType} failed for StreamId {StreamId}", 
                    handler.GetType().Name, streamId);
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Try stream not found handlers (activate actor) and retry dispatch
    /// </summary>
    private async Task<bool> TryActivateAndRetryAsync(string streamId, EventEnvelope envelope)
    {
        _logger.LogDebug("No event handler found for StreamId {StreamId}, trying activation handlers...", streamId);
        
        foreach (var notFoundHandler in _notFoundHandlers)
        {
            try
            {
                await notFoundHandler.HandleStreamNotFoundAsync(streamId);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "StreamNotFoundHandler failed for StreamId {StreamId}", streamId);
            }
        }
        
        // Retry with event handlers after activation
        foreach (var handler in _eventHandlers)
        {
            try
            {
                var handled = await handler.HandleEventAsync(streamId, envelope);
                if (handled)
                {
                    _logger.LogDebug("Event {EventId} handled after activation for StreamId {StreamId}", 
                        envelope.Id, streamId);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Event handler failed after activation for StreamId {StreamId}", 
                    handler.GetType().Name);
            }
        }
        
        return false;
    }
}
