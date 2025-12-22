using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// MassTransit consumer that dispatches incoming messages to agents.
/// 
/// Key design: Uses IMassTransitEventHandler to properly dispatch events to actors.
/// This ensures callbacks run in the correct actor context (e.g., Grain turn for Orleans).
/// </summary>
public class StreamMessageDispatcher : IConsumer<ByteArrayMessage>
{
    private readonly IEnumerable<IMassTransitEventHandler> _eventHandlers;
    private readonly IEnumerable<IStreamNotFoundHandler> _notFoundHandlers;
    private readonly ILogger<StreamMessageDispatcher> _logger;

    public StreamMessageDispatcher(
        IEnumerable<IMassTransitEventHandler> eventHandlers,
        IEnumerable<IStreamNotFoundHandler> notFoundHandlers,
        ILogger<StreamMessageDispatcher> logger)
    {
        _eventHandlers = eventHandlers;
        _notFoundHandlers = notFoundHandlers;
        _logger = logger;
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
        
        _logger.LogDebug("Received message for StreamId {StreamId}", streamId);
        
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
        
        // Try event handlers first (they route to the correct actor context)
        bool handled = false;
        foreach (var handler in _eventHandlers)
        {
            try
            {
                handled = await handler.HandleEventAsync(streamId, envelope);
                if (handled)
                {
                    _logger.LogDebug("Event {EventId} handled by {HandlerType} for StreamId {StreamId}", 
                        envelope.Id, handler.GetType().Name, streamId);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                _logger.LogWarning(ex, "Event handler {HandlerType} failed for StreamId {StreamId}", 
                    handler.GetType().Name, streamId);
            }
        }
        
        // If no handler could route it, try stream not found handlers (activate actor)
        if (!handled)
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
                    handled = await handler.HandleEventAsync(streamId, envelope);
                    if (handled)
                    {
                        _logger.LogDebug("Event {EventId} handled after activation for StreamId {StreamId}", 
                            envelope.Id, streamId);
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.LogWarning(ex, "Event handler failed after activation for StreamId {StreamId}", 
                        handler.GetType().Name);
                }
            }
        }
        
        // If still not handled, throw to trigger MassTransit retry
        if (!handled)
        {
            _logger.LogWarning("No handler could process event for StreamId {StreamId}. Throwing to trigger retry.", streamId);
            throw new System.InvalidOperationException($"No handler for StreamId {streamId}. Actor might be failing to activate.");
        }
    }
}
