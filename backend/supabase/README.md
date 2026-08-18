# Supabase migration

Project: `AWAKENCommunityFCM's App`

- Project ref: `yjapwstfawfdjxutczmd`
- Region: `ap-southeast-1` (Singapore)
- PostgreSQL: 17.6.1
- Source: Cloudflare D1 `community-football-club-manager`
- Source D1 id: `8bcd4ffb-d801-4d51-b607-5f0031b6cf6e`
- Media source retained on Cloudflare R2: `community-football-club-manager-files`

## Migration state

The Cloudflare D1 schema and data were imported into Supabase on 2026-08-18.
Supabase migration history contains `cloudflare_legacy_schema_20260818`.
The import was verified against the D1 export: 30 tables and 578 data rows
(excluding the SQLite-only `sqlite_sequence` metadata row).

The Worker still owns the `/v1` contract, custom password verifier and tenant
RBAC, but production now uses Supabase PostgreSQL through the server-side
D1-compatible adapter. D1 and R2 bindings remain configured for rollback and
private media storage; D1 is not deleted.

## Files

- `migrations/20260818133227_cloudflare_legacy_schema.sql` is the reproducible
  PostgreSQL schema used by the remote migration.
- `scripts/prepare-supabase-migration.mjs` converts a D1 SQL export into the
  schema and import batches. Generated data batches are ignored by Git because
  they contain user/account metadata.

## Production cutover completed

1. `SUPABASE_SECRET_KEY` is configured only as a Cloudflare Worker secret;
   `SUPABASE_URL` is a non-secret Worker variable.
2. `backend/src/supabase-d1.ts` translates the existing D1 repository contract
   to the service-only `public.d1_batch` RPC.
3. RLS/auth-link migrations are applied; private helpers are not exposed by the
   Data API.
4. `POST /v1/auth/supabase/exchange` bridges a verified Supabase Auth token to
   the current Worker session and refuses unlinked accounts.
5. Production smoke tests passed for health, login, session restore, snapshot,
   club/classes/users/notifications/tuition/evaluations and logout.

Never put a Supabase secret key, database password, or refresh token in the
repository, APK, or mobile client.
