-- Repair policies created by the original bridge migration. The earlier
-- private-helper migration moved the functions but left policies pointing at
-- public.current_app_*; this migration is safe to run on already-migrated
-- tenants and on a clean Supabase project.

begin;

create schema if not exists private;

create or replace function private.current_app_user_id()
returns text
language sql
stable
security definer
set search_path = public, private, pg_temp
as $$
  select l.app_user_id
    from public.auth_user_links l
   where l.auth_user_id = auth.uid()
     and l.is_active
   limit 1
$$;

create or replace function private.current_app_tenant_id()
returns text
language sql
stable
security definer
set search_path = public, private, pg_temp
as $$
  select u.tenant_id
    from public.users u
   where u.id = private.current_app_user_id()
   limit 1
$$;

create or replace function private.current_app_role()
returns text
language sql
stable
security definer
set search_path = public, private, pg_temp
as $$
  select u.role
    from public.users u
   where u.id = private.current_app_user_id()
   limit 1
$$;

create or replace function private.is_current_tenant(candidate_tenant_id text)
returns boolean
language sql
stable
security definer
set search_path = public, private, pg_temp
as $$
  select candidate_tenant_id is not null
     and candidate_tenant_id = private.current_app_tenant_id()
$$;

revoke all on schema private from public;
grant usage on schema private to authenticated, service_role;
revoke all on function private.current_app_user_id() from public, anon;
revoke all on function private.current_app_tenant_id() from public, anon;
revoke all on function private.current_app_role() from public, anon;
revoke all on function private.is_current_tenant(text) from public, anon;
grant execute on function private.current_app_user_id() to authenticated, service_role;
grant execute on function private.current_app_tenant_id() to authenticated, service_role;
grant execute on function private.current_app_role() to authenticated, service_role;
grant execute on function private.is_current_tenant(text) to authenticated, service_role;

revoke all on function public.current_app_user_id() from public, anon, authenticated;
revoke all on function public.current_app_tenant_id() from public, anon, authenticated;
revoke all on function public.current_app_role() from public, anon, authenticated;
revoke all on function public.is_current_tenant(text) from public, anon, authenticated;

drop policy if exists tenants_current_tenant on public.tenants;
create policy tenants_current_tenant on public.tenants
  for all to authenticated
  using (id = private.current_app_tenant_id()
         or owner_user_id = private.current_app_user_id())
  with check (id = private.current_app_tenant_id()
              or owner_user_id = private.current_app_user_id());

drop policy if exists users_same_tenant on public.users;
create policy users_same_tenant on public.users
  for all to authenticated
  using (private.current_app_role() = 'admin'
         or id = private.current_app_user_id()
         or private.is_current_tenant(tenant_id))
  with check (private.current_app_role() = 'admin'
              or id = private.current_app_user_id()
              or private.is_current_tenant(tenant_id));

drop policy if exists profiles_same_tenant on public.profiles;
create policy profiles_same_tenant on public.profiles
  for all to authenticated
  using (user_id = private.current_app_user_id()
         or private.is_current_tenant(tenant_id))
  with check (user_id = private.current_app_user_id()
              or private.is_current_tenant(tenant_id));

drop policy if exists auth_sessions_own on public.auth_sessions;
create policy auth_sessions_own on public.auth_sessions
  for all to authenticated
  using (user_id = private.current_app_user_id())
  with check (user_id = private.current_app_user_id());

drop policy if exists clubs_same_tenant on public.clubs;
create policy clubs_same_tenant on public.clubs
  for all to authenticated
  using (private.is_current_tenant(tenant_id))
  with check (private.is_current_tenant(tenant_id));

do $$
declare
  table_name text;
begin
  foreach table_name in array array[
    'venues', 'classes', 'class_coaches', 'class_enrollments',
    'training_sessions', 'session_coaches', 'coach_checkins',
    'attendance_records', 'tuition_invoices', 'payment_proofs', 'receipts',
    'coach_salaries', 'trainee_evaluations'
  ] loop
    execute format('drop policy if exists %I on public.%I', table_name || '_same_tenant', table_name);
    execute format(
      'create policy %I on public.%I for all to authenticated using (private.is_current_tenant(tenant_id)) with check (private.is_current_tenant(tenant_id))',
      table_name || '_same_tenant', table_name
    );
  end loop;
end;
$$;

drop policy if exists notifications_own on public.notifications;
create policy notifications_own on public.notifications
  for all to authenticated
  using (recipient_user_id = private.current_app_user_id())
  with check (recipient_user_id = private.current_app_user_id());

drop policy if exists uploads_owner_or_founder on public.uploads;
create policy uploads_owner_or_founder on public.uploads
  for all to authenticated
  using (owner_user_id = private.current_app_user_id()
         or (private.current_app_role() in ('founder', 'co_founder', 'admin')
             and private.is_current_tenant(tenant_id)))
  with check (owner_user_id = private.current_app_user_id()
              or (private.current_app_role() in ('founder', 'co_founder', 'admin')
                  and private.is_current_tenant(tenant_id)));

drop policy if exists audit_founder_or_admin on public.audit_logs;
create policy audit_founder_or_admin on public.audit_logs
  for select to authenticated
  using (private.current_app_role() in ('founder', 'co_founder', 'admin')
         and (private.current_app_role() = 'admin'
              or private.is_current_tenant(tenant_id)));

drop policy if exists external_link_own on public.external_account_links;
create policy external_link_own on public.external_account_links
  for all to authenticated
  using (user_id = private.current_app_user_id())
  with check (user_id = private.current_app_user_id());

drop policy if exists sync_cursor_own on public.sync_cursors;
create policy sync_cursor_own on public.sync_cursors
  for all to authenticated
  using (user_id = private.current_app_user_id())
  with check (user_id = private.current_app_user_id());

drop policy if exists idempotency_own on public.idempotency_keys;
create policy idempotency_own on public.idempotency_keys
  for all to authenticated
  using (user_id = private.current_app_user_id())
  with check (user_id = private.current_app_user_id());

drop policy if exists auth_link_own on public.auth_user_links;
create policy auth_link_own on public.auth_user_links
  for select to authenticated
  using (app_user_id = private.current_app_user_id());

commit;
