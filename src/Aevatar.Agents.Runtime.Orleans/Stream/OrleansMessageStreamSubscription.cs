using Aevatar.Agents.Abstractions;
using Orleans.Streams;

namespace Aevatar.Agents.Runtime.Orleans.Stream;

/// <summary>
/// Orleans stream subscription implementation.
/// Wraps Orleans StreamSubscriptionHandle to provide IMessageStreamSubscription interface.
/// UnsubscribeAsync only pauses processing; DisposeAsync actually unsubscribes.
/// </summary>
public class OrleansMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly StreamSubscriptionHandle<byte[]> _streamSubscriptionHandle;
    private readonly Action? _onPause;
    private readonly Action? _onResume;
    private readonly Action? _onDisposed;
    private bool _isActive;
    private bool _isDisposed;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive && !_isDisposed;

    public OrleansMessageStreamSubscription(
        Guid subscriptionId,
        string streamId,
        StreamSubscriptionHandle<byte[]> streamSubscriptionHandle,
        Action? onPause = null,
        Action? onResume = null,
        Action? onDisposed = null)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _streamSubscriptionHandle = streamSubscriptionHandle ?? throw new ArgumentNullException(nameof(streamSubscriptionHandle));
        _onPause = onPause;
        _onResume = onResume;
        _onDisposed = onDisposed;
        _isActive = true;
        _isDisposed = false;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Pauses message processing but keeps the Orleans subscription active.
    /// Messages arriving while paused will be ignored.
    /// Call ResumeAsync to start processing again.
    /// </remarks>
    public Task UnsubscribeAsync()
    {
        if (!_isActive || _isDisposed)
        {
            return Task.CompletedTask;
        }

        _isActive = false;
        _onPause?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Resumes message processing after UnsubscribeAsync was called.
    /// </remarks>
    public Task ResumeAsync()
    {
        if (_isActive || _isDisposed)
        {
            return Task.CompletedTask;
        }

        _isActive = true;
        _onResume?.Invoke();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Actually unsubscribes from Orleans stream and releases resources.
    /// After disposal, the subscription cannot be resumed.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _isActive = false;

        try
        {
            await _streamSubscriptionHandle.UnsubscribeAsync();
        }
        catch (Exception)
        {
            // If unsubscribe fails, ignore - we're disposing anyway
        }
        finally
        {
            _onDisposed?.Invoke();
        }
    }
}
