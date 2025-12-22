using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor stream subscription implementation
/// 由于Proto.Actor基于消息传递，这里主要是提供订阅管理接口
/// </summary>
internal class ProtoActorStreamSubscription : IMessageStreamSubscription
{
    private readonly Func<IMessage, Task> _handler;
    private readonly Func<IMessage, bool>? _filter;
    private readonly PID _targetPid;
    private readonly IRootContext _rootContext;
    private readonly Action _onDisposed;
    private bool _isActive;

    public Guid SubscriptionId { get; }
    public string StreamId { get; }
    public bool IsActive => _isActive;

    public ProtoActorStreamSubscription(
        Guid subscriptionId,
        string streamId,
        Func<IMessage, Task> handler,
        Func<IMessage, bool>? filter,
        PID targetPid,
        IRootContext rootContext,
        Action onDisposed)
    {
        SubscriptionId = subscriptionId;
        StreamId = streamId;
        _handler = handler;
        _filter = filter;
        _targetPid = targetPid;
        _rootContext = rootContext;
        _onDisposed = onDisposed;
        _isActive = true;
    }

    /// <summary>
    /// 处理接收到的消息
    /// </summary>
    public async Task HandleMessageAsync(IMessage message)
    {
        if (!_isActive)
        {
            return;
        }

        // 应用过滤器
        if (_filter != null && !_filter(message))
        {
            return;
        }

        await _handler(message);
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
        // 注意：不调用 _onDisposed，保留订阅在字典中，以支持Resume
        // _onDisposed 只在真正销毁时调用
        
        // 在Proto.Actor中，取消订阅意味着停止处理消息
        // 实际的消息路由由Actor系统管理
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

        // Proto.Actor的订阅是基于内存的
        // 只需要重新激活标志即可恢复消息处理
        _isActive = true;
        
        // 可选：发送一个恢复通知给目标Actor
        // _rootContext.Send(_targetPid, new SubscriptionResumed { SubscriptionId = SubscriptionId });
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (_isActive)
        {
            _isActive = false;
        }
        
        // 真正销毁时才从字典中移除
        _onDisposed?.Invoke();
        
        return ValueTask.CompletedTask;
    }
}
