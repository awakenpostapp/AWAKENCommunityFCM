import test from "node:test";
import assert from "node:assert/strict";
import {
  achievementPointsStatus,
  achievementVisibilityStatus,
} from "../src/domain.ts";

test("expired approved achievement is hidden but retains points", () => {
  assert.equal(
    achievementVisibilityStatus("2026-07-01T00:00:00.000Z", "2026-07-02T00:00:00.000Z"),
    "expired",
  );
  assert.equal(achievementPointsStatus("expired"), true);
  assert.equal(achievementPointsStatus("removed"), true);
  assert.equal(achievementPointsStatus("rejected"), false);
});

test("achievement visible-until boundary is deterministic", () => {
  assert.equal(
    achievementVisibilityStatus("2026-08-27T12:00:00.000Z", "2026-08-27T11:59:59.999Z"),
    "visible",
  );
  assert.equal(
    achievementVisibilityStatus("2026-08-27T12:00:00.000Z", "2026-08-27T12:00:00.000Z"),
    "visible",
  );
  assert.equal(
    achievementVisibilityStatus("2026-08-27T12:00:00.000Z", "2026-08-27T12:00:00.001Z"),
    "expired",
  );
});
