import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const routes = await readFile(new URL("../src/routes.ts", import.meta.url), "utf8");
const auth = await readFile(new URL("../src/auth.ts", import.meta.url), "utf8");

test("Supabase-backed roster ordering is PostgreSQL-compatible", () => {
  assert.doesNotMatch(routes, /ORDER BY full_name COLLATE NOCASE/iu);
  assert.match(routes, /ORDER BY lower\(full_name\)/iu);
});

test("refresh rotation uses compare-and-swap on the previous token hash", () => {
  assert.match(auth, /UPDATE auth_sessions[\s\S]*SET refresh_token_hash[\s\S]*WHERE id = \? AND refresh_token_hash = \? AND revoked_at IS NULL/iu);
  assert.match(auth, /if \(!rotated\.meta\.changes\)/iu);
});

test("payment proof review validates the decision and claims a pending proof", () => {
  assert.match(routes, /typeof body\.accepted !== "boolean"/iu);
  assert.match(routes, /UPDATE payment_proofs[\s\S]*review_status='pending'/iu);
  assert.match(routes, /payment_proof_already_reviewed/iu);
});

test("snapshot idempotency reserves a key before applying mutations", () => {
  assert.match(routes, /INSERT OR IGNORE INTO idempotency_keys/iu);
  assert.match(routes, /response_status[^\n]*425/iu);
  assert.match(routes, /mutation_in_progress/iu);
  assert.doesNotMatch(routes, /INSERT OR REPLACE INTO idempotency_keys/iu);
});

test("client audit writes are explicitly allowlisted", () => {
  assert.match(routes, /CLIENT_AUDIT_ACTIONS/iu);
  assert.match(routes, /CLIENT_AUDIT_ENTITY_TYPES/iu);
  assert.match(routes, /audit_action_not_allowed/iu);
});

test("hard delete fails safely when R2 cleanup is required but unavailable", () => {
  assert.match(routes, /uploads\.length\s*>\s*0\s*&&\s*!env\.FILES/iu);
  assert.match(routes, /storage_unavailable/iu);
});
