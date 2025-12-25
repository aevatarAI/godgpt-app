using Aevatar.Agents.Persistence.Supabase.Internal;
using Aevatar.Agents.Persistence.Supabase.Options;

namespace Aevatar.Agents.Persistence.Supabase.Setup;

/// <summary>
/// 生成 Supabase(Postgres) 初始化 SQL（建 schema / 建表 / 建索引 / 权限收紧 / RLS）。
/// 这个类同时用于：
/// - 运行时自动初始化（由 <see cref="SupabaseSchemaManager"/> 执行）
/// - 输出 SQL 文件给 DBA/运维做审计与手工部署
/// </summary>
public static class SupabaseSchemaScript
{
    public static IReadOnlyList<string> BuildStatements(SupabasePersistenceOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        // 先做强校验，避免将不安全的标识符拼进 SQL。
        var schema = SupabaseSql.Ident(options.Schema, nameof(options.Schema));

        var states = SupabaseSql.Ident(options.AgentStatesTable, nameof(options.AgentStatesTable));
        var configs = SupabaseSql.Ident(options.AgentConfigsTable, nameof(options.AgentConfigsTable));
        var routers = SupabaseSql.Ident(options.EventRouterHierarchiesTable, nameof(options.EventRouterHierarchiesTable));
        var memory = SupabaseSql.Ident(options.AiMemoryMessagesTable, nameof(options.AiMemoryMessagesTable));

        var ftsCfgLiteral = SupabaseSql.RegConfigLiteral(options.FullTextSearchConfig);

        var stmts = new List<string>(32);

        // ==============================
        // Schema
        // ==============================
        if (options.AutoCreateSchema)
        {
            stmts.Add($"CREATE SCHEMA IF NOT EXISTS {schema}");
        }

        // ==============================
        // Tables
        // ==============================
        if (options.AutoCreateTables)
        {
            // -------- Agent States --------
            // 用 (state_type, agent_id) 做主键，允许同一 agent_id 在不同 state 类型下并存（不同 agent 系统/测试更安全）。
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{states} (
  state_type text NOT NULL,
  agent_id text NOT NULL,
  state_data bytea NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (state_type, agent_id)
)");

            // -------- Agent Configs --------
            // config_type: TConfig 的完整类型名，用于隔离不同配置类型。
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{configs} (
  config_type text NOT NULL,
  agent_type text NOT NULL,
  agent_id text NOT NULL,
  config_data jsonb NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (config_type, agent_type, agent_id)
)");

            // -------- EventRouter Hierarchies --------
            // children_ids 用 text[] 存储（对齐 string agentId 语义）；查询模式主要是按 agent_id / parent_id。
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{routers} (
  agent_id text PRIMARY KEY,
  parent_id text NULL,
  children_ids text[] NOT NULL DEFAULT '{{}}'::text[],
  updated_at timestamptz NOT NULL DEFAULT now()
)");

            // -------- AI Memory Messages --------
            // id 由应用侧生成 Guid，避免依赖 pgcrypto/uuid-ossp 扩展。
            stmts.Add($@"
CREATE TABLE IF NOT EXISTS {schema}.{memory} (
  id uuid PRIMARY KEY,
  agent_id text NOT NULL,
  session_id text NULL,
  role text NOT NULL,
  content text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
)");
        }

        // ==============================
        // Indexes
        // ==============================
        if (options.AutoCreateIndexes)
        {
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_updated_at ON {schema}.{states} (updated_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{states}_agent_version ON {schema}.{states} (agent_id, version)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{configs}_updated_at ON {schema}.{configs} (updated_at DESC)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_parent_id ON {schema}.{routers} (parent_id)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{routers}_updated_at ON {schema}.{routers} (updated_at DESC)");

            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{memory}_agent_created_at ON {schema}.{memory} (agent_id, created_at DESC)");
            stmts.Add($"CREATE INDEX IF NOT EXISTS idx_{memory}_agent_session_created_at ON {schema}.{memory} (agent_id, session_id, created_at DESC)");

            // Full-Text Search：表达式索引（不依赖 generated column），兼容性最好。
            // 查询端必须使用同样的表达式：to_tsvector('<cfg>', content)
            stmts.Add(
                $"CREATE INDEX IF NOT EXISTS idx_{memory}_content_fts ON {schema}.{memory} USING gin (to_tsvector({ftsCfgLiteral}, content))");
        }

        // ==============================
        // Permissions (Lock down)
        // ==============================
        if (options.LockDownPublicAccess)
        {
            // 1) schema 级别：撤销 PUBLIC 使用权限，避免被 PostgREST 意外暴露
            stmts.Add($"REVOKE ALL ON SCHEMA {schema} FROM PUBLIC");

            // 2) table 级别：撤销 PUBLIC 权限
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{states} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{configs} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{routers} FROM PUBLIC");
            stmts.Add($"REVOKE ALL ON TABLE {schema}.{memory} FROM PUBLIC");

            // 3) Supabase 常见角色：anon/authenticated 可能不存在（非 Supabase 环境），所以用 DO block 做存在性判断
            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{states} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{configs} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{routers} FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{memory} FROM anon';
  END IF;
END
$$");

            stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA {schema} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{states} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{configs} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{routers} FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE {schema}.{memory} FROM authenticated';
  END IF;
END
$$");
        }

        // ==============================
        // RLS (optional)
        // ==============================
        if (options.EnableRowLevelSecurity)
        {
            stmts.Add($"ALTER TABLE {schema}.{states} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{configs} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{routers} ENABLE ROW LEVEL SECURITY");
            stmts.Add($"ALTER TABLE {schema}.{memory} ENABLE ROW LEVEL SECURITY");

            if (options.ForceRowLevelSecurity)
            {
                stmts.Add($"ALTER TABLE {schema}.{states} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{configs} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{routers} FORCE ROW LEVEL SECURITY");
                stmts.Add($"ALTER TABLE {schema}.{memory} FORCE ROW LEVEL SECURITY");
            }

            // 可选：给 Supabase 的 service_role 创建全通 policy（仅 PostgREST/JWT role=service_role 时生效）。
            if (options.CreateServiceRolePolicies)
            {
                stmts.Add($@"
DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'service_role') THEN
    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_states ON {schema}.{states}';
    EXECUTE 'CREATE POLICY aevatar_service_all_states ON {schema}.{states} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_configs ON {schema}.{configs}';
    EXECUTE 'CREATE POLICY aevatar_service_all_configs ON {schema}.{configs} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_routers ON {schema}.{routers}';
    EXECUTE 'CREATE POLICY aevatar_service_all_routers ON {schema}.{routers} FOR ALL TO service_role USING (true) WITH CHECK (true)';

    EXECUTE 'DROP POLICY IF EXISTS aevatar_service_all_memory ON {schema}.{memory}';
    EXECUTE 'CREATE POLICY aevatar_service_all_memory ON {schema}.{memory} FOR ALL TO service_role USING (true) WITH CHECK (true)';
  END IF;
END
$$");
            }
        }

        // 最后再做一次去空白，保证执行端不会拿到空 SQL。
        return stmts
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
    }

    public static string BuildSql(SupabasePersistenceOptions options)
    {
        var stmts = BuildStatements(options);
        return string.Join(";\n\n", stmts) + ";\n";
    }
}


