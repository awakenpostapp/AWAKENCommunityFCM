-- Generated from Cloudflare D1 export.
-- Source: community-football-club-manager-d1.sql
-- Generated at: 2026-08-18T13:27:42.306Z
-- Do not use this file for Supabase Auth credentials; legacy password verifiers remain in public.users for the migration bridge.

CREATE TABLE tenants (
  id TEXT PRIMARY KEY,
  slug TEXT NOT NULL UNIQUE,
  display_name TEXT NOT NULL,
  owner_user_id TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'deleted')),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
, founder_status TEXT NOT NULL DEFAULT 'approved' CHECK (founder_status IN ('pending','approved','disabled')));

CREATE TABLE users (
  id TEXT PRIMARY KEY,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  username TEXT NOT NULL,
  username_normalized TEXT NOT NULL UNIQUE,
  email TEXT NOT NULL DEFAULT '',
  email_normalized TEXT NOT NULL DEFAULT '',
  password_hash TEXT NOT NULL,
  password_salt TEXT NOT NULL,
  password_iterations INTEGER NOT NULL DEFAULT 310000,
  role TEXT NOT NULL CHECK (role IN ('admin', 'founder', 'coach', 'trainee')),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  is_tuition_supported INTEGER NOT NULL DEFAULT 0 CHECK (is_tuition_supported IN (0, 1)),
  must_change_password INTEGER NOT NULL DEFAULT 0 CHECK (must_change_password IN (0, 1)),
  failed_login_count INTEGER NOT NULL DEFAULT 0,
  lockout_until TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  CHECK ((role = 'admin' AND tenant_id IS NULL) OR (role <> 'admin' AND tenant_id IS NOT NULL))
);

CREATE TABLE profiles (
  user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  full_name TEXT NOT NULL,
  photo_object_key TEXT NOT NULL DEFAULT '',
  phone TEXT NOT NULL DEFAULT '',
  email TEXT NOT NULL DEFAULT '',
  date_of_birth TEXT,
  height_cm DOUBLE PRECISION NOT NULL DEFAULT 0,
  weight_kg DOUBLE PRECISION NOT NULL DEFAULT 0,
  guardian_name TEXT NOT NULL DEFAULT '',
  guardian_phone TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL
, coach_position TEXT NOT NULL DEFAULT '');

CREATE TABLE auth_sessions (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  refresh_token_hash TEXT NOT NULL UNIQUE,
  device_name TEXT NOT NULL DEFAULT '',
  ip_hash TEXT NOT NULL DEFAULT '',
  user_agent TEXT NOT NULL DEFAULT '',
  expires_at TEXT NOT NULL,
  revoked_at TEXT,
  created_at TEXT NOT NULL,
  last_used_at TEXT NOT NULL
);

CREATE TABLE clubs (
  tenant_id TEXT PRIMARY KEY REFERENCES tenants(id) ON DELETE CASCADE,
  team_name TEXT NOT NULL,
  logo_object_key TEXT NOT NULL DEFAULT '',
  phone TEXT NOT NULL DEFAULT '',
  email TEXT NOT NULL DEFAULT '',
  bank_name TEXT NOT NULL DEFAULT '',
  bank_bin TEXT NOT NULL DEFAULT '',
  bank_account_number TEXT NOT NULL DEFAULT '',
  bank_account_name TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL
);

CREATE TABLE venues (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  name TEXT NOT NULL,
  address TEXT NOT NULL DEFAULT '',
  notes TEXT NOT NULL DEFAULT '',
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE classes (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  venue_id TEXT REFERENCES venues(id) ON DELETE SET NULL,
  name TEXT NOT NULL,
  schedule_days TEXT NOT NULL DEFAULT '',
  start_time_minutes INTEGER NOT NULL DEFAULT 1020,
  end_time_minutes INTEGER NOT NULL DEFAULT 1110,
  tuition_session_count INTEGER NOT NULL DEFAULT 4 CHECK (tuition_session_count > 0),
  default_cycle_fee_vnd INTEGER NOT NULL DEFAULT 0 CHECK (default_cycle_fee_vnd >= 0),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL, start_date TEXT NOT NULL DEFAULT '2026-01-01', evaluation_request_open INTEGER NOT NULL DEFAULT 0 CHECK (evaluation_request_open IN (0,1)),
  CHECK (start_time_minutes >= 0 AND start_time_minutes < 1440),
  CHECK (end_time_minutes > start_time_minutes AND end_time_minutes <= 1440)
);

CREATE TABLE class_coaches (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  coach_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  salary_per_session_vnd INTEGER NOT NULL DEFAULT 0 CHECK (salary_per_session_vnd >= 0),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  assigned_at TEXT NOT NULL,
  UNIQUE (class_id, coach_user_id)
);

CREATE TABLE class_enrollments (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  cycle_fee_vnd INTEGER NOT NULL DEFAULT 0 CHECK (cycle_fee_vnd >= 0),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  enrolled_at TEXT NOT NULL, is_trial INTEGER NOT NULL DEFAULT 0, trial_session_count INTEGER NOT NULL DEFAULT 0,
  UNIQUE (class_id, trainee_user_id)
);

CREATE TABLE training_sessions (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  session_date TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'draft' CHECK (status IN ('draft', 'submitted', 'locked')),
  submitted_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  submitted_at TEXT,
  override_reason TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE (class_id, session_date)
);

CREATE TABLE session_coaches (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  session_id TEXT NOT NULL REFERENCES training_sessions(id) ON DELETE CASCADE,
  coach_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  snapshotted_at TEXT NOT NULL,
  UNIQUE (session_id, coach_user_id)
);

CREATE TABLE coach_checkins (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  session_id TEXT NOT NULL REFERENCES training_sessions(id) ON DELETE CASCADE,
  coach_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  checkin_selfie_object_key TEXT NOT NULL,
  checkout_selfie_object_key TEXT NOT NULL DEFAULT '',
  salary_per_session_vnd_snapshot INTEGER NOT NULL DEFAULT 0,
  checked_in_at TEXT NOT NULL,
  checked_out_at TEXT,
  approval_status TEXT NOT NULL DEFAULT 'pending' CHECK (approval_status IN ('pending', 'approved', 'rejected')),
  reviewed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  reviewed_at TEXT,
  review_note TEXT NOT NULL DEFAULT '', duration_seconds INTEGER NOT NULL DEFAULT 0,
  UNIQUE (session_id, coach_user_id)
);

CREATE TABLE attendance_records (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  session_id TEXT NOT NULL REFERENCES training_sessions(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  status TEXT NOT NULL CHECK (status IN ('unmarked', 'present', 'late', 'absent', 'excused')),
  recorded_by_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE RESTRICT,
  recorded_at TEXT NOT NULL,
  notes TEXT NOT NULL DEFAULT '',
  revision INTEGER NOT NULL DEFAULT 1,
  UNIQUE (session_id, trainee_user_id)
);

CREATE TABLE tuition_invoices (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  enrollment_id TEXT NOT NULL REFERENCES class_enrollments(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  cycle_number INTEGER NOT NULL CHECK (cycle_number > 0),
  cycle_count INTEGER NOT NULL DEFAULT 1 CHECK (cycle_count > 0),
  cycle_fee_vnd INTEGER NOT NULL CHECK (cycle_fee_vnd >= 0),
  amount_vnd INTEGER NOT NULL CHECK (amount_vnd >= 0),
  attended_session_count INTEGER NOT NULL DEFAULT 0,
  planned_session_count INTEGER NOT NULL DEFAULT 0,
  due_date TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'proof_submitted', 'paid', 'rejected', 'overdue', 'waived')),
  payment_content TEXT NOT NULL,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  UNIQUE (enrollment_id, cycle_number)
);

CREATE TABLE payment_proofs (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  invoice_id TEXT NOT NULL REFERENCES tuition_invoices(id) ON DELETE CASCADE,
  image_object_key TEXT NOT NULL,
  note TEXT NOT NULL DEFAULT '',
  submitted_at TEXT NOT NULL,
  reviewed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  reviewed_at TEXT,
  review_status TEXT NOT NULL DEFAULT 'pending' CHECK (review_status IN ('pending', 'accepted', 'rejected'))
);

CREATE TABLE receipts (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  invoice_id TEXT NOT NULL UNIQUE REFERENCES tuition_invoices(id) ON DELETE CASCADE,
  receipt_number TEXT NOT NULL UNIQUE,
  team_name_snapshot TEXT NOT NULL,
  trainee_name_snapshot TEXT NOT NULL,
  class_name_snapshot TEXT NOT NULL,
  cycle_snapshot TEXT NOT NULL,
  amount_vnd_snapshot INTEGER NOT NULL,
  confirmed_by_name_snapshot TEXT NOT NULL,
  confirmed_at TEXT NOT NULL,
  pdf_object_key TEXT NOT NULL DEFAULT ''
);

CREATE TABLE coach_salaries (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  coach_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  period TEXT NOT NULL,
  amount_vnd INTEGER NOT NULL DEFAULT 0,
  due_date TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'paid')),
  paid_at TEXT,
  paid_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  notes TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL,
  UNIQUE (coach_user_id, period)
);

CREATE TABLE notifications (
  id TEXT PRIMARY KEY,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  recipient_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  kind TEXT NOT NULL,
  title TEXT NOT NULL,
  message TEXT NOT NULL,
  related_entity_id TEXT NOT NULL DEFAULT '',
  is_read INTEGER NOT NULL DEFAULT 0 CHECK (is_read IN (0, 1)),
  created_at TEXT NOT NULL
);

CREATE TABLE uploads (
  id TEXT PRIMARY KEY,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  owner_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  purpose TEXT NOT NULL CHECK (purpose IN ('avatar', 'club_logo', 'checkin_selfie', 'checkout_selfie', 'payment_proof', 'receipt')),
  object_key TEXT NOT NULL UNIQUE,
  content_type TEXT NOT NULL,
  byte_size INTEGER NOT NULL,
  sha256 TEXT NOT NULL,
  created_at TEXT NOT NULL
);

CREATE TABLE sync_cursors (
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  device_id TEXT NOT NULL,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  last_client_mutation_id TEXT NOT NULL DEFAULT '',
  last_sync_at TEXT NOT NULL,
  PRIMARY KEY (user_id, device_id)
);

CREATE TABLE idempotency_keys (
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  idempotency_key TEXT NOT NULL,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  response_status INTEGER NOT NULL,
  response_json TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL,
  PRIMARY KEY (user_id, idempotency_key)
);

CREATE TABLE audit_logs (
  id TEXT PRIMARY KEY,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE SET NULL,
  actor_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  action TEXT NOT NULL,
  entity_type TEXT NOT NULL,
  entity_id TEXT NOT NULL,
  details_json TEXT NOT NULL DEFAULT '{}',
  created_at TEXT NOT NULL
);

CREATE TABLE external_account_links (id TEXT PRIMARY KEY, user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE, provider TEXT NOT NULL CHECK (provider IN ('google','apple')), subject TEXT NOT NULL, email TEXT NOT NULL DEFAULT '', display_name TEXT NOT NULL DEFAULT '', linked_at TEXT NOT NULL, updated_at TEXT NOT NULL, UNIQUE (provider, subject), UNIQUE (user_id, provider));

CREATE TABLE oauth_states (state TEXT PRIMARY KEY, provider TEXT NOT NULL CHECK (provider IN ('google','apple')), redirect_uri TEXT NOT NULL, code_verifier TEXT NOT NULL, expires_at TEXT NOT NULL, created_at TEXT NOT NULL);

CREATE TABLE oauth_exchange_tickets (ticket TEXT PRIMARY KEY, provider TEXT NOT NULL CHECK (provider IN ('google','apple')), subject TEXT NOT NULL, email TEXT NOT NULL DEFAULT '', display_name TEXT NOT NULL DEFAULT '', expires_at TEXT NOT NULL, used_at TEXT, created_at TEXT NOT NULL);

CREATE TABLE IF NOT EXISTS "d1_migrations"(
		id         BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,
		name       TEXT UNIQUE,
		applied_at TIMESTAMPTZ DEFAULT CURRENT_TIMESTAMP NOT NULL
);

CREATE TABLE public_registration_requests (
  idempotency_key TEXT PRIMARY KEY,
  username_normalized TEXT NOT NULL,
  response_json TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE public_registration_attempts (
  id TEXT PRIMARY KEY,
  ip_address TEXT NOT NULL,
  username_normalized TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE TABLE password_reset_tokens (
  id TEXT PRIMARY KEY,
  user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  token_hash TEXT NOT NULL UNIQUE,
  expires_at TEXT NOT NULL,
  used_at TEXT,
  requested_at TEXT NOT NULL,
  requested_ip_hash TEXT NOT NULL DEFAULT ''
);

CREATE TABLE trainee_evaluations (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  coach_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  evaluation_type TEXT NOT NULL CHECK (evaluation_type IN ('periodic', 'tournament_match')),
  title TEXT NOT NULL DEFAULT '',
  evaluation_date TEXT NOT NULL,
  overall_score INTEGER NOT NULL DEFAULT 0 CHECK (overall_score BETWEEN 0 AND 5),
  technical_score INTEGER NOT NULL DEFAULT 0 CHECK (technical_score BETWEEN 0 AND 5),
  tactical_score INTEGER NOT NULL DEFAULT 0 CHECK (tactical_score BETWEEN 0 AND 5),
  physical_score INTEGER NOT NULL DEFAULT 0 CHECK (physical_score BETWEEN 0 AND 5),
  attitude_score INTEGER NOT NULL DEFAULT 0 CHECK (attitude_score BETWEEN 0 AND 5),
  strengths TEXT NOT NULL DEFAULT '',
  improvements TEXT NOT NULL DEFAULT '',
  notes TEXT NOT NULL DEFAULT '',
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected')),
  review_note TEXT NOT NULL DEFAULT '',
  reviewed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  reviewed_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_users_tenant_role ON users(tenant_id, role, is_active);

CREATE INDEX IF NOT EXISTS idx_users_email ON users(email_normalized);

CREATE INDEX IF NOT EXISTS idx_profiles_tenant_name ON profiles(tenant_id, full_name);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_user ON auth_sessions(user_id, revoked_at, expires_at);

CREATE INDEX IF NOT EXISTS idx_venues_tenant ON venues(tenant_id, is_active, name);

CREATE INDEX IF NOT EXISTS idx_classes_tenant ON classes(tenant_id, is_active, name);

CREATE INDEX IF NOT EXISTS idx_class_coaches_user ON class_coaches(tenant_id, coach_user_id, is_active);

CREATE INDEX IF NOT EXISTS idx_enrollments_user ON class_enrollments(tenant_id, trainee_user_id, is_active);

CREATE INDEX IF NOT EXISTS idx_sessions_tenant_date ON training_sessions(tenant_id, session_date, status);

CREATE INDEX IF NOT EXISTS idx_checkins_review ON coach_checkins(tenant_id, approval_status, checked_in_at);

CREATE INDEX IF NOT EXISTS idx_attendance_trainee ON attendance_records(tenant_id, trainee_user_id, recorded_at);

CREATE INDEX IF NOT EXISTS idx_tuition_status ON tuition_invoices(tenant_id, trainee_user_id, status, due_date);

CREATE INDEX IF NOT EXISTS idx_payment_proofs_invoice ON payment_proofs(tenant_id, invoice_id, submitted_at);

CREATE INDEX IF NOT EXISTS idx_notifications_recipient ON notifications(recipient_user_id, is_read, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_uploads_owner ON uploads(owner_user_id, purpose, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_idempotency_expiry ON idempotency_keys(expires_at);

CREATE INDEX IF NOT EXISTS idx_audit_tenant_time ON audit_logs(tenant_id, created_at DESC);

CREATE INDEX IF NOT EXISTS idx_external_links_user ON external_account_links(user_id, provider);

CREATE INDEX IF NOT EXISTS idx_oauth_tickets_expiry ON oauth_exchange_tickets(expires_at, used_at);

CREATE INDEX IF NOT EXISTS idx_public_registration_expiry
  ON public_registration_requests(expires_at);

CREATE INDEX IF NOT EXISTS idx_public_registration_attempts_ip_time
  ON public_registration_attempts(ip_address, created_at);

CREATE INDEX IF NOT EXISTS idx_public_registration_attempts_expiry
  ON public_registration_attempts(expires_at);

CREATE INDEX IF NOT EXISTS idx_password_reset_tokens_lookup
  ON password_reset_tokens(token_hash, expires_at, used_at);

CREATE INDEX IF NOT EXISTS idx_password_reset_tokens_user
  ON password_reset_tokens(user_id, requested_at DESC);

CREATE INDEX IF NOT EXISTS idx_session_coaches_tenant_coach
  ON session_coaches(tenant_id, coach_user_id, session_id);

CREATE INDEX IF NOT EXISTS idx_checkins_tenant_coach_date
  ON coach_checkins(tenant_id, coach_user_id, checked_in_at DESC);

CREATE INDEX IF NOT EXISTS idx_checkins_session_coach_open
  ON coach_checkins(tenant_id, session_id, coach_user_id, checked_out_at);

CREATE INDEX IF NOT EXISTS idx_attendance_session_trainee
  ON attendance_records(tenant_id, session_id, trainee_user_id);

CREATE INDEX IF NOT EXISTS idx_tuition_enrollment_cycle
  ON tuition_invoices(tenant_id, enrollment_id, cycle_number);

CREATE INDEX IF NOT EXISTS idx_uploads_tenant_object
  ON uploads(tenant_id, object_key);

CREATE INDEX IF NOT EXISTS idx_auth_sessions_expiry
  ON auth_sessions(expires_at, revoked_at);

CREATE INDEX IF NOT EXISTS idx_profiles_tenant_coach_position
  ON profiles(tenant_id, coach_position);

CREATE INDEX IF NOT EXISTS idx_evaluations_trainee_history ON trainee_evaluations(tenant_id, trainee_user_id, evaluation_date DESC);

CREATE INDEX IF NOT EXISTS idx_evaluations_class_history ON trainee_evaluations(tenant_id, class_id, evaluation_date DESC);

CREATE INDEX IF NOT EXISTS idx_evaluations_coach_history ON trainee_evaluations(tenant_id, coach_user_id, evaluation_date DESC);

CREATE INDEX IF NOT EXISTS idx_classes_evaluation_request ON classes(tenant_id, evaluation_request_open, is_active);
