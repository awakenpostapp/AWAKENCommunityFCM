ALTER TABLE classes ADD COLUMN start_date TEXT NOT NULL DEFAULT '2026-01-01';

UPDATE classes
SET start_date = substr(created_at, 1, 10)
WHERE start_date = '2026-01-01'
  AND length(created_at) >= 10;
