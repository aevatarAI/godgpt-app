using System.Collections.Concurrent;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Message Stream 实现
/// 基于 Actor 消息传递实现 Stream 语义
/// </summary>
public class ProtoActorMessageStream : IMessageStream
{
    private readonly PID _targetPid;
    private readonly IRootContext _rootContext;
    private readonly ConcurrentDictionary<Guid, ProtoActorStreamSubscription> _subscriptions = new();

    public string StreamId { get; }

    public ProtoActorMessageStream(string streamId, PID targetPid, IRootContext rootContext)
    {
        StreamId = streamId;
        _targetPid = targetPid;
        _rootContext = rootContext;
    }

    /// <summary>
    /// 发布消息到 Stream（通过 Actor 消息传递）
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            // 发送 HandleEventMessage 到目标 Actor
            _rootContext.Send(_targetPid, new HandleEventMessage { Envelope = envelope });
            
            // 触发所有订阅的处理器
            var tasks = new List<Task>();
            Console.WriteLine($"ProtoActorMessageStream {StreamId} producing event, subscriptions count: {_subscriptions.Count}");
            foreach (var subscription in _subscriptions.Values)
            {
                if (subscription.IsActive)
                {
                    Console.WriteLine($"ProtoActorMessageStream {StreamId} invoking subscription {subscription.SubscriptionId}");
                    tasks.Add(subscription.HandleMessageAsync(envelope));
                }
            }
            
            if (tasks.Count > 0)
            {
                await Task.WhenAll(tasks);
            }
        }
        else
        {
            throw new InvalidOperationException(
                $"ProtoActorMessageStream only supports EventEnvelope, got {typeof(T).Name}");
        }
    }

    /// <summary>
    /// 订阅 Stream 消息
    /// </summary>
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler, 
        CancellationToken ct = default) where T : IMessage
    {
        return SubscribeAsync(handler, filter: null, ct);
    }
    
    /// <summary>
    /// 订阅 Stream 消息（带过滤器）
    /// </summary>
    public Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage
    {
        // 在 Proto.Actor 中，消息处理由 Actor 自身完成
        // 这里只需要创建一个订阅句柄用于管理
        var subscriptionId = Guid.NewGuid();
        
        // 转换为 IMessage 类型的 handler 和 filter
        Func<IMessage, Task> messageHandler = async (msg) =>
        {
            if (msg is T typedMsg)
            {
                await handler(typedMsg);
            }
        };
        
        Func<IMessage, bool>? messageFilter = null;
        if (filter != null)
        {
            messageFilter = (msg) => msg is T typedMsg && filter(typedMsg);
        }
        
        var subscription = new ProtoActorStreamSubscription(
            subscriptionId,
            StreamId,
            messageHandler,
            messageFilter,
            _targetPid,
            _rootContext,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        
        Console.WriteLine($"ProtoActorMessageStream {StreamId} added subscription {subscriptionId}, total subscriptions: {_subscriptions.Count}");
        
        // Proto.Actor 的实际消息处理在 Actor 的 Receive 方法中
        // 订阅只是记录 handler 供后续使用
        return Task.FromResult<IMessageStreamSubscription>(subscription);
    }
}