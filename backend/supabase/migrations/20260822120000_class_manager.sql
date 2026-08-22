begin;

alter table public.classes
  add column if not exists manager_user_id text references public.users(id) on delete set null;

create index if not exists idx_classes_manager
  on public.classes(tenant_id, manager_user_id, is_active);

commit;
