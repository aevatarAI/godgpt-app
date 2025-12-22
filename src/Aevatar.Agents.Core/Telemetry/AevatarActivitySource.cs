using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aevatar.Agents.Core.Telemetry;

// ============================================================
//  Aevatar Agent Framework - OpenTelemetry Integration (P3-6)
//  Distributed tracing and metrics for agent operations
// ============================================================

/// <summary>
/// Central activity source for all Aevatar agent operations.
/// Provides distributed tracing capabilities.
/// </summary>
public static class AevatarActivitySource
{
    // ============================================================
    //  Activity Source Configuration
    // ============================================================
    
    public const string SourceName = "Aevatar.Agents";
    public const string SourceVersion = "1.0.0";

    private static readonly ActivitySource Source = new(SourceName, SourceVersion);

    // ============================================================
    //  Agent Lifecycle Activities
    // ============================================================

    /// <summary>
    /// Start an activity for agent activation.
    /// </summary>
    public static Activity? StartAgentActivation(string agentId, string agentType)
    {
        return Source.StartActivity("agent.activate", ActivityKind.Internal)?
            .SetTag("agent.id", agentId.ToString())
            .SetTag("agent.type", agentType);
    }

    /// <summary>
    /// Start an activity for agent deactivation.
    /// </summary>
    public static Activity? StartAgentDeactivation(string agentId, string agentType)
    {
        return Source.StartActivity("agent.deactivate", ActivityKind.Internal)?
            .SetTag("agent.id", agentId.ToString())
            .SetTag("agent.type", agentType);
    }

    // ============================================================
    //  Event Processing Activities
    // ============================================================

    /// <summary>
    /// Start an activity for event handling.
    /// </summary>
    public static Activity? StartEventHandling(
        string agentId,
        string eventType,
        string? correlationId = null)
    {
        var activity = Source.StartActivity("agent.event.handle", ActivityKind.Consumer)?
            .SetTag("agent.id", agentId.ToString())
            .SetTag("event.type", eventType);

        if (!string.IsNullOrEmpty(correlationId))
        {
            activity?.SetTag("correlation.id", correlationId);
        }

        return activity;
    }

    /// <summary>
    /// Start an activity for event publishing.
    /// </summary>
    public static Activity? StartEventPublishing(
        Guid sourceAgentId,
        string eventType,
        string direction)
    {
        return Source.StartActivity("agent.event.publish", ActivityKind.Producer)?
            .SetTag("agent.id", sourceAgentId.ToString())
            .SetTag("event.type", eventType)
            .SetTag("event.direction", direction);
    }

    // ============================================================
    //  Stream Activities
    // ============================================================

    /// <summary>
    /// Start an activity for stream subscription.
    /// </summary>
    public static Activity? StartStreamSubscription(Guid subscriberId, Guid streamId)
    {
        return Source.StartActivity("stream.subscribe", ActivityKind.Client)?
            .SetTag("subscriber.id", subscriberId.ToString())
            .SetTag("stream.id", streamId.ToString());
    }

    /// <summary>
    /// Start an activity for stream message delivery.
    /// </summary>
    public static Activity? StartStreamDelivery(Guid streamId, int subscriberCount)
    {
        return Source.StartActivity("stream.deliver", ActivityKind.Internal)?
            .SetTag("stream.id", streamId.ToString())
            .SetTag("subscriber.count", subscriberCount);
    }

    // ============================================================
    //  Parent-Child Activities
    // ============================================================

    /// <summary>
    /// Start an activity for setting parent relationship.
    /// </summary>
    public static Activity? StartSetParent(Guid childId, Guid parentId)
    {
        return Source.StartActivity("agent.hierarchy.set_parent", ActivityKind.Internal)?
            .SetTag("child.id", childId.ToString())
            .SetTag("parent.id", parentId.ToString());
    }

    /// <summary>
    /// Start an activity for adding child relationship.
    /// </summary>
    public static Activity? StartAddChild(Guid parentId, Guid childId)
    {
        return Source.StartActivity("agent.hierarchy.add_child", ActivityKind.Internal)?
            .SetTag("parent.id", parentId.ToString())
            .SetTag("child.id", childId.ToString());
    }

    // ============================================================
    //  LLM Activities (for AI Agents)
    // ============================================================

    /// <summary>
    /// Start an activity for LLM call.
    /// </summary>
    public static Activity? StartLlmCall(
        string agentId,
        string providerName,
        string? model = null)
    {
        return Source.StartActivity("llm.call", ActivityKind.Client)?
            .SetTag("agent.id", agentId.ToString())
            .SetTag("llm.provider", providerName)
            .SetTag("llm.model", model ?? "default");
    }

    // ============================================================
    //  Error Recording
    // ============================================================

    /// <summary>
    /// Record an error on the current activity.
    /// </summary>
    public static void RecordError(Activity? activity, Exception exception)
    {
        activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
        // Add exception details as tags (RecordException is from OpenTelemetry SDK)
        activity?.AddTag("exception.type", exception.GetType().FullName);
        activity?.AddTag("exception.message", exception.Message);
        activity?.AddTag("exception.stacktrace", exception.StackTrace);
    }

    /// <summary>
    /// Mark activity as successful.
    /// </summary>
    public static void RecordSuccess(Activity? activity)
    {
        activity?.SetStatus(ActivityStatusCode.Ok);
    }
}

// ============================================================
//  Metrics
// ============================================================

/// <summary>
/// Metrics for Aevatar agent operations.
/// </summary>
public static class AevatarMetrics
{
    public const string MeterName = "Aevatar.Agents";
    public const string MeterVersion = "1.0.0";

    private static readonly Meter Meter = new(MeterName, MeterVersion);

    // ============================================================
    //  Counters
    // ============================================================

    private static readonly Counter<long> AgentActivations = Meter.CreateCounter<long>(
        "aevatar.agent.activations",
        "activations",
        "Total number of agent activations");

    private static readonly Counter<long> AgentDeactivations = Meter.CreateCounter<long>(
        "aevatar.agent.deactivations",
        "deactivations",
        "Total number of agent deactivations");

    private static readonly Counter<long> EventsProcessed = Meter.CreateCounter<long>(
        "aevatar.events.processed",
        "events",
        "Total number of events processed");

    private static readonly Counter<long> EventsPublished = Meter.CreateCounter<long>(
        "aevatar.events.published",
        "events",
        "Total number of events published");

    private static readonly Counter<long> LlmCalls = Meter.CreateCounter<long>(
        "aevatar.llm.calls",
        "calls",
        "Total number of LLM API calls");

    private static readonly Counter<long> LlmTokens = Meter.CreateCounter<long>(
        "aevatar.llm.tokens",
        "tokens",
        "Total number of LLM tokens consumed");

    private static readonly Counter<long> LlmErrors = Meter.CreateCounter<long>(
        "aevatar.llm.errors",
        "errors",
        "Total number of LLM call errors");

    // ============================================================
    //  Histograms
    // ============================================================

    private static readonly Histogram<double> EventProcessingDuration = Meter.CreateHistogram<double>(
        "aevatar.event.processing.duration",
        "ms",
        "Event processing duration in milliseconds");

    private static readonly Histogram<double> LlmCallDuration = Meter.CreateHistogram<double>(
        "aevatar.llm.call.duration",
        "ms",
        "LLM call duration in milliseconds");

    // ============================================================
    //  Gauges (using ObservableGauge pattern)
    // ============================================================

    private static long _activeAgentCount;

    static AevatarMetrics()
    {
        Meter.CreateObservableGauge(
            "aevatar.agents.active",
            () => Interlocked.Read(ref _activeAgentCount),
            "agents",
            "Number of currently active agents");
    }

    // ============================================================
    //  Recording Methods
    // ============================================================

    public static void RecordAgentActivation(string agentType)
    {
        AgentActivations.Add(1, new KeyValuePair<string, object?>("agent.type", agentType));
        Interlocked.Increment(ref _activeAgentCount);
    }

    public static void RecordAgentDeactivation(string agentType)
    {
        AgentDeactivations.Add(1, new KeyValuePair<string, object?>("agent.type", agentType));
        Interlocked.Decrement(ref _activeAgentCount);
    }

    public static void RecordEventProcessed(string eventType, double durationMs)
    {
        EventsProcessed.Add(1, new KeyValuePair<string, object?>("event.type", eventType));
        EventProcessingDuration.Record(durationMs, new KeyValuePair<string, object?>("event.type", eventType));
    }

    public static void RecordEventPublished(string eventType, string direction)
    {
        EventsPublished.Add(1,
            new KeyValuePair<string, object?>("event.type", eventType),
            new KeyValuePair<string, object?>("direction", direction));
    }

    public static void RecordLlmCall(string provider, double durationMs, int promptTokens, int completionTokens)
    {
        LlmCalls.Add(1, new KeyValuePair<string, object?>("provider", provider));
        LlmCallDuration.Record(durationMs, new KeyValuePair<string, object?>("provider", provider));
        LlmTokens.Add(promptTokens, 
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("token.type", "prompt"));
        LlmTokens.Add(completionTokens, 
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("token.type", "completion"));
    }

    public static void RecordLlmError(string provider, string errorType)
    {
        LlmErrors.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("error.type", errorType));
    }
}

