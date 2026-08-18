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

The Cloudflare Worker remains the production source of truth during the
cutover window. This is deliberate: the mobile client still speaks the
Cloudflare `/v1` contract, and the Worker still owns the current custom
password verifier and tenant RBAC. Do not switch or delete D1 until the
Supabase Auth bridge and Worker database adapter have passed the cutover
checks.

## Files

- `migrations/20260818133227_cloudflare_legacy_schema.sql` is the reproducible
  PostgreSQL schema used by the remote migration.
- `scripts/prepare-supabase-migration.mjs` converts a D1 SQL export into the
  schema and import batches. Generated data batches are ignored by Git because
  they contain user/account metadata.

## Next cutover requirements

1. Provision a Cloudflare Worker secret named `SUPABASE_SECRET_KEY` (the
   Supabase server-side secret key, never the publishable/anon key) and set
   `SUPABASE_URL=https://yjapwstfawfdjxutczmd.supabase.co` as a non-secret var.
2. Add the Auth bridge: create Supabase Auth identities, map them to the
   imported `public.users` rows, and keep legacy password verification until
   each user completes a password change.
3. Add the Postgres/Data API repository adapter and dual-read validation.
4. Run a write-free smoke test, then a short write cutover with D1 backup and
   rollback ready.

Never put a Supabase secret key, database password, or refresh token in the
repository, APK, or mobile client.
