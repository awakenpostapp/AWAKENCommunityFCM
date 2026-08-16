# Production hardening

The production Worker keeps its existing D1, R2 binding, secrets, and URL in
`wrangler.jsonc`.  Do not copy staging IDs or OAuth credentials into that
file.

## Staging isolation

Copy `wrangler.staging.example.jsonc` to a local `wrangler.staging.jsonc`,
replace the staging D1 ID and Worker callback URL, then create a separate R2
bucket and set the staging secrets with Wrangler.  The example deliberately
disables public Founder registration until the staging approval flow is ready.

## Secrets

`JWT_SECRET`, `ADMIN_BOOTSTRAP_SECRET`, and Google OAuth credentials are
Worker secrets.  They must be provisioned with `wrangler secret put` (or the
Cloudflare dashboard), never committed to source or placed in
`wrangler.jsonc`.  The repository ignores local OAuth client JSON files and
`.dev.vars` files; the secret scanner in `scripts/Scan-Secrets.ps1` is also
run by CI.

## Scheduled maintenance

The hourly cron first removes expired OAuth state/tickets, expired or revoked
refresh sessions, idempotency keys, registration rate-limit rows, password
reset tokens, and old unreferenced R2 upload objects.  It then repairs tenant
attendance, salary, and tuition derived state.  Cleanup is bounded and
idempotent so a transient R2 failure can be retried safely on the next run.

## Read-only smoke test

`npm run smoke` always checks the production health endpoint. To exercise a
real account without embedding credentials, provide a dedicated test account
only for the process environment:

```powershell
$env:SMOKE_USERNAME = 'read-only-test-account'
$env:SMOKE_PASSWORD = '...'
npm run smoke
Remove-Item Env:SMOKE_USERNAME, Env:SMOKE_PASSWORD
```

The authenticated path performs only login, `me`, snapshot, OAuth-link, and
notification reads; it does not create classes, attendance, invoices, or
media.

## Snapshot marker

The tenant snapshot marker uses one D1 batch of per-table aggregate queries.
Do not replace it with a large compound `UNION ALL`: D1 rejects the previous
18-term form with `too many terms in compound SELECT`, which prevents all
Founder, Coach, and Trainee snapshots from loading.
