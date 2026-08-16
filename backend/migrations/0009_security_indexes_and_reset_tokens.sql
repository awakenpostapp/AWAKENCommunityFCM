-- Security and query-shape hardening for production D1.
-- Keep this migration additive: existing tenant data and object keys are untouched.

CREATE TABLE password_reset_tokens (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  used_at TEXT,
  requested_at TEXT NOT NULL,
  requested_ip_hash TEXT NOT NULL DEFAULT ''
);

CREATE INDEX idx_password_reset_tokens_lookup
  ON password_reset_tokens(token_hash, expires_at, used_at);

CREATE INDEX idx_password_reset_tokens_user
  ON password_reset_tokens(user_id, requested_at DESC);

CREATE INDEX idx_session_coaches_tenant_coach
  ON session_coaches(tenant_id, coach_user_id, session_id);

CREATE INDEX idx_checkins_tenant_coach_date
  ON coach_checkins(tenant_id, coach_user_id, checked_in_at DESC);

CREATE INDEX idx_checkins_session_coach_open
  ON coach_checkins(tenant_id, session_id, coach_user_id, checked_out_at);

CREATE INDEX idx_attendance_session_trainee
  ON attendance_records(tenant_id, session_id, trainee_user_id);

CREATE INDEX idx_tuition_enrollment_cycle
  ON tuition_invoices(tenant_id, enrollment_id, cycle_number);

CREATE INDEX idx_uploads_tenant_object
  ON uploads(tenant_id, object_key);

CREATE INDEX idx_auth_sessions_expiry
  ON auth_sessions(expires_at, revoked_at);
