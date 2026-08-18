CREATE TABLE IF NOT EXISTS auth_user_links (
  app_user_id TEXT PRIMARY KEY,
  auth_user_id TEXT NOT NULL UNIQUE,
  provider TEXT NOT NULL DEFAULT 'supabase_auth',
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS auth_user_links_auth_user_idx
  ON auth_user_links (auth_user_id);
