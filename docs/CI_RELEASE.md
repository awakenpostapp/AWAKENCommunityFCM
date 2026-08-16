# CI, migration and release gates

- `backend-ci.yml` runs TypeScript typecheck, Worker bundling and a Wrangler
  deploy dry-run without production credentials.
- `security-scan.yml` checks tracked source for private keys, OAuth client JSON,
  tokens and non-placeholder secret values.
- `android-compile.yml` compiles the MAUI Android target on Windows. It does
  not sign or publish an APK.
- `release-android.yml` is manual. It creates versioned APK and Release AAB
  artifacts through `scripts/Build-AndroidArtifact.ps1`; the keystore is read
  only from the `ANDROID_KEYSTORE_BASE64` GitHub secret and is never committed.
- `backup-d1.yml` is manual/weekly and exports a private D1 SQL artifact plus a
  SHA-256 checksum. R2 objects remain in the private bucket; restoring media
  requires the corresponding R2 object inventory and is documented separately.

Production D1 migrations are intentionally not run automatically by CI. Apply
them from a reviewed commit with `wrangler d1 migrations apply ... --remote`,
then deploy the Worker with `--keep-vars` so dashboard-managed variables and
secrets remain unchanged.
