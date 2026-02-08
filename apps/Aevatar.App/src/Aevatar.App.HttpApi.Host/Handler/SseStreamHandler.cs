using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.GodGPT.Protos.GodChatStream;
using Aevatar.App.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Aevatar.App.HttpApi.Host.Handler;

/// <summary>
/// Handles SSE stream subscription and response writing for chat endpoints.
/// Encapsulates the common stream handling logic used by authenticated, guest, and voice chat.
/// </summary>
public class SseStreamHandler
{
    private readonly HttpContext _context;
    private readonly ILogger _logger;
    private readonly string _handlerName;
    private readonly string _sessionId;
    private readonly string _chatId;
    private readonly Stopwatch _stopwatch;
    private readonly CancellationToken _cancellationToken;
    
    private bool _firstFlag;
    private bool _ifLastChunk;
    private bool _clientDisconnected;
    private IMessageStreamSubscription? _messageSubscription;
    private TaskCompletionSource? _exitSignal;

    private readonly IMetricsRecorder _metricsRecorder;

    public bool IsLastChunkReceived => _ifLastChunk;
    public bool IsClientDisconnected => _clientDisconnected;

    public SseStreamHandler(
        HttpContext context,
        ILogger logger,
        string handlerName,
        string sessionId,
        string chatId,
        IMetricsRecorder metricsRecorder,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        _context = context;
        _logger = logger;
        _handlerName = handlerName;
        _sessionId = sessionId;
        _chatId = chatId;
        _metricsRecorder = metricsRecorder;
        _stopwatch = stopwatch;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Sets up SSE response headers.
    /// </summary>
    public void SetupSseHeaders()
    {
        _context.Response.ContentType = "text/event-stream";
        _context.Response.Headers.Connection = "keep-alive";
        _context.Response.Headers.CacheControl = "no-cache";
    }

    /// <summary>
    /// Subscribes to the message stream and handles incoming messages.
    /// </summary>
    public async Task<TaskCompletionSource> SubscribeAsync(IMessageStream messageStream)
    {
        _exitSignal = new TaskCompletionSource();
        
        _messageSubscription = await messageStream.SubscribeAsync<EventEnvelope>(async (envelope) =>
        {
            if (_clientDisconnected || _cancellationToken.IsCancellationRequested)
            {
                return;
            }
            
            try
            {
                if (!envelope.Payload.Is(GodChatStreamEnvelopeProto.Descriptor))
                {
                    _logger.LogDebug(
                        "[ChatMiddleware][{Handler}] Ignored payload: TypeUrl={TypeUrl}, SessionId={SessionId}, ChatId={ChatId}",
                        _handlerName, envelope.Payload.TypeUrl, _sessionId, _chatId);
                    return;
                }

                var streamProto = envelope.Payload.Unpack<GodChatStreamEnvelopeProto>();
                if (streamProto.ChatId != _chatId)
                {
                    return;
                }

                // Log payload type for voice chat debugging
                _logger.LogInformation(
                    "[ChatMiddleware][{Handler}] Received payload: PayloadCase={PayloadCase}, SessionId={SessionId}, ChatId={ChatId}, Seq={Seq}",
                    _handlerName, streamProto.PayloadCase, _sessionId, _chatId, streamProto.Seq);

                var httpResponse = ChatMiddlewareHelper.MapEnvelopeToHttpResponse(streamProto);
                
                // Log audio data presence for voice chat debugging
                if (streamProto.PayloadCase == GodChatStreamEnvelopeProto.PayloadOneofCase.Audio)
                {
                    _logger.LogInformation(
                        "[ChatMiddleware][{Handler}] Audio chunk received: AudioDataLen={AudioDataLen}, HasMetadataJson={HasMetadataJson}, HttpResponseAudioDataLen={HttpAudioLen}, HasHttpMetadata={HasHttpMeta}",
                        _handlerName, 
                        streamProto.Audio?.AudioData?.Length ?? 0,
                        !string.IsNullOrEmpty(streamProto.Audio?.AudioMetadataJson),
                        httpResponse.AudioData?.Length ?? 0,
                        httpResponse.AudioMetadata != null);
                }

                if (_clientDisconnected || _cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                if (!_firstFlag)
                {
                    await _context.Response.StartAsync(_cancellationToken);
                    _firstFlag = true;
                    _logger.LogInformation(
                        "[ChatMiddleware][{Handler}] Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                        _handlerName, _sessionId, _stopwatch.ElapsedMilliseconds);
                    
                    _metricsRecorder.Record($"{_handlerName}_first_response", _stopwatch);
                }

                var responseData = $"data: {JsonConvert.SerializeObject(httpResponse)}\n\n";
                await _context.Response.WriteAsync(responseData, _cancellationToken);
                await _context.Response.Body.FlushAsync(_cancellationToken);

                if (httpResponse.IsLastChunk)
                {
                    await _context.Response.WriteAsync("event: completed\n", _cancellationToken);
                    _context.Response.Body.Close();
                    _ifLastChunk = true;
                    _exitSignal.TrySetResult();
                    if (_messageSubscription != null)
                    {
                        await _messageSubscription.UnsubscribeAsync();
                    }
                    _metricsRecorder.Record(_handlerName, _stopwatch);
                }
            }
            catch (OperationCanceledException)
            {
                _clientDisconnected = true;
                _logger.LogDebug(
                    "[ChatMiddleware][{Handler}] Client disconnected (OperationCanceledException): SessionId={SessionId}, ChatId={ChatId}",
                    _handlerName, _sessionId, _chatId);
                _exitSignal.TrySetResult();
            }
            catch (ObjectDisposedException)
            {
                _clientDisconnected = true;
                _logger.LogDebug(
                    "[ChatMiddleware][{Handler}] HttpContext disposed (client disconnected): SessionId={SessionId}, ChatId={ChatId}",
                    _handlerName, _sessionId, _chatId);
                _exitSignal.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ChatMiddleware][{Handler}] Error processing message: SessionId={SessionId}, ChatId={ChatId}",
                    _handlerName, _sessionId, _chatId);
                _exitSignal.TrySetException(ex);
            }
        });

        return _exitSignal;
    }

    /// <summary>
    /// Waits for the stream to complete or client to disconnect.
    /// </summary>
    public async Task WaitForCompletionAsync()
    {
        if (_exitSignal == null) return;
        
        try
        {
            await _exitSignal.Task.WaitAsync(_cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _clientDisconnected = true;
            _logger.LogDebug(
                "[ChatMiddleware][{Handler}] Client disconnected while waiting: SessionId={SessionId}, ChatId={ChatId}",
                _handlerName, _sessionId, _chatId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, 
                "[ChatMiddleware][{Handler}] Unexpected error while waiting: SessionId={SessionId}, ChatId={ChatId}",
                _handlerName, _sessionId, _chatId);
        }
    }

    /// <summary>
    /// Cleans up resources and unsubscribes from the stream.
    /// </summary>
    public async Task CleanupAsync()
    {
        _clientDisconnected = true;
        if (_messageSubscription != null)
        {
            try
            {
                await _messageSubscription.UnsubscribeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, 
                    "[ChatMiddleware][{Handler}] Error unsubscribing: SessionId={SessionId}",
                    _handlerName, _sessionId);
            }
        }
    }

    /// <summary>
    /// Logs completion status.
    /// </summary>
    public void LogCompletion()
    {
        if (!_ifLastChunk && !_clientDisconnected)
        {
            _logger.LogDebug("[ChatMiddleware][{Handler}] No LastChunk: SessionId={SessionId}, ChatId={ChatId}",
                _handlerName, _sessionId, _chatId);
        }

        _logger.LogDebug("[ChatMiddleware][{Handler}] complete done SessionId={SessionId}, ClientDisconnected={ClientDisconnected}", 
            _handlerName, _sessionId, _clientDisconnected);
    }
}

