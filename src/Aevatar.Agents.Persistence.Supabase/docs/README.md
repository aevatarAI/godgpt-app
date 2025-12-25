## Aevatar.Agents.Persistence.Supabase

这个项目提供 **Supabase(Postgres) 直连版**持久化实现，用于替代 `Aevatar.Agents.Persistence.MongoDB`：

- **StateStore**：`SupabaseStateStore<TState>`（Protobuf -> `bytea`）
- **ConfigStore**：`SupabaseConfigStore<TConfig>`（`jsonb`）
- **EventRouterStore**：`SupabaseEventRouterStore`（parent/children）
- **AI Memory**：`SupabaseAIMemoryFactory` / `SupabaseAIMemory`（对话历史 + FTS 搜索）

### 目录结构

```
src/Aevatar.Agents.Persistence.Supabase/
├── DependencyInjection/                 # DI 扩展（AddAevatarSupabase + 注册 store）
├── Internal/                            # SQL 安全拼接/校验工具
├── Memory/                              # AI Memory（append-only）
├── Options/                             # SupabasePersistenceOptions
├── Setup/                               # 自动建表/建索引/权限收紧/RLS
├── Stores/                              # StateStore/ConfigStore/EventRouterStore
└── docs/
    ├── README.md
    └── schema.sql                       # 默认建库脚本（与默认 Options 对齐）
```

### 快速使用（推荐：自动初始化）

```csharp
using Aevatar.Agents.Core.Extensions;
using Aevatar.Agents.Persistence.Supabase.DependencyInjection;
using Aevatar.Agents.Persistence.Supabase.Stores;

// 1) 注册 Supabase(Postgres) 基础设施
services.AddAevatarSupabase(
    connectionString: configuration.GetConnectionString("SupabasePostgres")!,
    configure: o =>
    {
        o.Schema = "aevatar";              // 建议独立 schema
        o.LockDownPublicAccess = true;    // 默认收紧权限（推荐）
        o.EnableRowLevelSecurity = false; // 默认关闭，避免误伤直连服务端
    });

// 2) 接入 Aevatar Agent System（替换默认内存 store）
services.AddAevatarAgentSystem(options =>
{
    options.StateStoreType = typeof(SupabaseStateStore<>);
    options.ConfigStoreType = typeof(SupabaseConfigStore<>);
    options.EventRouterStoreType = typeof(SupabaseEventRouterStore);
});

// 3) 可选：AI Memory
services.AddSupabaseAIMemory();
```

### 手工部署（SQL 审计友好）

- 默认脚本见：`docs/schema.sql`
- 或者在代码里用 `SupabaseSchemaScript.BuildSql(options)` 输出完整 SQL，再交给 DBA 执行。

### 权限与暴露建议（Supabase 场景）

- 默认 `Schema = aevatar`：**不放在 public**，降低被 PostgREST 暴露的概率。
- 默认 `LockDownPublicAccess = true`：**撤销 PUBLIC/anon/authenticated 权限**，防止 anon key 直接读写。
- 若你“就是要”通过 PostgREST 暴露：
  - 开启 `EnableRowLevelSecurity = true`
  - 并自行补充更细粒度 Policy（本库仅可选生成 service_role 全通 policy）

### 约束（很重要）

- `Schema/Table` 名称要求：**全小写 + 下划线**（`[a-z][a-z0-9_]*`），用于避免 SQL 注入与引号陷阱。
- `TState` 必须是 **Protobuf IMessage**（符合框架核心铁律）。


