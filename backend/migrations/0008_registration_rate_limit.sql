CREATE TABLE IF NOT EXISTS public_registration_attempts (
  id TEXT PRIMARY KEY,
  ip_address TEXT NOT NULL,
  username_normalized TEXT NOT NULL,
  created_at TEXT NOT NULL,
  expires_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_public_registration_attempts_ip_time
  ON public_registration_attempts(ip_address, created_at);

CREATE INDEX IF NOT EXISTS idx_public_registration_attempts_expiry
  ON public_registration_attempts(expires_at);
