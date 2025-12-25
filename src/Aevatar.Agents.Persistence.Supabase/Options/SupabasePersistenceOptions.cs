namespace Aevatar.Agents.Persistence.Supabase.Options;

/// <summary>
/// Supabase(Postgres) 持久化选项。
///
/// 设计目标：
/// - 以 Supabase 托管的 Postgres 作为存储后端（直连数据库）
/// - 自动建表/建索引（可关闭）
/// - 默认“最小暴露”：尽量避免被 Supabase PostgREST 直接暴露出去（可配置）
/// </summary>
public sealed class SupabasePersistenceOptions
{
    /// <summary>
    /// Postgres 连接串（来自 Supabase Dashboard -> Project Settings -> Database）。
    /// </summary>
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// 存储所在 schema。默认使用独立 schema，避免与 public/业务表混在一起。
    /// </summary>
    public string Schema { get; set; } = "aevatar";

    // ==============================
    // 表名（建议保持全小写 + 下划线）
    // ==============================

    public string AgentStatesTable { get; set; } = "agent_states";

    public string AgentConfigsTable { get; set; } = "agent_configs";

    public string EventRouterHierarchiesTable { get; set; } = "agent_event_router_hierarchies";

    public string AiMemoryMessagesTable { get; set; } = "ai_memory_messages";

    // ==============================
    // 自动初始化
    // ==============================

    /// <summary>
    /// 是否自动创建 Schema（如果不存在）。
    /// </summary>
    public bool AutoCreateSchema { get; set; } = true;

    /// <summary>
    /// 是否自动创建表（如果不存在）。
    /// </summary>
    public bool AutoCreateTables { get; set; } = true;

    /// <summary>
    /// 是否自动创建索引（如果不存在）。
    /// </summary>
    public bool AutoCreateIndexes { get; set; } = true;

    // ==============================
    // 权限 / 暴露控制
    // ==============================

    /// <summary>
    /// 是否默认收紧权限：撤销 PUBLIC/anon/authenticated 对 schema/table 的权限。
    ///
    /// 说明：
    /// - 这不会影响 table owner（通常是你用于初始化/服务端连接的数据库用户）。
    /// - 目的：防止在 Supabase 默认暴露 PostgREST 的情况下，被 anon key 直接读写。
    /// </summary>
    public bool LockDownPublicAccess { get; set; } = true;

    /// <summary>
    /// 是否启用 Row Level Security（默认不启用，避免误伤直连服务端）。
    ///
    /// 建议：
    /// - 如果你打算通过 Supabase PostgREST 暴露这些表，则应启用 RLS 并配置 Policy。
    /// - 本库仅提供最基础的“service_role 全通”policy（可选）。
    /// </summary>
    public bool EnableRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// 是否强制对 table owner 也启用 RLS（FORCE ROW LEVEL SECURITY）。
    /// 默认关闭，避免服务端直连（owner）被 RLS 拦截。
    /// </summary>
    public bool ForceRowLevelSecurity { get; set; } = false;

    /// <summary>
    /// 是否为 Supabase 的 service_role 创建“全通”Policy。
    /// 仅当 EnableRowLevelSecurity=true 时生效。
    /// </summary>
    public bool CreateServiceRolePolicies { get; set; } = false;

    // ==============================
    // AI Memory
    // ==============================

    /// <summary>
    /// Postgres Full-Text Search 配置（regconfig）。
    /// 常用：simple / english / chinese(需自定义)。
    /// </summary>
    public string FullTextSearchConfig { get; set; } = "simple";
}


