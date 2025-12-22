using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Core;
using Aevatar.Agents.Core.Factory;
using Microsoft.Extensions.Logging;
using Proto;

namespace Aevatar.Agents.Runtime.ProtoActor;

/// <summary>
/// Proto.Actor 运行时的 Agent Actor 工厂
/// </summary>
public class ProtoActorGAgentActorFactory : GAgentActorFactoryBase
{
    private readonly ActorSystem _actorSystem;
    private readonly ProtoActorMessageStreamRegistry _streamRegistry;

    public ProtoActorGAgentActorFactory(
        IServiceProvider serviceProvider,
        ActorSystem actorSystem,
        ILogger<ProtoActorGAgentActorFactory> logger)
        : base(serviceProvider, logger)
    {
        _actorSystem = actorSystem;
        _streamRegistry = new ProtoActorMessageStreamRegistry(actorSystem.Root);
    }

    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    /// <summary>
    /// 为已存在的 Agent 实例创建 Actor（内部方法，供自动发现使用）
    /// </summary>
    protected override Task<IGAgentActor> CreateActorInstanceAsync(IGAgent agent, string id,
        CancellationToken ct = default)
    {
        _logger.LogDebug("[Factory] Creating ProtoActor Actor for Agent - Type: {AgentType}, Id: {Id}",
            agent.GetType().Name, id);

        // 创建 ProtoActor Actor
        var props = Props.FromProducer(() => new AgentActor());
        var actorPid = _actorSystem.Root.Spawn(props);

        var actor = new ProtoActorGAgentActor(
            agent,
            _actorSystem.Root,
            actorPid,
            _streamRegistry);

        _logger.LogInformation("Created ProtoActor agent actor instance {Id}", id);

        return Task.FromResult<IGAgentActor>(actor);
    }
}