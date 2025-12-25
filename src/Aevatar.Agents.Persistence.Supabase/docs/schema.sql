-- Aevatar Supabase(Postgres) Persistence Schema (default)
-- -------------------------------------------------------
-- This script matches default SupabasePersistenceOptions:
-- - Schema: aevatar
-- - Tables:
--   - agent_states
--   - agent_configs
--   - agent_event_router_hierarchies
--   - ai_memory_messages
-- - LockDownPublicAccess: true
-- - EnableRowLevelSecurity: false

CREATE SCHEMA IF NOT EXISTS aevatar;

-- ==============================
-- Tables
-- ==============================

CREATE TABLE IF NOT EXISTS aevatar.agent_states (
  state_type text NOT NULL,
  agent_id text NOT NULL,
  state_data bytea NOT NULL,
  version bigint NOT NULL DEFAULT 1,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (state_type, agent_id)
);

CREATE TABLE IF NOT EXISTS aevatar.agent_configs (
  config_type text NOT NULL,
  agent_type text NOT NULL,
  agent_id text NOT NULL,
  config_data jsonb NOT NULL,
  updated_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (config_type, agent_type, agent_id)
);

CREATE TABLE IF NOT EXISTS aevatar.agent_event_router_hierarchies (
  agent_id text PRIMARY KEY,
  parent_id text NULL,
  children_ids text[] NOT NULL DEFAULT '{}'::text[],
  updated_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS aevatar.ai_memory_messages (
  id uuid PRIMARY KEY,
  agent_id text NOT NULL,
  session_id text NULL,
  role text NOT NULL,
  content text NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

-- ==============================
-- Indexes
-- ==============================

CREATE INDEX IF NOT EXISTS idx_agent_states_updated_at ON aevatar.agent_states (updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_agent_states_agent_version ON aevatar.agent_states (agent_id, version);

CREATE INDEX IF NOT EXISTS idx_agent_configs_updated_at ON aevatar.agent_configs (updated_at DESC);

CREATE INDEX IF NOT EXISTS idx_agent_event_router_hierarchies_parent_id ON aevatar.agent_event_router_hierarchies (parent_id);
CREATE INDEX IF NOT EXISTS idx_agent_event_router_hierarchies_updated_at ON aevatar.agent_event_router_hierarchies (updated_at DESC);

CREATE INDEX IF NOT EXISTS idx_ai_memory_messages_agent_created_at ON aevatar.ai_memory_messages (agent_id, created_at DESC);
CREATE INDEX IF NOT EXISTS idx_ai_memory_messages_agent_session_created_at ON aevatar.ai_memory_messages (agent_id, session_id, created_at DESC);

-- Full-Text Search index (config: simple)
CREATE INDEX IF NOT EXISTS idx_ai_memory_messages_content_fts
  ON aevatar.ai_memory_messages
  USING gin (to_tsvector('simple', content));

-- ==============================
-- Permissions (Lock down)
-- ==============================

REVOKE ALL ON SCHEMA aevatar FROM PUBLIC;
REVOKE ALL ON TABLE aevatar.agent_states FROM PUBLIC;
REVOKE ALL ON TABLE aevatar.agent_configs FROM PUBLIC;
REVOKE ALL ON TABLE aevatar.agent_event_router_hierarchies FROM PUBLIC;
REVOKE ALL ON TABLE aevatar.ai_memory_messages FROM PUBLIC;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'anon') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA aevatar FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_states FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_configs FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_event_router_hierarchies FROM anon';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.ai_memory_messages FROM anon';
  END IF;
END
$$;

DO $$
BEGIN
  IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'authenticated') THEN
    EXECUTE 'REVOKE ALL ON SCHEMA aevatar FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_states FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_configs FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.agent_event_router_hierarchies FROM authenticated';
    EXECUTE 'REVOKE ALL ON TABLE aevatar.ai_memory_messages FROM authenticated';
  END IF;
END
$$;


