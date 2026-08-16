ALTER TABLE class_enrollments ADD COLUMN is_trial INTEGER NOT NULL DEFAULT 0;
ALTER TABLE class_enrollments ADD COLUMN trial_session_count INTEGER NOT NULL DEFAULT 0;
