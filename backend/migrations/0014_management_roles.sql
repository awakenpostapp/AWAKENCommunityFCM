-- Expand the tenant user role constraint without rewriting any existing row.
-- This migration is intentionally idempotent at the table/index level only
-- because Wrangler applies each numbered migration once.
PRAGMA foreign_keys = OFF;

CREATE TABLE users_management_roles (
  id TEXT PRIMARY KEY,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  username TEXT NOT NULL,
  username_normalized TEXT NOT NULL UNIQUE,
  email TEXT NOT NULL DEFAULT '',
  email_normalized TEXT NOT NULL DEFAULT '',
  password_hash TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  password_iterations INTEGER NOT NULL DEFAULT 310000,
  role TEXT NOT NULL CHECK (role IN ('admin', 'founder', 'co_founder', 'manager', 'coach', 'trainee')),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  is_tuition_supported INTEGER NOT NULL DEFAULT 0 CHECK (is_tuition_supported IN (0, 1)),
  must_change_password INTEGER NOT NULL DEFAULT 0 CHECK (must_change_password IN (0, 1)),
  failed_login_count INTEGER NOT NULL DEFAULT 0,
  lockout_until TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK ((role = 'admin' AND tenant_id IS NULL) OR (role <> 'admin' AND tenant_id IS NOT NULL))
);

INSERT INTO users_management_roles (
  id, tenant_id, username, username_normalized, email, email_normalized,
  password_hash, password_salt, password_iterations, role, is_active,
  is_tuition_supported, must_change_password, failed_login_count,
  lockout_until, created_at, updated_at
)
SELECT id, tenant_id, username, username_normalized, email, email_normalized,
  password_hash, password_salt, password_iterations, role, is_active,
  is_tuition_supported, must_change_password, failed_login_count,
  lockout_until, created_at, updated_at
FROM users;

DROP TABLE users;
ALTER TABLE users_management_roles RENAME TO users;
CREATE INDEX idx_users_tenant_role ON users(tenant_id, role, is_active);
CREATE INDEX idx_users_email ON users(email_normalized);

PRAGMA foreign_keys = ON;
PRAGMA foreign_key_check;
