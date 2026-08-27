import test from "node:test";
import assert from "node:assert/strict";
import {
  canCreateAchievement,
  canReviewAchievement,
  canRemoveAchievement,
} from "../src/authorization.ts";
import {
  assertCanCreateAchievement,
  assertCanReviewAchievement,
  assertCanRemoveAchievement,
} from "../src/route-authorization.ts";

test("achievement mutation matrix follows role hierarchy", () => {
  assert.equal(canCreateAchievement("founder"), true);
  assert.equal(canCreateAchievement("co_founder"), true);
  assert.equal(canCreateAchievement("coach"), true);
  assert.equal(canCreateAchievement("manager"), false);
  assert.equal(canCreateAchievement("trainee"), false);

  assert.equal(canReviewAchievement("founder"), true);
  assert.equal(canReviewAchievement("co_founder"), true);
  assert.equal(canReviewAchievement("coach"), false);

  assert.equal(canRemoveAchievement("founder"), true);
  assert.equal(canRemoveAchievement("co_founder"), false);
  assert.equal(canRemoveAchievement("manager"), false);
});

test("achievement route assertions expose stable forbidden codes", () => {
  assert.doesNotThrow(() => assertCanCreateAchievement("coach"));
  assert.doesNotThrow(() => assertCanReviewAchievement("founder"));
  assert.doesNotThrow(() => assertCanReviewAchievement("co_founder"));
  assert.doesNotThrow(() => assertCanRemoveAchievement("founder"));

  assert.throws(() => assertCanCreateAchievement("manager"), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 403 && candidate.code === "forbidden_achievement_create";
  });
  assert.throws(() => assertCanReviewAchievement("coach"), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 403 && candidate.code === "forbidden_achievement_review";
  });
  assert.throws(() => assertCanRemoveAchievement("co_founder"), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 403 && candidate.code === "forbidden_achievement_remove";
  });
});
