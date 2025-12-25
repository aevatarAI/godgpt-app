using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Persistence.Supabase.Options;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Memory;

/// <summary>
/// Supabase-backed <see cref="IAevatarAIMemoryFactory"/>。
/// 创建按 agent_id（+ 可选 session_id）隔离的 <see cref="SupabaseAIMemory"/> 实例。
/// </summary>
public sealed class SupabaseAIMemoryFactory : IAevatarAIMemoryFactory
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IOptions<SupabasePersistenceOptions> _options;

    public SupabaseAIMemoryFactory(NpgsqlDataSource dataSource, IOptions<SupabasePersistenceOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public IAevatarAIMemory Create(string agentId, string? sessionId = null)
    {
        return new SupabaseAIMemory(_dataSource, _options, agentId, sessionId);
    }
}


