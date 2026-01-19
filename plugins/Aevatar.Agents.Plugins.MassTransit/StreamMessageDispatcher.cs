using System.Linq;
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
        
        // DIAGNOSTIC: Check if StreamId contains unexpected quotes from JSON serialization
        var originalStreamId = streamId;
        var streamIdLength = streamId?.Length ?? 0;
        
        // Log first and last character ASCII codes for precise diagnosis
        var firstCharCode = streamIdLength > 0 ? (int)streamId[0] : -1;
        var lastCharCode = streamIdLength > 1 ? (int)streamId[streamIdLength - 1] : -1;
        _logger.LogDebug("[StreamMessageDispatcher] StreamId analysis - Length={Length}, FirstChar='{First}' (0x{FirstHex:X2}), LastChar='{Last}' (0x{LastHex:X2})",
            streamIdLength, 
            streamIdLength > 0 ? streamId[0] : '?', firstCharCode, 
            streamIdLength > 1 ? streamId[streamIdLength - 1] : '?', lastCharCode);
        
        var hasLeadingQuote = streamIdLength > 0 && (streamId[0] == '"' || streamId[0] == '\'' || streamId[0] == '\u201C' || streamId[0] == '\u201D');
        var hasTrailingQuote = streamIdLength > 1 && (streamId[streamIdLength - 1] == '"' || streamId[streamIdLength - 1] == '\'' || streamId[streamIdLength - 1] == '\u201C' || streamId[streamIdLength - 1] == '\u201D');
        
        if (hasLeadingQuote && hasTrailingQuote && streamIdLength > 2)
        {
            // Strip JSON-encoded quotes (including Unicode quotes)
            streamId = streamId[1..^1];
            _logger.LogWarning("[StreamMessageDispatcher] StreamId had quotes, stripped: Original='{Original}' (len={OrigLen}) -> Stripped='{Stripped}' (len={StrippedLen})",
                originalStreamId, originalStreamId?.Length ?? 0, streamId, streamId?.Length ?? 0);
        }
        else if (streamId?.Contains('"') == true || streamId?.Contains('\'') == true || 
                 streamId?.Contains('\u201C') == true || streamId?.Contains('\u201D') == true)
        {
            // StreamId contains quotes but not at start/end - log for investigation
            _logger.LogWarning("[StreamMessageDispatcher] StreamId contains quotes but not at boundaries: StreamId='{StreamId}', Length={Length}, FirstChar='{First}' (0x{FirstHex:X2}), LastChar='{Last}' (0x{LastHex:X2})",
                streamId, streamIdLength, 
                streamIdLength > 0 ? streamId[0] : '?', firstCharCode,
                streamIdLength > 1 ? streamId[streamIdLength - 1] : '?', lastCharCode);
        }
        
        // CRITICAL: Always try to strip quotes if StreamId length suggests it might have quotes
        // GUID format is exactly 36 characters. If we have 36 but it looks quoted, try stripping anyway
        if (streamIdLength == 36 && (hasLeadingQuote || hasTrailingQuote))
        {
            // This is suspicious - GUID should be 36 chars, but if it has quotes it should be 38
            // Try aggressive quote stripping anyway
            var stripped = streamId.Trim('"', '\'', '\u201C', '\u201D', ' ', '\t');
            if (stripped.Length < streamIdLength)
            {
                _logger.LogWarning("[StreamMessageDispatcher] Aggressive quote stripping: '{Original}' (len={OrigLen}) -> '{Stripped}' (len={StrippedLen})",
                    streamId, streamIdLength, stripped, stripped.Length);
                streamId = stripped;
            }
        }
        
        _logger.LogInformation("[StreamMessageDispatcher] Consuming message - StreamId='{StreamId}', StreamIdLength={Length}, OriginalLength={OriginalLength}, DispatchHandler={DispatchHandler}",
            streamId, streamId?.Length ?? 0, originalStreamId?.Length ?? 0, _dispatchHandler);
        
        // Log raw bytes to detect hidden characters or encoding issues
        if (!string.IsNullOrEmpty(streamId))
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(streamId);
            var hex = string.Join(" ", bytes.Take(50).Select(b => b.ToString("X2")));
            _logger.LogDebug("[StreamMessageDispatcher] StreamId raw bytes (first 50): {Hex}", hex);
        }
        
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
