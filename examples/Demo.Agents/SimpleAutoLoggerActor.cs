using Aevatar.Agents;
using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Microsoft.Extensions.Logging;

namespace Demo.Agents;

/// <summary>
/// 演示自动 Logger 注入的 Actor 示例
/// </summary>
public class SimpleAutoLoggerActor : GAgentActorBase
{
    /// <summary>
    /// 仅使用 Agent 参数的构造函数 - 支持自动 Logger 注入
    /// </summary>
    public SimpleAutoLoggerActor(IGAgent agent)
        : base(agent)
    {
        // Logger 将被自动注入，无需手动传入
    }

    protected override Task SendToSelfAsync(EventEnvelope envelope, CancellationToken ct)
    {
        // 使用自动注入的 Logger
        Logger.LogInformation("Actor {ActorId} sending event {EventId} to self", 
            Id, envelope.Id);
        
        // 实际发送逻辑（简化示例）
        return HandleEventAsync(envelope, ct);
    }

    protected override Task SendEventToActorAsync(string actorId, EventEnvelope envelope, CancellationToken ct)
    {
        // 使用自动注入的 Logger
        Logger.LogInformation("Actor {ActorId} sending event {EventId} to actor {TargetActorId}", 
            Id, envelope.Id, actorId);
        
        // 在实际实现中，这里会通过某种机制发送事件到目标 Actor
        return Task.CompletedTask;
    }

    protected override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // 使用自动注入的 Logger
        Logger.LogInformation("Actor {ActorId} activated with agent type {AgentType}", 
            Id, Agent.GetType().Name);
        
        await Task.CompletedTask;
    }

    protected override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // 使用自动注入的 Logger
        Logger.LogInformation("Actor {ActorId} deactivated", Id);
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 重写事件处理，添加日志
    /// </summary>
    public override async Task HandleEventAsync(EventEnvelope envelope, CancellationToken ct = default)
    {
        Logger.LogDebug("Actor {ActorId} received event {EventId} from {PublisherId}", 
            Id, envelope.Id, envelope.PublisherId);
        
        await base.HandleEventAsync(envelope, ct);
    }
}
