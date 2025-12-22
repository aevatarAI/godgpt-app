using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Plugins.MassTransit;

/// <summary>
/// Subscription handle for MassTransitMessageStream.
/// </summary>
internal class MassTransitMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<Task> _unsubscribeAction;
    private bool _isActive;

    /// <inheritdoc />
    public Guid SubscriptionId { get; }

    /// <inheritdoc />
    public string StreamId { get; }

    /// <inheritdoc />
    public bool IsActive => _isActive;

    public MassTransitMessageStreamSubscription(
        Guid subscriptionId,
        string streamId,
        Func<Task> unsubscribeAction)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _unsubscribeAction = unsubscribeAction;
        _isActive = true;
    }

    /// <inheritdoc />
    public async Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return;
        }

        await _unsubscribeAction();
        _isActive = false;
    }

    /// <inheritdoc />
    public Task ResumeAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnsubscribeAsync();
    }
}
