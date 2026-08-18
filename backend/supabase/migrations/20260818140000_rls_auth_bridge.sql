-- Supabase cutover foundation.
-- The mobile app still reaches the Worker; the Worker uses the service-only
-- d1_batch RPC while the Auth bridge is rolled out. Direct public table access
-- remains denied until safe DTO views/RPCs are added.

CREATE TABLE IF NOT EXISTS public.auth_user_links (
  app_user_id TEXT PRIMARY KEY REFERENCES public.users(id) ON DELETE CASCADE,
  auth_user_id UUID NOT NULL UNIQUE REFERENCES auth.users(id) ON DELETE CASCADE,
  provider TEXT NOT NULL DEFAULT 'supabase_auth',
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
  updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS auth_user_links_auth_user_idx
  ON public.auth_user_links (auth_user_id);

CREATE OR REPLACE FUNCTION public.current_app_user_id()
RETURNS TEXT
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
  SELECT l.app_user_id
    FROM public.auth_user_links l
   WHERE l.auth_user_id = auth.uid()
     AND l.is_active
   LIMIT 1
$$;

CREATE OR REPLACE FUNCTION public.current_app_tenant_id()
RETURNS TEXT
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
  SELECT u.tenant_id
    FROM public.users u
   WHERE u.id = public.current_app_user_id()
   LIMIT 1
$$;

CREATE OR REPLACE FUNCTION public.current_app_role()
RETURNS TEXT
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
  SELECT u.role
    FROM public.users u
   WHERE u.id = public.current_app_user_id()
   LIMIT 1
$$;

CREATE OR REPLACE FUNCTION public.is_current_tenant(candidate_tenant_id TEXT)
RETURNS BOOLEAN
LANGUAGE sql
STABLE
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
  SELECT candidate_tenant_id IS NOT NULL
     AND candidate_tenant_id = public.current_app_tenant_id()
$$;

-- This is the only SQL execution surface used by the Worker adapter. It is
-- deliberately restricted to DML/SELECT and granted only to service_role.
CREATE OR REPLACE FUNCTION public.d1_batch(p_queries JSONB)
RETURNS JSONB
LANGUAGE plpgsql
SECURITY DEFINER
SET search_path = public, pg_temp
AS $$
DECLARE
  query_item JSONB;
  statement TEXT;
  rows JSONB;
  changes BIGINT;
  output JSONB := '[]'::JSONB;
BEGIN
  IF jsonb_typeof(p_queries) <> 'array' THEN
    RAISE EXCEPTION 'p_queries must be a JSON array';
  END IF;

  FOR query_item IN SELECT value FROM jsonb_array_elements(p_queries) LOOP
    statement := trim(query_item ->> 'sql');
    IF statement IS NULL OR statement = '' THEN
      RAISE EXCEPTION 'empty SQL statement';
    END IF;
    IF statement ~ ';'
       OR statement ~* '(^|[^[:alpha:]])(CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|COPY|DO|CALL)([^[:alpha:]]|$)' THEN
      RAISE EXCEPTION 'statement is not allowed';
    END IF;
    IF statement !~* '^[[:space:]]*(SELECT|INSERT|UPDATE|DELETE|WITH)([[:space:]]|$)' THEN
      RAISE EXCEPTION 'statement type is not allowed';
    END IF;

    IF statement ~* '^[[:space:]]*(SELECT|WITH)([[:space:]]|$)'
       OR statement ~* '(^|[^[:alpha:]])RETURNING([^[:alpha:]]|$)' THEN
      EXECUTE format(
        'SELECT COALESCE(jsonb_agg(to_jsonb(q)), ''[]''::jsonb) FROM (%s) q',
        statement
      ) INTO rows;
      output := output || jsonb_build_array(
        jsonb_build_object('results', COALESCE(rows, '[]'::jsonb),
                           'meta', jsonb_build_object('changes', 0))
      );
    ELSE
      EXECUTE statement;
      GET DIAGNOSTICS changes = ROW_COUNT;
      output := output || jsonb_build_array(
        jsonb_build_object('results', '[]'::jsonb,
                           'meta', jsonb_build_object('changes', changes))
      );
    END IF;
  END LOOP;
  RETURN output;
END;
$$;

REVOKE ALL ON FUNCTION public.d1_batch(JSONB) FROM PUBLIC, anon, authenticated;
GRANT EXECUTE ON FUNCTION public.d1_batch(JSONB) TO service_role;

-- Every public table is protected. Worker requests use the service-only RPC;
-- direct client access is intentionally not granted during the bridge phase.
DO $$
DECLARE
  table_name TEXT;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'tenants', 'users', 'profiles', 'auth_sessions', 'clubs', 'venues',
    'classes', 'class_coaches', 'class_enrollments', 'training_sessions',
    'session_coaches', 'coach_checkins', 'attendance_records',
    'tuition_invoices', 'payment_proofs', 'receipts', 'coach_salaries',
    'notifications', 'uploads', 'sync_cursors', 'idempotency_keys',
    'audit_logs', 'external_account_links', 'oauth_states',
    'oauth_exchange_tickets', 'd1_migrations', 'public_registration_requests',
    'public_registration_attempts', 'password_reset_tokens',
    'trainee_evaluations', 'auth_user_links'
  ] LOOP
    EXECUTE format('ALTER TABLE public.%I ENABLE ROW LEVEL SECURITY', table_name);
  END LOOP;
END;
$$;

REVOKE ALL ON ALL TABLES IN SCHEMA public FROM anon, authenticated;
REVOKE ALL ON TABLE public.users FROM anon, authenticated;

DROP POLICY IF EXISTS tenants_current_tenant ON public.tenants;
CREATE POLICY tenants_current_tenant ON public.tenants
  FOR ALL TO authenticated
  USING (id = public.current_app_tenant_id()
         OR owner_user_id = public.current_app_user_id())
  WITH CHECK (id = public.current_app_tenant_id()
              OR owner_user_id = public.current_app_user_id());

DROP POLICY IF EXISTS users_same_tenant ON public.users;
CREATE POLICY users_same_tenant ON public.users
  FOR ALL TO authenticated
  USING (public.current_app_role() = 'admin'
         OR id = public.current_app_user_id()
         OR public.is_current_tenant(tenant_id))
  WITH CHECK (public.current_app_role() = 'admin'
              OR id = public.current_app_user_id()
              OR public.is_current_tenant(tenant_id));

DROP POLICY IF EXISTS profiles_same_tenant ON public.profiles;
CREATE POLICY profiles_same_tenant ON public.profiles
  FOR ALL TO authenticated
  USING (user_id = public.current_app_user_id()
         OR public.is_current_tenant(tenant_id))
  WITH CHECK (user_id = public.current_app_user_id()
              OR public.is_current_tenant(tenant_id));

DROP POLICY IF EXISTS auth_sessions_own ON public.auth_sessions;
CREATE POLICY auth_sessions_own ON public.auth_sessions
  FOR ALL TO authenticated
  USING (user_id = public.current_app_user_id())
  WITH CHECK (user_id = public.current_app_user_id());

DROP POLICY IF EXISTS clubs_same_tenant ON public.clubs;
CREATE POLICY clubs_same_tenant ON public.clubs
  FOR ALL TO authenticated
  USING (public.is_current_tenant(tenant_id))
  WITH CHECK (public.is_current_tenant(tenant_id));

DO $$
DECLARE
  table_name TEXT;
BEGIN
  FOREACH table_name IN ARRAY ARRAY[
    'venues', 'classes', 'class_coaches', 'class_enrollments',
    'training_sessions', 'session_coaches', 'coach_checkins',
    'attendance_records', 'tuition_invoices', 'payment_proofs', 'receipts',
    'coach_salaries', 'trainee_evaluations'
  ] LOOP
    EXECUTE format('DROP POLICY IF EXISTS %I ON public.%I', table_name || '_same_tenant', table_name);
    EXECUTE format(
      'CREATE POLICY %I ON public.%I FOR ALL TO authenticated USING (public.is_current_tenant(tenant_id)) WITH CHECK (public.is_current_tenant(tenant_id))',
      table_name || '_same_tenant', table_name
    );
  END LOOP;
END;
$$;

DROP POLICY IF EXISTS notifications_own ON public.notifications;
CREATE POLICY notifications_own ON public.notifications
  FOR ALL TO authenticated
  USING (recipient_user_id = public.current_app_user_id())
  WITH CHECK (recipient_user_id = public.current_app_user_id());

DROP POLICY IF EXISTS uploads_owner_or_founder ON public.uploads;
CREATE POLICY uploads_owner_or_founder ON public.uploads
  FOR ALL TO authenticated
  USING (owner_user_id = public.current_app_user_id()
         OR (public.current_app_role() IN ('founder', 'admin')
             AND public.is_current_tenant(tenant_id)))
  WITH CHECK (owner_user_id = public.current_app_user_id()
              OR (public.current_app_role() IN ('founder', 'admin')
                  AND public.is_current_tenant(tenant_id)));

DROP POLICY IF EXISTS audit_founder_or_admin ON public.audit_logs;
CREATE POLICY audit_founder_or_admin ON public.audit_logs
  FOR SELECT TO authenticated
  USING (public.current_app_role() = 'admin'
         OR (public.current_app_role() = 'founder'
             AND public.is_current_tenant(tenant_id)));

DROP POLICY IF EXISTS external_link_own ON public.external_account_links;
CREATE POLICY external_link_own ON public.external_account_links
  FOR ALL TO authenticated
  USING (user_id = public.current_app_user_id())
  WITH CHECK (user_id = public.current_app_user_id());

DROP POLICY IF EXISTS sync_cursor_own ON public.sync_cursors;
CREATE POLICY sync_cursor_own ON public.sync_cursors
  FOR ALL TO authenticated
  USING (user_id = public.current_app_user_id())
  WITH CHECK (user_id = public.current_app_user_id());

DROP POLICY IF EXISTS idempotency_own ON public.idempotency_keys;
CREATE POLICY idempotency_own ON public.idempotency_keys
  FOR ALL TO authenticated
  USING (user_id = public.current_app_user_id())
  WITH CHECK (user_id = public.current_app_user_id());

DROP POLICY IF EXISTS auth_link_own ON public.auth_user_links;
CREATE POLICY auth_link_own ON public.auth_user_links
  FOR SELECT TO authenticated
  USING (app_user_id = public.current_app_user_id());

-- Sensitive operational tables have no direct client policy during the bridge.
-- These are intentionally denied until a role-specific DTO/RPC is published.
DROP POLICY IF EXISTS deny_d1_migrations ON public.d1_migrations;
CREATE POLICY deny_d1_migrations ON public.d1_migrations
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
DROP POLICY IF EXISTS deny_oauth_states ON public.oauth_states;
CREATE POLICY deny_oauth_states ON public.oauth_states
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
DROP POLICY IF EXISTS deny_oauth_exchange_tickets ON public.oauth_exchange_tickets;
CREATE POLICY deny_oauth_exchange_tickets ON public.oauth_exchange_tickets
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
DROP POLICY IF EXISTS deny_password_reset_tokens ON public.password_reset_tokens;
CREATE POLICY deny_password_reset_tokens ON public.password_reset_tokens
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
DROP POLICY IF EXISTS deny_registration_requests ON public.public_registration_requests;
CREATE POLICY deny_registration_requests ON public.public_registration_requests
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
DROP POLICY IF EXISTS deny_registration_attempts ON public.public_registration_attempts;
CREATE POLICY deny_registration_attempts ON public.public_registration_attempts
  FOR ALL TO authenticated USING (FALSE) WITH CHECK (FALSE);
