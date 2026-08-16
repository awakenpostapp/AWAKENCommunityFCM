PRAGMA foreign_keys = ON;

CREATE TABLE tenants (
  id TEXT PRIMARY KEY,
  slug TEXT NOT NULL UNIQUE COLLATE NOCASE,
  display_name TEXT NOT NULL,
  owner_user_id TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'active' CHECK (status IN ('active', 'suspended', 'deleted')),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

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

CREATE INDEX idx_users_tenant_role ON users(tenant_id, role, is_active);
CREATE INDEX idx_users_email ON users(email_normalized);

CREATE TABLE profiles (
  user_id TEXT PRIMARY KEY REFERENCES users(id) ON DELETE CASCADE,
  tenant_id TEXT REFERENCES tenants(id) ON DELETE CASCADE,
  full_name TEXT NOT NULL,
  photo_object_key TEXT NOT NULL DEFAULT '',
  phone TEXT NOT NULL DEFAULT '',
  email TEXT NOT NULL DEFAULT '',
  date_of_birth TEXT,
  height_cm REAL NOT NULL DEFAULT 0,
  weight_kg REAL NOT NULL DEFAULT 0,
  guardian_name TEXT NOT NULL DEFAULT '',
  guardian_phone TEXT NOT NULL DEFAULT '',
  updated_at TEXT NOT NULL
);

CREATE INDEX idx_profiles_tenant_name ON profiles(tenant_id, full_name);

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

CREATE INDEX idx_auth_sessions_user ON auth_sessions(user_id, revoked_at, expires_at);

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

CREATE INDEX idx_venues_tenant ON venues(tenant_id, is_active, name);

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
  updated_at TEXT NOT NULL,
  CHECK (start_time_minutes >= 0 AND start_time_minutes < 1440),
  CHECK (end_time_minutes > start_time_minutes AND end_time_minutes <= 1440)
);

CREATE INDEX idx_classes_tenant ON classes(tenant_id, is_active, name);

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

CREATE INDEX idx_class_coaches_user ON class_coaches(tenant_id, coach_user_id, is_active);

CREATE TABLE class_enrollments (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  class_id TEXT NOT NULL REFERENCES classes(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  cycle_fee_vnd INTEGER NOT NULL DEFAULT 0 CHECK (cycle_fee_vnd >= 0),
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  enrolled_at TEXT NOT NULL,
  UNIQUE (class_id, trainee_user_id)
);

CREATE INDEX idx_enrollments_user ON class_enrollments(tenant_id, trainee_user_id, is_active);

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

CREATE INDEX idx_sessions_tenant_date ON training_sessions(tenant_id, session_date, status);

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
  review_note TEXT NOT NULL DEFAULT '',
  UNIQUE (session_id, coach_user_id)
);

CREATE INDEX idx_checkins_review ON coach_checkins(tenant_id, approval_status, checked_in_at);

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

CREATE INDEX idx_attendance_trainee ON attendance_records(tenant_id, trainee_user_id, recorded_at);

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

CREATE INDEX idx_tuition_status ON tuition_invoices(tenant_id, trainee_user_id, status, due_date);

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

CREATE INDEX idx_payment_proofs_invoice ON payment_proofs(tenant_id, invoice_id, submitted_at);

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

CREATE INDEX idx_notifications_recipient ON notifications(recipient_user_id, is_read, created_at DESC);

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

CREATE INDEX idx_uploads_owner ON uploads(owner_user_id, purpose, created_at DESC);

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

CREATE INDEX idx_idempotency_expiry ON idempotency_keys(expires_at);

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

CREATE INDEX idx_audit_tenant_time ON audit_logs(tenant_id, created_at DESC);
