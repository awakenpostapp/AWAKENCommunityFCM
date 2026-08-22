import test from "node:test";
import assert from "node:assert/strict";
import {
  AUTO_ABSENT_REVIEW_NOTE,
  SAFETY_CLOSED_REVIEW_NOTE,
  canApproveCoachCheckIn,
  canSubmitCoachCheckOut,
  isPayableCoachCheckIn,
} from "../src/attendance-state.ts";

const base = {
  checkedInAt: "2026-08-22T10:00:00.000Z",
  checkedOutAt: null as string | null,
  checkinSelfieObjectKey: "tenant/check-in.jpg",
  checkoutSelfieObjectKey: "",
  approvalStatus: "pending" as const,
  reviewNote: "",
};

test("only a real pending check-in can be checked out", () => {
  assert.equal(canSubmitCoachCheckOut(base), true);
  assert.equal(canSubmitCoachCheckOut({ ...base, reviewNote: AUTO_ABSENT_REVIEW_NOTE, checkedOutAt: "2026-08-22T18:00:00.000Z" }), false);
  assert.equal(canSubmitCoachCheckOut({ ...base, reviewNote: SAFETY_CLOSED_REVIEW_NOTE, checkedOutAt: "2026-08-22T18:00:00.000Z" }), false);
  assert.equal(canSubmitCoachCheckOut({ ...base, approvalStatus: "approved" }), false);
});

test("Founder can approve only a pending check-in with checkout evidence", () => {
  assert.equal(canApproveCoachCheckIn({
    ...base,
    checkedOutAt: "2026-08-22T11:30:00.000Z",
    checkoutSelfieObjectKey: "tenant/check-out.jpg",
  }), true);
  assert.equal(canApproveCoachCheckIn({ ...base, checkedOutAt: "2026-08-22T11:30:00.000Z" }), false);
  assert.equal(canApproveCoachCheckIn({
    ...base,
    checkedOutAt: "2026-08-22T18:00:00.000Z",
    reviewNote: SAFETY_CLOSED_REVIEW_NOTE,
  }), false);
});

test("salary eligibility excludes automatic absence and safety close", () => {
  assert.equal(isPayableCoachCheckIn({
    ...base,
    approvalStatus: "approved",
    checkedOutAt: "2026-08-22T11:30:00.000Z",
    checkoutSelfieObjectKey: "tenant/check-out.jpg",
  }), true);
  assert.equal(isPayableCoachCheckIn({
    ...base,
    approvalStatus: "approved",
    checkedOutAt: "2026-08-22T18:00:00.000Z",
    reviewNote: AUTO_ABSENT_REVIEW_NOTE,
  }), false);
  assert.equal(isPayableCoachCheckIn({
    ...base,
    approvalStatus: "approved",
    checkedOutAt: "2026-08-22T18:00:00.000Z",
    reviewNote: SAFETY_CLOSED_REVIEW_NOTE,
  }), false);
});
