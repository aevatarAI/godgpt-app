using Aevatar.Agents.Abstractions;
using Aevatar.Agents.Runtime.Orleans;
using Aevatar.Agents.Runtime.Orleans.Stream;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aevatar.Agents.Runtime.Orleans.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 添加 Orleans Agent Actor 运行时支持
    /// </summary>
    public static IServiceCollection AddOrleansAgentRuntime(this IServiceCollection services)
    {
        // 注册工厂
        services.TryAddSingleton<IGAgentActorFactory, OrleansGAgentActorFactory>();
        services.TryAddSingleton<OrleansGAgentActorFactory>(); // 同时也注册具体类型，以防万一

        // 注册管理器
        services.TryAddSingleton<IGAgentActorManager, OrleansGAgentActorManager>();
        
        // 注册 Stream Not Found Handler
        services.AddSingleton<IStreamNotFoundHandler, OrleansStreamNotFoundHandler>();

        // 注册 Stream Factory (统一 Stream 创建)
        services.TryAddSingleton<OrleansStreamFactory>();

        return services;
    }
}

