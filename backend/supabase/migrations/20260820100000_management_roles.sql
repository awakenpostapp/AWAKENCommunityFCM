begin;

alter table public.users drop constraint if exists users_role_check;
alter table public.users
  add constraint users_role_check
  check (role in ('admin', 'founder', 'co_founder', 'manager', 'coach', 'trainee'));

create or replace function private.is_founder_like()
returns boolean
language sql
stable
security definer
set search_path = public, private
as $$
  select private.current_app_role() in ('founder', 'co_founder');
$$;

create or replace function private.can_approve_operations()
returns boolean
language sql
stable
security definer
set search_path = public, private
as $$
  select private.current_app_role() in ('founder', 'co_founder', 'manager');
$$;

revoke all on function private.is_founder_like() from public, anon;
revoke all on function private.can_approve_operations() from public, anon;
grant execute on function private.is_founder_like() to authenticated, service_role;
grant execute on function private.can_approve_operations() to authenticated, service_role;

-- Co-Founder follows the Founder management scope, while Manager can review
-- operational records without gaining Founder-only account/configuration
-- privileges.  The Worker remains the only mutation surface in production;
-- these policies keep direct Supabase access consistent with that boundary.
drop policy if exists audit_founder_or_admin on public.audit_logs;
create policy audit_founder_or_admin on public.audit_logs
  for select to authenticated
  using (
    private.current_app_role() = 'admin'
    or (private.can_approve_operations() and private.is_current_tenant(tenant_id))
  );

drop policy if exists uploads_owner_or_founder on public.uploads;
create policy uploads_owner_or_founder on public.uploads
  for all to authenticated
  using (
    owner_user_id = private.current_app_user_id()
    or (private.is_founder_like() and private.is_current_tenant(tenant_id))
  )
  with check (
    owner_user_id = private.current_app_user_id()
    or (private.is_founder_like() and private.is_current_tenant(tenant_id))
  );

commit;
