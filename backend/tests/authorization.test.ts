import test from "node:test";
import assert from "node:assert/strict";
import {
  canApproveOperations,
  canChangeAccountStatus,
  canCreateMember,
  canDeleteTarget,
  canEditMemberProfile,
  isFounderLike,
} from "../src/authorization.ts";

test("manager may create operational members but not management roles", () => {
  assert.equal(canCreateMember("manager", "coach"), true);
  assert.equal(canCreateMember("manager", "trainee"), true);
  assert.equal(canCreateMember("manager", "co_founder"), false);
  assert.equal(canCreateMember("manager", "manager"), false);
});

test("co-founder has founder capabilities but cannot delete a co-founder", () => {
  assert.equal(isFounderLike("co_founder"), true);
  assert.equal(canApproveOperations("co_founder"), true);
  assert.equal(canDeleteTarget("co_founder", "co_founder"), false);
  assert.equal(canDeleteTarget("founder", "co_founder"), true);
});

test("manager cannot change profiles or account status", () => {
  assert.equal(canEditMemberProfile("manager", "coach"), false);
  assert.equal(canChangeAccountStatus("manager", "trainee"), false);
});
