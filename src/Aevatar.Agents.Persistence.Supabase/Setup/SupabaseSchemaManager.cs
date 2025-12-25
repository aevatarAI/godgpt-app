using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Aevatar.Agents.Persistence.Supabase.Options;
using Npgsql;

namespace Aevatar.Agents.Persistence.Supabase.Setup;

/// <summary>
/// 运行时自动初始化（建表/建索引/权限收紧/RLS）。
///
/// 约束：
/// - 需要数据库用户具备对应 DDL 权限；否则会抛异常。
/// - 初始化是幂等的（IF NOT EXISTS + DO block），且每个 DataSource/Schema 只执行一次。
/// </summary>
internal static class SupabaseSchemaManager
{
    private static readonly ConcurrentDictionary<string, bool> Initialized = new();

    internal static void EnsureInitialized(NpgsqlDataSource dataSource, SupabasePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);

        // 如果全部开关都关了，直接跳过。
        if (!options.AutoCreateSchema &&
            !options.AutoCreateTables &&
            !options.AutoCreateIndexes &&
            !options.LockDownPublicAccess &&
            !options.EnableRowLevelSecurity)
        {
            return;
        }

        var key = BuildKey(dataSource, options);
        if (!Initialized.TryAdd(key, true))
        {
            return;
        }

        try
        {
            using var conn = dataSource.OpenConnection();
            ExecuteStatements(conn, SupabaseSchemaScript.BuildStatements(options));
        }
        catch
        {
            // 初始化失败允许重试（避免一次失败把进程永远锁死在“已初始化”状态）。
            Initialized.TryRemove(key, out _);
            throw;
        }
    }

    private static void ExecuteStatements(NpgsqlConnection conn, IReadOnlyList<string> statements)
    {
        foreach (var sql in statements)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
    }

    private static string BuildKey(NpgsqlDataSource dataSource, SupabasePersistenceOptions options)
    {
        // 用 DataSource 的 identity hash + schema/table 配置构造 key。
        // 目的：同一进程内避免重复 DDL，且允许多数据源并存。
        var dsKey = RuntimeHelpers.GetHashCode(dataSource);

        return string.Join(
            "|",
            dsKey.ToString(),
            options.Schema,
            options.AgentStatesTable,
            options.AgentConfigsTable,
            options.EventRouterHierarchiesTable,
            options.AiMemoryMessagesTable,
            options.FullTextSearchConfig,
            options.AutoCreateSchema.ToString(),
            options.AutoCreateTables.ToString(),
            options.AutoCreateIndexes.ToString(),
            options.LockDownPublicAccess.ToString(),
            options.EnableRowLevelSecurity.ToString(),
            options.ForceRowLevelSecurity.ToString(),
            options.CreateServiceRolePolicies.ToString());
    }
}


