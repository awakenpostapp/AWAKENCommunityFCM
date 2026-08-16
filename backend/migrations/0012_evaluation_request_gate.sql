PRAGMA foreign_keys = ON;

-- A Founder explicitly opens this gate before a Coach may create or revise
-- trainee evaluations for the class. Existing classes remain closed until
-- the Founder opens a request in the app.
ALTER TABLE classes ADD COLUMN evaluation_request_open INTEGER NOT NULL DEFAULT 0
  CHECK (evaluation_request_open IN (0, 1));

CREATE INDEX IF NOT EXISTS idx_classes_evaluation_request
  ON classes(tenant_id, evaluation_request_open, is_active);
