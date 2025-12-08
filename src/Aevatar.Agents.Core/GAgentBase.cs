using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Abstractions.Attributes;
using Aevatar.Agents.Abstractions.Helpers;
using Aevatar.Agents.Core.EventSourcing;
using Aevatar.Agents.Core.Helpers;
using Aevatar.Agents.Core.Observability;
using Aevatar.Agents.Core.StateProtection;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Type = System.Type;

namespace Aevatar.Agents.Core;

/// <summary>
/// Non-generic base class for all GAgents.
/// Provides event handler auto-discovery and invocation infrastructure.
/// This class focuses solely on event processing without state management concerns.
/// </summary>
public abstract class GAgentBase : IGAgent
{
    // ============ Fields ============

    /// <summary>
    /// Agent unique identifier
    /// Can be set internally by factories for recovery scenarios
    /// </summary>
    public Guid Id { get; internal set; }

    /// <summary>
    /// Event publisher for sending events
    /// </summary>
    protected IEventPublisher? EventPublisher;

    /// <summary>
    /// Logger property - supports automatic injection
    /// </summary>
    protected ILogger Logger { get; set; } = NullLogger.Instance;

    // Event handler cache (type -> metadata list)
    private static readonly ConcurrentDictionary<Type, EventHandlerMetadata[]> HandlerCache = new();
    private static readonly IEventHandlerDiscoverer DefaultDiscoverer = new ReflectionEventHandlerDiscoverer();

    // Cached Unpack method info to avoid repeated reflection lookups
    private static MethodInfo? _cachedUnpackMethod;
    private static bool _cachedUnpackMethodIsInstance;
    private static readonly object _unpackMethodLock = new();

    /// <summary>
    /// Finds the Unpack method definition using reflection.
    /// This method is cached after first successful lookup.
    /// </summary>
    /// <param name="isInstanceMethod">Output: whether the found method is an instance method</param>
    /// <returns>The MethodInfo for Unpack, or null if not found</returns>
    private static MethodInfo? FindUnpackMethodDefinition(out bool isInstanceMethod)
    {
        lock (_unpackMethodLock)
        {
            if (_cachedUnpackMethod != null)
            {
                isInstanceMethod = _cachedUnpackMethodIsInstance;
                return _cachedUnpackMethod;
            }

            // 1. Try instance method Unpack<T>() first
            var instanceMethod = typeof(Any).GetMethod("Unpack", Type.EmptyTypes);
            if (instanceMethod is { IsGenericMethod: true })
            {
                _cachedUnpackMethod = instanceMethod;
                _cachedUnpackMethodIsInstance = true;
                isInstanceMethod = true;
                return instanceMethod;
            }

            // 2. Fallback: Find Unpack<T> extension method dynamically
            var extensionMethod = typeof(Any).Assembly
                .GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Public))
                .FirstOrDefault(m => m is { Name: "Unpack", IsGenericMethod: true }
                    && m.GetParameters().Length == 1
                    && m.GetParameters()[0].ParameterType == typeof(Any));

            if (extensionMethod != null)
            {
                _cachedUnpackMethod = extensionMethod;
                _cachedUnpackMethodIsInstance = false;
                isInstanceMethod = false;
                return extensionMethod;
            }

            isInstanceMethod = false;
            return null;
        }
    }

    /// <summary>
    /// Metadata for cached event handlers to avoid repeated reflection
    /// </summary>
    public class EventHandlerMetadata
    {
        public MethodInfo Method { get; }
        public Type ParameterType { get; }
        public bool IsAllEventHandler { get; }
        public bool AllowSelfHandling { get; }
        public Func<Any, IMessage>? Unpacker { get; }

        public EventHandlerMetadata(MethodInfo method)
        {
            Method = method;
            ParameterType = method.GetParameters()[0].ParameterType;

            var allHandlerAttr = method.GetCustomAttribute<AllEventHandlerAttribute>();
            var eventHandlerAttr = method.GetCustomAttribute<EventHandlerAttribute>();

            IsAllEventHandler = allHandlerAttr != null;
            AllowSelfHandling = eventHandlerAttr?.AllowSelfHandling ?? allHandlerAttr?.AllowSelfHandling ?? false;

            // Pre-compile Unpack delegate for specific message types
            if (!IsAllEventHandler && typeof(IMessage).IsAssignableFrom(ParameterType))
            {
                try
                {
                    var unpackMethodDef = FindUnpackMethodDefinition(out var isInstanceMethod);

                    if (unpackMethodDef != null)
                    {
                        var unpackMethod = unpackMethodDef.MakeGenericMethod(ParameterType);

                        var anyParam = System.Linq.Expressions.Expression.Parameter(typeof(Any), "any");

                        System.Linq.Expressions.MethodCallExpression call;
                        if (isInstanceMethod)
                        {
                            call = System.Linq.Expressions.Expression.Call(anyParam, unpackMethod);
                        }
                        else
                        {
                            call = System.Linq.Expressions.Expression.Call(unpackMethod, anyParam);
                        }

                        var cast = System.Linq.Expressions.Expression.Convert(call, typeof(IMessage));
                        var lambda = System.Linq.Expressions.Expression.Lambda<Func<Any, IMessage>>(cast, anyParam);
                        Unpacker = lambda.Compile();
                    }
                }
                catch (Exception)
                {
                    // Fallback or ignore if unpacker cannot be created
                    Unpacker = null;
                }
            }
        }
    }

    // ============ Constructors ============

    /// <summary>
    /// Default constructor - generates a new ID
    /// </summary>
    public GAgentBase()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Constructor with specific ID
    /// </summary>
    public GAgentBase(Guid id)
    {
        Id = id;
    }

    // ============ IGAgent Implementation ============

    /// <summary>
    /// Get agent category for routing.
    /// Defaults to [StreamTopic] attribute value, or the simple type name if not present.
    /// </summary>
    public virtual string GetAgentCategory()
    {
        var attr = GetType().GetCustomAttribute<StreamTopicAttribute>();
        if (attr != null)
        {
            return attr.Topic;
        }
        return GetType().Name;
    }

    /// <summary>
    /// Get agent description
    /// </summary>
    public virtual string GetDescription()
    {
        return GetType().Name;
    }

    /// <summary>
    /// Get agent description - async version
    /// </summary>
    /// <returns></returns>
    public virtual Task<string> GetDescriptionAsync()
    {
        return Task.FromResult(GetDescription());
    }

    /// <summary>
    /// Get all subscribed event types
    /// </summary>
    public virtual Task<List<Type>> GetAllSubscribedEventsAsync(bool includeAllEventHandler = false)
    {
        var handlers = GetEventHandlers();
        var eventTypes = new HashSet<Type>();

        foreach (var handler in handlers)
        {
            // Skip EventEnvelope (AllEventHandler) if not requested
            if (!includeAllEventHandler && handler.IsAllEventHandler)
                continue;

            // Only include IMessage types
            if (typeof(IMessage).IsAssignableFrom(handler.ParameterType))
            {
                eventTypes.Add(handler.ParameterType);
            }
        }

        return Task.FromResult(eventTypes.ToList());
    }

    public async Task ActivateAsync(CancellationToken ct = default)
    {
        // Allow State modification during agent activation
        // This is necessary for initializing agent state before event processing begins
        using (StateProtectionContext.BeginInitializationScope())
        {
            await OnActivateAsync(ct);
        }
    }

    public async Task DeactivateAsync(CancellationToken ct = default)
    {
        await OnDeactivateAsync(ct);
    }

    // ============ Event Publishing ============

    /// <summary>
    /// Publish event (delegates to EventPublisher) - Broadcast mode
    /// </summary>
    protected async Task<string> PublishAsync<TEvent>(
        TEvent evt,
        EventDirection direction = EventDirection.Down,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        if (EventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var eventId = await EventPublisher.PublishEventAsync(evt, direction, ct);

            // Record publish metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));

            return eventId;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "PublishEvent");
            throw;
        }
    }

    /// <summary>
    /// Point-to-point send - Direct delivery mode.
    /// Sends directly to specified agent, bypassing hierarchical broadcast.
    /// </summary>
    /// <param name="targetAgentId">Target agent ID</param>
    /// <param name="evt">Event message</param>
    /// <param name="onArrivalDirection">
    /// Propagation direction after arrival:
    /// - Unspecified: Pure P2P, only target processes, no propagation
    /// - Down: Target processes then broadcasts to all its children
    /// - Up: Target processes then propagates up to its parent
    /// - Both: Target processes then propagates in both directions
    /// </param>
    /// <param name="ct">Cancellation token</param>
    /// <typeparam name="TEvent">Event type</typeparam>
    /// <returns>Event ID</returns>
    protected async Task<string> SendToAsync<TEvent>(
        Guid targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default)
        where TEvent : IMessage
    {
        if (EventPublisher == null)
        {
            throw new InvalidOperationException(
                "EventPublisher is not set. Make sure the Actor layer has initialized this agent.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var eventId = await EventPublisher.SendToAsync(targetAgentId, evt, onArrivalDirection, ct);

            // Record send metrics
            stopwatch.Stop();
            AgentMetrics.RecordEventPublished(typeof(TEvent).Name, Id.ToString());
            AgentMetrics.EventPublishLatency.Record(stopwatch.ElapsedMilliseconds,
                new KeyValuePair<string, object?>("event.type", typeof(TEvent).Name),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()),
                new KeyValuePair<string, object?>("mode", "point-to-point"));

            return eventId;
        }
        catch (Exception ex)
        {
            // Record exception metrics
            AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), "SendToEvent");
            throw;
        }
    }

    // EventPublisher is now injected via EventPublisherInjector
    // No public setter method needed

    // ============ Event Handler Discovery ============

    /// <summary>
    /// Get all event handler metadata (cached)
    /// </summary>
    public EventHandlerMetadata[] GetEventHandlers()
    {
        var type = GetType();
        return HandlerCache.GetOrAdd(type, _ =>
        {
            var methods = GetEventHandlerDiscoverer().DiscoverEventHandlers(type);
            var metadata = methods.Select(m => new EventHandlerMetadata(m)).ToArray();
            Logger.LogDebug("Discovered {Count} event handlers for {Type}", metadata.Length, type.Name);
            return metadata;
        });
    }

    /// <summary>
    /// Get the event handler discoverer to use.
    /// Defaults to ReflectionEventHandlerDiscoverer.
    /// </summary>
    protected virtual IEventHandlerDiscoverer GetEventHandlerDiscoverer()
    {
        return DefaultDiscoverer;
    }

    // ============ Event Handler Invocation ============

    /// <summary>
    /// Handle event - entry point that can be overridden by derived classes
    /// Derived classes should override this to add state management
    /// </summary>
    public virtual async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        await HandleEventCoreAsync(envelope, ct);
    }

    /// <summary>
    /// Core event handling implementation without state management
    /// This method contains the actual event processing logic
    /// </summary>
    protected virtual async Task HandleEventCoreAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        // Create event handling log scope
        var eventType = envelope.Payload?.TypeUrl?.Split('/').LastOrDefault() ?? "Unknown";

        using var scope = LoggingScope.CreateEventHandlingScope(
            Logger,
            Id,
            envelope.Id,
            eventType,
            envelope.CorrelationId);

        var stopwatch = Stopwatch.StartNew();
        var handled = false;

        var handlers = GetEventHandlers();

        foreach (var handler in handlers)
        {
            try
            {
                // Check if it should handle event
                if (!ShouldHandleEvent(handler, envelope))
                {
                    continue;
                }

                // AllEventHandler - pass EventEnvelope directly
                if (handler.IsAllEventHandler)
                {
                    await InvokeHandler(handler.Method, envelope, ct);
                    handled = true;
                    continue;
                }

                // EventHandler - unpack Payload
                if (envelope.Payload != null)
                {
                    IMessage? message = null;
                    try
                    {
                        // Use pre-compiled Unpacker delegate if available
                        if (handler.Unpacker != null)
                        {
                            message = handler.Unpacker(envelope.Payload);
                            Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName} using Unpacker",
                                message?.GetType().Name ?? "null", handler.Method.Name);
                        }
                        else if (!handler.IsAllEventHandler && envelope.Payload != null)
                        {
                            Logger.LogDebug("Unpacker is null for handler {HandlerName}. Attempting reflection fallback.", handler.Method.Name);
                            
                            // Fallback: Try to unpack using cached reflection method
                            try
                            {
                                var unpackMethodDef = FindUnpackMethodDefinition(out var isInstanceMethod);

                                if (unpackMethodDef != null)
                                {
                                    var genericUnpack = unpackMethodDef.MakeGenericMethod(handler.ParameterType);
                                    message = isInstanceMethod
                                        ? (IMessage?)genericUnpack.Invoke(envelope.Payload, null)
                                        : (IMessage?)genericUnpack.Invoke(null, [envelope.Payload]);
                                    Logger.LogDebug("Unpacked message of type {MessageType} for handler {HandlerName} using reflection fallback",
                                        message?.GetType().Name ?? "null", handler.Method.Name);
                                }
                                else
                                {
                                    Logger.LogError("CRITICAL: Could not find Any.Unpack method via reflection for handler {HandlerName}", handler.Method.Name);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logger.LogDebug(ex, "Failed to unpack payload for handler {HandlerName} using reflection fallback.", handler.Method.Name);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Unpack failed, possibly type mismatch, skip
                        Logger.LogTrace(ex, "Failed to unpack event payload for handler {Handler}", handler.Method.Name);
                    }

                    if (message != null)
                    {
                        Logger.LogDebug("Invoking handler {HandlerName} with message {MessageType}", handler.Method.Name, message.GetType().Name);
                        await InvokeHandler(handler.Method, message, ct);
                        handled = true;
                    }
                    else
                    {
                         // Log why message is null if we expected it to work
                         if (!handler.IsAllEventHandler)
                         {
                            var msg = $"Skipping handler {handler.Method.Name} because message could not be unpacked (Type mismatch or Unpack failure). Expected: {handler.ParameterType.FullName}, Actual URL: {envelope.Payload.TypeUrl}";
                            Logger.LogDebug(msg);
                         }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error handling event in {Handler}", handler.Method.Name);

                // Record exception metrics
                AgentMetrics.RecordException(ex.GetType().Name, Id.ToString(), $"HandleEvent:{handler.Method.Name}");

                // Publish exception event
                await PublishExceptionEventAsync(envelope, handler.Method.Name, ex);

                // Continue processing other handlers
            }
        }

        // Record event handling metrics
        stopwatch.Stop();
        if (handled)
        {
            AgentMetrics.RecordEventHandled(eventType, Id.ToString(), stopwatch.ElapsedMilliseconds);
        }
        else
        {
            // No handler processed this event
            AgentMetrics.EventsDropped.Add(1,
                new KeyValuePair<string, object?>("event.type", eventType),
                new KeyValuePair<string, object?>("agent.id", Id.ToString()));
        }
    }

    /// <summary>
    /// Determine if an event should be handled (can be used by subclasses)
    /// </summary>
    private bool ShouldHandleEvent(EventHandlerMetadata handler, EventEnvelope envelope)
    {
        // If self-handling is not allowed and publisher is self, skip
        if (!handler.AllowSelfHandling && envelope.PublisherId == Id.ToString())
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Invoke handler method (can be used by subclasses)
    /// </summary>
    protected async Task InvokeHandler(MethodInfo handler, object parameter, CancellationToken ct)
    {
        Logger.LogDebug("Invoking handler method {HandlerName} on {AgentType} with parameter type {ParameterType}",
            handler.Name, GetType().Name, parameter.GetType().Name);

        // Create event handler scope to allow State modifications
        using var scope = StateProtectionContext.BeginEventHandlerScope();

        try
        {
            var result = handler.Invoke(this, new[] { parameter });

            if (result is Task task)
            {
                await task;
            }

            Logger.LogDebug("Handler method {HandlerName} completed", handler.Name);
        }
        catch (TargetInvocationException tie) when (tie.InnerException != null)
        {
            // Unwrap the TargetInvocationException to get the actual exception
            throw tie.InnerException;
        }
    }

    // ============ Resource Management ============

    /// <summary>
    /// Prepare resource context
    /// </summary>
    public virtual Task PrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        Logger.LogDebug("Preparing resource context for Agent {Id} with {ResourceCount} resources",
            Id, context.Count);

        return OnPrepareResourceContextAsync(context, ct);
    }

    /// <summary>
    /// Resource context preparation callback (overridden by subclasses)
    /// </summary>
    protected virtual Task OnPrepareResourceContextAsync(ResourceContext context, CancellationToken ct = default)
    {
        // Default implementation: do nothing
        // Subclasses can override to handle resources
        return Task.CompletedTask;
    }

    // ============ Exception Handling ============

    /// <summary>
    /// Publish exception event
    /// </summary>
    protected virtual async Task PublishExceptionEventAsync(
        EventEnvelope originalEnvelope,
        string handlerName,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            // Build complete exception message including inner exceptions
            var fullExceptionMessage = ExceptionFormatter.BuildFullExceptionMessage(exception);

            var exceptionEvent = new EventHandlerExceptionEvent
            {
                AgentId = Id.ToString(),
                EventId = originalEnvelope.Id,
                HandlerName = handlerName,
                EventType = originalEnvelope.Payload?.TypeUrl ?? "Unknown",
                ExceptionMessage = fullExceptionMessage,
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = TimestampHelper.GetUtcNow()
            };

            Logger.LogDebug("Publishing exception event for handler {Handler}", handlerName);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing exception event");
        }
    }

    /// <summary>
    /// Publish framework exception event
    /// </summary>
    protected virtual async Task PublishFrameworkExceptionAsync(
        string operation,
        Exception exception)
    {
        try
        {
            if (EventPublisher == null)
                return;

            var exceptionEvent = new GAgentBaseExceptionEvent
            {
                AgentId = Id.ToString(),
                Operation = operation,
                ExceptionMessage = ExceptionFormatter.BuildFullExceptionMessage(exception),
                StackTrace = exception.StackTrace ?? string.Empty,
                Timestamp = TimestampHelper.GetUtcNow()
            };

            Logger.LogDebug("Publishing framework exception event for operation {Operation}", operation);

            await EventPublisher.PublishEventAsync(exceptionEvent, EventDirection.Up);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error publishing framework exception event");
        }
    }

    // ============ Lifecycle Callbacks (optional override) ============

    /// <summary>
    /// Activation callback
    /// </summary>
    protected virtual Task OnActivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} of type {AgentType} activated", Id, GetType().Name);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Deactivation callback
    /// </summary>
    protected virtual Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Logger.LogDebug("Agent {Id} deactivated", Id);
        return Task.CompletedTask;
    }
}