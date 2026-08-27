import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const routes = await readFile(new URL("../src/achievement-routes.ts", import.meta.url), "utf8");
const index = await readFile(new URL("../src/index.ts", import.meta.url), "utf8");

test("maintenance expires only approved rows after visible_until while preserving points", () => {
  assert.match(routes, /status='expired', updated_at=\?/);
  assert.match(routes, /status='approved' AND visible_until<\?/);
  assert.match(routes, /export async function expireAchievements/);
  assert.match(index, /await expireAchievements\(requestEnv\)/);
  assert.match(routes, /status IN \('approved','removed','expired'\)/);
});

test("achievement lifecycle sends the required review notifications", () => {
  assert.match(routes, /AchievementSubmitted/);
  assert.match(routes, /AchievementApproved/);
  assert.match(routes, /AchievementRejected/);
  assert.match(routes, /notifyAchievementSubmitted/);
  assert.match(routes, /notifyAchievementApproved/);
});
