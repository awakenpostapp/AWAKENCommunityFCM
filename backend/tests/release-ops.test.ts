import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const root = new URL("../../", import.meta.url);
const releaseWorkflow = await readFile(new URL(".github/workflows/release-android.yml", root), "utf8");
const backupWorkflow = await readFile(new URL(".github/workflows/backup-online.yml", root), "utf8");
const stagingConfig = await readFile(new URL("backend/wrangler.staging.example.jsonc", root), "utf8");

test("Android release workflow creates all builds but publishes only the Release APK", () => {
  assert.match(releaseWorkflow, /contents:\s*write/iu);
  assert.match(releaseWorkflow, /Build Release APK[\s\S]*Configuration Release/iu);
  assert.match(releaseWorkflow, /Build Release AAB[\s\S]*-Bundle/iu);
  assert.match(releaseWorkflow, /Build Debug APK[\s\S]*Configuration Debug/iu);
  assert.match(releaseWorkflow, /gh release create/iu);
  assert.match(releaseWorkflow, /AWAKENCommunityFCM-v\$displayVersion-build\$buildNumber-Release/iu);
  // Keep this scoped to the command line itself; release notes may mention
  // the intentionally unpublished AAB/Debug validation builds.
  assert.doesNotMatch(releaseWorkflow, /gh release create[^\r\n]*\.aab/iu);
  assert.doesNotMatch(releaseWorkflow, /gh release create[^\r\n]*Debug/iu);
});

test("online backup workflow captures Supabase, D1 and R2 inventory", () => {
  assert.match(backupWorkflow, /SUPABASE_DB_URL/iu);
  assert.match(backupWorkflow, /pg_dump/iu);
  assert.match(backupWorkflow, /wrangler d1 export/iu);
  assert.match(backupWorkflow, /r2 object list/iu);
  assert.match(backupWorkflow, /sha256sum/iu);
});

test("staging does not silently use the legacy D1 role schema", () => {
  assert.match(stagingConfig, /"DATA_BACKEND"\s*:\s*"supabase"/iu);
  assert.match(stagingConfig, /"SUPABASE_URL"/iu);
});
