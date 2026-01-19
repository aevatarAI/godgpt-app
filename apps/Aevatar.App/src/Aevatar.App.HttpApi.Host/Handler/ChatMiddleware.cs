using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aevatar.Anonymous;
using Aevatar.Application.Constants;
using Aevatar.App.Domain.Shared;
using Aevatar.App.HttpApi.Extensions;
using Aevatar.App.Application.Contracts.Services;
using Aevatar.App.Application.Services;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Context;
using Aevatar.Agents.Abstractions.Extensions;
using Aevatar.Agents.Core.Context;
using Aevatar.Application.Grains.Agents.Anonymous;
using Aevatar.Application.Grains.Agents.ChatManager;
using Aevatar.Application.Grains.Agents.ChatManager.Chat;
using Aevatar.Application.Grains.Agents.ChatManager.Common;
using Aevatar.Application.Grains.GodChat.Dtos;
using Aevatar.Application.Grains.ChatManager.UserQuota;
using Aevatar.Quantum;
using GodGPT.GAgents.SpeechChat;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Orleans;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using Google.Protobuf.WellKnownTypes;

namespace Aevatar.App.HttpApi.Host.Handler;

/// <summary>
/// Middleware for handling streaming chat requests.
/// Supports authenticated chat, guest chat, and voice chat with SSE responses.
/// </summary>
public class ChatMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ChatMiddleware> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IIpLocationService _ipLocationService;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IMessageStreamProvider? _messageStreamProvider;
    private readonly IAgentContextAccessor _agentContextAccessor;

    public ChatMiddleware(
        RequestDelegate next,
        ILogger<ChatMiddleware> logger,
        IClusterClient clusterClient,
        ILocalizationService localizationService,
        IIpLocationService ipLocationService,
        IGAgentActorFactory actorFactory,
        IAgentContextAccessor agentContextAccessor,
        IMessageStreamProvider? messageStreamProvider = null)
    {
        _next = next;
        _logger = logger;
        _localizationService = localizationService;
        _ipLocationService = ipLocationService;
        _actorFactory = actorFactory;
        _agentContextAccessor = agentContextAccessor;
        _messageStreamProvider = messageStreamProvider;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        var pathBase = context.Request.PathBase.Value ?? "";
        var fullPath = pathBase + path;

        if (pathBase == "/api/gotgpt/chat" || fullPath.Contains("/api/gotgpt/chat"))
        {
            await HandleAuthenticatedChatAsync(context);
        }
        else if (pathBase == "/api/godgpt/voice/chat" || fullPath.Contains("/api/godgpt/voice/chat"))
        {
            await HandleVoiceChatAsync(context);
        }
        else if (pathBase == "/api/godgpt/guest/chat" || fullPath.Contains("/api/godgpt/guest/chat"))
        {
            await HandleGuestChatAsync(context);
        }
        else
        {
            await _next(context);
        }
    }

    private async Task HandleAuthenticatedChatAsync(HttpContext context)
    {
        var language = context.GetGodGPTLanguage();

        // Validate authentication
        if (!TryGetAuthenticatedUserId(context, language, out var userId))
            return;

        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonConvert.DeserializeObject<QuantumChatRequestDto>(body);
        if (request == null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Invalid request body");
            return;
        }

        var clientIp = context.GetClientIpAddress();
        var appType = context.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
        request.Region = ChatMiddlewareHelper.ResolveRegion(request.Region, isCN);

        // Set agent context
        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(AgentContextKeys.IsCN, isCN);
        agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());

        // Validate images
        if (request.Images != null && request.Images.Count > ChatMiddlewareHelper.MaxImageCount)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            var parameters = new Dictionary<string, string> { ["TooManyFiles"] = ChatMiddlewareHelper.MaxImageCount.ToString() };
            await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.TooManyFiles, language, parameters));
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            
            // Try to get CorrelationId from HttpContext (set by UseCorrelationId middleware)
            // Fallback to HttpContext.TraceIdentifier if CorrelationId is not available
            var correlationIdHeader = context.Request.Headers["X-Correlation-ID"].ToString();
            var correlationId = !string.IsNullOrEmpty(correlationIdHeader) 
                               ? correlationIdHeader 
                               : context.TraceIdentifier;

            // Validate session
            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId.ToString());
            var manager = managerActor.As<IChatManagerGAgent>();
            if (!await manager.IsUserSessionAsync(request.SessionId))
            {
                await WriteSessionError(context, request.SessionId, language);
                return;
            }

            // Get message stream
            var sessionIdStr = request.SessionId.ToString();
            var chatId = Guid.NewGuid().ToString();
            
            // Generate TraceId: CorrelationId_SessionId_ChatId (for ES query correlation)
            // Use CorrelationId from middleware if available, otherwise use SessionId_ChatId
            var traceId = !string.IsNullOrEmpty(correlationId) && correlationId != context.TraceIdentifier
                ? $"{correlationId}_{request.SessionId:N}_{chatId}"
                : $"{request.SessionId:N}_{chatId}";
            
            _logger.LogInformation("[ChatMiddleware][TraceId={TraceId}] Getting message stream - SessionId={SessionId}, SessionIdString='{SessionIdString}', Length={Length}, CorrelationId={CorrelationId}",
                traceId, request.SessionId, sessionIdStr, sessionIdStr.Length, correlationId);
            
            var messageStream = GetMessageStream(sessionIdStr);
            if (messageStream == null)
            {
                await WriteStreamNotAvailableError(context, sessionIdStr);
                return;
            }
            
            _logger.LogInformation("[ChatMiddleware][TraceId={TraceId}] Message stream obtained - SessionId={SessionId}, StreamId={StreamId}",
                traceId, request.SessionId, messageStream.StreamId);

            var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(request.SessionId.ToString());
            var godChat = godChatActor.As<IGodChat>();

            // Build proto input
            var protoInput = BuildStartStreamChatInput(request, chatId);

            // Setup SSE handler and subscribe
            var sseHandler = new SseStreamHandler(context, _logger, "HandleAuthenticatedChatAsync", 
                request.SessionId.ToString(), chatId, stopwatch, context.RequestAborted);
            sseHandler.SetupSseHeaders();
            
            _logger.LogInformation("[ChatMiddleware][TraceId={TraceId}] STEP1 - Subscribing to stream: SessionId={SessionId}, ChatId={ChatId}, ElapsedMs={ElapsedMs}ms",
                traceId, request.SessionId, chatId, stopwatch.ElapsedMilliseconds);
            
            var exitSignal = await sseHandler.SubscribeAsync(messageStream);
            
            _logger.LogInformation("[ChatMiddleware][TraceId={TraceId}] STEP2 - Subscription done, calling StartStreamChatAsync: SessionId={SessionId}, ChatId={ChatId}, ElapsedMs={ElapsedMs}ms",
                traceId, request.SessionId, chatId, stopwatch.ElapsedMilliseconds);
            
            // Trigger chat - this should return quickly (fire-and-forget for HTTP requests)
            await godChat.StartStreamChatAsync(protoInput);
            
            _logger.LogInformation("[ChatMiddleware][TraceId={TraceId}] STEP3 - StartStreamChatAsync returned, waiting for stream: SessionId={SessionId}, ChatId={ChatId}, ElapsedMs={ElapsedMs}ms",
                traceId, request.SessionId, chatId, stopwatch.ElapsedMilliseconds);
            
            // Wait and cleanup
            await sseHandler.WaitForCompletionAsync();
            await sseHandler.CleanupAsync();
            sseHandler.LogCompletion();
        }
        catch (InvalidOperationException e)
        {
            await HandleBusinessException(context, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatMiddleware][HandleAuthenticatedChatAsync] Error: {Error}", ex.Message);
        }
    }

    private async Task HandleGuestChatAsync(HttpContext context)
    {
        var clientIp = context.GetClientIpAddress();
        var userHashId = CommonHelper.GetAnonymousUserGAgentId(clientIp).Replace("AnonymousUser_", "");
        var language = context.GetGodGPTLanguage();

        try
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<GuestChatRequestDto>(body);
            var appType = context.GetGodGPTAppType();
            var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
            
            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidRequestBody, language));
                return;
            }

            request.Region = ChatMiddlewareHelper.ResolveRegion(request.Region, isCN);

            var agentContext = _agentContextAccessor.GetOrCreate();
            agentContext.Set(AgentContextKeys.IsCN, isCN);
            agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
            var stopwatch = Stopwatch.StartNew();

            // Get anonymous user
            var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
            var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
            var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
            
            if (!await anonymousUserGrain.CanChatAsync())
            {
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.DailyChatLimitExceeded, language));
                return;
            }

            var sessionInfo = await anonymousUserGrain.GetCurrentSessionAsync();
            if (sessionInfo == null)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.NoActiveGuestSession, language));
                return;
            }

            var chatId = Guid.NewGuid().ToString();
            var sessionId = sessionInfo.SessionId;

            var messageStream = GetMessageStream(sessionId);
            if (messageStream == null)
            {
                await WriteStreamNotAvailableError(context, sessionId);
                return;
            }

            var sseHandler = new SseStreamHandler(context, _logger, "HandleGuestChatAsync", 
                sessionId, chatId, stopwatch, context.RequestAborted);
            sseHandler.SetupSseHeaders();
            
            await sseHandler.SubscribeAsync(messageStream);
            await anonymousUserGrain.GuestChatAsync(request.Content, chatId);
            
            await sseHandler.WaitForCompletionAsync();
            await sseHandler.CleanupAsync();
            sseHandler.LogCompletion();
        }
        catch (InvalidOperationException ex)
        {
            await HandleGuestBusinessException(context, ex, userHashId, language);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatMiddleware][HandleGuestChatAsync] Error for user: {UserHashId}", userHashId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language));
        }
    }

    private async Task HandleVoiceChatAsync(HttpContext context)
    {
        var language = context.GetGodGPTLanguage();
        
        if (!TryGetAuthenticatedUserId(context, language, out var userId))
            return;

        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonConvert.DeserializeObject<VoiceChatRequestDto>(body);
        var clientIp = context.GetClientIpAddress();
        var appType = context.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());

        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidRequestBody, language));
            return;
        }

        request.Region = ChatMiddlewareHelper.ResolveRegion(request.Region, isCN);

        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(AgentContextKeys.IsCN, isCN);
        agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());

        if (request.VoiceLanguage == VoiceLanguageEnum.Unset)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnsetLanguage, language));
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();

            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId.ToString());
            var manager = managerActor.As<IChatManagerGAgent>();
            if (!await manager.IsUserSessionAsync(request.SessionId))
            {
                await WriteSessionError(context, request.SessionId, language);
                return;
            }

            if (context.RequestAborted.IsCancellationRequested) return;

            var messageStream = GetMessageStream(request.SessionId.ToString());
            if (messageStream == null)
            {
                await WriteStreamNotAvailableError(context, request.SessionId.ToString());
                return;
            }

            var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(request.SessionId.ToString());
            var godChat = godChatActor.As<IGodChat>();
            var chatId = Guid.NewGuid().ToString();

            // Voice chat timeout
            var voiceChatCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, voiceChatCts.Token);

            try
            {
                var sseHandler = new SseStreamHandler(context, _logger, "HandleVoiceChatAsync", 
                    request.SessionId.ToString(), chatId, stopwatch, combinedCts.Token);
                sseHandler.SetupSseHeaders();
                
                await sseHandler.SubscribeAsync(messageStream);
                
                await godChat.StreamVoiceChatWithSessionAsync(request.SessionId, string.Empty, request.Content, "",
                    chatId, null, true, request.Region, request.VoiceLanguage, request.VoiceDurationSeconds);
                
                await sseHandler.WaitForCompletionAsync();
                await sseHandler.CleanupAsync();
                sseHandler.LogCompletion();
            }
            finally
            {
                voiceChatCts.Dispose();
                combinedCts.Dispose();
            }
        }
        catch (InvalidOperationException ex)
        {
            await HandleVoiceBusinessException(context, ex, request.SessionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatMiddleware][HandleVoiceChatAsync] Error - SessionId={SessionId}", request.SessionId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language));
        }
    }

    #region Helper Methods

    private bool TryGetAuthenticatedUserId(HttpContext context, GodGPTChatLanguage language, out Guid userId)
    {
        userId = Guid.Empty;
        
        if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.Unauthorized, language)).Wait();
            return false;
        }

        var userIdStr = context.User.FindFirst("sub")?.Value ?? 
                        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToRetrieveUserId, language)).Wait();
            return false;
        }

        return true;
    }

    private IMessageStream? GetMessageStream(string streamId)
    {
        return _messageStreamProvider?.GetStream(streamId, "GodChat");
    }

    private async Task WriteSessionError(HttpContext context, Guid sessionId, GodGPTChatLanguage language)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        var parameters = new Dictionary<string, string> { ["sessionId"] = sessionId.ToString() };
        await context.Response.WriteAsync(_localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToLoadConversation, language, parameters));
    }

    private async Task WriteStreamNotAvailableError(HttpContext context, string sessionId)
    {
        _logger.LogError("[ChatMiddleware] MassTransit stream not configured. SessionId={SessionId}", sessionId);
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await context.Response.WriteAsync("Streaming is not available (MassTransit stream provider is not configured).");
    }

    private StartStreamChatInputProto BuildStartStreamChatInput(QuantumChatRequestDto request, string chatId)
    {
        var protoInput = new StartStreamChatInputProto
        {
            SessionId = request.SessionId.ToString(),
            SysmLlm = string.Empty,
            Content = request.Content,
            ChatId = chatId,
            IsHttpRequest = true,
            Region = request.Region ?? "",
        };
        
        if (request.Images != null && request.Images.Count > 0)
            protoInput.Images.AddRange(request.Images);
        
        if (request.UserLocalTime.HasValue)
            protoInput.UserLocalTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.UserLocalTime.Value, DateTimeKind.Utc));
        
        if (!string.IsNullOrEmpty(request.UserTimeZoneId))
            protoInput.UserTimeZoneId = request.UserTimeZoneId;
        
        return protoInput;
    }

    private async Task HandleBusinessException(HttpContext context, InvalidOperationException e)
    {
        var statusCode = StatusCodes.Status500InternalServerError;
        if (e.Data.Contains("Code") && int.TryParse((string?)e.Data["Code"], out var code))
        {
            statusCode = code switch
            {
                ExecuteActionStatus.InsufficientCredits => StatusCodes.Status402PaymentRequired,
                ExecuteActionStatus.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status500InternalServerError
            };
        }
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(e.Message);
    }

    private async Task HandleGuestBusinessException(HttpContext context, InvalidOperationException ex, string userHashId, GodGPTChatLanguage language)
    {
        var statusCode = StatusCodes.Status400BadRequest;
        if (ex.Message.Contains("Daily chat limit exceeded"))
            statusCode = StatusCodes.Status429TooManyRequests;
        else if (ex.Data.Contains("Code") && int.TryParse(ex.Data["Code"]?.ToString(), out var code))
        {
            statusCode = code switch
            {
                ExecuteActionStatus.InsufficientCredits => StatusCodes.Status402PaymentRequired,
                ExecuteActionStatus.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
                >= 10000 => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status400BadRequest
            };
        }
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(ex.Message);
    }

    private async Task HandleVoiceBusinessException(HttpContext context, InvalidOperationException ex, Guid sessionId)
    {
        var statusCode = StatusCodes.Status500InternalServerError;
        if (ex.Data.Contains("Code") && int.TryParse(ex.Data["Code"]?.ToString(), out var code))
        {
            statusCode = code switch
            {
                ExecuteActionStatus.InsufficientCredits => StatusCodes.Status402PaymentRequired,
                ExecuteActionStatus.RateLimitExceeded => StatusCodes.Status429TooManyRequests,
                >= 10000 => StatusCodes.Status400BadRequest,
                _ => StatusCodes.Status500InternalServerError
            };
        }
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsync(ex.Message);
    }

    #endregion
}
