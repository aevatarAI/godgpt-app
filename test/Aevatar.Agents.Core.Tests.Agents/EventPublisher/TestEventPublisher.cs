using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Core.Tests.EventPublisher;

/// <summary>
/// Simple test implementation of IEventPublisher for testing purposes
/// Tracks all published events and their directions
/// </summary>
public class TestEventPublisher : IEventPublisher
{
    public class PublishedEventInfo
    {
        public IMessage Event { get; set; } = null!;
        public EventDirection Direction { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public DateTime PublishedAt { get; set; }
    }

    /// <summary>
    /// Information about point-to-point sent events
    /// </summary>
    public class SentEventInfo
    {
        public Guid TargetAgentId { get; set; }
        public IMessage Event { get; set; } = null!;
        public EventDirection OnArrivalDirection { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string EventId { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    public List<PublishedEventInfo> PublishedEvents { get; } = new();
    public List<SentEventInfo> SentEvents { get; } = new();
    public int AttemptedPublishCount { get; private set; }
    public int AttemptedSendCount { get; private set; }
    public bool ShouldThrowException { get; set; }
    public string ExceptionMessage { get; set; } = "Test exception";

    public Task<string> PublishEventAsync<TEvent>(
        TEvent evt, 
        EventDirection direction = EventDirection.Down, 
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        AttemptedPublishCount++;

        if (ShouldThrowException)
        {
            throw new InvalidOperationException(ExceptionMessage);
        }

        var eventId = Guid.NewGuid().ToString();
        PublishedEvents.Add(new PublishedEventInfo
        {
            Event = evt,
            Direction = direction,
            EventType = typeof(TEvent).Name,
            EventId = eventId,
            PublishedAt = DateTime.UtcNow
        });

        return Task.FromResult(eventId);
    }

    public Task<string> SendToAsync<TEvent>(
        Guid targetAgentId,
        TEvent evt,
        EventDirection onArrivalDirection = EventDirection.Unspecified,
        CancellationToken ct = default,
        bool isInternalCall = false)
        where TEvent : IMessage
    {
        AttemptedSendCount++;

        if (ShouldThrowException)
        {
            throw new InvalidOperationException(ExceptionMessage);
        }

        var eventId = Guid.NewGuid().ToString();
        SentEvents.Add(new SentEventInfo
        {
            TargetAgentId = targetAgentId,
            Event = evt,
            OnArrivalDirection = onArrivalDirection,
            EventType = typeof(TEvent).Name,
            EventId = eventId,
            SentAt = DateTime.UtcNow
        });

        return Task.FromResult(eventId);
    }

    public void Clear()
    {
        PublishedEvents.Clear();
        SentEvents.Clear();
        AttemptedPublishCount = 0;
        AttemptedSendCount = 0;
    }
}