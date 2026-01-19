using System.Linq;
using Aevatar.Agents.Abstractions;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Generic;
using System.Threading.Tasks;
using Google.Protobuf;

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
        
        // ============================================================
        // EARLY FILTER: For LocalHandler (broadcast mode), check if we have
        // a local subscriber BEFORE any heavy processing (deserialization, reflection).
        // This is O(1) lookup - avoids wasting resources on irrelevant messages.
        // ============================================================
        if (_dispatchHandler == DispatchHandler.LocalHandler)
        {
            var streamProvider = _serviceProvider?.GetService<MassTransitMessageStreamProvider>();
            if (streamProvider == null || !streamProvider.HasSubscriber(streamId))
            {
                // No local subscriber - skip immediately without heavy processing
                // Silent return - this is expected in broadcast mode, other instances handle it
                return;
            }
        }
        
        // === Proceed with heavy processing only for relevant messages ===
        
        // Parse the envelope first to extract TraceId
        EventEnvelope envelope;
        try
        {
            envelope = EventEnvelope.Parser.ParseFrom(data);
        }
        catch (System.Exception ex)
        {
            _logger.LogError(ex, "[StreamMessageDispatcher] Failed to parse EventEnvelope for StreamId {StreamId}", streamId);
            throw;
        }
        
        // Extract TraceId from envelope for ES correlation: SessionId_ChatId
        // Use TypeUrl string matching to avoid direct dependency on GodGPT assembly
        string? traceId = null;
        try
        {
            // Check if this is a GodChatStreamEnvelopeProto by TypeUrl
            var typeUrl = envelope.Payload.TypeUrl;
            if (typeUrl.Contains("GodChatStreamEnvelopeProto") || typeUrl.Contains("godchat.stream.GodChatStreamEnvelopeProto"))
            {
                // Use reflection to unpack without direct dependency
                // TypeUrl format: type.googleapis.com/aevatar.agents.godgpt.godchat.stream.GodChatStreamEnvelopeProto
                var messageType = Type.GetType("Aevatar.Agents.GodGPT.Protos.GodChatStream.GodChatStreamEnvelopeProto, Aevatar.Agents.GodGPT");
                if (messageType != null)
                {
                    var parserProperty = messageType.GetProperty("Parser", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (parserProperty != null)
                    {
                        var parser = parserProperty.GetValue(null);
                        var unpackMethod = envelope.Payload.GetType().GetMethod("Unpack", new[] { messageType });
                        if (unpackMethod != null && parser != null)
                        {
                            var streamProto = unpackMethod.Invoke(envelope.Payload, new[] { parser });
                            if (streamProto != null)
                            {
                                var streamIdProp = messageType.GetProperty("StreamId");
                                var chatIdProp = messageType.GetProperty("ChatId");
                                var streamIdValue = streamIdProp?.GetValue(streamProto) as string;
                                var chatIdValue = chatIdProp?.GetValue(streamProto) as string;
                                
                                if (!string.IsNullOrEmpty(streamIdValue) && !string.IsNullOrEmpty(chatIdValue))
                                {
                                    // Format: SessionId_ChatId (SessionId without dashes for shorter format)
                                    var sessionIdGuid = Guid.TryParse(streamIdValue.Replace("\"", ""), out var parsed) ? parsed : Guid.Empty;
                                    traceId = sessionIdGuid != Guid.Empty ? $"{sessionIdGuid:N}_{chatIdValue}" : null;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // Log reflection failure for debugging
            _logger.LogDebug(ex, "[StreamMessageDispatcher] Failed to extract TraceId from envelope - TypeUrl={TypeUrl}, StreamId={StreamId}", 
                envelope.Payload.TypeUrl, streamId);
            // Ignore - TraceId is optional for non-GodChat messages or if reflection fails
        }
        
        var traceIdPrefix = traceId != null ? $"[TraceId={traceId}]" : "";
        
        // Defensive: Strip quotes from StreamId if present (handles edge cases in JSON serialization)
        var originalStreamId = streamId;
        if (!string.IsNullOrEmpty(streamId))
        {
            // Try stripping quotes (both standard and Unicode quotes)
            var stripped = streamId.Trim('"', '\'', '\u201C', '\u201D', ' ', '\t');
            if (stripped.Length != streamId.Length)
            {
                streamId = stripped;
                _logger.LogDebug("[StreamMessageDispatcher]{TraceId} Stripped quotes from StreamId: '{Original}' -> '{Stripped}'",
                    traceIdPrefix, originalStreamId, streamId);
            }
        }
        
        _logger.LogInformation("[StreamMessageDispatcher]{TraceId} Consuming message - StreamId='{StreamId}', DispatchHandler={DispatchHandler}",
            traceIdPrefix, streamId, _dispatchHandler);
        
        // ============================================================
        // Dispatch based on configured handler type
        // ============================================================
        if (_dispatchHandler == DispatchHandler.LocalHandler)
        {
            // LocalHandler: Already confirmed subscriber exists in early filter
            // Dispatch to local memory stream subscribers
            await TryDispatchToLocalStreamAsync(streamId, data, envelope);
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
