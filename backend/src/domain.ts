export type UserRole = "admin" | "founder" | "co_founder" | "manager" | "coach" | "trainee";

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
