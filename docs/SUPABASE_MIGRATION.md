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

Backup cuối trước khi bật nguồn dữ liệu Supabase:

`backups/cloudflare-d1-pre-supabase-cutover-20260818-213206/community-football-club-manager-d1.sql`

SHA-256:

`71B5136962D979601037BC713D0A25B0B25E9CFD66AEAC771E10A39A8D1BC078`

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

Production is now running with `DATA_BACKEND=supabase` in the Cloudflare Worker.
The mobile client continues to call the same `/v1` endpoints; the Worker keeps
the privileged Supabase key server-side and uses the locked-down `d1_batch`
RPC. R2 remains the private media store. D1 is retained as a read-only rollback
snapshot and has not been deleted.

RLS is enabled on all public tables. Direct `anon`/`authenticated` table access
is revoked; the service role is the only role allowed to execute the adapter
RPC. Supabase Auth exchange is available at
`POST /v1/auth/supabase/exchange` and only linked active accounts can obtain a
Worker session.
