using Aevatar.Agents.AI;
using Aevatar.Agents.AI.Abstractions;
using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Options;
using Aevatar.Agents.Persistence.Supabase.Setup;
using Google.Protobuf.WellKnownTypes;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Memory;

/// <summary>
/// Supabase(Postgres) 版 AI Memory：
/// - 追加写（append-only）
/// - 按 agent_id（+ 可选 session_id）隔离
/// - 支持 Full-Text Search（优先），失败则降级 ILIKE（best-effort）
/// </summary>
public sealed class SupabaseAIMemory : IAevatarAIMemory
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly SupabasePersistenceOptions _options;
    private readonly string _table;
    private readonly string _agentId;
    private readonly string? _sessionId;

    public SupabaseAIMemory(
        NpgsqlDataSource dataSource,
        IOptions<SupabasePersistenceOptions> options,
        string agentId,
        string? sessionId = null)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(agentId))
        {
            throw new ArgumentException("agentId cannot be null/empty.", nameof(agentId));
        }

        SupabaseSchemaManager.EnsureInitialized(_dataSource, _options);

        _table = SupabaseSql.Table(_options.Schema, _options.AiMemoryMessagesTable, nameof(_options.AiMemoryMessagesTable));
        _agentId = agentId.Trim();
        _sessionId = string.IsNullOrWhiteSpace(sessionId) ? null : sessionId.Trim();
    }

    public async Task AddMessageAsync(
        string role,
        string content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        var docId = Guid.NewGuid();
        var normalizedRole = string.IsNullOrWhiteSpace(role) ? "unknown" : role.Trim();

        var sql = $@"
INSERT INTO {_table} (id, agent_id, session_id, role, content, created_at)
VALUES (@id, @agent_id, @session_id, @role, @content, now())";

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("id", docId);
        cmd.Parameters.AddWithValue("agent_id", _agentId);
        cmd.Parameters.AddWithValue("session_id", (object?)_sessionId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("role", normalizedRole);
        cmd.Parameters.AddWithValue("content", content);

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AevatarConversationEntry>> GetHistoryAsync(
        int? limit = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var effectiveLimit = limit is > 0 ? limit.Value : 200;
        effectiveLimit = Math.Clamp(effectiveLimit, 1, 2000);

        var hasSession = !string.IsNullOrWhiteSpace(_sessionId);
        var sql = hasSession
            ? $"SELECT role, content, created_at FROM {_table} WHERE agent_id = @agent_id AND session_id = @session_id ORDER BY created_at DESC LIMIT @limit"
            : $"SELECT role, content, created_at FROM {_table} WHERE agent_id = @agent_id ORDER BY created_at DESC LIMIT @limit";

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", _agentId);
        if (hasSession)
        {
            cmd.Parameters.AddWithValue("session_id", _sessionId!);
        }

        cmd.Parameters.AddWithValue("limit", effectiveLimit);

        var docs = new List<(string Role, string Content, DateTime CreatedAt)>(effectiveLimit);

        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var role = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            var content = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
            var createdAt = reader.IsDBNull(2) ? DateTime.UtcNow : reader.GetDateTime(2);
            docs.Add((role, content, createdAt));
        }

        // 查询是倒序取的，为了对齐调用方预期，翻转成时间正序。
        docs.Reverse();

        var result = new List<AevatarConversationEntry>(docs.Count);
        foreach (var d in docs)
        {
            var utc = d.CreatedAt.Kind == DateTimeKind.Utc
                ? d.CreatedAt
                : DateTime.SpecifyKind(d.CreatedAt, DateTimeKind.Utc);

            result.Add(new AevatarConversationEntry
            {
                Role = d.Role ?? string.Empty,
                Content = d.Content ?? string.Empty,
                Timestamp = Timestamp.FromDateTime(utc)
            });
        }

        return result;
    }

    public async Task ClearHistoryAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hasSession = !string.IsNullOrWhiteSpace(_sessionId);
        var sql = hasSession
            ? $"DELETE FROM {_table} WHERE agent_id = @agent_id AND session_id = @session_id"
            : $"DELETE FROM {_table} WHERE agent_id = @agent_id";

        await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("agent_id", _agentId);
        if (hasSession)
        {
            cmd.Parameters.AddWithValue("session_id", _sessionId!);
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> SearchAsync(
        string query,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Array.Empty<string>();
        }

        var k = Math.Clamp(topK, 1, 50);
        var hasSession = !string.IsNullOrWhiteSpace(_sessionId);
        var ftsCfgLiteral = SupabaseSql.RegConfigLiteral(_options.FullTextSearchConfig);

        // 优先走 FTS（需要表达式索引匹配：to_tsvector('<cfg>', content)）
        try
        {
            var ftsSql = hasSession
                ? $@"
SELECT role, content
FROM {_table}
WHERE agent_id = @agent_id
  AND session_id = @session_id
  AND to_tsvector({ftsCfgLiteral}, content) @@ websearch_to_tsquery({ftsCfgLiteral}, @query)
ORDER BY created_at DESC
LIMIT @k"
                : $@"
SELECT role, content
FROM {_table}
WHERE agent_id = @agent_id
  AND to_tsvector({ftsCfgLiteral}, content) @@ websearch_to_tsquery({ftsCfgLiteral}, @query)
ORDER BY created_at DESC
LIMIT @k";

            await using var conn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = ftsSql;
            cmd.Parameters.AddWithValue("agent_id", _agentId);
            if (hasSession)
            {
                cmd.Parameters.AddWithValue("session_id", _sessionId!);
            }

            cmd.Parameters.AddWithValue("query", query);
            cmd.Parameters.AddWithValue("k", k);

            var result = new List<string>(k);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var role = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
                var content = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                result.Add($"[{role}] {content}".Trim());
            }

            return result;
        }
        catch
        {
            // ignore and fallback
        }

        // 降级：ILIKE（best-effort，可能慢；但保证“能用”）
        var likeSql = hasSession
            ? $@"
SELECT role, content
FROM {_table}
WHERE agent_id = @agent_id
  AND session_id = @session_id
  AND content ILIKE '%' || @query || '%'
ORDER BY created_at DESC
LIMIT @k"
            : $@"
SELECT role, content
FROM {_table}
WHERE agent_id = @agent_id
  AND content ILIKE '%' || @query || '%'
ORDER BY created_at DESC
LIMIT @k";

        await using var fallbackConn = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var fallbackCmd = fallbackConn.CreateCommand();
        fallbackCmd.CommandText = likeSql;
        fallbackCmd.Parameters.AddWithValue("agent_id", _agentId);
        if (hasSession)
        {
            fallbackCmd.Parameters.AddWithValue("session_id", _sessionId!);
        }

        fallbackCmd.Parameters.AddWithValue("query", query);
        fallbackCmd.Parameters.AddWithValue("k", k);

        var fallbackResult = new List<string>(k);
        await using var fallbackReader = await fallbackCmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await fallbackReader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var role = fallbackReader.IsDBNull(0) ? string.Empty : fallbackReader.GetString(0);
            var content = fallbackReader.IsDBNull(1) ? string.Empty : fallbackReader.GetString(1);
            fallbackResult.Add($"[{role}] {content}".Trim());
        }

        return fallbackResult;
    }
}


