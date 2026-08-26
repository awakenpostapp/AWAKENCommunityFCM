import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  assertCanApproveOperations,
  assertCanChangeAccountStatus,
  assertCanCreateClass,
  assertCanCreateMember,
  assertCanDeleteTarget,
} from "../src/route-authorization.ts";
import { validateClassCreationPayload } from "../src/class-validation.ts";

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

test("class creation is Founder-like only while approvals allow Manager", () => {
  assert.throws(() => assertCanCreateClass("manager"), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 403 && candidate.code === "forbidden_class_create";
  });
  assert.doesNotThrow(() => assertCanCreateClass("founder"));
  assert.doesNotThrow(() => assertCanCreateClass("co_founder"));
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

test("member deletion follows the Founder/Co-Founder target matrix", () => {
  assert.doesNotThrow(() => assertCanDeleteTarget("founder", "coach"));
  assert.doesNotThrow(() => assertCanDeleteTarget("founder", "trainee"));
  assert.doesNotThrow(() => assertCanDeleteTarget("co_founder", "manager"));
  assert.throws(() => assertCanDeleteTarget("manager", "coach"), isForbidden);
  assert.throws(() => assertCanDeleteTarget("co_founder", "co_founder"), isForbidden);
  assert.throws(() => assertCanDeleteTarget("founder", "founder"), isForbidden);
});

test("new class payload requires at least one Coach", () => {
  assert.throws(() => validateClassCreationPayload({ coachUserIds: [] }), (error: unknown) => {
    const candidate = error as { status?: number; code?: string };
    return candidate.status === 400 && candidate.code === "coach_required";
  });
  assert.deepEqual(
    validateClassCreationPayload({ coachUserIds: ["coach-1", "coach-1", "coach-2"] }),
    ["coach-1", "coach-2"],
  );
});

test("evaluation visibility includes co-founder management", async () => {
  const source = await readFile(new URL("../src/routes.ts", import.meta.url), "utf8");
  assert.match(source, /\? IN \('founder', 'co_founder'\)/);
});

test("member delete route removes restricted attendance references and audits the deletion", async () => {
  const routesSource = await readFile(new URL("../src/routes.ts", import.meta.url), "utf8");
  const indexSource = await readFile(new URL("../src/index.ts", import.meta.url), "utf8");
  assert.match(routesSource, /export async function deleteMember\(/);
  assert.match(routesSource, /DELETE FROM attendance_records WHERE tenant_id=\? AND recorded_by_user_id=\?/);
  assert.match(routesSource, /DELETE FROM audit_logs WHERE tenant_id=\? AND actor_user_id=\?/);
  assert.match(routesSource, /member\.deleted/);
  assert.match(routesSource, /env\.FILES\.delete/);
  assert.match(indexSource, /params = match\(path, \/\^\\\/v1\\\/users\\\//);
  assert.match(indexSource, /if \(method === "DELETE" && params\) return deleteMember/);
});
