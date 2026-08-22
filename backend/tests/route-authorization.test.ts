import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  assertCanApproveOperations,
  assertCanChangeAccountStatus,
  assertCanCreateClass,
  assertCanCreateMember,
} from "../src/route-authorization.ts";

function isForbidden(error: unknown): boolean {
  const candidate = error as { status?: number; code?: string };
  return candidate?.status === 403;
}

test("member creation route follows the role matrix", () => {
  assert.doesNotThrow(() => assertCanCreateMember("founder", "manager"));
  assert.doesNotThrow(() => assertCanCreateMember("co_founder", "coach"));
  assert.doesNotThrow(() => assertCanCreateMember("manager", "trainee"));
  assert.throws(() => assertCanCreateMember("manager", "co_founder"), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 403 && candidate.code === "forbidden_member_role";
  });
});

test("class creation and approvals allow Manager but not unrelated roles", () => {
  assert.doesNotThrow(() => assertCanCreateClass("manager"));
  assert.doesNotThrow(() => assertCanApproveOperations("manager"));
  assert.doesNotThrow(() => assertCanApproveOperations("co_founder"));
  assert.throws(() => assertCanCreateClass("coach"), isForbidden);
  assert.throws(() => assertCanApproveOperations("coach"), isForbidden);
});

test("only Founder-like roles can manage account status/password", () => {
  assert.doesNotThrow(() => assertCanChangeAccountStatus("founder", "manager"));
  assert.doesNotThrow(() => assertCanChangeAccountStatus("co_founder", "co_founder"));
  assert.throws(() => assertCanChangeAccountStatus("manager", "coach"), isForbidden);
});

test("evaluation visibility includes co-founder management", async () => {
  const source = await readFile(new URL("../src/routes.ts", import.meta.url), "utf8");
  assert.match(source, /\? IN \('founder', 'co_founder'\)/);
});
