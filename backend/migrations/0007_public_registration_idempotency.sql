CREATE TABLE IF NOT EXISTS public_registration_requests (
  idempotency_key TEXT PRIMARY KEY,
  username_normalized TEXT NOT NULL,
  response_json TEXT NOT NULL DEFAULT '',
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_public_registration_expiry
  ON public_registration_requests(expires_at);
