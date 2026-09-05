import test from "node:test";
import assert from "node:assert/strict";
import {
  COACH_POSITION_KEYS,
  isCoachPositionKey,
  isSalaryEligibleCoachPosition,
} from "../src/domain.ts";

test("Coach catalog accepts Assistant Coach and Intern positions", () => {
  assert.equal(COACH_POSITION_KEYS.includes("assistant_coach"), true);
  assert.equal(COACH_POSITION_KEYS.includes("intern"), true);
  assert.equal(isCoachPositionKey("assistant_coach"), true);
  assert.equal(isCoachPositionKey("intern"), true);
});

test("Intern assignments are not salary eligible while Assistant Coach remains paid", () => {
  assert.equal(isSalaryEligibleCoachPosition("intern"), false);
  assert.equal(isSalaryEligibleCoachPosition("assistant_coach"), true);
  assert.equal(isSalaryEligibleCoachPosition("head_coach_manager"), true);
});
