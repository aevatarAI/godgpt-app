using Aevatar.Agents.Abstractions.Persistence;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Options;
using Aevatar.Agents.Persistence.Supabase.Setup;
using Google.Protobuf;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Stores;

/// <summary>
/// Supabase(Postgres) StateStore 实现：
/// - 使用 Protobuf bytea 存储（与 MongoDB 版本一致）
/// - 通过 (state_type, agent_id) 唯一键实现幂等 upsert
/// - version 字段用于 EventSourcing Snapshot 版本标记（框架当前语义）
/// </summary>
/// <typeparam name="TState">必须是 Protobuf IMessage</typeparam>
public sealed class SupabaseStateStore<TState> : IVersionedStateStore<TState>
    where TState : class, IMessage<TState>, new()
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabasePersistenceOptions _options;
    private readonly string _table;
    private readonly string _stateTypeName;

    public SupabaseStateStore(NpgsqlDataSource dataSource, IOptions<SupabasePersistenceOptions> options)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

        // 自动初始化（幂等）
        SupabaseSchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.AgentStatesTable, nameof(_options.AgentStatesTable));
        _stateTypeName = typeof(TState).FullName ?? typeof(TState).Name;
    }

    public async Task<TState?> LoadAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        const string selectColumns = "state_data";
        var sql = $"SELECT {selectColumns} FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            return null;
        }

        var data = reader.IsDBNull(0) ? null : reader.GetFieldValue<byte[]>(0);
        if (data == null || data.Length == 0)
        {
            return null;
        }

        var state = new TState();
        state.MergeFrom(data);
        return state;
    }

    public Task SaveAsync(string agentId, TState state, CancellationToken ct = default)
        => SaveInternalAsync(agentId, state, version: 1, ct);

    public Task SaveAsync(string agentId, TState state, long expectedVersion, CancellationToken ct = default)
        // NOTE:
        // - Abstractions 里参数名叫 expectedVersion，但框架当前实际语义是“snapshot version”。
        // - MongoDB 版本也直接写入该值。
        => SaveInternalAsync(agentId, state, expectedVersion, ct);

    public async Task<long> GetCurrentVersionAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT version FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (result is null || result is DBNull)
        {
            return 0;
        }

        return Convert.ToInt64(result);
    }

    public async Task DeleteAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"DELETE FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> ExistsAsync(string agentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var sql = $"SELECT 1 FROM {_table} WHERE state_type = @state_type AND agent_id = @agent_id LIMIT 1";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);

        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null && result is not DBNull;
    }

    private async Task SaveInternalAsync(string agentId, TState state, long version, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        agentId = agentId.Trim();

        var data = state.ToByteArray();

        var sql = $@"
INSERT INTO {_table} (state_type, agent_id, state_data, version, updated_at)
VALUES (@state_type, @agent_id, @state_data, @version, now())
ON CONFLICT (state_type, agent_id)
DO UPDATE SET
  state_data = EXCLUDED.state_data,
  version = EXCLUDED.version,
  updated_at = EXCLUDED.updated_at";

        await using var conn = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("state_type", _stateTypeName);
        cmd.Parameters.AddWithValue("agent_id", agentId);
        cmd.Parameters.AddWithValue("state_data", data);
        cmd.Parameters.AddWithValue("version", version);

        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Supabase StateStore factory（用于手工 DI/高级场景）。
/// </summary>
public static class SupabaseStateStoreFactory
{
    public static Func<IServiceProvider, object> Create<TState>()
        where TState : class, IMessage<TState>, new()
    {
        return sp =>
        {
            var dataSource = sp.GetService(typeof(NpgsqlDataSource)) as NpgsqlDataSource
                ?? throw new InvalidOperationException(
                    "NpgsqlDataSource not registered. Call services.AddAevatarSupabase(...) first.");

            var options = sp.GetService(typeof(IOptions<SupabasePersistenceOptions>)) as IOptions<SupabasePersistenceOptions>
                ?? throw new InvalidOperationException(
                    "SupabasePersistenceOptions not registered. Call services.AddAevatarSupabase(...) first.");

            return new SupabaseStateStore<TState>(dataSource, options);
        };
    }
}


