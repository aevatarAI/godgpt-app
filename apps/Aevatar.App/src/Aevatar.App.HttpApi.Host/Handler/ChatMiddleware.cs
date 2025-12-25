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
using Orleans.Runtime;
using Orleans.Streams;
using Aevatar.Agents.GodGPT.Protos.GodChat;
using ResponseStreamGodChatProto = Aevatar.Agents.GodGPT.Protos.GodChat.ResponseStreamGodChatProto;
using Google.Protobuf.WellKnownTypes;
using Aevatar.Agents.GodGPT.Protos;
using Aevatar.Agents; // For EventEnvelope

namespace Aevatar.App.HttpApi.Host.Handler;

/// <summary>
/// Middleware for handling streaming chat requests.
/// Supports authenticated chat, guest chat, and voice chat with SSE responses.
/// </summary>
public class ChatMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IClusterClient _clusterClient;
    private readonly ILogger<ChatMiddleware> _logger;
    private readonly ILocalizationService _localizationService;
    private readonly IIpLocationService _ipLocationService;
    private readonly IGAgentActorFactory _actorFactory;
    private readonly IMessageStreamProvider? _messageStreamProvider;
    private readonly IAgentContextAccessor _agentContextAccessor;

    private const int MaxImageCount = 10;
    private const string CNDefaultRegion = "CN";
    private const string DefaultRegion = "DEFAULT";
    private const string CNConsoleRegion = "CNCONSOLE";
    private const string ConsoleRegion = "CONSOLE";
    private const string StreamNamespace = "AevatarAgents";
    private const string StreamProviderName = "AevatarAgents";

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
        _clusterClient = clusterClient;
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

        _logger.LogDebug("[ChatMiddleware] Processing request - PathBase: {PathBase}, Path: {Path}, FullPath: {FullPath}",
            pathBase, path, fullPath);

        // Handle regular authenticated chat
        if (pathBase == "/api/gotgpt/chat" || fullPath.Contains("/api/gotgpt/chat"))
        {
            await HandleAuthenticatedChatAsync(context);
            return;
        }
        // Handle voice chat
        else if (pathBase == "/api/godgpt/voice/chat" || fullPath.Contains("/api/godgpt/voice/chat"))
        {
            await HandleVoiceChatAsync(context);
            return;
        }
        // Handle guest (anonymous) chat
        else if (pathBase == "/api/godgpt/guest/chat" || fullPath.Contains("/api/godgpt/guest/chat"))
        {
            await HandleGuestChatAsync(context);
            return;
        }
        else
        {
            await _next(context);
        }
    }

    private async Task HandleAuthenticatedChatAsync(HttpContext context)
    {
        var language = context.GetGodGPTLanguage();

        if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.Unauthorized, language);
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Unauthorized: User is not authenticated");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(localizedMessage);
            await context.Response.Body.FlushAsync();
            return;
        }

        var userIdStr = context.User.FindFirst("sub")?.Value ?? 
                        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Unauthorized: Unable to retrieve UserId.");
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToRetrieveUserId, language);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(localizedMessage);
            await context.Response.Body.FlushAsync();
            return;
        }

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
        
        if (string.IsNullOrWhiteSpace(request.Region))
        {
            request.Region = isCN ? CNDefaultRegion : DefaultRegion;
        }
        else if (request.Region.Equals(ConsoleRegion) && isCN)
        {
            request.Region = CNConsoleRegion;
        }

        // Set context using runtime-agnostic IAgentContext API
        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(AgentContextKeys.IsCN, isCN);

        if (request.Images != null && request.Images.Count > MaxImageCount)
        {
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] {UserId} Too many files. {Count}", userId, request.Images.Count);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            var parameters = new Dictionary<string, string> { ["TooManyFiles"] = MaxImageCount.ToString() };
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.TooManyFiles, language, parameters);
            await context.Response.WriteAsync(localizedMessage);
            await context.Response.Body.FlushAsync();
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug(
                "[ChatMiddleware][HandleAuthenticatedChatAsync] http start: SessionId={SessionId}, UserId={UserId}, ClientIp={ClientIp}, IsCN={IsCN}, Region={Region}",
                request.SessionId, userId, clientIp, isCN, request.Region);

            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId.ToString());
            var manager = managerActor.As<IChatManagerGAgent>();
            if (!await manager.IsUserSessionAsync(request.SessionId))
            {
                _logger.LogError("[ChatMiddleware][HandleAuthenticatedChatAsync] sessionInfoIsNull sessionId={SessionId}", request.SessionId);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var parameters = new Dictionary<string, string> { ["sessionId"] = request.SessionId.ToString() };
                var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToLoadConversation, language, parameters);
                await context.Response.WriteAsync(localizedMessage);
                await context.Response.Body.FlushAsync();
                return;
            }

            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers.CacheControl = "no-cache";

            // Use MassTransit Stream if available, otherwise fallback to Orleans Stream
            IMessageStream? messageStream = null;
            if (_messageStreamProvider != null)
            {
                messageStream = _messageStreamProvider.GetStream(request.SessionId.ToString(), "GodChat");
                _logger.LogDebug(
                    "[ChatMiddleware][HandleAuthenticatedChatAsync] Using MassTransit Stream for SessionId={SessionId}",
                    request.SessionId);
            }
            
            // Fallback to Orleans Stream if MassTransit not available
            IAsyncStream<ResponseStreamGodChat>? responseStream = null;
            if (messageStream == null)
            {
                var streamProvider = _clusterClient.GetStreamProvider(StreamProviderName);
                var streamId = StreamId.Create(StreamNamespace, request.SessionId);
                responseStream = streamProvider.GetStream<ResponseStreamGodChat>(streamId);
                _logger.LogDebug(
                    "[ChatMiddleware][HandleAuthenticatedChatAsync] Using Orleans Stream for SessionId={SessionId}, Namespace={Namespace}",
                    request.SessionId, StreamNamespace);
            }
            var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(request.SessionId.ToString());
            var godChat = godChatActor.As<IGodChat>();

            // Set language context using IAgentContext API
            agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] SessionId={SessionId}, UserId={UserId}, Language={Language}",
                request.SessionId, userId, language);

            var chatId = Guid.NewGuid().ToString();
            var protoInput = new StartStreamChatInputProto
            {
                SessionId = request.SessionId.ToString(),
                SysmLlm = string.Empty,
                Content = request.Content,
                ChatId = chatId,
                // PromptSettings = null means don't set the optional field
                IsHttpRequest = true,
                Region = request.Region ?? "",
            };
            // Add images if present
            if (request.Images != null && request.Images.Count > 0)
            {
                protoInput.Images.AddRange(request.Images);
            }
            // Set user local time if present
            if (request.UserLocalTime.HasValue)
            {
                protoInput.UserLocalTime = Timestamp.FromDateTime(DateTime.SpecifyKind(request.UserLocalTime.Value, DateTimeKind.Utc));
            }
            // Set user timezone if present
            if (!string.IsNullOrEmpty(request.UserTimeZoneId))
            {
                protoInput.UserTimeZoneId = request.UserTimeZoneId;
            }

            var exitSignal = new TaskCompletionSource();
            IMessageStreamSubscription? messageSubscription = null;
            StreamSubscriptionHandle<ResponseStreamGodChat>? orleansSubscription = null;
            var firstFlag = false;
            var ifLastChunk = false;

            // CRITICAL: Subscribe BEFORE calling StartStreamChatAsync to avoid race condition
            // Messages may arrive immediately after StartStreamChatAsync is called
            if (messageStream != null)
            {
                // Subscribe to MassTransit Stream
                messageSubscription = await messageStream.SubscribeAsync<EventEnvelope>(async (envelope) =>
                {
                    try
                    {
                        // Unpack ResponseStreamGodChatProto from EventEnvelope
                        if (!envelope.Payload.Is(ResponseStreamGodChatProto.Descriptor))
                        {
                            return;
                        }

                        var proto = envelope.Payload.Unpack<ResponseStreamGodChatProto>();
                        var chatResponse = GodChatConversions.FromProto(proto); // Convert to ResponseStreamGodChat

                        if (chatResponse.ChatId != chatId)
                        {
                            return;
                        }

                        if (!firstFlag)
                        {
                            await context.Response.StartAsync();
                            firstFlag = true;
                            _logger.LogDebug(
                                "[ChatMiddleware][HandleAuthenticatedChatAsync] MassTransit Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                                request.SessionId, stopwatch.ElapsedMilliseconds);
                        }

                        var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                        await context.Response.WriteAsync(responseData);
                        await context.Response.Body.FlushAsync();

                        if (chatResponse.IsLastChunk)
                        {
                            await context.Response.WriteAsync("event: completed\n");
                            context.Response.Body.Close();
                            ifLastChunk = true;
                            exitSignal.TrySetResult();
                            if (messageSubscription != null)
                            {
                                await messageSubscription.UnsubscribeAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[ChatMiddleware][HandleAuthenticatedChatAsync] Error processing MassTransit message: SessionId={SessionId}, ChatId={ChatId}",
                            request.SessionId, chatId);
                        exitSignal.TrySetException(ex);
                    }
                });
            }
            else if (responseStream != null)
            {
                // Fallback to Orleans Stream
                orleansSubscription = await responseStream.SubscribeAsync(async (chatResponse, token) =>
                {
                    if (chatResponse.ChatId != chatId)
                    {
                        return;
                    }

                    if (!firstFlag)
                    {
                        await context.Response.StartAsync();
                        firstFlag = true;
                        _logger.LogDebug(
                            "[ChatMiddleware][HandleAuthenticatedChatAsync] Orleans Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                            request.SessionId, stopwatch.ElapsedMilliseconds);
                    }

                    var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                    await context.Response.WriteAsync(responseData);
                    await context.Response.Body.FlushAsync();

                    if (chatResponse.IsLastChunk)
                    {
                        await context.Response.WriteAsync("event: completed\n");
                        context.Response.Body.Close();
                        ifLastChunk = true;
                        exitSignal.TrySetResult();
                        if (orleansSubscription != null)
                        {
                            await orleansSubscription.UnsubscribeAsync();
                        }
                    }
                }, ex =>
                {
                    _logger.LogError(
                        "[ChatMiddleware][HandleAuthenticatedChatAsync] Orleans Stream error: {Error} - SessionId={SessionId}, ChatId={ChatId}",
                        ex.Message, request.SessionId, chatId);
                    exitSignal.TrySetException(ex);
                    return Task.CompletedTask;
                }, () =>
                {
                    _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Orleans Stream oncomplete");
                    exitSignal.TrySetResult();
                    return Task.CompletedTask;
                });
                _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Subscribed to MassTransit Stream for SessionId={SessionId}", request.SessionId);
            }
            else if (responseStream != null)
            {
                // Fallback to Orleans Stream subscription already handled above
                _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Subscribed to Orleans Stream for SessionId={SessionId}", request.SessionId);
            }

            // Now that subscription is active, trigger the chat
            await godChat.StartStreamChatAsync(protoInput);
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] Started chat for SessionId={SessionId}, ChatId={ChatId}", request.SessionId, chatId);

            try
            {
                await exitSignal.Task.WaitAsync(context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatMiddleware][HandleAuthenticatedChatAsync] catch error: {Error}", ex);
            }
            finally
            {
                if (messageSubscription != null)
                {
                    await messageSubscription.UnsubscribeAsync();
                }
                if (orleansSubscription != null)
                {
                    await orleansSubscription.UnsubscribeAsync();
                }
            }

            if (!ifLastChunk)
            {
                _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] No LastChunk: SessionId={SessionId}, ChatId={ChatId}",
                    request.SessionId, chatId);
            }

            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] complete done SessionId={SessionId}", request.SessionId);
        }
        catch (InvalidOperationException e)
        {
            var statusCode = StatusCodes.Status500InternalServerError;
            if (e.Data.Contains("Code") && int.TryParse((string?)e.Data["Code"], out var code))
            {
                if (code == ExecuteActionStatus.InsufficientCredits)
                {
                    statusCode = StatusCodes.Status402PaymentRequired;
                }
                else if (code == ExecuteActionStatus.RateLimitExceeded)
                {
                    statusCode = StatusCodes.Status429TooManyRequests;
                }
            }
            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(e.Message);
            await context.Response.Body.FlushAsync();
            _logger.LogDebug("[ChatMiddleware][HandleAuthenticatedChatAsync] {Error}", e.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError("[ChatMiddleware][HandleAuthenticatedChatAsync] Error in SSE stream: {Error}", ex.Message);
        }
    }

    private async Task HandleGuestChatAsync(HttpContext context)
    {
        var clientIp = context.GetClientIpAddress();
        var userHashId = CommonHelper.GetAnonymousUserGAgentId(clientIp).Replace("AnonymousUser_", "");
        var language = context.GetGodGPTLanguage();
        _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Processing request for user: {UserHashId}, Language={Language}", userHashId, language);

        try
        {
            var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
            var request = JsonConvert.DeserializeObject<GuestChatRequestDto>(body);
            var appType = context.GetGodGPTAppType();
            var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());
            
            if (request == null || string.IsNullOrWhiteSpace(request.Content))
            {
                _logger.LogWarning("[ChatMiddleware][HandleGuestChatAsync] Invalid request body for user: {UserHashId}", userHashId);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidRequestBody, language);
                await context.Response.WriteAsync(localizedMessage);
                return;
            }

            if (string.IsNullOrWhiteSpace(request.Region))
            {
                request.Region = isCN ? CNDefaultRegion : DefaultRegion;
            }
            else if (request.Region.Equals(ConsoleRegion) && isCN)
            {
                request.Region = CNConsoleRegion;
            }

            // Set context using runtime-agnostic IAgentContext API
            var agentContext = _agentContextAccessor.GetOrCreate();
            agentContext.Set(AgentContextKeys.IsCN, isCN);
            var stopwatch = Stopwatch.StartNew();
            _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Start processing guest chat for user: {UserHashId}, ClientIP={ClientIp}, IsCN={IsCN}, Region={Region}",
                userHashId, clientIp, isCN, request.Region);

            // Get or create anonymous user agent for this IP
            var agentId = CommonHelper.GetAnonymousUserGAgentId(clientIp);
            var anonymousUserActor = await _actorFactory.CreateGAgentActorAsync<AnonymousUserGAgent>(agentId);
            var anonymousUserGrain = anonymousUserActor.As<IAnonymousUserGAgent>();
            
            // Set language context using IAgentContext API
            agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
            _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Start processing guest chat for user: {UserHashId}, Language={Language}", userHashId, language);

            // Check if user can still chat
            if (!await anonymousUserGrain.CanChatAsync())
            {
                _logger.LogWarning("[ChatMiddleware][HandleGuestChatAsync] Chat limit exceeded for user: {UserHashId}", userHashId);
                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.DailyChatLimitExceeded, language);
                await context.Response.WriteAsync(localizedMessage);
                return;
            }

            // Get current session
            var sessionInfo = await anonymousUserGrain.GetCurrentSessionAsync();
            if (sessionInfo == null)
            {
                _logger.LogWarning("[ChatMiddleware][HandleGuestChatAsync] No active session for user: {UserHashId}", userHashId);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.NoActiveGuestSession, language);
                await context.Response.WriteAsync(localizedMessage);
                return;
            }

            var chatId = Guid.NewGuid().ToString();
            var sessionId = sessionInfo.SessionId;

            _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Found session {SessionId} for user: {UserHashId}", sessionId, userHashId);

            // Set up SSE response headers
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers.CacheControl = "no-cache";

            // Use MassTransit Stream if available, otherwise fallback to Orleans Stream
            IMessageStream? messageStream = null;
            if (_messageStreamProvider != null)
            {
                messageStream = _messageStreamProvider.GetStream(sessionId, "GodChat");
                _logger.LogDebug(
                    "[ChatMiddleware][HandleGuestChatAsync] Using MassTransit Stream for SessionId={SessionId}",
                    sessionId);
            }

            // Fallback to Orleans Stream if MassTransit not available
            IAsyncStream<ResponseStreamGodChat>? responseStream = null;
            if (messageStream == null)
            {
                var streamProvider = _clusterClient.GetStreamProvider(StreamProviderName);
                var streamId = StreamId.Create(StreamNamespace, sessionId);
                responseStream = streamProvider.GetStream<ResponseStreamGodChat>(streamId);
                _logger.LogDebug(
                    "[ChatMiddleware][HandleGuestChatAsync] Using Orleans Stream for SessionId={SessionId}, Namespace={Namespace}",
                    sessionId, StreamNamespace);
            }

            // Handle streaming response
            var exitSignal = new TaskCompletionSource();
            IMessageStreamSubscription? messageSubscription = null;
            StreamSubscriptionHandle<ResponseStreamGodChat>? orleansSubscription = null;
            var firstFlag = false;
            var ifLastChunk = false;

            // CRITICAL: Subscribe BEFORE calling GuestChatAsync to avoid race condition
            if (messageStream != null)
            {
                // Subscribe to MassTransit Stream
                messageSubscription = await messageStream.SubscribeAsync<EventEnvelope>(async (envelope) =>
                {
                    try
                    {
                        // Unpack ResponseStreamGodChatProto from EventEnvelope
                        if (!envelope.Payload.Is(ResponseStreamGodChatProto.Descriptor))
                        {
                            return;
                        }

                        var proto = envelope.Payload.Unpack<ResponseStreamGodChatProto>();
                        var chatResponse = GodChatConversions.FromProto(proto);

                        if (chatResponse.ChatId != chatId)
                        {
                            return;
                        }

                        if (!firstFlag)
                        {
                            await context.Response.StartAsync();
                            firstFlag = true;
                            _logger.LogDebug(
                                "[ChatMiddleware][HandleGuestChatAsync] MassTransit Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                                sessionId, stopwatch.ElapsedMilliseconds);
                        }

                        var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                        await context.Response.WriteAsync(responseData);
                        await context.Response.Body.FlushAsync();

                        if (chatResponse.IsLastChunk)
                        {
                            await context.Response.WriteAsync("event: completed\n");
                            context.Response.Body.Close();
                            ifLastChunk = true;
                            exitSignal.TrySetResult();
                            if (messageSubscription != null)
                            {
                                await messageSubscription.UnsubscribeAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[ChatMiddleware][HandleGuestChatAsync] Error processing MassTransit message: SessionId={SessionId}, ChatId={ChatId}",
                            sessionId, chatId);
                        exitSignal.TrySetException(ex);
                    }
                });
            }
            else if (responseStream != null)
            {
                // Fallback to Orleans Stream
                orleansSubscription = await responseStream.SubscribeAsync(async (chatResponse, token) =>
                {
                    if (chatResponse.ChatId != chatId)
                    {
                        return;
                    }

                    if (!firstFlag)
                    {
                        await context.Response.StartAsync();
                        firstFlag = true;
                        _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Orleans Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                            sessionId, stopwatch.ElapsedMilliseconds);
                    }

                    var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                    await context.Response.WriteAsync(responseData);
                    await context.Response.Body.FlushAsync();

                    if (chatResponse.IsLastChunk)
                    {
                        await context.Response.WriteAsync("event: completed\n");
                        context.Response.Body.Close();
                        ifLastChunk = true;
                        exitSignal.TrySetResult();
                        if (orleansSubscription != null)
                        {
                            await orleansSubscription.UnsubscribeAsync();
                        }
                    }
                }, ex =>
                {
                    _logger.LogError("[ChatMiddleware][HandleGuestChatAsync] Orleans Stream error: SessionId={SessionId}, ChatId={ChatId}, Error={Error}",
                        sessionId, chatId, ex.Message);
                    exitSignal.TrySetException(ex);
                    return Task.CompletedTask;
                }, () =>
                {
                    _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Orleans Stream completed for user: {UserHashId}", userHashId);
                    exitSignal.TrySetResult();
                    return Task.CompletedTask;
                });
            }

            // Now that subscription is active, trigger the chat
            await anonymousUserGrain.GuestChatAsync(request.Content, chatId);
            _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Guest chat executed for user: {UserHashId}, ChatId={ChatId}", userHashId, chatId);

            try
            {
                await exitSignal.Task.WaitAsync(context.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogError("[ChatMiddleware][HandleGuestChatAsync] Error waiting for stream completion: {Error}", ex.Message);
            }
            finally
            {
                if (messageSubscription != null)
                {
                    await messageSubscription.UnsubscribeAsync();
                }
                if (orleansSubscription != null)
                {
                    await orleansSubscription.UnsubscribeAsync();
                }
            }

            if (!ifLastChunk)
            {
                _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] No LastChunk received for user: {UserHashId}, ChatId={ChatId}", userHashId, chatId);
            }

            _logger.LogDebug("[ChatMiddleware][HandleGuestChatAsync] Completed guest chat for user: {UserHashId}, Duration={Duration}ms",
                userHashId, stopwatch.ElapsedMilliseconds);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = StatusCodes.Status400BadRequest;

            if (ex.Message.Contains("Daily chat limit exceeded"))
            {
                statusCode = StatusCodes.Status429TooManyRequests;
            }
            else if (ex.Message.Contains("No active guest session"))
            {
                statusCode = StatusCodes.Status400BadRequest;
            }
            else if (ex.Data.Contains("Code") && int.TryParse(ex.Data["Code"]?.ToString(), out var code))
            {
                if (code == ExecuteActionStatus.InsufficientCredits)
                {
                    statusCode = StatusCodes.Status402PaymentRequired;
                }
                else if (code == ExecuteActionStatus.RateLimitExceeded)
                {
                    statusCode = StatusCodes.Status429TooManyRequests;
                }
                else if (code >= 10000)
                {
                    statusCode = StatusCodes.Status400BadRequest;
                    _logger.LogWarning("[ChatMiddleware][HandleGuestChatAsync] Business error code {Code} converted to 400 for user: {UserHashId}", code, userHashId);
                }
            }

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(ex.Message);
            _logger.LogWarning(ex, "[ChatMiddleware][HandleGuestChatAsync] Operation error for user: {UserHashId}, Status={Status}", userHashId, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatMiddleware][HandleGuestChatAsync] Unexpected error for user: {UserHashId}", userHashId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language);
            await context.Response.WriteAsync(localizedMessage);
        }
    }

    private async Task HandleVoiceChatAsync(HttpContext context)
    {
        var language = context.GetGodGPTLanguage();
        
        // Check user authentication
        if (context.User?.Identity == null || !context.User.Identity.IsAuthenticated)
        {
            _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Unauthorized: User is not authenticated");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.Unauthorized, language);
            await context.Response.WriteAsync(localizedMessage);
            await context.Response.Body.FlushAsync();
            return;
        }

        // Extract user ID from claims
        var userIdStr = context.User.FindFirst("sub")?.Value ?? 
                        context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Unauthorized: Unable to retrieve UserId.");
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToRetrieveUserId, language);
            await context.Response.WriteAsync(localizedMessage);
            await context.Response.Body.FlushAsync();
            return;
        }

        // Parse request body
        var body = await new StreamReader(context.Request.Body).ReadToEndAsync();
        var request = JsonConvert.DeserializeObject<VoiceChatRequestDto>(body);
        var clientIp = context.GetClientIpAddress();
        var appType = context.GetGodGPTAppType();
        var isCN = await _ipLocationService.IsInMainlandChinaAsync(clientIp, appType.ToString());

        if (request == null || string.IsNullOrWhiteSpace(request.Content))
        {
            _logger.LogWarning("[ChatMiddleware][HandleVoiceChatAsync] Invalid request body for user: {UserId}", userId);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InvalidRequestBody, language);
            await context.Response.WriteAsync(localizedMessage);
            return;
        }

        if (string.IsNullOrWhiteSpace(request.Region))
        {
            request.Region = isCN ? CNDefaultRegion : DefaultRegion;
        }
        else if (request.Region.Equals(ConsoleRegion) && isCN)
        {
            request.Region = CNConsoleRegion;
        }

        // Set context using runtime-agnostic IAgentContext API
        var agentContext = _agentContextAccessor.GetOrCreate();
        agentContext.Set(AgentContextKeys.IsCN, isCN);

        if (request.VoiceLanguage == VoiceLanguageEnum.Unset)
        {
            _logger.LogWarning("[ChatMiddleware][HandleVoiceChatAsync] unset language UserId={UserId}, Language={Language}", userId, request.VoiceLanguage);
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnsetLanguage, language);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync(localizedMessage);
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());
            _logger.LogDebug(
                "[ChatMiddleware][HandleVoiceChatAsync] HTTP start - SessionId={SessionId}, UserId={UserId}, MessageType={MessageType}, VoiceLanguage={VoiceLanguage}, Language={Language}, ClientIp={ClientIp}, IsCN={IsCN}, Region={Region}",
                request.SessionId, userId, request.MessageType, request.VoiceLanguage, language, clientIp, isCN, request.Region);

            // Validate session access
            var managerActor = await _actorFactory.CreateGAgentActorAsync<ChatGAgentManager>(userId.ToString());
            var manager = managerActor.As<IChatManagerGAgent>();
            if (!await manager.IsUserSessionAsync(request.SessionId))
            {
                _logger.LogError("[ChatMiddleware][HandleVoiceChatAsync] Session not found or access denied - SessionId={SessionId}, UserId={UserId}",
                    request.SessionId, userId);
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                var parameters = new Dictionary<string, string> { ["sessionId"] = request.SessionId.ToString() };
                var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.UnableToLoadConversation, language, parameters);
                await context.Response.WriteAsync(localizedMessage);
                await context.Response.Body.FlushAsync();
                return;
            }

            // Check if client is still connected before proceeding
            if (context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation("[ChatMiddleware][HandleVoiceChatAsync] Client disconnected before voice chat start - SessionId={SessionId}", request.SessionId);
                return;
            }

            // Set SSE response headers
            context.Response.ContentType = "text/event-stream";
            context.Response.Headers.Connection = "keep-alive";
            context.Response.Headers.CacheControl = "no-cache";

            // Use MassTransit Stream if available, otherwise fallback to Orleans Stream
            IMessageStream? messageStream = null;
            if (_messageStreamProvider != null)
            {
                messageStream = _messageStreamProvider.GetStream(request.SessionId.ToString(), "GodChat");
                _logger.LogDebug(
                    "[ChatMiddleware][HandleVoiceChatAsync] Using MassTransit Stream for SessionId={SessionId}",
                    request.SessionId);
            }

            // Fallback to Orleans Stream if MassTransit not available
            IAsyncStream<ResponseStreamGodChat>? responseStream = null;
            if (messageStream == null)
            {
                var streamProvider = _clusterClient.GetStreamProvider(StreamProviderName);
                var streamId = StreamId.Create(StreamNamespace, request.SessionId);
                responseStream = streamProvider.GetStream<ResponseStreamGodChat>(streamId);
                _logger.LogDebug(
                    "[ChatMiddleware][HandleVoiceChatAsync] Using Orleans Stream for SessionId={SessionId}, Namespace={Namespace}",
                    request.SessionId, StreamNamespace);
            }

            var godChatActor = await _actorFactory.CreateGAgentActorAsync<GodChatGAgent>(request.SessionId.ToString());
            var godChat = godChatActor.As<IGodChat>();
            agentContext.Set(GodGPTContextKeys.GodGPTLanguage, language.ToString());

            // Generate unique chat ID
            var chatId = Guid.NewGuid().ToString();

            // Add timeout for voice chat operation
            var voiceChatTimeout = TimeSpan.FromMinutes(5);
            var voiceChatCts = new CancellationTokenSource(voiceChatTimeout);
            var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, voiceChatCts.Token);

            // Handle streaming response
            var exitSignal = new TaskCompletionSource();
            IMessageStreamSubscription? messageSubscription = null;
            StreamSubscriptionHandle<ResponseStreamGodChat>? orleansSubscription = null;
            var firstFlag = false;
            var ifLastChunk = false;

            // CRITICAL: Subscribe BEFORE calling StreamVoiceChatWithSessionAsync to avoid race condition
            if (messageStream != null)
            {
                // Subscribe to MassTransit Stream
                messageSubscription = await messageStream.SubscribeAsync<EventEnvelope>(async (envelope) =>
                {
                    try
                    {
                        // Unpack ResponseStreamGodChatProto from EventEnvelope
                        if (!envelope.Payload.Is(ResponseStreamGodChatProto.Descriptor))
                        {
                            return;
                        }

                        var proto = envelope.Payload.Unpack<ResponseStreamGodChatProto>();
                        var chatResponse = GodChatConversions.FromProto(proto);

                        if (chatResponse.ChatId != chatId)
                        {
                            return;
                        }

                        if (!firstFlag)
                        {
                            await context.Response.StartAsync();
                            firstFlag = true;
                            _logger.LogDebug(
                                "[ChatMiddleware][HandleVoiceChatAsync] MassTransit Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                                request.SessionId, stopwatch.ElapsedMilliseconds);
                        }

                        var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                        await context.Response.WriteAsync(responseData);
                        await context.Response.Body.FlushAsync();

                        if (chatResponse.IsLastChunk)
                        {
                            await context.Response.WriteAsync("event: completed\n");
                            context.Response.Body.Close();
                            ifLastChunk = true;
                            exitSignal.TrySetResult();
                            if (messageSubscription != null)
                            {
                                await messageSubscription.UnsubscribeAsync();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "[ChatMiddleware][HandleVoiceChatAsync] Error processing MassTransit message: SessionId={SessionId}, ChatId={ChatId}",
                            request.SessionId, chatId);
                        exitSignal.TrySetException(ex);
                    }
                });
            }
            else if (responseStream != null)
            {
                // Fallback to Orleans Stream
                orleansSubscription = await responseStream.SubscribeAsync(async (chatResponse, token) =>
                {
                    if (chatResponse.ChatId != chatId)
                    {
                        return;
                    }

                    if (!firstFlag)
                    {
                        await context.Response.StartAsync();
                        firstFlag = true;
                        _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Orleans Stream got first message: SessionId={SessionId}, Duration={Duration}ms",
                            request.SessionId, stopwatch.ElapsedMilliseconds);
                    }

                    var responseData = $"data: {JsonConvert.SerializeObject(chatResponse.ConvertToHttpResponse())}\n\n";
                    await context.Response.WriteAsync(responseData);
                    await context.Response.Body.FlushAsync();

                    if (chatResponse.IsLastChunk)
                    {
                        await context.Response.WriteAsync("event: completed\n");
                        context.Response.Body.Close();
                        ifLastChunk = true;
                        exitSignal.TrySetResult();
                        if (orleansSubscription != null)
                        {
                            await orleansSubscription.UnsubscribeAsync();
                        }
                    }
                }, ex =>
                {
                    _logger.LogError("[ChatMiddleware][HandleVoiceChatAsync] Orleans Stream error: SessionId={SessionId}, ChatId={ChatId}, Error={Error}",
                        request.SessionId, chatId, ex.Message);

                    if (ex is OperationCanceledException || ex is TaskCanceledException)
                    {
                        _logger.LogInformation("[ChatMiddleware][HandleVoiceChatAsync] Stream cancelled - SessionId={SessionId}, ChatId={ChatId}",
                            request.SessionId, chatId);
                        exitSignal.TrySetCanceled();
                    }
                    else
                    {
                        exitSignal.TrySetException(ex);
                    }

                    return Task.CompletedTask;
                }, () =>
                {
                    _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Orleans Stream completed - SessionId={SessionId}", request.SessionId);
                    exitSignal.TrySetResult();
                    return Task.CompletedTask;
                });
            }

            // Now that subscription is active, initiate voice chat
            try
            {
                await godChat.StreamVoiceChatWithSessionAsync(request.SessionId, string.Empty, request.Content, "",
                    chatId, null, true, request.Region, request.VoiceLanguage, request.VoiceDurationSeconds);
                _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Voice chat initiated - SessionId={SessionId}, ChatId={ChatId}, Duration={Duration}ms",
                    request.SessionId, chatId, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException) when (voiceChatCts.Token.IsCancellationRequested)
            {
                _logger.LogWarning("[ChatMiddleware][HandleVoiceChatAsync] Voice chat timeout - SessionId={SessionId}, ChatId={ChatId}", request.SessionId, chatId);
                context.Response.StatusCode = StatusCodes.Status408RequestTimeout;
                await context.Response.WriteAsync("Voice chat operation timed out");
                return;
            }

            try
            {
                await exitSignal.Task.WaitAsync(combinedCts.Token);
            }
            catch (OperationCanceledException ex)
            {
                if (voiceChatCts.Token.IsCancellationRequested)
                {
                    _logger.LogWarning("[ChatMiddleware][HandleVoiceChatAsync] Voice chat timeout during streaming - SessionId={SessionId}", request.SessionId);
                }
                else if (context.RequestAborted.IsCancellationRequested)
                {
                    _logger.LogInformation("[ChatMiddleware][HandleVoiceChatAsync] Client disconnected - SessionId={SessionId}", request.SessionId);
                }
                else
                {
                    _logger.LogInformation("[ChatMiddleware][HandleVoiceChatAsync] Stream cancelled - SessionId={SessionId}, Reason={Reason}",
                        request.SessionId, ex.Message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ChatMiddleware][HandleVoiceChatAsync] Unexpected error waiting for stream completion - SessionId={SessionId}", request.SessionId);
            }
            finally
            {
                voiceChatCts.Dispose();
                combinedCts.Dispose();
                if (messageSubscription != null)
                {
                    await messageSubscription.UnsubscribeAsync();
                }
                if (orleansSubscription != null)
                {
                    await orleansSubscription.UnsubscribeAsync();
                }
            }

            if (!ifLastChunk)
            {
                _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] No LastChunk received - SessionId={SessionId}, ChatId={ChatId}",
                    request.SessionId, chatId);
            }

            _logger.LogDebug("[ChatMiddleware][HandleVoiceChatAsync] Voice chat completed - SessionId={SessionId}, Duration={Duration}ms",
                request.SessionId, stopwatch.ElapsedMilliseconds);
        }
        catch (InvalidOperationException ex)
        {
            var statusCode = StatusCodes.Status500InternalServerError;
            if (ex.Data.Contains("Code") && int.TryParse(ex.Data["Code"]?.ToString(), out var code))
            {
                if (code == ExecuteActionStatus.InsufficientCredits)
                {
                    statusCode = StatusCodes.Status402PaymentRequired;
                }
                else if (code == ExecuteActionStatus.RateLimitExceeded)
                {
                    statusCode = StatusCodes.Status429TooManyRequests;
                }
                else if (code >= 10000)
                {
                    statusCode = StatusCodes.Status400BadRequest;
                    _logger.LogWarning("[ChatMiddleware][HandleVoiceChatAsync] Business error code {Code} converted to 400 for SessionId={SessionId}", code, request.SessionId);
                }
            }

            context.Response.StatusCode = statusCode;
            await context.Response.WriteAsync(ex.Message);
            _logger.LogWarning(ex, "[ChatMiddleware][HandleVoiceChatAsync] Operation error - SessionId={SessionId}, Status={Status}", request.SessionId, statusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChatMiddleware][HandleVoiceChatAsync] Unexpected error - SessionId={SessionId}", request.SessionId);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            var localizedMessage = _localizationService.GetLocalizedException(GodGPTExceptionMessageKeys.InternalServerError, language);
            await context.Response.WriteAsync(localizedMessage);
        }
    }
}

