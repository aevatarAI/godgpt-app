namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Message stream subscription handle
/// 允许管理和取消订阅
/// </summary>
public interface IMessageStreamSubscription : IAsyncDisposable
{
    /// <summary>
    /// 订阅ID
    /// </summary>
    Guid SubscriptionId { get; }
    
    /// <summary>
    /// 关联的Stream ID
    /// </summary>
    string StreamId { get; }
    
    /// <summary>
    /// 是否已激活
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// 取消订阅
    /// </summary>
    Task UnsubscribeAsync();
    
    /// <summary>
    /// 恢复订阅（用于重连场景）
    /// </summary>
    Task ResumeAsync();
}
