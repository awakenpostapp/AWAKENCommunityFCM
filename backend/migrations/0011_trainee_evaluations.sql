PRAGMA foreign_keys = ON;

CREATE TABLE IF NOT EXISTS trainee_evaluations (
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

CREATE INDEX IF NOT EXISTS idx_evaluations_trainee_history
  ON trainee_evaluations(tenant_id, trainee_user_id, evaluation_date DESC);
CREATE INDEX IF NOT EXISTS idx_evaluations_class_history
  ON trainee_evaluations(tenant_id, class_id, evaluation_date DESC);
CREATE INDEX IF NOT EXISTS idx_evaluations_coach_history
  ON trainee_evaluations(tenant_id, coach_user_id, evaluation_date DESC);
