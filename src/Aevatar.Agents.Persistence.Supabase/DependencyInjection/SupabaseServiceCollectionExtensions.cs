using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Persistence.Supabase.Memory;
using Aevatar.Agents.Persistence.Supabase.Options;
using Aevatar.Agents.Persistence.Supabase.Stores;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.DependencyInjection;

/// <summary>
/// Supabase(Postgres) 持久化 DI 扩展。
///
/// 设计：
/// - 通过 <see cref="NpgsqlDataSource"/> 管理连接池（推荐）
/// - store 构造时自动触发建表/建索引/权限收紧（幂等）
/// </summary>
public static class SupabaseServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Supabase(Postgres) 的基础设施（Options + NpgsqlDataSource）。
    /// </summary>
    public static IServiceCollection AddAevatarSupabase(
        this IServiceCollection services,
        string connectionString,
        Action<SupabasePersistenceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(connectionString);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(o =>
            {
                o.ConnectionString = connectionString;
                configure?.Invoke(o);
            });

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SupabasePersistenceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"{nameof(SupabasePersistenceOptions)}.{nameof(SupabasePersistenceOptions.ConnectionString)} is required.");
            }

            // NpgsqlDataSource.Create 不会立即连库，直到第一次 OpenConnection 才会触发真实连接。
            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// 注册 Supabase(Postgres) 的基础设施（Options + NpgsqlDataSource）。
    /// 适合从 IConfiguration 绑定后再微调。
    /// </summary>
    public static IServiceCollection AddAevatarSupabase(
        this IServiceCollection services,
        Action<SupabasePersistenceOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services
            .AddOptions<SupabasePersistenceOptions>()
            .Configure(configure);

        services.AddSingleton(sp =>
        {
            var options = sp.GetRequiredService<IOptions<SupabasePersistenceOptions>>().Value;
            if (string.IsNullOrWhiteSpace(options.ConnectionString))
            {
                throw new InvalidOperationException(
                    $"{nameof(SupabasePersistenceOptions)}.{nameof(SupabasePersistenceOptions.ConnectionString)} is required.");
            }

            return NpgsqlDataSource.Create(options.ConnectionString);
        });

        return services;
    }

    /// <summary>
    /// 注册 Supabase 的 StateStore（特定 TState）。
    /// 注意：在框架里更常见的做法是：
    /// options.StateStoreType = typeof(SupabaseStateStore&lt;&gt;)
    /// </summary>
    public static IServiceCollection AddSupabaseStateStore<TState>(this IServiceCollection services)
        where TState : class, IMessage<TState>, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseStateStore<TState>>();
        return services;
    }

    /// <summary>
    /// 注册 Supabase 的 ConfigStore（特定 TConfig）。
    /// </summary>
    public static IServiceCollection AddSupabaseConfigStore<TConfig>(this IServiceCollection services)
        where TConfig : class, new()
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseConfigStore<TConfig>>();
        return services;
    }

    /// <summary>
    /// 注册 Supabase 的 EventRouterStore。
    /// </summary>
    public static IServiceCollection AddSupabaseEventRouterStore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<SupabaseEventRouterStore>();
        return services;
    }

    /// <summary>
    /// 注册 Supabase-backed AI Memory factory。
    /// </summary>
    public static IServiceCollection AddSupabaseAIMemory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<IAevatarAIMemoryFactory, SupabaseAIMemoryFactory>();
        return services;
    }
}


