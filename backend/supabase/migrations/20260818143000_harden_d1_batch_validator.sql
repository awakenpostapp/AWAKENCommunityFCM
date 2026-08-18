-- The first validator treated the `create` prefix in columns such as
-- `created_at` as the CREATE keyword because `_` is not an ASCII letter.
-- Use identifier-aware boundaries so normal application DML is accepted while
-- DDL and administrative statements remain blocked.

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
       OR statement ~* '(^|[^[:alnum:]_])(CREATE|ALTER|DROP|TRUNCATE|GRANT|REVOKE|COPY|DO|CALL)([^[:alnum:]_]|$)' THEN
      RAISE EXCEPTION 'statement is not allowed';
    END IF;
    IF statement !~* '^[[:space:]]*(SELECT|INSERT|UPDATE|DELETE|WITH)([[:space:]]|$)' THEN
      RAISE EXCEPTION 'statement type is not allowed';
    END IF;

    IF statement ~* '^[[:space:]]*(SELECT|WITH)([[:space:]]|$)'
       OR statement ~* '(^|[^[:alnum:]_])RETURNING([^[:alnum:]_]|$)' THEN
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

