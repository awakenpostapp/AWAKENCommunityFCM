import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const migrationDir = new URL("../supabase/migrations/", import.meta.url);
const bridge = await readFile(new URL("20260818140000_rls_auth_bridge.sql", migrationDir), "utf8");
const privateHelpers = await readFile(new URL("20260818142000_private_rls_helpers.sql", migrationDir), "utf8");
const repair = await readFile(new URL("20260822100000_rls_helper_repair.sql", migrationDir), "utf8");

test("RLS migration never alters a missing helper and creates private helpers", () => {
  assert.doesNotMatch(privateHelpers, /ALTER\s+FUNCTION\s+public\.rls_auto_enable/iu);
  assert.match(privateHelpers, /CREATE\s+SCHEMA\s+IF\s+NOT\s+EXISTS\s+private/iu);
  assert.match(privateHelpers, /CREATE\s+OR\s+REPLACE\s+FUNCTION\s+private\.current_app_user_id/iu);
});

test("RLS policies use private helper functions", () => {
  assert.match(bridge, /CREATE\s+OR\s+REPLACE\s+FUNCTION\s+public\.current_app_user_id/iu);
  assert.doesNotMatch(repair, /(?:using|with\s+check)\s*\([^;]*public\.current_app_/isu);
  assert.doesNotMatch(repair, /(?:using|with\s+check)\s*\([^;]*public\.is_current_tenant/isu);
  assert.match(repair, /DROP\s+POLICY\s+IF\s+EXISTS\s+tenants_current_tenant/iu);
  assert.match(repair, /private\.current_app_tenant_id\s*\(/iu);
  assert.match(repair, /private\.is_current_tenant\s*\(/iu);
});

test("repair migration is transactional and grants policy helper execution", () => {
  assert.match(repair, /\bbegin;[\s\S]*commit;\s*$/iu);
  assert.match(repair, /GRANT\s+EXECUTE\s+ON\s+FUNCTION\s+private\.current_app_user_id\(\)\s+TO\s+authenticated/iu);
  assert.match(repair, /REVOKE\s+ALL\s+ON\s+FUNCTION\s+public\.current_app_user_id\(\)/iu);
  assert.match(repair, /current_app_role\(\)\s+in\s+\('founder',\s+'co_founder',\s+'admin'\)/iu);
});
