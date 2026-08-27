export type UserRole = "admin" | "founder" | "co_founder" | "manager" | "coach" | "trainee";

export type AchievementCategory = "match_ranking" | "weekly_class_ranking";
export type AchievementStatus = "pending" | "approved" | "rejected" | "removed" | "expired";

export const ACHIEVEMENT_CATEGORIES: readonly AchievementCategory[] = [
  "match_ranking",
  "weekly_class_ranking",
];

export const ACHIEVEMENT_STATUSES: readonly AchievementStatus[] = [
  "pending",
  "approved",
  "rejected",
  "removed",
  "expired",
];

/** Points are intentionally finite and mirror the approved achievement board. */
export const ACHIEVEMENT_POINTS: readonly number[] = [500, 150, 100, 60, 30, 20, 15, 10, -10, -30];

export interface AchievementBadgeRow {
  id: string;
  key: string;
  name: string;
  category: AchievementCategory;
  asset_key: string;
  display_size: "hero" | "medium" | "compact";
  points: number;
  sort_order: number;
  is_active: number;
  created_at: string;
  updated_at: string;
}

export interface TraineeAchievementRow {
  id: string;
  tenant_id: string;
  trainee_user_id: string;
  badge_id: string;
  class_id: string | null;
  category: AchievementCategory;
  title: string;
  event_name: string;
  reason: string;
  awarded_for_date: string;
  points_snapshot: number;
  status: AchievementStatus;
  created_by_user_id: string | null;
  reviewed_by_user_id: string | null;
  reviewed_at: string | null;
  review_note: string;
  visible_until: string;
  removed_at: string | null;
  created_at: string;
  updated_at: string;
}

/**
 * The public achievement feed is visible for exactly 30 days.  Keep the
 * comparison inclusive at the boundary so a record does not disappear a few
 * milliseconds early when a client and Worker evaluate the same timestamp.
 */
export function achievementVisibilityStatus(
  visibleUntil: string,
  now: string = nowIso(),
): "visible" | "expired" {
  const visibleAt = Date.parse(visibleUntil);
  const nowAt = Date.parse(now);
  if (!Number.isFinite(visibleAt) || !Number.isFinite(nowAt)) return "expired";
  return visibleAt >= nowAt ? "visible" : "expired";
}

/** Approved, expired and removed records retain their points forever. */
export function achievementPointsStatus(status: string): boolean {
  const normalized = status.trim().toLowerCase();
  return normalized === "approved" || normalized === "removed" || normalized === "expired";
}

export function isAchievementCategory(value: unknown): value is AchievementCategory {
  return typeof value === "string"
    && (ACHIEVEMENT_CATEGORIES as readonly string[]).includes(value);
}

export function isAchievementStatus(value: unknown): value is AchievementStatus {
  return typeof value === "string"
    && (ACHIEVEMENT_STATUSES as readonly string[]).includes(value);
}

export const COACH_POSITION_KEYS = [
  "head_coach_manager",
  "goalkeeping_coach",
  "fitness_coach",
  "technical_coach",
  "tactical_coach",
  "rehabilitation_conditioning_coach",
  "performance_coach",
] as const;

export function isCoachPositionKey(value: unknown): value is typeof COACH_POSITION_KEYS[number] {
  return typeof value === "string"
    && (COACH_POSITION_KEYS as readonly string[]).includes(value);
}

export interface AuthUser {
  id: string;
  tenantId: string | null;
  username: string;
  role: UserRole;
  sessionId: string;
  mustChangePassword: boolean;
}

export interface UserRow {
  id: string;
  tenant_id: string | null;
  username: string;
  username_normalized: string;
  email: string;
  email_normalized: string;
  password_hash: string;
  password_salt: string;
  password_iterations: number;
  role: UserRole;
  is_active: number;
  is_tuition_supported: number;
  must_change_password: number;
  failed_login_count: number;
  lockout_until: string | null;
  created_at: string;
  updated_at: string;
}

export interface ProfileRow {
  user_id: string;
  tenant_id: string | null;
  full_name: string;
  photo_object_key: string;
  phone: string;
  email: string;
  date_of_birth: string | null;
  height_cm: number;
  weight_kg: number;
  guardian_name: string;
  guardian_phone: string;
  coach_position: string;
  updated_at: string;
}

export interface ClubRow {
  tenant_id: string;
  team_name: string;
  logo_object_key: string;
  phone: string;
  email: string;
  bank_name: string;
  bank_bin: string;
  bank_account_number: string;
  bank_account_name: string;
  updated_at: string;
}

export const nowIso = (): string => new Date().toISOString();
export const newId = (): string => crypto.randomUUID().replaceAll("-", "");
export const normalizeUsername = (value: string): string => value.trim().toLowerCase();
export const normalizeEmail = (value: string): string => value.trim().toLowerCase();

export function publicUser(row: UserRow): Record<string, unknown> {
  return {
    id: row.id,
    tenantId: row.tenant_id,
    username: row.username,
    email: row.email,
    role: row.role,
    isActive: row.is_active === 1,
    isTuitionSupported: row.is_tuition_supported === 1,
    mustChangePassword: row.must_change_password === 1,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

export function publicProfile(row: ProfileRow | null): Record<string, unknown> | null {
  if (!row) return null;
  return {
    userId: row.user_id,
    fullName: row.full_name,
    photoObjectKey: row.photo_object_key,
    phone: row.phone,
    email: row.email,
    dateOfBirth: row.date_of_birth,
    heightCm: row.height_cm,
    weightKg: row.weight_kg,
    guardianName: row.guardian_name,
    guardianPhone: row.guardian_phone,
    coachPosition: row.coach_position ?? "",
    updatedAt: row.updated_at,
  };
}

export function publicClub(row: ClubRow | null): Record<string, unknown> | null {
  if (!row) return null;
  return {
    tenantId: row.tenant_id,
    teamName: row.team_name,
    logoObjectKey: row.logo_object_key,
    phone: row.phone,
    email: row.email,
    bankName: row.bank_name,
    bankBin: row.bank_bin,
    bankAccountNumber: row.bank_account_number,
    bankAccountName: row.bank_account_name,
    updatedAt: row.updated_at,
  };
}
