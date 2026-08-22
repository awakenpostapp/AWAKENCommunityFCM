-- Keep SECURITY DEFINER helpers out of the exposed public schema.
-- This migration is safe for clean installs: it does not assume an optional
-- public.rls_auto_enable() function exists and it creates private helpers
-- before later role migrations reference them.

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

-- Existing public helpers may have been created by the bridge migration. They
-- remain harmless for compatibility but are no longer callable by clients;
-- the repair migration replaces all policies with private-helper versions.
revoke all on function public.current_app_user_id() from public, anon, authenticated;
revoke all on function public.current_app_tenant_id() from public, anon, authenticated;
revoke all on function public.current_app_role() from public, anon, authenticated;
revoke all on function public.is_current_tenant(text) from public, anon, authenticated;

commit;
