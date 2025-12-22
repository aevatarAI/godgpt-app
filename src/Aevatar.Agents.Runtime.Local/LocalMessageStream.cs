using System.Collections.Concurrent;
using System.Threading.Channels;
using Aevatar.Agents.Abstractions;
using Google.Protobuf;

namespace Aevatar.Agents.Runtime.Local;

/// <summary>
/// Local 运行时的 Message Stream 实现
/// 基于 System.Threading.Channels 提供高性能的消息队列
/// </summary>
public class LocalMessageStream : IMessageStream
{
    private readonly Channel<EventEnvelope> _channel;
    private readonly ConcurrentDictionary<Guid, LocalMessageStreamSubscription> _subscriptions = new();
    private readonly CancellationTokenSource _cts = new();

    public string StreamId { get; }

    public LocalMessageStream(string streamId, int capacity = 1000)
    {
        StreamId = streamId;
        _channel = Channel.CreateBounded<EventEnvelope>(new BoundedChannelOptions(capacity)
        {
            SingleReader = false,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

        // 启动消息处理循环
        _ = ProcessMessagesAsync();
    }

    /// <summary>
    /// 发布消息到 Stream
    /// </summary>
    public async Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage
    {
        if (message is EventEnvelope envelope)
        {
            await _channel.Writer.WriteAsync(envelope, ct);
        }
        else
        {
            throw new InvalidOperationException(
                $"LocalMessageStream only supports EventEnvelope, got {typeof(T).Name}");
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
        var subscriptionId = Guid.NewGuid();
        Func<EventEnvelope, Task> envelopeHandler;
        
        if (typeof(T) == typeof(EventEnvelope))
        {
            envelopeHandler = async env =>
            {
                var typedMessage = (T)(object)env;
                if (filter != null && !filter(typedMessage))
                {
                    return;
                }
                await handler(typedMessage);
            };
        }
        else
        {
            // 只订阅特定类型的事件（通过 Payload 类型 URL 过滤）
            var expectedTypeUrl = $"type.googleapis.com/{typeof(T).FullName}";
            envelopeHandler = async env =>
            {
                if (env.Payload != null && env.Payload.TypeUrl.Contains(typeof(T).Name))
                {
                    try
                    {
                        // 使用反射 Unpack
                        var unpackMethod = typeof(Google.Protobuf.WellKnownTypes.Any)
                            .GetMethod("Unpack", Type.EmptyTypes)
                            ?.MakeGenericMethod(typeof(T));

                        if (unpackMethod != null)
                        {
                            var message = (T)unpackMethod.Invoke(env.Payload, null)!;
                            if (filter != null && !filter(message))
                            {
                                return;
                            }
                            await handler(message);
                        }
                    }
                    catch (Exception)
                    {
                        // 忽略类型不匹配的事件
                    }
                }
            };
        }
        
        var subscription = new LocalMessageStreamSubscription(
            subscriptionId,
            StreamId,
            envelopeHandler,
            () => _subscriptions.TryRemove(subscriptionId, out _));
        
        _subscriptions.TryAdd(subscriptionId, subscription);
        return Task.FromResult<IMessageStreamSubscription>(subscription);
    }

    /// <summary>
    /// 处理消息循环
    /// </summary>
    private async Task ProcessMessagesAsync()
    {
        await foreach (var envelope in _channel.Reader.ReadAllAsync(_cts.Token))
        {
            // 并发调用所有活跃的订阅者
            var tasks = _subscriptions.Values
                .Where(sub => sub.IsActive)
                .Select(subscription =>
                    Task.Run(async () =>
                    {
                        try
                        {
                            await subscription.HandleMessageAsync(envelope);
                        }
                        catch (Exception)
                        {
                            // 忽略订阅者错误，不影响其他订阅者
                        }
                    }));

            await Task.WhenAll(tasks);
        }
    }

    /// <summary>
    /// 停止 Stream
    /// </summary>
    public void Stop()
    {
        _channel.Writer.Complete();
        _cts.Cancel();
    }
}