-- A class may have one optional operational Manager. Keep this additive so
-- existing classes and historical attendance/finance rows survive.
ALTER TABLE classes ADD COLUMN manager_user_id TEXT REFERENCES users(id) ON DELETE SET NULL;

CREATE INDEX idx_classes_manager
  ON classes(tenant_id, manager_user_id, is_active);
