namespace Aevatar.Agents.Abstractions;

/// <summary>
/// Agent Actor 工厂提供者接口
/// 用于提供 Agent 类型的工厂方法
/// </summary>
public interface IGAgentActorFactoryProvider
{
    /// <summary>
    /// 注册指定 Agent 类型的工厂方法
    /// </summary>
    /// <typeparam name="TAgent">Agent 类型</typeparam>
    /// <param name="factory">工厂委托</param>
    void RegisterFactory<TAgent>(Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory)
        where TAgent : IGAgent;
    
    /// <summary>
    /// 注册指定 Agent 类型的工厂方法（使用类型参数）
    /// </summary>
    /// <param name="agentType">Agent 类型</param>
    /// <param name="factory">工厂委托</param>
    void RegisterFactory(Type agentType, Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>> factory);
    
    /// <summary>
    /// 获取指定 Agent 类型的工厂方法
    /// </summary>
    /// <param name="agentType">Agent 类型</param>
    /// <returns>工厂委托，如果未找到则返回 null</returns>
    Func<IGAgentActorFactory, string, CancellationToken, Task<IGAgentActor>>? GetFactory(Type agentType);
}