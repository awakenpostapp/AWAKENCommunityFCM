export const AUTO_ABSENT_REVIEW_NOTE = "AUTO_ABSENT_NO_CHECKIN";
export const SAFETY_CLOSED_REVIEW_NOTE = "SAFETY_CLOSED_NO_CHECKOUT";
export const HISTORICAL_MANUAL_COACH_REVIEW_NOTE = "Founder ghi nhận buổi học cũ; Coach đã dạy";

export type CoachCheckInApprovalStatus = "pending" | "approved" | "rejected";

export interface CoachCheckInState {
  checkedInAt: string;
  checkedOutAt: string | null;
  checkinSelfieObjectKey: string;
  checkoutSelfieObjectKey: string;
  approvalStatus: CoachCheckInApprovalStatus | string;
  reviewNote: string;
}

function hasAutomaticCloseMarker(state: Pick<CoachCheckInState, "reviewNote">): boolean {
  return state.reviewNote === AUTO_ABSENT_REVIEW_NOTE
    || state.reviewNote === SAFETY_CLOSED_REVIEW_NOTE;
}

/** A Coach checkout can only close a real, pending, still-open check-in. */
export function canSubmitCoachCheckOut(state: CoachCheckInState): boolean {
  return Boolean(
    state.checkedInAt
    && !state.checkedOutAt
    && state.checkinSelfieObjectKey
    && state.approvalStatus === "pending"
    && !hasAutomaticCloseMarker(state),
  );
}

/** Founder/Manager review requires a real checkout selfie and a pending row. */
export function canApproveCoachCheckIn(state: CoachCheckInState): boolean {
  return Boolean(
    state.checkedInAt
    && state.checkedOutAt
    && state.checkinSelfieObjectKey
    && state.checkoutSelfieObjectKey
    && state.approvalStatus === "pending"
    && !hasAutomaticCloseMarker(state),
  );
}

/** Only approved real checkout or explicit historical Coach-taught rows pay. */
export function isPayableCoachCheckIn(state: CoachCheckInState): boolean {
  return Boolean(
    state.approvalStatus === "approved"
    && state.checkedOutAt
    && !hasAutomaticCloseMarker(state)
    && (state.checkoutSelfieObjectKey || state.reviewNote.includes(HISTORICAL_MANUAL_COACH_REVIEW_NOTE)),
  );
}
