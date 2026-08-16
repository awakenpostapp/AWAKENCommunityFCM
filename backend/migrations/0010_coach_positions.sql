-- Store a stable catalog key for each Coach teaching position.
-- Additive migration: existing profiles remain valid with an empty position.
ALTER TABLE profiles ADD COLUMN coach_position TEXT NOT NULL DEFAULT '';

CREATE INDEX idx_profiles_tenant_coach_position
  ON profiles(tenant_id, coach_position);
