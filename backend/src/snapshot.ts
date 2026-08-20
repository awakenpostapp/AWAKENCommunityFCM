import { hashPassword } from "./auth";
import {
  AuthUser, ClubRow, ProfileRow, UserRow, normalizeEmail, normalizeUsername, nowIso, newId,
  publicClub, publicProfile, publicUser, isCoachPositionKey,
} from "./domain";
import { ApiError, optionalText, requireDateKey, requireInteger, requireText } from "./http";
import { allRows, assertTenantEntity } from "./repository";
import { isFounderLike } from "./authorization";

type Row = Record<string, unknown>;

function camelKey(value: string): string {
  return value.replace(/_([a-z])/gu, (_, letter: string) => letter.toUpperCase());
}

function camelRows(rows: Row[]): Row[] {
  return rows.map((row) => Object.fromEntries(Object.entries(row).map(([key, value]) => {
    // SQLite stores booleans as INTEGER. The public JSON contract must use
    // proper booleans so strict mobile clients do not reject the snapshot.
    const normalized = key.startsWith("is_") && (value === 0 || value === 1)
      ? value === 1
      : value;
    return [camelKey(key), normalized];
  })));
}

function asBoolean(value: unknown): boolean {
  return value === true || value === 1 || value === "1";
}

function safeUsers(rows: Row[]): Row[] {
  return camelRows(rows).map((row) => ({
    id: row.id,
    tenantId: row.tenantId,
    username: row.username,
    email: row.email,
    role: row.role,
    isActive: asBoolean(row.isActive),
    isTuitionSupported: asBoolean(row.isTuitionSupported),
    mustChangePassword: asBoolean(row.mustChangePassword),
    createdAt: row.createdAt,
    updatedAt: row.updatedAt,
  }));
}

/**
 * A member snapshot is not an account directory.  Coaches and trainees only
 * need the public identity required to render a class roster; peer trainee
 * email, password state, and tuition-support flags are intentionally omitted.
 * The signed-in user's complete public account row remains available through
 * currentUser.
 */
function scopedMemberUsers(
  rows: Row[],
  viewerRole: "coach" | "trainee",
  currentUserId: string,
): Row[] {
  return camelRows(rows).map((row) => {
    const id = String(row.id ?? "");
    const role = String(row.role ?? "");
    const isOwn = id === currentUserId;
    const isOtherTrainee = role === "trainee" && !isOwn;
    if (isOtherTrainee) {
      return {
        id: row.id,
        tenantId: row.tenantId,
        username: row.username,
        role: row.role,
        isActive: asBoolean(row.isActive),
        createdAt: row.createdAt,
        updatedAt: row.updatedAt,
      };
    }

    // Coaches can coordinate with the class team.  A trainee can see coach
    // contact information, but neither role should receive account lifecycle
    // fields for another member.
    return {
      id: row.id,
      tenantId: row.tenantId,
      username: row.username,
      email: row.email,
      role: row.role,
      isActive: asBoolean(row.isActive),
      ...(isOwn ? {
        isTuitionSupported: asBoolean(row.isTuitionSupported),
        mustChangePassword: asBoolean(row.mustChangePassword),
      } : {}),
      createdAt: row.createdAt,
      updatedAt: row.updatedAt,
    };
  });
}

/**
 * Profiles in a Coach/Trainee snapshot are deliberately field-scoped.  The
 * UI may render a teammate card, but it must never receive another trainee's
 * guardian contact, date of birth, phone, or body measurements just because
 * those columns exist in D1.  The signed-in user's full profile is returned
 * separately as currentProfile.
 */
function scopedMemberProfiles(
  rows: Row[],
  users: Row[],
  viewerRole: "coach" | "trainee",
  currentUserId: string,
): Row[] {
  const roles = new Map(users.map((row) => [String(row.id), String(row.role)]));
  return camelRows(rows).map((row) => {
    const userId = String(row.userId ?? "");
    const role = roles.get(userId) ?? "";
    const isOtherTrainee = role === "trainee" && userId !== currentUserId;
    const isOwnProfile = userId === currentUserId;
    if (isOtherTrainee || (viewerRole === "trainee" && isOwnProfile)) {
      return {
        userId,
        fullName: row.fullName ?? "",
        photoObjectKey: row.photoObjectKey ?? "",
        updatedAt: row.updatedAt,
      };
    }

    // Staff contact details are useful to a Coach/Trainee for class
    // coordination, but personal/guardian fields stay server-side.
    return {
      userId,
      fullName: row.fullName ?? "",
      photoObjectKey: row.photoObjectKey ?? "",
      phone: row.phone ?? "",
      email: row.email ?? "",
      ...(role === "coach" ? { coachPosition: row.coachPosition ?? "" } : {}),
      updatedAt: row.updatedAt,
    };
  });
}

const USER_PUBLIC_COLUMNS = `id, tenant_id, username, email, role, is_active,
  is_tuition_supported, must_change_password, created_at, updated_at`;

/**
 * A missing checkout must never keep a roster grant or a live timer open
 * forever.  We close stale rows at a conservative eight-hour cap.  The empty
 * checkout object key is deliberate: it marks a safety close, not a valid
 * checkout, so no salary can be approved until the Coach uploads the real
 * checkout selfie.
 */
export const MAX_OPEN_CHECKIN_SECONDS = 8 * 60 * 60;
export const CHECKIN_OPEN_LEAD_MINUTES = 60;
export const CHECKIN_LOCK_AFTER_END_MINUTES = 120;
export const AUTO_ABSENT_REVIEW_NOTE = "AUTO_ABSENT_NO_CHECKIN";
export const FOUNDER_SUBSTITUTED_COACH_REVIEW_NOTE = "FOUNDER_SUBSTITUTED_COACH";
export const FOUNDER_NO_ATTENDANCE_REVIEW_NOTE = "FOUNDER_NO_ATTENDANCE";
const HISTORICAL_SUBSTITUTION_MARKER = "Coach không dạy; Founder điểm danh thay Coach";
const HISTORICAL_MANUAL_MARKER = "Founder ghi nhận buổi học cũ; Coach đã dạy";
const HISTORICAL_NO_ATTENDANCE_MARKER = "Coach không dạy (Founder không điểm danh dạy)";
const VIETNAM_UTC_OFFSET_MINUTES = 7 * 60;

export type HistoricalAttendanceMode =
  | "founder_substituted"
  | "coach_taught_manually"
  | "coach_no_attendance";

/**
 * Historical attendance is persisted as a human-readable reason because the
 * original schema predates an explicit mode column.  Keep the reason
 * idempotent: older clients sometimes sent the already-suffixed value back,
 * which produced duplicated markers and made the next edit ambiguous.
 */
export function canonicalHistoricalOverrideReason(
  reason: string,
  mode: HistoricalAttendanceMode,
): string {
  let base = reason.trim();
  const markers = [
    HISTORICAL_SUBSTITUTION_MARKER,
    HISTORICAL_MANUAL_MARKER,
    HISTORICAL_NO_ATTENDANCE_MARKER,
  ];
  let changed = true;
  while (changed) {
    changed = false;
    for (const marker of markers) {
      const suffix = `· ${marker}`;
      if (base.endsWith(suffix)) {
        base = base.slice(0, -suffix.length).trim().replace(/[·\s]+$/u, "").trim();
        changed = true;
      } else if (base === marker) {
        base = "";
        changed = true;
      }
    }
  }
  const marker = mode === "coach_no_attendance"
    ? HISTORICAL_NO_ATTENDANCE_MARKER
    : mode === "coach_taught_manually"
      ? HISTORICAL_MANUAL_MARKER
      : HISTORICAL_SUBSTITUTION_MARKER;
  return `${base || "Bổ sung buổi học cũ theo lịch lớp"} · ${marker}`;
}

type ScheduledClassRow = {
  id: string;
  schedule_days: string;
  start_date: string;
  start_time_minutes: number;
  end_time_minutes: number;
};

type ClassCoachRow = {
  class_id: string;
  coach_user_id: string;
  salary_per_session_vnd: number;
  assigned_at: string;
};

function dateKey(year: number, month: number, day: number): string {
  return `${year.toString().padStart(4, "0")}-${month.toString().padStart(2, "0")}-${day.toString().padStart(2, "0")}`;
}

function vietnamDateKey(now: Date): string {
  const shifted = new Date(now.getTime() + VIETNAM_UTC_OFFSET_MINUTES * 60_000);
  return dateKey(shifted.getUTCFullYear(), shifted.getUTCMonth() + 1, shifted.getUTCDate());
}

function addDaysToDateKey(value: string, offset: number): string {
  const [year, month, day] = value.split("-").map(Number);
  const date = new Date(Date.UTC(year!, month! - 1, day!) + offset * 86_400_000);
  return dateKey(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
}

/**
 * A salary becomes payable on the 10th after its first Founder confirmation.
 * Confirmations on/before day 10 use that month's date; later confirmations
 * roll to day 10 of the following month. Calendar math follows Vietnam time.
 */
export function salaryDueDateForConfirmation(confirmedAt: string | Date): string {
  const instant = confirmedAt instanceof Date ? confirmedAt : new Date(confirmedAt);
  const shifted = new Date(instant.getTime() + VIETNAM_UTC_OFFSET_MINUTES * 60_000);
  let year = shifted.getUTCFullYear();
  let month = shifted.getUTCMonth() + 1;
  if (shifted.getUTCDate() > 10) {
    month += 1;
    if (month > 12) {
      month = 1;
      year += 1;
    }
  }
  return dateKey(year, month, 10);
}

function scheduledBoundaryUtc(sessionDate: string, minutes: number): Date {
  const [year, month, day] = sessionDate.split("-").map(Number);
  const clamped = Math.max(0, Math.trunc(minutes));
  return new Date(Date.UTC(year!, month! - 1, day!)
    + clamped * 60_000
    - VIETNAM_UTC_OFFSET_MINUTES * 60_000);
}

function isScheduledOn(classRow: ScheduledClassRow, sessionDate: string): boolean {
  const [year, month, day] = sessionDate.split("-").map(Number);
  const weekday = new Date(Date.UTC(year!, month! - 1, day!)).getUTCDay();
  return classRow.schedule_days.split(",").some((item) => Number(item) === weekday);
}

/**
 * Creates a session snapshot for recent scheduled dates and records an
 * explicit locked absence after the class end plus two hours. This is called
 * by snapshots and by the hourly Worker cron, so it works even when a Coach
 * never opens the app. The marker is intentionally a review note for schema
 * compatibility with already-provisioned D1 databases.
 */
export async function markMissedCoachCheckIns(
  env: Env,
  tenantId: string,
  now = new Date(),
): Promise<void> {
  const classes = await allRows<ScheduledClassRow>(env.DB.prepare(
    `SELECT id, schedule_days, start_date, start_time_minutes, end_time_minutes
     FROM classes WHERE tenant_id=? AND is_active=1`,
  ).bind(tenantId));
  const assignments = await allRows<ClassCoachRow>(env.DB.prepare(
    `SELECT class_id, coach_user_id, salary_per_session_vnd, assigned_at
     FROM class_coaches WHERE tenant_id=? AND is_active=1`,
  ).bind(tenantId));
  if (classes.length === 0 || assignments.length === 0) return;

  const today = vietnamDateKey(now);
  // Keep a bounded lookback so a long-offline tenant is repaired without
  // materializing an unbounded number of historical draft sessions.
  for (let offset = -14; offset <= 0; offset += 1) {
    const sessionDate = addDaysToDateKey(today, offset);
    for (const classRow of classes) {
      if (classRow.start_date && sessionDate < classRow.start_date) continue;
      if (!isScheduledOn(classRow, sessionDate)) continue;
      const classAssignments = assignments.filter((item) => {
        if (item.class_id !== classRow.id) return false;
        const assignedAt = Date.parse(item.assigned_at);
        return !Number.isFinite(assignedAt)
          || assignedAt <= scheduledBoundaryUtc(sessionDate, classRow.end_time_minutes).getTime();
      });
      if (classAssignments.length === 0) continue;

      let session = await env.DB.prepare(
        `SELECT id FROM training_sessions WHERE tenant_id=? AND class_id=? AND session_date=? LIMIT 1`,
      ).bind(tenantId, classRow.id, sessionDate).first<{ id: string }>();
      if (!session) {
        const sessionId = newId();
        const nowIsoValue = nowIso();
        await env.DB.prepare(
          `INSERT OR IGNORE INTO training_sessions
           (id, tenant_id, class_id, session_date, status, created_at, updated_at)
           VALUES (?, ?, ?, ?, 'draft', ?, ?)`,
        ).bind(sessionId, tenantId, classRow.id, sessionDate, nowIsoValue, nowIsoValue).run();
        session = await env.DB.prepare(
          `SELECT id FROM training_sessions WHERE tenant_id=? AND class_id=? AND session_date=? LIMIT 1`,
        ).bind(tenantId, classRow.id, sessionDate).first<{ id: string }>();
      }
      if (!session) continue;

      const assignmentStatements = classAssignments.map((item) => env.DB.prepare(
        `INSERT OR IGNORE INTO session_coaches
         (id, tenant_id, session_id, coach_user_id, snapshotted_at)
         VALUES (?, ?, ?, ?, ?)`,
      ).bind(newId(), tenantId, session!.id, item.coach_user_id, nowIso()));
      for (let offset = 0; offset < assignmentStatements.length; offset += 100) {
        await env.DB.batch(assignmentStatements.slice(offset, offset + 100));
      }

      const lockAt = scheduledBoundaryUtc(
        sessionDate,
        classRow.end_time_minutes + CHECKIN_LOCK_AFTER_END_MINUTES,
      );
      if (now.getTime() < lockAt.getTime()) continue;

      const existing = await allRows<{ coach_user_id: string }>(env.DB.prepare(
        `SELECT coach_user_id FROM coach_checkins WHERE tenant_id=? AND session_id=?`,
      ).bind(tenantId, session.id));
      const existingCoachIds = new Set(existing.map((item) => item.coach_user_id));
      const statements: D1PreparedStatement[] = [];
      for (const assignment of classAssignments) {
        if (existingCoachIds.has(assignment.coach_user_id)) continue;
        const absentId = newId();
        statements.push(env.DB.prepare(
          `INSERT OR IGNORE INTO coach_checkins
           (id, tenant_id, session_id, coach_user_id, checkin_selfie_object_key,
            checkout_selfie_object_key, salary_per_session_vnd_snapshot, checked_in_at,
            checked_out_at, duration_seconds, approval_status, review_note)
           VALUES (?, ?, ?, ?, '', '', ?, ?, ?, 0, 'rejected', ?)`,
        ).bind(absentId, tenantId, session.id, assignment.coach_user_id,
          Math.max(0, assignment.salary_per_session_vnd), lockAt.toISOString(),
          lockAt.toISOString(), AUTO_ABSENT_REVIEW_NOTE));
        existingCoachIds.add(assignment.coach_user_id);
      }
      for (let offset = 0; offset < statements.length; offset += 100) {
        await env.DB.batch(statements.slice(offset, offset + 100));
      }
    }
  }
}

export async function markMissedCoachCheckInsForAllTenants(env: Env): Promise<void> {
  const tenants = await allRows<{ id: string }>(env.DB.prepare(
    "SELECT id FROM tenants WHERE status='active'",
  ));
  for (const tenant of tenants) {
    await runTenantMaintenance(env, tenant.id);
  }
}

/**
 * Remove short-lived security material and unreferenced media outside the
 * request path.  All predicates are intentionally bounded by an age/expiry
 * value so a delayed cron cannot remove a newly-created OAuth flow or upload.
 */
export async function cleanupExpiredSecurityRows(env: Env): Promise<void> {
  const now = nowIso();
  const usedTicketCutoff = new Date(Date.now() - 60 * 60_000).toISOString();
  const staleUploadCutoff = new Date(Date.now() - 24 * 60 * 60_000).toISOString();

  await env.DB.batch([
    env.DB.prepare("DELETE FROM oauth_states WHERE expires_at <= ?").bind(now),
    env.DB.prepare(
      "DELETE FROM oauth_exchange_tickets WHERE expires_at <= ? OR (used_at IS NOT NULL AND used_at <= ?)",
    ).bind(now, usedTicketCutoff),
    env.DB.prepare(
      "DELETE FROM auth_sessions WHERE expires_at <= ? OR (revoked_at IS NOT NULL AND revoked_at <= ?)",
    ).bind(now, usedTicketCutoff),
    env.DB.prepare("DELETE FROM idempotency_keys WHERE expires_at <= ?").bind(now),
    env.DB.prepare("DELETE FROM public_registration_requests WHERE expires_at <= ?").bind(now),
    env.DB.prepare("DELETE FROM public_registration_attempts WHERE expires_at <= ?").bind(now),
    env.DB.prepare("DELETE FROM password_reset_tokens WHERE expires_at <= ? OR used_at IS NOT NULL").bind(now),
  ]);

  // An upload row is considered orphaned only when no domain record points at
  // its object key.  Keep the query bounded; a future run will continue the
  // cleanup if a tenant has a large abandoned-upload backlog.
  const orphaned = await allRows<{ tenant_id: string | null; object_key: string }>(env.DB.prepare(
    `SELECT u.tenant_id, u.object_key
     FROM uploads u
     WHERE u.created_at < ?
       AND u.object_key <> ''
       AND NOT EXISTS (
         SELECT 1 FROM profiles p
         WHERE p.tenant_id = u.tenant_id AND p.photo_object_key = u.object_key
       )
       AND NOT EXISTS (
         SELECT 1 FROM clubs c
         WHERE c.tenant_id = u.tenant_id AND c.logo_object_key = u.object_key
       )
       AND NOT EXISTS (
         SELECT 1 FROM coach_checkins ci
         WHERE ci.tenant_id = u.tenant_id
           AND (ci.checkin_selfie_object_key = u.object_key
             OR ci.checkout_selfie_object_key = u.object_key)
       )
       AND NOT EXISTS (
         SELECT 1 FROM payment_proofs pp
         WHERE pp.tenant_id = u.tenant_id AND pp.image_object_key = u.object_key
       )
       AND NOT EXISTS (
         SELECT 1 FROM receipts r
         WHERE r.tenant_id = u.tenant_id AND r.pdf_object_key = u.object_key
       )
     LIMIT 100`,
  ).bind(staleUploadCutoff));
  if (orphaned.length === 0) return;

  const results = await Promise.all(orphaned.map(async (row) => {
    try {
      if (env.FILES) await env.FILES.delete(row.object_key);
      return row;
    } catch {
      // Keep the metadata row when R2 is temporarily unavailable so the next
      // cron can retry the object deletion safely.
      return null;
    }
  }));
  const deleted = results.filter((row): row is { tenant_id: string | null; object_key: string } => row !== null);
  if (deleted.length > 0) {
    await env.DB.batch(deleted.map((row) => env.DB.prepare(
      "DELETE FROM uploads WHERE tenant_id IS ? AND object_key=?",
    ).bind(row.tenant_id, row.object_key)));
  }
}

/**
 * Repairs derived operational state for one tenant.  This work is deliberately
 * kept out of the read/snapshot path: a GET must be side-effect free and must
 * not scan fourteen days of schedules before returning the data the app needs.
 * The hourly Worker cron calls this method, while mutations that create a new
 * enrollment/session already create their required rows transactionally.
 */
export async function runTenantMaintenance(env: Env, tenantId: string): Promise<void> {
  await autoCloseStaleCheckIns(env, tenantId);
  await markMissedCoachCheckIns(env, tenantId);
  await recomputePendingCoachSalaries(env, tenantId);
  await recomputePendingCoachSalaryDueDates(env, tenantId);
  await ensureCoachSalaryReminders(env, tenantId);
  await ensureInitialTuitionInvoices(env, tenantId);
  await ensureTuitionCycleProgress(env, tenantId);
  // A trial can convert to official status during the progress pass; create
  // its first invoice immediately instead of waiting for the next cron tick.
  await ensureInitialTuitionInvoices(env, tenantId);
}

export async function autoCloseStaleCheckIns(
  env: Env,
  tenantId: string,
  coachUserId?: string,
): Promise<void> {
  const rows = await allRows<{ id: string; checked_in_at: string }>(env.DB.prepare(
    `SELECT id, checked_in_at FROM coach_checkins
     WHERE tenant_id=? AND checked_out_at IS NULL
       ${coachUserId ? "AND coach_user_id=?" : ""}`,
  ).bind(...(coachUserId ? [tenantId, coachUserId] : [tenantId])));
  const now = Date.now();
  const updates = rows.flatMap((row) => {
    const checkedInMs = Date.parse(row.checked_in_at);
    if (!Number.isFinite(checkedInMs)
        || now - checkedInMs < MAX_OPEN_CHECKIN_SECONDS * 1000) {
      return [];
    }
    const closeAt = new Date(checkedInMs + MAX_OPEN_CHECKIN_SECONDS * 1000).toISOString();
    return [env.DB.prepare(
      `UPDATE coach_checkins SET checked_out_at=?, duration_seconds=?, checkout_selfie_object_key=''
       WHERE id=? AND tenant_id=? AND checked_out_at IS NULL`,
    ).bind(closeAt, MAX_OPEN_CHECKIN_SECONDS, row.id, tenantId)];
  });
  if (updates.length > 0) {
    // D1 batches are atomic; keep the batch bounded in case an old tenant has
    // accumulated many abandoned sessions.
    for (let offset = 0; offset < updates.length; offset += 100) {
      await env.DB.batch(updates.slice(offset, offset + 100));
    }
  }
}

async function recomputePendingCoachSalaries(env: Env, tenantId: string): Promise<void> {
  // Keep legacy pending rows from older builds from paying an approved
  // check-in that never reached checkout. Paid rows remain immutable history.
  await env.DB.prepare(
    `UPDATE coach_salaries
     SET amount_vnd = COALESCE((
       SELECT SUM(ci.salary_per_session_vnd_snapshot)
       FROM coach_checkins ci
       JOIN training_sessions ts
         ON ts.id=ci.session_id AND ts.tenant_id=ci.tenant_id
       WHERE ci.tenant_id=coach_salaries.tenant_id
         AND ci.coach_user_id=coach_salaries.coach_user_id
         AND ci.approval_status='approved'
         AND ci.checked_out_at IS NOT NULL
        AND (ci.checkout_selfie_object_key <> ''
             OR ci.review_note LIKE '%Founder ghi nhận buổi học cũ; Coach đã dạy%')
         AND substr(ts.session_date, 1, 7)=coach_salaries.period
     ), 0),
     updated_at=?
     WHERE tenant_id=? AND status='pending'`,
  ).bind(nowIso(), tenantId).run();
}

async function recomputePendingCoachSalaryDueDates(env: Env, tenantId: string): Promise<void> {
  const rows = await allRows<{
    id: string;
    due_date: string;
    confirmed_at: string;
  }>(env.DB.prepare(
    `SELECT cs.id, cs.due_date, MIN(ci.reviewed_at) AS confirmed_at
       FROM coach_salaries cs
       JOIN coach_checkins ci
         ON ci.tenant_id=cs.tenant_id AND ci.coach_user_id=cs.coach_user_id
       JOIN training_sessions ts
         ON ts.id=ci.session_id AND ts.tenant_id=ci.tenant_id
      WHERE cs.tenant_id=? AND cs.status='pending' AND cs.amount_vnd>0
        AND ci.approval_status='approved' AND ci.reviewed_at IS NOT NULL
        AND ci.checked_out_at IS NOT NULL
        AND (ci.checkout_selfie_object_key<>''
             OR ci.review_note LIKE '%Founder ghi nhận buổi học cũ; Coach đã dạy%')
        AND substr(ts.session_date, 1, 7)=cs.period
      GROUP BY cs.id, cs.due_date`,
  ).bind(tenantId));
  const statements = rows.flatMap((row) => {
    const dueDate = salaryDueDateForConfirmation(row.confirmed_at);
    return dueDate === row.due_date
      ? []
      : [env.DB.prepare(
        "UPDATE coach_salaries SET due_date=?, updated_at=? WHERE id=? AND tenant_id=? AND status='pending'",
      ).bind(dueDate, nowIso(), row.id, tenantId)];
  });
  for (let offset = 0; offset < statements.length; offset += 100) {
    await env.DB.batch(statements.slice(offset, offset + 100));
  }
}

async function ensureCoachSalaryReminders(env: Env, tenantId: string): Promise<void> {
  const today = vietnamDateKey(new Date());
  const salaries = await allRows<{
    id: string;
    period: string;
    due_date: string;
    coach_name: string;
  }>(env.DB.prepare(
    `SELECT cs.id, cs.period, cs.due_date,
            COALESCE(NULLIF(p.full_name, ''), u.username) AS coach_name
       FROM coach_salaries cs
       JOIN users u ON u.id=cs.coach_user_id AND u.tenant_id=cs.tenant_id
       LEFT JOIN profiles p ON p.user_id=u.id AND p.tenant_id=u.tenant_id
      WHERE cs.tenant_id=? AND cs.status='pending' AND cs.amount_vnd>0 AND cs.due_date<=?`,
  ).bind(tenantId, today));
  for (const salary of salaries) {
    const isWarning = today >= addDaysToDateKey(salary.due_date, 5);
    const title = isWarning
      ? "Cảnh báo lương Coach quá hạn 5 ngày"
      : "Đến kỳ thanh toán lương Coach";
    const message = isWarning
      ? `Lương ${salary.coach_name} kỳ ${salary.period} đã quá hạn từ ${salary.due_date}. Vui lòng thanh toán ngay.`
      : `Đã đến kỳ thanh toán lương ${salary.coach_name} kỳ ${salary.period}, hạn ${salary.due_date}.`;
    await env.DB.prepare(
      `INSERT INTO notifications
       (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, is_read, created_at)
       SELECT lower(hex(randomblob(16))), ?, u.id, 'salary_reminder', ?, ?, ?, 0, ?
         FROM users u
        WHERE u.tenant_id=? AND u.role='founder' AND u.is_active=1
          AND NOT EXISTS (
            SELECT 1 FROM notifications n
             WHERE n.recipient_user_id=u.id AND n.kind='salary_reminder'
               AND n.related_entity_id=? AND n.title=?
          )`,
    ).bind(tenantId, title, message, salary.id, nowIso(), tenantId, salary.id, title).run();
  }
}

async function ensureInitialTuitionInvoices(env: Env, tenantId: string): Promise<void> {
  const now = nowIso();
  await env.DB.prepare(
    `INSERT INTO tuition_invoices (id, tenant_id, enrollment_id, trainee_user_id, class_id,
       cycle_number, cycle_count, cycle_fee_vnd, amount_vnd, attended_session_count,
       planned_session_count, due_date, status, payment_content, created_at, updated_at)
     SELECT lower(hex(randomblob(16))), ce.tenant_id, ce.id, ce.trainee_user_id, ce.class_id,
       1, 1, ce.cycle_fee_vnd, ce.cycle_fee_vnd, 0, c.tuition_session_count,
       date('now'), 'pending',
       COALESCE(NULLIF(p.full_name, ''), u.username) || ' dong hoc phi', ?, ?
     FROM class_enrollments ce
     JOIN classes c ON c.id = ce.class_id AND c.tenant_id = ce.tenant_id
     JOIN users u ON u.id = ce.trainee_user_id AND u.tenant_id = ce.tenant_id
     LEFT JOIN profiles p ON p.user_id = u.id AND p.tenant_id = ce.tenant_id
     WHERE ce.tenant_id = ? AND ce.is_active = 1 AND ce.is_trial = 0 AND u.is_tuition_supported = 0
       AND NOT EXISTS (
         SELECT 1 FROM tuition_invoices ti
         WHERE ti.enrollment_id = ce.id AND ti.cycle_number = 1
       )
     ON CONFLICT(enrollment_id, cycle_number) DO NOTHING`,
  ).bind(now, now, tenantId).run();
}

/**
 * Keeps cycle progress and the next tuition cycle in sync with delivered
 * lessons.  This is intentionally server-side so every device sees the same
 * warning after the second completed lesson and the same next-cycle reminder.
 * Paid invoices remain in D1 for receipt/audit history; the mobile UI hides a
 * paid invoice once its full cycle has been delivered.
 */
async function ensureTuitionCycleProgress(env: Env, tenantId: string): Promise<void> {
  const enrollments = await allRows<{
    id: string;
    class_id: string;
    trainee_user_id: string;
    enrolled_at: string;
    tuition_session_count: number;
    cycle_fee_vnd: number;
    is_tuition_supported: number;
    is_trial: number;
    trial_session_count: number;
  }>(env.DB.prepare(
    `SELECT ce.id, ce.class_id, ce.trainee_user_id, ce.enrolled_at,
            c.tuition_session_count, ce.cycle_fee_vnd, u.is_tuition_supported,
            ce.is_trial, ce.trial_session_count
     FROM class_enrollments ce
     JOIN classes c ON c.id=ce.class_id AND c.tenant_id=ce.tenant_id
     JOIN users u ON u.id=ce.trainee_user_id AND u.tenant_id=ce.tenant_id
     WHERE ce.tenant_id=? AND ce.is_active=1 AND c.is_active=1
       AND u.is_active=1 AND u.is_tuition_supported=0`,
  ).bind(tenantId));

  const today = nowIso().slice(0, 10);
  for (const enrollment of enrollments) {
    const perCycle = Math.max(1, Number(enrollment.tuition_session_count ?? 1));
    const invoices = await allRows<{
      id: string;
      cycle_number: number;
      cycle_count: number;
      cycle_fee_vnd: number;
      amount_vnd: number;
      attended_session_count: number;
      planned_session_count: number;
      status: string;
      due_date: string;
    }>(env.DB.prepare(
      `SELECT id, cycle_number, cycle_count, cycle_fee_vnd, amount_vnd,
              attended_session_count, planned_session_count, status, due_date
       FROM tuition_invoices
       WHERE tenant_id=? AND enrollment_id=? ORDER BY cycle_number ASC`,
    ).bind(tenantId, enrollment.id));
    const completedRow = await env.DB.prepare(
      `SELECT COUNT(DISTINCT ts.id) AS count
       FROM training_sessions ts
       JOIN attendance_records ar
         ON ar.session_id=ts.id AND ar.tenant_id=ts.tenant_id
        AND ar.trainee_user_id=? AND ar.status <> 'unmarked'
       WHERE ts.tenant_id=? AND ts.class_id=?
         AND ts.status IN ('submitted', 'locked')
         AND substr(ts.session_date, 1, 10) >= substr(?, 1, 10)`,
    ).bind(enrollment.trainee_user_id, tenantId, enrollment.class_id, enrollment.enrolled_at)
      .first<{ count: number }>();
    const completed = Math.max(0, Number(completedRow?.count ?? 0));
    if (Number(enrollment.is_trial) === 1) {
      const trialTarget = Math.max(1, Math.min(5, Number(enrollment.trial_session_count ?? 1)));
      if (completed < trialTarget) continue;
      await env.DB.prepare(
        `UPDATE class_enrollments SET is_trial=0, trial_session_count=0, enrolled_at=?
         WHERE id=? AND tenant_id=? AND is_trial=1`,
      ).bind(nowIso(), enrollment.id, tenantId).run();
      if (invoices.length === 0) continue;
    }
    if (invoices.length === 0) continue;
    const paidCycles = invoices
      .filter(item => item.status === "paid")
      .reduce((sum, item) => sum + Math.max(1, Number(item.cycle_count ?? 1)), 0);

    const updates: D1PreparedStatement[] = [];
    const proofObjectKeysToDelete: string[] = [];
    for (const invoice of invoices) {
      const previousCycles = invoices
        .filter(item => item.cycle_number < invoice.cycle_number)
        .reduce((sum, item) => sum + Math.max(1, Number(item.cycle_count ?? 1)), 0);
      const planned = Math.max(1, Number(invoice.planned_session_count ?? 0) ||
        perCycle * Math.max(1, Number(invoice.cycle_count ?? 1)));
      const attended = Math.max(0, Math.min(
        planned,
        completed - previousCycles * perCycle,
      ));
      const nextStatus = invoice.status === "pending" && invoice.due_date < today
        ? "overdue"
        : invoice.status;
      if (attended !== Number(invoice.attended_session_count ?? 0)
          || nextStatus !== invoice.status) {
        updates.push(env.DB.prepare(
          `UPDATE tuition_invoices
           SET attended_session_count=?, status=?, updated_at=?
           WHERE id=? AND tenant_id=?`,
        ).bind(attended, nextStatus, nowIso(), invoice.id, tenantId));
      }

      if (invoice.status !== "paid" && invoice.status !== "proof_submitted" && attended >= 2) {
        const existing = await env.DB.prepare(
          `SELECT 1 FROM notifications
           WHERE tenant_id=? AND recipient_user_id=? AND kind='tuition_reminder'
             AND related_entity_id=? LIMIT 1`,
        ).bind(tenantId, enrollment.trainee_user_id, invoice.id).first();
        if (!existing) {
          updates.push(env.DB.prepare(
            `INSERT INTO notifications
             (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
             VALUES (?, ?, ?, 'tuition_reminder', 'Cảnh báo học phí',
                     'Bạn đã học đủ 2 buổi nhưng chưa đóng học phí chu kỳ này.', ?, ?)`,
          ).bind(newId(), tenantId, enrollment.trainee_user_id, invoice.id, nowIso()));
        }
      }

      // A fully delivered paid cycle no longer needs its payment bill. Keep
      // the paid invoice/receipt as accounting history, but remove the proof
      // row and private R2 image so the trainee's bill list stays concise.
      if (invoice.status === "paid" && attended >= planned) {
        const proofs = await allRows<{ id: string; image_object_key: string }>(env.DB.prepare(
          "SELECT id, image_object_key FROM payment_proofs WHERE tenant_id=? AND invoice_id=?",
        ).bind(tenantId, invoice.id));
        for (const proof of proofs) {
          if (proof.image_object_key) proofObjectKeysToDelete.push(proof.image_object_key);
          if (proof.image_object_key) {
            updates.push(env.DB.prepare(
              "DELETE FROM uploads WHERE tenant_id=? AND object_key=?",
            ).bind(tenantId, proof.image_object_key));
          }
          updates.push(env.DB.prepare(
            "DELETE FROM payment_proofs WHERE id=? AND tenant_id=?",
          ).bind(proof.id, tenantId));
        }
      }
    }

    if (updates.length > 0) {
      for (let offset = 0; offset < updates.length; offset += 100) {
        await env.DB.batch(updates.slice(offset, offset + 100));
      }
    }
    if (proofObjectKeysToDelete.length > 0 && env.FILES) {
      await Promise.allSettled(proofObjectKeysToDelete.map((key) => env.FILES.delete(key)));
    }

    const hasOpenInvoice = invoices.some(item => item.status !== "paid" && item.status !== "waived");
    if (!hasOpenInvoice && paidCycles > 0 && completed >= paidCycles * perCycle) {
      const nextCycleNumber = Math.max(...invoices.map(item => Number(item.cycle_number))) + 1;
      const existingNext = invoices.some(item => Number(item.cycle_number) === nextCycleNumber);
      if (!existingNext) {
        const profile = await env.DB.prepare(
          "SELECT full_name FROM profiles WHERE tenant_id=? AND user_id=? LIMIT 1",
        ).bind(tenantId, enrollment.trainee_user_id).first<{ full_name: string }>();
        const fee = Math.max(0, Number(enrollment.cycle_fee_vnd ?? 0));
        const now = nowIso();
        const invoiceId = newId();
        await env.DB.batch([
          env.DB.prepare(
            `INSERT INTO tuition_invoices
             (id, tenant_id, enrollment_id, trainee_user_id, class_id, cycle_number,
              cycle_count, cycle_fee_vnd, amount_vnd, attended_session_count,
              planned_session_count, due_date, status, payment_content, created_at, updated_at)
             VALUES (?, ?, ?, ?, ?, ?, 1, ?, ?, 0, ?, ?, 'pending', ?, ?, ?)
             ON CONFLICT(enrollment_id, cycle_number) DO NOTHING`,
          ).bind(invoiceId, tenantId, enrollment.id, enrollment.trainee_user_id,
            enrollment.class_id, nextCycleNumber, fee, fee, perCycle, today,
            `${profile?.full_name || "Hoc vien"} dong hoc phi`, now, now),
          env.DB.prepare(
            `INSERT INTO notifications
             (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
             VALUES (?, ?, ?, 'tuition_reminder', 'Nhắc đóng học phí chu kỳ tiếp theo',
                     'Chu kỳ trước đã hoàn tất. Vui lòng đóng học phí chu kỳ tiếp theo.', ?, ?)`,
          ).bind(newId(), tenantId, enrollment.trainee_user_id, invoiceId, now),
        ]);
      }
    }
  }
}

async function tenantSyncVersion(env: Env, tenantId: string): Promise<number> {
  // Keep this as one D1 batch instead of a large UNION ALL compound SELECT.
  // D1/SQLite rejects compound SELECTs above its configured term limit; the
  // old 18-way UNION made every tenant snapshot fail with SQLITE_ERROR. A
  // batch still uses one database round-trip while each statement remains a
  // small, index-friendly aggregate.
  const statements = [
    ["users", "updated_at"],
    ["profiles", "updated_at"],
    ["clubs", "updated_at"],
    ["venues", "updated_at"],
    ["classes", "updated_at"],
    ["class_coaches", "assigned_at"],
    ["class_enrollments", "enrolled_at"],
    ["training_sessions", "updated_at"],
    ["session_coaches", "snapshotted_at"],
    ["coach_checkins", "MAX(checked_in_at, checked_out_at, reviewed_at)"],
    ["attendance_records", "recorded_at"],
    ["tuition_invoices", "updated_at"],
    ["payment_proofs", "MAX(submitted_at, reviewed_at)"],
    ["receipts", "confirmed_at"],
    ["coach_salaries", "updated_at"],
    ["trainee_evaluations", "updated_at"],
    ["notifications", "created_at"],
    ["audit_logs", "created_at"],
  ] as const;
  const results = await env.DB.batch(
    statements.map(([table, timestampExpression]) =>
      env.DB.prepare(
        `SELECT MAX(${timestampExpression}) AS latest, COUNT(*) AS row_count
           FROM ${table} WHERE tenant_id=?`,
      ).bind(tenantId),
    ),
  );

  let latestMs = 0;
  let rowCount = 0;
  for (const result of results) {
    const row = (result.results?.[0] ?? {}) as {
      latest?: string | null;
      row_count?: number;
    };
    const parsed = row.latest ? Date.parse(row.latest) : 0;
    if (Number.isFinite(parsed) && parsed > latestMs) {
      latestMs = parsed;
    }
    rowCount += Math.max(0, Number(row.row_count ?? 0));
  }

  if (latestMs <= 0) return 1;
  return latestMs * 1000 + rowCount;
}

export async function getSnapshot(
  env: Env,
  auth: AuthUser,
  afterSyncVersion?: number,
): Promise<Record<string, unknown>> {
  if (auth.role === "admin" || !auth.tenantId) {
    throw new ApiError(403, "forbidden", "Admin không có snapshot vận hành đội bóng.");
  }

  const tenantId = auth.tenantId;
  const syncVersion = await tenantSyncVersion(env, tenantId);
  if (afterSyncVersion && afterSyncVersion === syncVersion) {
    return {
      unchanged: true,
      syncVersion,
      serverTime: nowIso(),
      role: auth.role,
    };
  }
  const identityBatch = await env.DB.batch([
    env.DB.prepare("SELECT * FROM clubs WHERE tenant_id = ? LIMIT 1").bind(tenantId),
    env.DB.prepare("SELECT * FROM users WHERE id = ? LIMIT 1").bind(auth.id),
    env.DB.prepare("SELECT * FROM profiles WHERE user_id = ? LIMIT 1").bind(auth.id),
    env.DB.prepare("SELECT * FROM notifications WHERE recipient_user_id = ? ORDER BY created_at DESC LIMIT 500")
      .bind(auth.id),
  ]);
  const club = (identityBatch[0]?.results?.[0] ?? null) as ClubRow | null;
  const currentUserRow = (identityBatch[1]?.results?.[0] ?? null) as UserRow | null;
  const currentProfileRow = (identityBatch[2]?.results?.[0] ?? null) as ProfileRow | null;
  if (!currentUserRow) throw new ApiError(401, "invalid_session", "Tài khoản không còn hiệu lực.");
  const identity = {
    currentUser: publicUser(currentUserRow),
    currentProfile: publicProfile(currentProfileRow ?? null),
    activeClub: publicClub(club ?? null),
    club: publicClub(club ?? null),
  };
  const notifications = camelRows((identityBatch[3]?.results ?? []) as Row[]);

  if (isFounderLike(auth.role) || auth.role === "manager") {
    // These collections are independent reads.  D1 batch executes them in a
    // single database round-trip while preserving the same result order,
    // which is materially faster than fifteen concurrent service calls.
    const batch = await env.DB.batch([
      env.DB.prepare(`SELECT ${USER_PUBLIC_COLUMNS} FROM users WHERE tenant_id = ?`).bind(tenantId),
      env.DB.prepare("SELECT * FROM profiles WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM venues WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM classes WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM class_coaches WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM class_enrollments WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM training_sessions WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM session_coaches WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM coach_checkins WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM attendance_records WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM tuition_invoices WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM payment_proofs WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM receipts WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM coach_salaries WHERE tenant_id = ?").bind(tenantId),
      env.DB.prepare("SELECT * FROM audit_logs WHERE tenant_id = ? ORDER BY created_at DESC LIMIT 500").bind(tenantId),
    ]);
    const rowsAt = (index: number): Row[] => (batch[index]?.results ?? []) as Row[];
    const users = rowsAt(0);
    const profiles = rowsAt(1);
    const venues = rowsAt(2);
    const classes = rowsAt(3);
    const classCoaches = rowsAt(4);
    const enrollments = rowsAt(5);
    const sessions = rowsAt(6);
    const sessionCoaches = rowsAt(7);
    const checkIns = rowsAt(8);
    const attendance = rowsAt(9);
    const invoices = rowsAt(10);
    const proofs = rowsAt(11);
    const receipts = rowsAt(12);
    const salaries = rowsAt(13);
    const auditLogs = rowsAt(14);
    return {
      syncVersion,
      serverTime: nowIso(),
      role: auth.role,
      ...identity,
      users: safeUsers(users),
      profiles: camelRows(profiles),
      venues: camelRows(venues),
      classes: camelRows(classes),
      classCoaches: camelRows(classCoaches),
      classEnrollments: camelRows(enrollments),
      trainingSessions: camelRows(sessions),
      sessionCoaches: camelRows(sessionCoaches),
      coachCheckIns: camelRows(checkIns),
      attendanceRecords: camelRows(attendance),
      tuitionInvoices: camelRows(invoices),
      paymentProofs: camelRows(proofs),
      receipts: camelRows(receipts),
      coachSalaries: camelRows(salaries),
      auditLogs: camelRows(auditLogs),
      notifications,
    };
  }

  if (auth.role === "coach") {
    const classScope = `SELECT class_id FROM class_coaches
      WHERE tenant_id = ? AND coach_user_id = ? AND is_active = 1`;
    // A Coach may receive the roster for an evaluation only after the Founder
    // opens the evaluation request for that class.  An open check-in remains
    // another valid roster grant for attendance, and is intentionally kept in
    // the same scope so checkout still closes the attendance roster.
    const openClassScope = `
      SELECT c.id AS class_id
      FROM classes c
      WHERE c.tenant_id = ?
        AND c.evaluation_request_open = 1
        AND c.is_active = 1
        AND EXISTS (
          SELECT 1 FROM class_coaches evaluation_cc
          WHERE evaluation_cc.tenant_id = c.tenant_id
            AND evaluation_cc.class_id = c.id
            AND evaluation_cc.coach_user_id = ?
            AND evaluation_cc.is_active = 1
        )
      UNION
      SELECT DISTINCT ts.class_id
      FROM coach_checkins ci
      JOIN training_sessions ts ON ts.id = ci.session_id
      WHERE ci.tenant_id = ? AND ci.coach_user_id = ? AND ci.checked_out_at IS NULL`;
    const memberScope = `SELECT u.id FROM users u WHERE u.tenant_id = ? AND (
      u.id = ? OR u.role = 'founder' OR
      (u.role = 'coach' AND EXISTS (
        SELECT 1 FROM class_coaches cc WHERE cc.coach_user_id = u.id AND cc.is_active = 1
          AND cc.class_id IN (${classScope})
      )) OR
      (u.role = 'trainee' AND EXISTS (
        SELECT 1 FROM class_enrollments ce WHERE ce.trainee_user_id = u.id AND ce.is_active = 1
          AND ce.class_id IN (${openClassScope})
      )))`;
    const memberBindings = [
      tenantId,
      auth.id,
      tenantId,
      auth.id,
      tenantId,
      auth.id,
      tenantId,
      auth.id,
    ];
    const coachBatch = await env.DB.batch([
      env.DB.prepare(`SELECT ${USER_PUBLIC_COLUMNS} FROM users WHERE id IN (${memberScope})`)
        .bind(...memberBindings),
      env.DB.prepare(`SELECT p.* FROM profiles p WHERE p.user_id IN (${memberScope})`)
        .bind(...memberBindings),
      env.DB.prepare(`SELECT * FROM venues WHERE tenant_id = ? AND id IN
        (SELECT venue_id FROM classes WHERE id IN (${classScope}))`).bind(tenantId, tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM classes WHERE tenant_id = ? AND id IN (${classScope})`)
        .bind(tenantId, tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM class_coaches WHERE tenant_id = ? AND class_id IN (${classScope})`)
        .bind(tenantId, tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM class_enrollments WHERE tenant_id = ? AND class_id IN (${openClassScope})`)
        .bind(tenantId, tenantId, auth.id, tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM training_sessions WHERE tenant_id = ? AND class_id IN (${classScope})`)
        .bind(tenantId, tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM session_coaches WHERE tenant_id = ? AND coach_user_id = ?`)
        .bind(tenantId, auth.id),
      env.DB.prepare(`SELECT * FROM coach_checkins WHERE tenant_id = ? AND coach_user_id = ?`)
        .bind(tenantId, auth.id),
      env.DB.prepare(`SELECT ar.* FROM attendance_records ar
        JOIN coach_checkins ci ON ci.session_id = ar.session_id AND ci.coach_user_id = ?
        WHERE ar.tenant_id = ? AND ci.checked_out_at IS NULL`).bind(auth.id, tenantId),
      env.DB.prepare("SELECT * FROM coach_salaries WHERE tenant_id = ? AND coach_user_id = ?")
        .bind(tenantId, auth.id),
    ]);
    const coachRowsAt = (index: number): Row[] => (coachBatch[index]?.results ?? []) as Row[];
    const users = coachRowsAt(0);
    const profiles = coachRowsAt(1);
    const venues = coachRowsAt(2);
    const classes = coachRowsAt(3);
    const classCoaches = coachRowsAt(4);
    const enrollments = coachRowsAt(5);
    const sessions = coachRowsAt(6);
    const sessionCoaches = coachRowsAt(7);
    const checkIns = coachRowsAt(8);
    const attendance = coachRowsAt(9);
    const salaries = coachRowsAt(10);
    return {
      syncVersion,
      serverTime: nowIso(),
      role: auth.role,
      ...identity,
      users: scopedMemberUsers(users, "coach", auth.id),
      profiles: scopedMemberProfiles(profiles, users, "coach", auth.id),
      venues: camelRows(venues),
      classes: camelRows(classes),
      classCoaches: camelRows(classCoaches),
      classEnrollments: camelRows(enrollments),
      trainingSessions: camelRows(sessions),
      sessionCoaches: camelRows(sessionCoaches),
      coachCheckIns: camelRows(checkIns),
      attendanceRecords: camelRows(attendance),
      tuitionInvoices: [],
      paymentProofs: [],
      receipts: [],
      coachSalaries: camelRows(salaries),
      notifications,
    };
  }

  const classScope = `SELECT class_id FROM class_enrollments
    WHERE tenant_id = ? AND trainee_user_id = ? AND is_active = 1`;
  const memberScope = `SELECT u.id FROM users u WHERE u.tenant_id = ? AND (
    u.role = 'founder' OR u.id = ? OR
    (u.role = 'trainee' AND EXISTS (SELECT 1 FROM class_enrollments ce
      WHERE ce.trainee_user_id = u.id AND ce.is_active = 1 AND ce.class_id IN (${classScope}))) OR
    (u.role = 'coach' AND EXISTS (SELECT 1 FROM class_coaches cc
      WHERE cc.coach_user_id = u.id AND cc.is_active = 1 AND cc.class_id IN (${classScope}))))`;
  const memberBindings = [tenantId, auth.id, tenantId, auth.id, tenantId, auth.id];
  const traineeBatch = await env.DB.batch([
    env.DB.prepare(`SELECT ${USER_PUBLIC_COLUMNS} FROM users WHERE id IN (${memberScope})`)
      .bind(...memberBindings),
    env.DB.prepare(`SELECT p.* FROM profiles p WHERE p.user_id IN (${memberScope})`)
      .bind(...memberBindings),
    env.DB.prepare(`SELECT * FROM venues WHERE tenant_id = ? AND id IN
      (SELECT venue_id FROM classes WHERE id IN (${classScope}))`).bind(tenantId, tenantId, auth.id),
    env.DB.prepare(`SELECT * FROM classes WHERE tenant_id = ? AND id IN (${classScope})`)
      .bind(tenantId, tenantId, auth.id),
    env.DB.prepare(`SELECT * FROM class_coaches WHERE tenant_id = ? AND class_id IN (${classScope})`)
      .bind(tenantId, tenantId, auth.id),
    env.DB.prepare(`SELECT * FROM class_enrollments WHERE tenant_id = ? AND class_id IN (${classScope})`)
      .bind(tenantId, tenantId, auth.id),
    env.DB.prepare(`SELECT * FROM training_sessions WHERE tenant_id = ? AND class_id IN (${classScope})`)
      .bind(tenantId, tenantId, auth.id),
    env.DB.prepare("SELECT * FROM attendance_records WHERE tenant_id = ? AND trainee_user_id = ?")
      .bind(tenantId, auth.id),
    env.DB.prepare("SELECT * FROM tuition_invoices WHERE tenant_id = ? AND trainee_user_id = ?")
      .bind(tenantId, auth.id),
    env.DB.prepare(`SELECT pp.* FROM payment_proofs pp JOIN tuition_invoices ti ON ti.id = pp.invoice_id
      WHERE pp.tenant_id = ? AND ti.trainee_user_id = ?`).bind(tenantId, auth.id),
    env.DB.prepare(`SELECT r.* FROM receipts r JOIN tuition_invoices ti ON ti.id = r.invoice_id
      WHERE r.tenant_id = ? AND ti.trainee_user_id = ?`).bind(tenantId, auth.id),
  ]);
  const traineeRowsAt = (index: number): Row[] => (traineeBatch[index]?.results ?? []) as Row[];
  const users = traineeRowsAt(0);
  const profiles = traineeRowsAt(1);
  const venues = traineeRowsAt(2);
  const classes = traineeRowsAt(3);
  const coaches = traineeRowsAt(4);
  const enrollments = traineeRowsAt(5);
  const sessions = traineeRowsAt(6);
  const attendance = traineeRowsAt(7);
  const invoices = traineeRowsAt(8);
  const proofs = traineeRowsAt(9);
  const receipts = traineeRowsAt(10);
  return {
    syncVersion,
    serverTime: nowIso(),
    role: auth.role,
    ...identity,
    users: scopedMemberUsers(users, "trainee", auth.id),
    profiles: scopedMemberProfiles(profiles, users, "trainee", auth.id),
    venues: camelRows(venues),
    classes: camelRows(classes),
    classCoaches: camelRows(coaches),
    classEnrollments: camelRows(enrollments),
    trainingSessions: camelRows(sessions),
    sessionCoaches: [],
    coachCheckIns: [],
    attendanceRecords: camelRows(attendance),
    tuitionInvoices: camelRows(invoices),
    paymentProofs: camelRows(proofs),
    receipts: camelRows(receipts),
    coachSalaries: [],
    notifications,
  };
}

function list(body: Row, key: string): Row[] {
  const value = body[key];
  if (value === undefined) return [];
  if (!Array.isArray(value) || value.some((item) => !item || typeof item !== "object" || Array.isArray(item))) {
    throw new ApiError(400, "validation_error", `${key} phải là một mảng object.`);
  }
  return value as Row[];
}

function idOf(row: Row): string {
  const value = row.id;
  if (value === undefined || value === null || value === "") return newId();
  return requireText(value, "id", 64);
}

function rowsWithIds(body: Row, key: string): Row[] {
  const rows = list(body, key);
  for (const row of rows) row.id = idOf(row);
  return rows;
}

async function ensureIdAvailable(env: Env, table: string, id: string, tenantId: string): Promise<void> {
  const existing = await env.DB.prepare(`SELECT tenant_id FROM ${table} WHERE id = ? LIMIT 1`)
    .bind(id).first<{ tenant_id: string }>();
  if (existing && existing.tenant_id !== tenantId) {
    throw new ApiError(403, "tenant_boundary", "ID thuộc tenant khác.");
  }
}

/**
 * Checks all incoming IDs for a table in one query.  The old implementation
 * performed one D1 round-trip per row inside the snapshot mutation loop; a
 * class with ten enrollments therefore paid for ten serial reads before its
 * single write batch.  The endpoint still enforces the tenant boundary, but
 * now performs one bounded lookup per collection in parallel.
 */
async function ensureIdsAvailable(
  env: Env,
  table: string,
  ids: readonly string[],
  tenantId: string,
): Promise<void> {
  const uniqueIds = [...new Set(ids)].filter(Boolean);
  if (uniqueIds.length === 0) return;
  const placeholders = uniqueIds.map(() => "?").join(",");
  const rows = await allRows<{ id: string; tenant_id: string }>(env.DB.prepare(
    `SELECT id, tenant_id FROM ${table} WHERE id IN (${placeholders})`,
  ).bind(...uniqueIds));
  if (rows.some((row) => row.tenant_id !== tenantId)) {
    throw new ApiError(403, "tenant_boundary", "ID thuộc tenant khác.");
  }
}

async function assertTenantOrIncoming(
  env: Env,
  table: "users" | "venues" | "classes" | "training_sessions" | "tuition_invoices" | "uploads",
  id: string,
  tenantId: string,
  incomingIds: ReadonlySet<string>,
): Promise<void> {
  if (incomingIds.has(id)) return;
  await assertTenantEntity(env, table, id, tenantId);
}

async function assertMemberRole(
  env: Env,
  userId: string,
  tenantId: string,
  expectedRole: "coach" | "trainee",
  incomingRoles: ReadonlyMap<string, string>,
): Promise<void> {
  const incomingRole = incomingRoles.get(userId);
  if (incomingRole !== undefined) {
    if (incomingRole !== expectedRole) throw new ApiError(400, `invalid_${expectedRole}`, "Role thành viên không hợp lệ.");
    return;
  }
  const row = await env.DB.prepare("SELECT id FROM users WHERE id=? AND tenant_id=? AND role=?")
    .bind(userId, tenantId, expectedRole).first();
  if (!row) throw new ApiError(400, `invalid_${expectedRole}`, "Thành viên không thuộc đội.");
}

export async function applySnapshot(env: Env, auth: AuthUser, body: Row): Promise<Record<string, unknown>> {
  if (auth.role === "admin" || !auth.tenantId) throw new ApiError(403, "forbidden", "Admin không có snapshot đội bóng.");
  const tenantId = auth.tenantId;
  const statements: D1PreparedStatement[] = [];
  const now = nowIso();
  const userRows = rowsWithIds(body, "users");
  const profileRows = list(body, "profiles");
  const venueRows = rowsWithIds(body, "venues");
  const classRows = rowsWithIds(body, "classes");
  const classCoachRows = rowsWithIds(body, "classCoaches");
  const enrollmentRows = rowsWithIds(body, "classEnrollments");
  const sessionRows = rowsWithIds(body, "trainingSessions");
  const sessionCoachRows = rowsWithIds(body, "sessionCoaches");
  const attendanceRows = rowsWithIds(body, "attendanceRecords");
  const invoiceRows = rowsWithIds(body, "tuitionInvoices");
  const salaryRows = rowsWithIds(body, "coachSalaries");
  const notificationRows = rowsWithIds(body, "notifications");
  const routeOnlyRows = ["coachCheckIns", "paymentProofs", "receipts", "auditLogs"]
    .flatMap((key) => list(body, key));
  const requestedChanges = userRows.length + profileRows.length + venueRows.length + classRows.length +
    classCoachRows.length + enrollmentRows.length + sessionRows.length + sessionCoachRows.length +
    attendanceRows.length + invoiceRows.length + salaryRows.length + notificationRows.length + routeOnlyRows.length +
    (body.currentProfile !== undefined || body.profile !== undefined ? 1 : 0) +
    (body.activeClub !== undefined || body.club !== undefined ? 1 : 0);
  if (requestedChanges > 100) {
    throw new ApiError(413, "too_many_changes", "Mỗi lần sync tối đa 100 thay đổi; client cần chia batch.");
  }

  const importableUserRows = userRows.filter((row) => row.role === "co_founder"
    || row.role === "manager" || row.role === "coach" || row.role === "trainee");
  const incomingUserIds = new Set(importableUserRows.map((row) => String(row.id)));
  const incomingRoles = new Map(importableUserRows.map((row) => [String(row.id), String(row.role)]));
  const incomingVenueIds = new Set(venueRows.map((row) => String(row.id)));
  const incomingClassIds = new Set(classRows.map((row) => String(row.id)));
  const incomingSessionIds = new Set(sessionRows.map((row) => String(row.id)));
  const incomingEnrollmentById = new Map(enrollmentRows.map((row) => [String(row.id), row]));

  await Promise.all([
    ensureIdsAvailable(env, "users", importableUserRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "venues", venueRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "classes", classRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "class_coaches", classCoachRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "class_enrollments", enrollmentRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "training_sessions", sessionRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "session_coaches", sessionCoachRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "tuition_invoices", invoiceRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "coach_salaries", salaryRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "notifications", notificationRows.map((row) => String(row.id)), tenantId),
    ensureIdsAvailable(env, "attendance_records", attendanceRows.map((row) => String(row.id)), tenantId),
  ]);

  const profileInput = body.currentProfile ?? body.profile;
  if (profileInput !== undefined) {
    if (auth.role === "manager") {
      throw new ApiError(403, "forbidden_profile_edit", "Manager chỉ được thực hiện các nghiệp vụ đã được cấp quyền.");
    }
    if (!profileInput || typeof profileInput !== "object" || Array.isArray(profileInput)) {
      throw new ApiError(400, "validation_error", "profile không hợp lệ.");
    }
    const profile = profileInput as Row;
    const requestedCoachPosition = optionalText(profile.coachPosition, "coachPosition", 80);
    const coachPosition = auth.role === "coach" ? requestedCoachPosition : "";
    if (coachPosition && !isCoachPositionKey(coachPosition)) {
      throw new ApiError(400, "validation_error", "Vị trí Coach không hợp lệ.");
    }
    statements.push(env.DB.prepare(
      `UPDATE profiles SET full_name = ?, phone = ?, email = ?, date_of_birth = ?, height_cm = ?, weight_kg = ?,
       guardian_name = ?, guardian_phone = ?, coach_position = ?, updated_at = ? WHERE user_id = ? AND tenant_id = ?`,
    ).bind(
      requireText(profile.fullName, "fullName", 180),
      optionalText(profile.phone, "phone", 40),
      optionalText(profile.email, "email", 200),
      profile.dateOfBirth ? requireText(profile.dateOfBirth, "dateOfBirth", 10) : null,
      Number(profile.heightCm ?? 0), Number(profile.weightKg ?? 0),
      optionalText(profile.guardianName, "guardianName", 180),
      optionalText(profile.guardianPhone, "guardianPhone", 40), coachPosition, now, auth.id, tenantId,
    ));
  }

  if (isFounderLike(auth.role)) {
    const clubInput = body.activeClub ?? body.club;
    if (clubInput !== undefined) {
      if (!clubInput || typeof clubInput !== "object" || Array.isArray(clubInput)) {
        throw new ApiError(400, "validation_error", "club không hợp lệ.");
      }
      const club = clubInput as Row;
      statements.push(env.DB.prepare(
        `UPDATE clubs SET team_name = ?, phone = ?, email = ?, bank_name = ?, bank_bin = ?,
         bank_account_number = ?, bank_account_name = ?, updated_at = ? WHERE tenant_id = ?`,
      ).bind(
        requireText(club.teamName, "teamName", 180), optionalText(club.phone, "phone", 40),
        optionalText(club.email, "email", 200), optionalText(club.bankName, "bankName", 180),
        optionalText(club.bankBin, "bankBin", 20), optionalText(club.bankAccountNumber, "bankAccountNumber", 80),
        optionalText(club.bankAccountName, "bankAccountName", 180), now, tenantId,
      ));
    }

    for (const item of userRows) {
      const role = requireText(item.role, "user.role", 20);
      // The current Founder and any Admin are server-authoritative and never
      // imported from an offline snapshot.
      if (role === "founder" || role === "admin") continue;
      if (role !== "co_founder" && role !== "manager" && role !== "coach" && role !== "trainee") {
        throw new ApiError(400, "validation_error", "Snapshot chỉ nhập Co-Founder, Manager, Coach hoặc Trainee.");
      }
      const id = String(item.id);
      const username = requireText(item.username, "user.username", 80);
      const normalized = normalizeUsername(username);
      const collision = await env.DB.prepare(
        "SELECT id FROM users WHERE username_normalized=? AND id<>? LIMIT 1",
      ).bind(normalized, id).first<{ id: string }>();
      if (collision) throw new ApiError(409, "username_exists", "Username đã được sử dụng.");
      const existing = await env.DB.prepare("SELECT * FROM users WHERE id=? LIMIT 1").bind(id).first<UserRow>();
      if (existing) {
        if (existing.tenant_id !== tenantId || existing.role !== role) {
          throw new ApiError(403, "tenant_boundary", "Không được đổi tenant hoặc role của thành viên.");
        }
        statements.push(env.DB.prepare(
          `UPDATE users SET username=?, username_normalized=?, email=?, email_normalized=?, is_active=?,
           is_tuition_supported=?, updated_at=? WHERE id=? AND tenant_id=? AND role=?`,
        ).bind(username, normalized, optionalText(item.email, "user.email", 200),
          normalizeEmail(optionalText(item.email, "user.email", 200)), item.isActive === false ? 0 : 1,
          role === "trainee" && item.isTuitionSupported === true ? 1 : 0, now, id, tenantId, role));
      } else {
        const bootstrap = await hashPassword("12345678");
        const email = optionalText(item.email, "user.email", 200);
        statements.push(env.DB.prepare(
          `INSERT INTO users (id, tenant_id, username, username_normalized, email, email_normalized,
           password_hash, password_salt, password_iterations, role, is_active, is_tuition_supported,
           must_change_password, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)`,
        ).bind(id, tenantId, username, normalized, email, normalizeEmail(email), bootstrap.hash, bootstrap.salt,
          bootstrap.iterations, role, item.isActive === false ? 0 : 1,
          role === "trainee" && item.isTuitionSupported === true ? 1 : 0,
          typeof item.createdAt === "string" ? item.createdAt : now, now));
      }
    }

    for (const item of profileRows) {
      const userId = requireText(item.userId, "profile.userId", 64);
      if (userId === auth.id && profileInput !== undefined) continue;
      await assertTenantOrIncoming(env, "users", userId, tenantId, incomingUserIds);
      const profileRole = incomingRoles.get(userId)
        ?? (await env.DB.prepare("SELECT role FROM users WHERE id=? AND tenant_id=? LIMIT 1")
          .bind(userId, tenantId).first<{ role: string }>())?.role
        ?? "";
      const requestedCoachPosition = optionalText(item.coachPosition, "profile.coachPosition", 80);
      const coachPosition = profileRole === "coach" ? requestedCoachPosition : "";
      if (coachPosition && !isCoachPositionKey(coachPosition)) {
        throw new ApiError(400, "validation_error", "Vị trí Coach không hợp lệ.");
      }
      statements.push(env.DB.prepare(
        `INSERT INTO profiles (user_id, tenant_id, full_name, photo_object_key, phone, email, date_of_birth,
         height_cm, weight_kg, guardian_name, guardian_phone, coach_position, updated_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(user_id) DO UPDATE SET full_name=excluded.full_name,
         photo_object_key=excluded.photo_object_key, phone=excluded.phone, email=excluded.email,
         date_of_birth=excluded.date_of_birth, height_cm=excluded.height_cm, weight_kg=excluded.weight_kg,
         guardian_name=excluded.guardian_name, guardian_phone=excluded.guardian_phone,
         coach_position=excluded.coach_position,
         updated_at=excluded.updated_at WHERE profiles.tenant_id=excluded.tenant_id`,
      ).bind(userId, tenantId, requireText(item.fullName, "profile.fullName", 180),
        optionalText(item.photoObjectKey, "profile.photoObjectKey", 500), optionalText(item.phone, "profile.phone", 40),
        optionalText(item.email, "profile.email", 200),
        item.dateOfBirth ? requireText(item.dateOfBirth, "profile.dateOfBirth", 10) : null,
        Number(item.heightCm ?? 0), Number(item.weightKg ?? 0),
        optionalText(item.guardianName, "profile.guardianName", 180),
        optionalText(item.guardianPhone, "profile.guardianPhone", 40), coachPosition, now));
    }

    for (const venue of venueRows) {
      const id = idOf(venue);
      statements.push(env.DB.prepare(
        `INSERT INTO venues (id, tenant_id, name, address, notes, is_active, created_at, updated_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(id) DO UPDATE SET name=excluded.name, address=excluded.address, notes=excluded.notes,
          is_active=excluded.is_active, updated_at=excluded.updated_at WHERE venues.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, requireText(venue.name, "venue.name", 180), optionalText(venue.address, "venue.address", 500),
        optionalText(venue.notes, "venue.notes", 1000), venue.isActive === false ? 0 : 1, now, now));
    }

    for (const item of classRows) {
      const id = idOf(item);
      const venueId = item.venueId ? requireText(item.venueId, "class.venueId", 64) : null;
      if (venueId) await assertTenantOrIncoming(env, "venues", venueId, tenantId, incomingVenueIds);
      statements.push(env.DB.prepare(
        `INSERT INTO classes (id, tenant_id, venue_id, name, schedule_days, start_date, start_time_minutes, end_time_minutes,
          tuition_session_count, default_cycle_fee_vnd, evaluation_request_open, is_active, created_at, updated_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(id) DO UPDATE SET venue_id=excluded.venue_id, name=excluded.name, schedule_days=excluded.schedule_days,
          start_date=excluded.start_date,
          start_time_minutes=excluded.start_time_minutes, end_time_minutes=excluded.end_time_minutes,
          tuition_session_count=excluded.tuition_session_count, default_cycle_fee_vnd=excluded.default_cycle_fee_vnd,
          evaluation_request_open=excluded.evaluation_request_open,
          is_active=excluded.is_active, updated_at=excluded.updated_at WHERE classes.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, venueId, requireText(item.name, "class.name", 180), optionalText(item.scheduleDays, "scheduleDays", 50),
        item.startDate === undefined
          ? now.slice(0, 10)
          : requireDateKey(item.startDate, "class.startDate"),
        requireInteger(item.startTimeMinutes, "startTimeMinutes", 0, 1439),
        requireInteger(item.endTimeMinutes, "endTimeMinutes", 1, 1440),
        requireInteger(item.tuitionSessionCount, "tuitionSessionCount", 1, 100),
        requireInteger(item.defaultCycleFeeVnd, "defaultCycleFeeVnd", 0, 2_000_000_000),
        item.evaluationRequestOpen === true ? 1 : 0,
        item.isActive === false ? 0 : 1, now, now));
    }

    for (const item of classCoachRows) {
      const id = idOf(item);
      const classId = requireText(item.classId, "classCoach.classId", 64);
      const coachUserId = requireText(item.coachUserId, "classCoach.coachUserId", 64);
      await assertTenantOrIncoming(env, "classes", classId, tenantId, incomingClassIds);
      await assertMemberRole(env, coachUserId, tenantId, "coach", incomingRoles);
      statements.push(env.DB.prepare(
        `INSERT INTO class_coaches (id, tenant_id, class_id, coach_user_id, salary_per_session_vnd, is_active, assigned_at)
         VALUES (?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(class_id, coach_user_id) DO UPDATE SET salary_per_session_vnd=excluded.salary_per_session_vnd,
         is_active=excluded.is_active WHERE class_coaches.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, classId, coachUserId,
        requireInteger(item.salaryPerSessionVnd ?? 0, "salaryPerSessionVnd", 0, 2_000_000_000),
        item.isActive === false ? 0 : 1, typeof item.assignedAt === "string" ? item.assignedAt : now));
    }

    for (const item of enrollmentRows) {
      const id = idOf(item);
      const classId = requireText(item.classId, "enrollment.classId", 64);
      const traineeUserId = requireText(item.traineeUserId, "enrollment.traineeUserId", 64);
      await assertTenantOrIncoming(env, "classes", classId, tenantId, incomingClassIds);
      await assertMemberRole(env, traineeUserId, tenantId, "trainee", incomingRoles);
      if (item.isTrial === true) {
        const supported = await env.DB.prepare(
          "SELECT is_tuition_supported FROM users WHERE id=? AND tenant_id=? LIMIT 1",
        ).bind(traineeUserId, tenantId).first<{ is_tuition_supported: number }>();
        if (supported?.is_tuition_supported === 1) {
          throw new ApiError(400, "trial_not_allowed", "Cáº§u thá»§ Ä‘Æ°á»£c há»— trá»£ khÃ´ng thá»ƒ Ä‘Äƒng kÃ½ há»c thá»­.");
        }
      }
      statements.push(env.DB.prepare(
        `INSERT INTO class_enrollments
         (id, tenant_id, class_id, trainee_user_id, cycle_fee_vnd, is_trial, trial_session_count, is_active, enrolled_at)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(class_id, trainee_user_id) DO UPDATE SET cycle_fee_vnd=excluded.cycle_fee_vnd,
         is_trial=excluded.is_trial, trial_session_count=excluded.trial_session_count,
         is_active=excluded.is_active WHERE class_enrollments.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, classId, traineeUserId,
        requireInteger(item.cycleFeeVnd ?? 0, "cycleFeeVnd", 0, 2_000_000_000),
        item.isTrial === true ? 1 : 0,
        item.isTrial === true
          ? requireInteger(item.trialSessionCount ?? 1, "trialSessionCount", 1, 5)
          : 0,
        item.isActive === false ? 0 : 1, typeof item.enrolledAt === "string" ? item.enrolledAt : now));
    }

    for (const item of sessionRows) {
      const id = idOf(item);
      const classId = requireText(item.classId, "session.classId", 64);
      await assertTenantOrIncoming(env, "classes", classId, tenantId, incomingClassIds);
      const status = requireText(item.status ?? "draft", "session.status", 20);
      if (!["draft", "submitted", "locked"].includes(status)) {
        throw new ApiError(400, "validation_error", "Trạng thái buổi học không hợp lệ.");
      }
      const submittedBy = item.submittedByUserId
        ? requireText(item.submittedByUserId, "submittedByUserId", 64)
        : null;
      if (submittedBy) await assertTenantOrIncoming(env, "users", submittedBy, tenantId, incomingUserIds);
      statements.push(env.DB.prepare(
        `INSERT INTO training_sessions (id, tenant_id, class_id, session_date, status, submitted_by_user_id,
         submitted_at, override_reason, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(class_id, session_date) DO UPDATE SET status=excluded.status,
         submitted_by_user_id=excluded.submitted_by_user_id, submitted_at=excluded.submitted_at,
         override_reason=excluded.override_reason, updated_at=excluded.updated_at
         WHERE training_sessions.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, classId, requireText(item.sessionDate, "sessionDate", 10), status,
        submittedBy,
        typeof item.submittedAt === "string" ? item.submittedAt : null,
        optionalText(item.overrideReason, "overrideReason", 500), now, now));
    }

    for (const item of sessionCoachRows) {
      const id = idOf(item);
      const sessionId = requireText(item.sessionId, "sessionCoach.sessionId", 64);
      const coachUserId = requireText(item.coachUserId, "sessionCoach.coachUserId", 64);
      await assertTenantOrIncoming(env, "training_sessions", sessionId, tenantId, incomingSessionIds);
      await assertMemberRole(env, coachUserId, tenantId, "coach", incomingRoles);
      statements.push(env.DB.prepare(
        `INSERT INTO session_coaches (id, tenant_id, session_id, coach_user_id, snapshotted_at)
         VALUES (?, ?, ?, ?, ?) ON CONFLICT(session_id, coach_user_id) DO UPDATE SET
         snapshotted_at=excluded.snapshotted_at WHERE session_coaches.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, sessionId, coachUserId, typeof item.snapshottedAt === "string" ? item.snapshottedAt : now));
    }

    for (const item of invoiceRows) {
      const id = idOf(item);
      const enrollmentId = requireText(item.enrollmentId, "invoice.enrollmentId", 64);
      const incomingEnrollment = incomingEnrollmentById.get(enrollmentId);
      const enrollment = incomingEnrollment
        ? {
            class_id: requireText(incomingEnrollment.classId, "invoice.classId", 64),
            trainee_user_id: requireText(incomingEnrollment.traineeUserId, "invoice.traineeUserId", 64),
          }
        : await env.DB.prepare(
            "SELECT class_id, trainee_user_id FROM class_enrollments WHERE id=? AND tenant_id=?",
          ).bind(enrollmentId, tenantId).first<{ class_id: string; trainee_user_id: string }>();
      if (!enrollment) {
        throw new ApiError(400, "invalid_enrollment", "Ghi danh không thuộc đội.");
      }
      const status = requireText(item.status ?? "pending", "invoice.status", 30);
      if (!["pending", "proof_submitted", "paid", "rejected", "overdue", "waived"].includes(status)) {
        throw new ApiError(400, "validation_error", "Trạng thái học phí không hợp lệ.");
      }
      const cycleCount = requireInteger(item.cycleCount ?? 1, "cycleCount", 1, 24);
      const cycleFee = requireInteger(item.cycleFeeVnd ?? 0, "cycleFeeVnd", 0, 2_000_000_000);
      statements.push(env.DB.prepare(
        `INSERT INTO tuition_invoices (id, tenant_id, enrollment_id, trainee_user_id, class_id, cycle_number,
         cycle_count, cycle_fee_vnd, amount_vnd, attended_session_count, planned_session_count, due_date,
         status, payment_content, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(enrollment_id, cycle_number) DO UPDATE SET cycle_count=excluded.cycle_count,
         cycle_fee_vnd=excluded.cycle_fee_vnd, amount_vnd=excluded.amount_vnd,
         attended_session_count=excluded.attended_session_count, planned_session_count=excluded.planned_session_count,
         due_date=excluded.due_date, status=excluded.status, payment_content=excluded.payment_content,
         updated_at=excluded.updated_at WHERE tuition_invoices.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, enrollmentId, enrollment.trainee_user_id, enrollment.class_id,
        requireInteger(item.cycleNumber, "cycleNumber", 1, 10000), cycleCount, cycleFee,
        requireInteger(item.amountVnd ?? cycleFee * cycleCount, "amountVnd", 0, 2_000_000_000),
        requireInteger(item.attendedSessionCount ?? 0, "attendedSessionCount", 0, 10000),
        requireInteger(item.plannedSessionCount ?? 0, "plannedSessionCount", 0, 10000),
        requireText(item.dueDate, "dueDate", 10), status,
        requireText(item.paymentContent ?? "Hoc phi", "paymentContent", 300), now, now));
    }

    for (const item of salaryRows) {
      const id = idOf(item);
      const coachUserId = requireText(item.coachUserId, "salary.coachUserId", 64);
      await assertMemberRole(env, coachUserId, tenantId, "coach", incomingRoles);
      const status = item.status === "paid" ? "paid" : "pending";
      statements.push(env.DB.prepare(
        `INSERT INTO coach_salaries (id, tenant_id, coach_user_id, period, amount_vnd, due_date, status,
         paid_at, paid_by_user_id, notes, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(coach_user_id, period) DO UPDATE SET amount_vnd=excluded.amount_vnd,
         due_date=excluded.due_date, status=excluded.status, paid_at=excluded.paid_at,
         paid_by_user_id=excluded.paid_by_user_id, notes=excluded.notes, updated_at=excluded.updated_at
         WHERE coach_salaries.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, coachUserId, requireText(item.period, "period", 20),
        requireInteger(item.amountVnd ?? 0, "amountVnd", 0, 2_000_000_000), requireText(item.dueDate, "dueDate", 10),
        status, typeof item.paidAt === "string" ? item.paidAt : null,
        status === "paid" ? auth.id : null, optionalText(item.notes, "notes", 500), now));
    }

    for (const item of notificationRows) {
      const recipientUserId = requireText(item.recipientUserId, "notification.recipientUserId", 64);
      await assertTenantOrIncoming(env, "users", recipientUserId, tenantId, incomingUserIds);
      const id = idOf(item);
      statements.push(env.DB.prepare(
        `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id,
         is_read, created_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
         ON CONFLICT(id) DO UPDATE SET title=excluded.title, message=excluded.message,
         is_read=excluded.is_read WHERE notifications.tenant_id=excluded.tenant_id`,
      ).bind(id, tenantId, recipientUserId, requireText(item.kind ?? "announcement", "kind", 50),
        requireText(item.title, "title", 180), requireText(item.message, "message", 2000),
        optionalText(item.relatedEntityId, "relatedEntityId", 64), item.isRead === true ? 1 : 0,
        typeof item.createdAt === "string" ? item.createdAt : now));
    }
  }

  if (!isFounderLike(auth.role)) {
    const privilegedCollections = [
      "users", "profiles", "venues", "classes", "classCoaches", "classEnrollments", "trainingSessions", "sessionCoaches",
      "tuitionInvoices", "coachSalaries", "notifications",
    ].filter((key) => list(body, key).length > 0);
    if (privilegedCollections.length > 0) {
      throw new ApiError(403, "forbidden_sync_collections",
        "Role hiện tại không được đồng bộ các collection này.", { collections: privilegedCollections });
    }
  }

  const routeOnlyCollections = ["coachCheckIns", "paymentProofs", "receipts", "auditLogs"]
    .filter((key) => list(body, key).length > 0);
  if (routeOnlyCollections.length > 0) {
    throw new ApiError(422, "route_required",
      "Một số dữ liệu nhạy cảm phải dùng endpoint nghiệp vụ riêng để kiểm tra quyền và file R2.",
      { collections: routeOnlyCollections });
  }

  for (const record of attendanceRows) {
    const sessionId = requireText(record.sessionId, "attendance.sessionId", 64);
    const traineeId = requireText(record.traineeUserId, "attendance.traineeUserId", 64);
    await assertTenantOrIncoming(env, "training_sessions", sessionId, tenantId, incomingSessionIds);
    await assertTenantOrIncoming(env, "users", traineeId, tenantId, incomingUserIds);
    if (auth.role === "coach") {
      const open = await env.DB.prepare(
        `SELECT ci.id FROM coach_checkins ci JOIN training_sessions ts ON ts.id = ci.session_id
         WHERE ci.tenant_id = ? AND ci.session_id = ? AND ci.coach_user_id = ? AND ci.checked_out_at IS NULL LIMIT 1`,
      ).bind(tenantId, sessionId, auth.id).first();
      if (!open) throw new ApiError(403, "checkin_required", "Coach chỉ điểm danh sau check-in và trước check-out.");
    } else if (!isFounderLike(auth.role)) {
      throw new ApiError(403, "forbidden", "Học viên không được sửa điểm danh.");
    }
    const status = requireText(record.status, "attendance.status", 20);
    if (!["unmarked", "present", "late", "absent", "excused"].includes(status)) {
      throw new ApiError(400, "validation_error", "Trạng thái điểm danh không hợp lệ.");
    }
    const id = idOf(record);
    statements.push(env.DB.prepare(
      `INSERT INTO attendance_records (id, tenant_id, session_id, trainee_user_id, status, recorded_by_user_id,
        recorded_at, notes, revision) VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1)
       ON CONFLICT(session_id, trainee_user_id) DO UPDATE SET status=excluded.status,
        recorded_by_user_id=excluded.recorded_by_user_id, recorded_at=excluded.recorded_at,
        notes=excluded.notes, revision=attendance_records.revision+1 WHERE attendance_records.tenant_id=excluded.tenant_id`,
    ).bind(id, tenantId, sessionId, traineeId, status, auth.id, now,
      optionalText(record.notes, "attendance.notes", 500)));
  }

  const totalItems = statements.length;
  if (totalItems > 100) throw new ApiError(413, "too_many_statements", "Batch tạo quá 100 câu lệnh; client cần chia nhỏ hơn.");
  if (statements.length > 0) await env.DB.batch(statements);
  await ensureInitialTuitionInvoices(env, tenantId);
  return { applied: totalItems > 0, appliedCount: totalItems, serverTime: now, syncVersion: 1 };
}
