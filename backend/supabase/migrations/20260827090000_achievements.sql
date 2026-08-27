begin;

create table if not exists public.achievement_badges (
  id text primary key,
  key text not null unique,
  name text not null,
  category text not null check (category in ('match_ranking', 'weekly_class_ranking')),
  asset_key text not null default '',
  display_size text not null default 'medium' check (display_size in ('hero', 'medium', 'compact')),
  points integer not null check (points in (500, 150, 100, 60, 30, 20, 15, 10, -10, -30)),
  sort_order integer not null default 0,
  is_active integer not null default 1 check (is_active in (0, 1)),
  created_at timestamptz not null,
  updated_at timestamptz not null
);

create table if not exists public.trainee_achievements (
  id text primary key,
  tenant_id text not null references public.tenants(id) on delete cascade,
  trainee_user_id text not null references public.users(id) on delete cascade,
  badge_id text not null references public.achievement_badges(id) on delete restrict,
  class_id text references public.classes(id) on delete set null,
  category text not null check (category in ('match_ranking', 'weekly_class_ranking')),
  title text not null default '',
  event_name text not null default '',
  reason text not null default '',
  awarded_for_date date not null,
  points_snapshot integer not null check (points_snapshot in (500, 150, 100, 60, 30, 20, 15, 10, -10, -30)),
  status text not null default 'pending' check (status in ('pending', 'approved', 'rejected', 'removed', 'expired')),
  created_by_user_id text references public.users(id) on delete set null,
  reviewed_by_user_id text references public.users(id) on delete set null,
  reviewed_at timestamptz,
  review_note text not null default '',
  visible_until timestamptz not null,
  removed_at timestamptz,
  created_at timestamptz not null,
  updated_at timestamptz not null
);

create index if not exists idx_achievement_badges_active
  on public.achievement_badges(is_active, category, sort_order);
create index if not exists idx_achievements_tenant_status
  on public.trainee_achievements(tenant_id, status, updated_at desc);
create index if not exists idx_achievements_trainee_visibility
  on public.trainee_achievements(tenant_id, trainee_user_id, status, visible_until desc);
create index if not exists idx_achievements_category_date
  on public.trainee_achievements(tenant_id, category, awarded_for_date desc);
create index if not exists idx_achievements_creator
  on public.trainee_achievements(tenant_id, created_by_user_id, status, created_at desc);

insert into public.achievement_badges
  (id, key, name, category, asset_key, display_size, points, sort_order, is_active, created_at, updated_at)
values
  ('badge_cup_ngoai_hang', 'cup_ngoai_hang', 'Cup Ngoại Hạng', 'match_ranking', 'achievement/cup_ngoai_hang', 'hero', 500, 10, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_cup_hang_1', 'cup_hang_1', 'Cup Hạng 1', 'match_ranking', 'achievement/cup_hang_1', 'hero', 150, 20, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_cup_hang_2', 'cup_hang_2', 'Cup Hạng 2', 'match_ranking', 'achievement/cup_hang_2', 'hero', 100, 30, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_cup_hang_3', 'cup_hang_3', 'Cup Hạng 3', 'match_ranking', 'achievement/cup_hang_3', 'hero', 60, 40, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_huy_chuong_vang', 'huy_chuong_vang', 'Huy Chương Vàng', 'match_ranking', 'achievement/huy_chuong_vang', 'hero', 150, 50, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_huy_chuong_bac', 'huy_chuong_bac', 'Huy Chương Bạc', 'match_ranking', 'achievement/huy_chuong_bac', 'hero', 100, 60, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_huy_chuong_dong', 'huy_chuong_dong', 'Huy Chương Đồng', 'match_ranking', 'achievement/huy_chuong_dong', 'hero', 60, 70, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_gang_tay_vang', 'gang_tay_vang', 'Găng Tay Vàng', 'match_ranking', 'achievement/gang_tay_vang', 'hero', 100, 80, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_qua_bong_vang', 'qua_bong_vang', 'Quả Bóng Vàng', 'match_ranking', 'achievement/qua_bong_vang', 'hero', 100, 90, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_cau_thu_xuat_sac', 'cau_thu_xuat_sac', 'Cầu Thủ Xuất Sắc', 'match_ranking', 'achievement/cau_thu_xuat_sac', 'hero', 100, 100, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_vong_nguyet_que', 'vong_nguyet_que', 'Vòng Nguyệt Quế', 'match_ranking', 'achievement/vong_nguyet_que', 'hero', 60, 110, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_the_vang', 'the_vang', 'Thẻ Vàng', 'match_ranking', 'achievement/the_vang', 'compact', -10, 120, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_the_do', 'the_do', 'Thẻ Đỏ', 'match_ranking', 'achievement/the_do', 'compact', -30, 130, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_tham_gia', 'tham_gia', 'Tham Gia', 'weekly_class_ranking', 'achievement/tham_gia', 'medium', 10, 200, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_tich_cuc', 'tich_cuc', 'Tích Cực', 'weekly_class_ranking', 'achievement/tich_cuc', 'medium', 15, 210, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_ghi_ban', 'ghi_ban', 'Ghi Bàn', 'weekly_class_ranking', 'achievement/ghi_ban', 'medium', 15, 220, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_giu_sach_luoi', 'giu_sach_luoi', 'Giữ Sạch Lưới', 'weekly_class_ranking', 'achievement/giu_sach_luoi', 'medium', 20, 230, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_fair_play', 'fair_play', 'Fair Play', 'weekly_class_ranking', 'achievement/fair_play', 'medium', 10, 240, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_tinh_than_tot', 'tinh_than_tot', 'Tinh Thần Tốt', 'weekly_class_ranking', 'achievement/tinh_than_tot', 'medium', 10, 250, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_tien_bo', 'tien_bo', 'Tiến Bộ', 'weekly_class_ranking', 'achievement/tien_bo', 'medium', 20, 260, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z'),
  ('badge_no_luc_xuat_sac', 'no_luc_xuat_sac', 'Nỗ Lực Xuất Sắc', 'weekly_class_ranking', 'achievement/no_luc_xuat_sac', 'medium', 30, 270, 1, '2026-08-27T00:00:00Z', '2026-08-27T00:00:00Z')
on conflict (key) do nothing;

alter table public.achievement_badges enable row level security;
alter table public.trainee_achievements enable row level security;

revoke all on public.achievement_badges from anon;
revoke all on public.trainee_achievements from anon;
grant select on public.achievement_badges to authenticated;
grant select on public.trainee_achievements to authenticated;
grant all on public.achievement_badges to service_role;
grant all on public.trainee_achievements to service_role;

drop policy if exists achievement_badges_active_read on public.achievement_badges;
create policy achievement_badges_active_read on public.achievement_badges
  for select to authenticated
  using (is_active = 1);

drop policy if exists achievements_scoped_read on public.trainee_achievements;
create policy achievements_scoped_read on public.trainee_achievements
  for select to authenticated
  using (
    private.is_current_tenant(tenant_id)
    and (
      private.current_app_role() in ('founder', 'co_founder')
      or (
        private.current_app_role() = 'trainee'
        and trainee_user_id = private.current_app_user_id()
        and status = 'approved'
        and visible_until >= now()
      )
      or (
        private.current_app_role() = 'coach'
        and (
          created_by_user_id = private.current_app_user_id()
          or exists (
            select 1 from public.class_coaches cc
             where cc.tenant_id = trainee_achievements.tenant_id
               and cc.class_id = trainee_achievements.class_id
               and cc.coach_user_id = private.current_app_user_id()
               and cc.is_active
          )
        )
      )
    )
  );

commit;
