PRAGMA foreign_keys = ON;

-- Shared catalog: tenant-independent so every team renders the same source
-- badges while a later asset upload can replace only asset_key metadata.
CREATE TABLE IF NOT EXISTS achievement_badges (
  id TEXT PRIMARY KEY,
  key TEXT NOT NULL UNIQUE,
  name TEXT NOT NULL,
  category TEXT NOT NULL CHECK (category IN ('match_ranking', 'weekly_class_ranking')),
  asset_key TEXT NOT NULL DEFAULT '',
  display_size TEXT NOT NULL DEFAULT 'medium' CHECK (display_size IN ('hero', 'medium', 'compact')),
  points INTEGER NOT NULL CHECK (points IN (500, 150, 100, 60, 30, 20, 15, 10, -10, -30)),
  sort_order INTEGER NOT NULL DEFAULT 0,
  is_active INTEGER NOT NULL DEFAULT 1 CHECK (is_active IN (0, 1)),
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE TABLE IF NOT EXISTS trainee_achievements (
  id TEXT PRIMARY KEY,
  tenant_id TEXT NOT NULL REFERENCES tenants(id) ON DELETE CASCADE,
  trainee_user_id TEXT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
  badge_id TEXT NOT NULL REFERENCES achievement_badges(id) ON DELETE RESTRICT,
  class_id TEXT REFERENCES classes(id) ON DELETE SET NULL,
  category TEXT NOT NULL CHECK (category IN ('match_ranking', 'weekly_class_ranking')),
  title TEXT NOT NULL DEFAULT '',
  event_name TEXT NOT NULL DEFAULT '',
  reason TEXT NOT NULL DEFAULT '',
  awarded_for_date TEXT NOT NULL,
  points_snapshot INTEGER NOT NULL CHECK (points_snapshot IN (500, 150, 100, 60, 30, 20, 15, 10, -10, -30)),
  status TEXT NOT NULL DEFAULT 'pending' CHECK (status IN ('pending', 'approved', 'rejected', 'removed', 'expired')),
  created_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  reviewed_by_user_id TEXT REFERENCES users(id) ON DELETE SET NULL,
  reviewed_at TEXT,
  review_note TEXT NOT NULL DEFAULT '',
  visible_until TEXT NOT NULL,
  removed_at TEXT,
  created_at TEXT NOT NULL,
  updated_at TEXT NOT NULL
);

CREATE INDEX IF NOT EXISTS idx_achievement_badges_active
  ON achievement_badges(is_active, category, sort_order);
CREATE INDEX IF NOT EXISTS idx_achievements_tenant_status
  ON trainee_achievements(tenant_id, status, updated_at DESC);
CREATE INDEX IF NOT EXISTS idx_achievements_trainee_visibility
  ON trainee_achievements(tenant_id, trainee_user_id, status, visible_until DESC);
CREATE INDEX IF NOT EXISTS idx_achievements_category_date
  ON trainee_achievements(tenant_id, category, awarded_for_date DESC);
CREATE INDEX IF NOT EXISTS idx_achievements_creator
  ON trainee_achievements(tenant_id, created_by_user_id, status, created_at DESC);

INSERT OR IGNORE INTO achievement_badges
  (id, key, name, category, asset_key, display_size, points, sort_order, is_active, created_at, updated_at)
VALUES
  ('badge_cup_ngoai_hang', 'cup_ngoai_hang', 'Cup Ngoại Hạng', 'match_ranking', 'achievement/cup_ngoai_hang', 'hero', 500, 10, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_cup_hang_1', 'cup_hang_1', 'Cup Hạng 1', 'match_ranking', 'achievement/cup_hang_1', 'hero', 150, 20, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_cup_hang_2', 'cup_hang_2', 'Cup Hạng 2', 'match_ranking', 'achievement/cup_hang_2', 'hero', 100, 30, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_cup_hang_3', 'cup_hang_3', 'Cup Hạng 3', 'match_ranking', 'achievement/cup_hang_3', 'hero', 60, 40, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_huy_chuong_vang', 'huy_chuong_vang', 'Huy Chương Vàng', 'match_ranking', 'achievement/huy_chuong_vang', 'hero', 150, 50, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_huy_chuong_bac', 'huy_chuong_bac', 'Huy Chương Bạc', 'match_ranking', 'achievement/huy_chuong_bac', 'hero', 100, 60, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_huy_chuong_dong', 'huy_chuong_dong', 'Huy Chương Đồng', 'match_ranking', 'achievement/huy_chuong_dong', 'hero', 60, 70, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_gang_tay_vang', 'gang_tay_vang', 'Găng Tay Vàng', 'match_ranking', 'achievement/gang_tay_vang', 'hero', 100, 80, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_qua_bong_vang', 'qua_bong_vang', 'Quả Bóng Vàng', 'match_ranking', 'achievement/qua_bong_vang', 'hero', 100, 90, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_cau_thu_xuat_sac', 'cau_thu_xuat_sac', 'Cầu Thủ Xuất Sắc', 'match_ranking', 'achievement/cau_thu_xuat_sac', 'hero', 100, 100, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_vong_nguyet_que', 'vong_nguyet_que', 'Vòng Nguyệt Quế', 'match_ranking', 'achievement/vong_nguyet_que', 'hero', 60, 110, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_the_vang', 'the_vang', 'Thẻ Vàng', 'match_ranking', 'achievement/the_vang', 'compact', -10, 120, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_the_do', 'the_do', 'Thẻ Đỏ', 'match_ranking', 'achievement/the_do', 'compact', -30, 130, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_tham_gia', 'tham_gia', 'Tham Gia', 'weekly_class_ranking', 'achievement/tham_gia', 'medium', 10, 200, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_tich_cuc', 'tich_cuc', 'Tích Cực', 'weekly_class_ranking', 'achievement/tich_cuc', 'medium', 15, 210, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_ghi_ban', 'ghi_ban', 'Ghi Bàn', 'weekly_class_ranking', 'achievement/ghi_ban', 'medium', 15, 220, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_giu_sach_luoi', 'giu_sach_luoi', 'Giữ Sạch Lưới', 'weekly_class_ranking', 'achievement/giu_sach_luoi', 'medium', 20, 230, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_fair_play', 'fair_play', 'Fair Play', 'weekly_class_ranking', 'achievement/fair_play', 'medium', 10, 240, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_tinh_than_tot', 'tinh_than_tot', 'Tinh Thần Tốt', 'weekly_class_ranking', 'achievement/tinh_than_tot', 'medium', 10, 250, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_tien_bo', 'tien_bo', 'Tiến Bộ', 'weekly_class_ranking', 'achievement/tien_bo', 'medium', 20, 260, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z'),
  ('badge_no_luc_xuat_sac', 'no_luc_xuat_sac', 'Nỗ Lực Xuất Sắc', 'weekly_class_ranking', 'achievement/no_luc_xuat_sac', 'medium', 30, 270, 1, '2026-08-27T00:00:00.000Z', '2026-08-27T00:00:00.000Z');
