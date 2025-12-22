using Google.Protobuf;

namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream interface for event streaming
/// 支持订阅管理和类型筛选
/// </summary>
public interface IMessageStream
{
    /// <summary>
    /// Stream唯一标识符
    /// Format varies by runtime:
    /// - Orleans: "AgentType:Guid" (e.g., "ChatAgent:12345678-...")
    /// - Local/Proto: "Guid" (e.g., "12345678-...")
    /// </summary>
    string StreamId { get; }

    /// <summary>
    /// 发布消息到stream
    /// </summary>
    Task ProduceAsync<T>(T message, CancellationToken ct = default) where T : IMessage;

    /// <summary>
    /// 订阅stream消息
    /// </summary>
    /// <returns>订阅句柄，用于管理订阅生命周期</returns>
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        CancellationToken ct = default) where T : IMessage;

    /// <summary>
    /// 订阅stream消息（带类型过滤器）
    /// </summary>
    /// <returns>订阅句柄，用于管理订阅生命周期</returns>
    Task<IMessageStreamSubscription> SubscribeAsync<T>(
        Func<T, Task> handler,
        Func<T, bool>? filter,
        CancellationToken ct = default) where T : IMessage;
}