import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import { validateTenantUserRole } from "../src/authorization.ts";

test("role migration accepts co-founder and manager without removing legacy roles", async () => {
  const d1 = await readFile(new URL("../migrations/0014_management_roles.sql", import.meta.url), "utf8");
  const supabase = await readFile(
    new URL("../supabase/migrations/20260820100000_management_roles.sql", import.meta.url),
    "utf8",
  );
  for (const role of ["admin", "founder", "co_founder", "manager", "coach", "trainee"]) {
    assert.match(d1, new RegExp(`'${role}'`));
    assert.match(supabase, new RegExp(`'${role}'`));
  }
});

test("tenant user role validation fails closed", () => {
  assert.equal(validateTenantUserRole("co_founder"), "co_founder");
  assert.equal(validateTenantUserRole("manager"), "manager");
  assert.throws(() => validateTenantUserRole("admin"), /Role/);
  assert.throws(() => validateTenantUserRole("founder"), /Role/);
  assert.throws(() => validateTenantUserRole("unknown"), /Role/);
});
