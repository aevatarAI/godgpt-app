using Aevatar.Agents.Abstractions;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local message stream subscription implementation
/// 管理本地消息流的订阅生命周期
/// </summary>
internal class LocalMessageStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<EventEnvelope, Task> _handler;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive;

    public LocalMessageStreamSubscription(
        Guid subscriptionId,
        string streamId,
        Func<EventEnvelope, Task> handler,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handler = handler;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    public async Task HandleMessageAsync(EventEnvelope envelope)
    {
        if (!_isActive)
        {
            return;
        }

        await _handler(envelope);
    }

    /// <summary>
    /// 取消订阅
    /// </summary>
    public Task UnsubscribeAsync()
    {
        if (!_isActive)
        {
            return Task.CompletedTask;
        }

        _isActive = false;
        // 不要调用_onDisposed，保留订阅在字典中以支持Resume
        // 只有在DisposeAsync时才真正移除
        return Task.CompletedTask;
    }

    /// <summary>
    /// 恢复订阅
    /// </summary>
    public Task ResumeAsync()
    {
        if (_isActive)
        {
            return Task.CompletedTask;
        }

        // Local stream基于内存Channel
        // 恢复订阅只需要重新激活处理标志
        _isActive = true;
        
        // 注意：如果Channel已关闭，无法恢复
        // 调用方应该处理这种情况并创建新订阅
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (!_isActive)
        {
            // 即使已经取消订阅，也要确保从字典中移除
            _onDisposed?.Invoke();
            return;
        }
        
        _isActive = false;
        _onDisposed?.Invoke(); // 真正从字典中移除
    }
}
