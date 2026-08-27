import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const routes = await readFile(new URL("../src/achievement-routes.ts", import.meta.url), "utf8");
const index = await readFile(new URL("../src/index.ts", import.meta.url), "utf8");

test("achievement API exposes catalog, proposal, review and removal routes", () => {
  assert.match(index, /\/v1\/achievement-badges/);
  assert.match(index, /\/v1\/achievements/);
  assert.match(index, /reviewAchievement/);
  assert.match(index, /removeAchievement/);
  assert.match(routes, /export async function achievementBadges/);
  assert.match(routes, /export async function achievements/);
  assert.match(routes, /export async function reviewAchievement/);
  assert.match(routes, /export async function removeAchievement/);
});

test("achievement route enforces the approval and expiry lifecycle", () => {
  assert.match(routes, /status: AchievementStatus = auth\.role === "coach" \? "pending" : "approved"/);
  assert.match(routes, /assertCanReviewAchievement\(auth\.role\)/);
  assert.match(routes, /assertCanRemoveAchievement\(auth\.role\)/);
  assert.match(routes, /status='expired'/);
  assert.match(routes, /30 \* DAY_MS/);
  assert.match(routes, /points_snapshot/);
});

test("Coach proposals require a reason and class access", () => {
  assert.match(routes, /auth\.role === "coach"\s*\n\s*\? requireText\(body\.reason, "reason", 2_000\)/);
  assert.match(routes, /Coach phải chọn lớp học được phân công/);
  assert.match(routes, /class_coaches/);
  assert.match(routes, /class_enrollments/);
});
