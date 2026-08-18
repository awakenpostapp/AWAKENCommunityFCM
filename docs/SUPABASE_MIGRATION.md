# Supabase migration record

Updated: 2026-08-18

## Target

`AWAKENCommunityFCM's App` · project ref `yjapwstfawfdjxutczmd` · Singapore
(`ap-southeast-1`) · PostgreSQL 17.6.1.

## Backup

Cloudflare D1 was exported before import:

`backups/cloudflare-d1-20260818-202225/community-football-club-manager-d1.sql`

SHA-256:

`2083320C01FF603FF591E34B712C5FC2487F1FE35CC9E67548891B1DC1F07602`

The R2 bucket `community-football-club-manager-files` remains on Cloudflare
and continues to hold private avatars, logos, selfies, bills and PDFs. The
database stores its object keys, so no file path rewrite was performed.

## Validation

| Check | Result |
| --- | --- |
| D1 tables exported | 30 |
| D1 data INSERT statements | 578 |
| Supabase tables | 30 |
| Supabase imported rows | 578 |
| Supabase migration | `20260818133227_cloudflare_legacy_schema` |
| Supabase region | Singapore |

The imported counts match the D1 export table-by-table. Empty operational
tables (OAuth tickets/states, reset tokens, registration rate-limit rows and
sync cursors) were preserved as empty tables. SQLite's internal
`sqlite_sequence` row was intentionally excluded.

## Cutover status

Data migration is complete, but production has **not** been switched yet.
Cloudflare D1 remains authoritative until the Worker is given a server-side
Supabase secret and the Auth/Postgres adapter is tested. This prevents a client
release from accidentally mixing the custom D1 password verifier with
Supabase Auth or exposing a privileged key.

Cloudflare Worker/R2 settings and production secrets were not changed.
