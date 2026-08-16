-- Capture the teaching timer result at checkout. Open check-ins remain zero
-- and the client derives their live elapsed value from checked_in_at.
ALTER TABLE coach_checkins ADD COLUMN duration_seconds INTEGER NOT NULL DEFAULT 0;

UPDATE coach_checkins
SET duration_seconds = CAST(MAX(0, (julianday(checked_out_at) - julianday(checked_in_at)) * 86400) AS INTEGER)
WHERE duration_seconds = 0 AND checked_out_at IS NOT NULL;
