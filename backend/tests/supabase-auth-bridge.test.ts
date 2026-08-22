import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const source = await readFile(new URL("../src/supabase-auth.ts", import.meta.url), "utf8");

test("Supabase metadata cannot select an application account", async () => {
  assert.doesNotMatch(source, /metadataUserId/iu);
  assert.doesNotMatch(source, /user_metadata\?\.app_user_id/iu);
});

test("email-only identity cannot select an application account", async () => {
  assert.doesNotMatch(source, /if\s*\(!user\s*&&\s*email\)/iu);
  assert.doesNotMatch(source, /email_normalized\s*=\s*\?/iu);
});

test("only an active server-created identity mapping selects the account", async () => {
  assert.match(source, /JOIN\s+auth_user_links\s+l\s+ON\s+l\.app_user_id\s*=\s*u\.id/iu);
  assert.match(source, /l\.auth_user_id\s*=\s*\?/iu);
  assert.match(source, /l\.is_active\s*=\s*1/iu);
});

test("the exchange does not write an implicit auth mapping", async () => {
  assert.doesNotMatch(source, /INSERT\s+OR\s+REPLACE\s+INTO\s+auth_user_links/iu);
  assert.doesNotMatch(source, /DB\.batch\(\[\s*env\.DB\.prepare\([\s\S]*auth_user_links/iu);
});
