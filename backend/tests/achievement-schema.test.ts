import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const d1 = await readFile(new URL("../migrations/0016_achievements.sql", import.meta.url), "utf8");
const supabase = await readFile(new URL("../supabase/migrations/20260827090000_achievements.sql", import.meta.url), "utf8");

const points = [500, 150, 100, 60, 30, 20, 15, 10, -10, -30];

test("achievement migrations use additive, tenant-scoped tables", () => {
  for (const sql of [d1, supabase]) {
    assert.match(sql, /achievement_badges/);
    assert.match(sql, /trainee_achievements/);
    assert.match(sql, /tenant_id/);
    assert.match(sql, /status/);
    for (const point of points) assert.ok(sql.includes(String(point)), `missing point ${point}`);
  }
  assert.match(d1, /INSERT OR IGNORE INTO achievement_badges/i);
  assert.match(supabase, /ON CONFLICT \(key\) DO NOTHING/i);
});

test("achievement lifecycle and visibility constraints are present", () => {
  for (const sql of [d1, supabase]) {
    assert.match(sql, /pending/);
    assert.match(sql, /approved/);
    assert.match(sql, /rejected/);
    assert.match(sql, /removed/);
    assert.match(sql, /expired/);
    assert.match(sql, /visible_until/);
    assert.match(sql, /created_by_user_id/);
    assert.match(sql, /reviewed_by_user_id/);
  }
  assert.match(supabase, /enable row level security/i);
  assert.match(supabase, /private\.is_current_tenant\(tenant_id\)/i);
});
