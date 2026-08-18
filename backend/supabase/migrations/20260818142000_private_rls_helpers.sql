-- Keep SECURITY DEFINER helpers out of the exposed public schema.
-- The application uses the service-only d1_batch RPC; direct Data API
-- callers must never be able to invoke these authorization helpers.

CREATE SCHEMA IF NOT EXISTS private;

ALTER FUNCTION public.current_app_user_id() SET SCHEMA private;
ALTER FUNCTION public.current_app_tenant_id() SET SCHEMA private;
ALTER FUNCTION public.current_app_role() SET SCHEMA private;
ALTER FUNCTION public.is_current_tenant(text) SET SCHEMA private;

-- This helper is retained for compatibility with the existing trigger setup,
-- but is not part of the public API surface either.
ALTER FUNCTION public.rls_auto_enable() SET SCHEMA private;

REVOKE ALL ON SCHEMA private FROM PUBLIC;
GRANT USAGE ON SCHEMA private TO authenticated;

REVOKE ALL ON FUNCTION private.current_app_user_id() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION private.current_app_tenant_id() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION private.current_app_role() FROM PUBLIC, anon;
REVOKE ALL ON FUNCTION private.is_current_tenant(text) FROM PUBLIC, anon;
GRANT EXECUTE ON FUNCTION private.current_app_user_id() TO authenticated;
GRANT EXECUTE ON FUNCTION private.current_app_tenant_id() TO authenticated;
GRANT EXECUTE ON FUNCTION private.current_app_role() TO authenticated;
GRANT EXECUTE ON FUNCTION private.is_current_tenant(text) TO authenticated;

REVOKE ALL ON FUNCTION private.rls_auto_enable() FROM PUBLIC, anon, authenticated;

