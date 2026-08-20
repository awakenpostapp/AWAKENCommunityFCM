import { hashPassword, validatePassword } from "./auth";
import {
  ClubRow,
  ProfileRow,
  UserRole,
  UserRow,
  newId,
  normalizeEmail,
  normalizeUsername,
  nowIso,
  isCoachPositionKey,
} from "./domain";
import { validateTenantUserRole } from "./authorization";
import { ApiError, optionalText, requireText } from "./http";

export interface FounderInput {
  username?: unknown;
  fullName?: unknown;
  email?: unknown;
  password?: unknown;
  teamName?: unknown;
}

function slugify(value: string): string {
  const base = value.normalize("NFD").replace(/[\u0300-\u036f]/gu, "").toLowerCase()
    .replace(/[^a-z0-9]+/gu, "-").replace(/^-|-$/gu, "").slice(0, 48) || "club";
  return `${base}-${newId().slice(0, 8)}`;
}

export async function createFounder(
  env: Env,
  input: FounderInput,
  mustChangePassword: boolean,
  pendingApproval = false,
): Promise<{ user: UserRow; profile: ProfileRow; club: ClubRow }> {
  const username = requireText(input.username, "Username", 80);
  if (username.length < 3) throw new ApiError(400, "validation_error", "Username phải có ít nhất 3 ký tự.");
  const fullName = requireText(input.fullName, "Tên Sáng lập & Điều hành", 180);
  const email = optionalText(input.email, "Email", 200);
  // Admin-created Founder accounts use the same bootstrap password as other
  // system-created accounts. Public self-registration must still provide a
  // strong password because it is not authenticated by Admin yet.
  // Admin-created Founders use the fixed bootstrap password.  The mobile
  // client sends that value explicitly, so treat both an omitted value and
  // the fixed value as the Admin bootstrap path. Public self-registration
  // remains subject to the strong password policy while it is pending.
  const useAdminBootstrapPassword = !pendingApproval
    && (input.password === undefined || input.password === "12345678");
  const password = useAdminBootstrapPassword
    ? "12345678"
    : validatePassword(input.password);
  const teamName = optionalText(input.teamName, "Tên đội", 180) || "Community Football Club";
  const usernameNormalized = normalizeUsername(username);

  const duplicate = await env.DB.prepare("SELECT id FROM users WHERE username_normalized = ? LIMIT 1")
    .bind(usernameNormalized).first<{ id: string }>();
  if (duplicate) throw new ApiError(409, "username_exists", "Username đã được sử dụng.");

  const passwordData = await hashPassword(password);
  const tenantId = newId();
  const userId = newId();
  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      "INSERT INTO tenants (id, slug, display_name, owner_user_id, status, founder_status, created_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
    ).bind(
      tenantId,
      slugify(teamName),
      teamName,
      userId,
      pendingApproval ? "suspended" : "active",
      pendingApproval ? "pending" : "approved",
      now,
      now,
    ),
    env.DB.prepare(
      `INSERT INTO users
       (id, tenant_id, username, username_normalized, email, email_normalized, password_hash, password_salt,
        password_iterations, role, is_active, must_change_password, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, 'founder', ?, ?, ?, ?)`,
    ).bind(userId, tenantId, username, usernameNormalized, email, normalizeEmail(email), passwordData.hash,
      passwordData.salt, passwordData.iterations, pendingApproval ? 0 : 1,
      mustChangePassword ? 1 : 0, now, now),
    env.DB.prepare(
      `INSERT INTO profiles (user_id, tenant_id, full_name, email, updated_at) VALUES (?, ?, ?, ?, ?)`,
    ).bind(userId, tenantId, fullName, email, now),
    env.DB.prepare(
      `INSERT INTO clubs (tenant_id, team_name, email, updated_at) VALUES (?, ?, ?, ?)`,
    ).bind(tenantId, teamName, email, now),
  ]);

  const bundle = await getUserBundle(env, userId);
  if (!bundle.user || !bundle.profile || !bundle.club) throw new Error("Founder bundle was not created");
  return { user: bundle.user, profile: bundle.profile, club: bundle.club };
}

export async function createTenantUser(
  env: Env,
  tenantId: string,
  input: { username?: unknown; fullName?: unknown; email?: unknown; password?: unknown; role?: unknown;
    isTuitionSupported?: unknown; phone?: unknown; guardianName?: unknown; guardianPhone?: unknown;
    coachPosition?: unknown },
): Promise<UserRow> {
  const username = requireText(input.username, "Username", 80);
  const fullName = requireText(input.fullName, "Họ tên", 180);
  const email = optionalText(input.email, "Email", 200);
  const role = validateTenantUserRole(input.role);
  const coachPosition = role === "coach"
    ? optionalText(input.coachPosition, "Vị trí Coach", 80)
    : "";
  if (coachPosition && !isCoachPositionKey(coachPosition)) {
    throw new ApiError(400, "validation_error", "Vị trí Coach không hợp lệ.");
  }
  // Preserve the mobile app's first-login contract. A caller-supplied password
  // must satisfy the strong policy; the fixed bootstrap password is immediately
  // gated by must_change_password=1.
  const password = input.password === undefined ? "12345678" : validatePassword(input.password);
  const normalized = normalizeUsername(username);
  if (await env.DB.prepare("SELECT id FROM users WHERE username_normalized = ? LIMIT 1").bind(normalized).first()) {
    throw new ApiError(409, "username_exists", "Username đã được sử dụng.");
  }

  const passwordData = await hashPassword(password);
  const id = newId();
  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO users
       (id, tenant_id, username, username_normalized, email, email_normalized, password_hash, password_salt,
        password_iterations, role, is_active, is_tuition_supported, must_change_password, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, 1, ?, ?)`,
    ).bind(id, tenantId, username, normalized, email, normalizeEmail(email), passwordData.hash, passwordData.salt,
      passwordData.iterations, role, role === "trainee" && input.isTuitionSupported === true ? 1 : 0, now, now),
    env.DB.prepare(
      `INSERT INTO profiles (user_id, tenant_id, full_name, phone, email, guardian_name, guardian_phone, coach_position, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(id, tenantId, fullName, optionalText(input.phone, "Số điện thoại", 40), email,
      optionalText(input.guardianName, "Người giám hộ", 180),
      optionalText(input.guardianPhone, "SĐT người giám hộ", 40), coachPosition, now),
  ]);
  const created = await env.DB.prepare("SELECT * FROM users WHERE id = ?").bind(id).first<UserRow>();
  if (!created) throw new Error("User was not created");
  return created;
}

export async function getUserBundle(env: Env, userId: string): Promise<{
  user: UserRow | null;
  profile: ProfileRow | null;
  club: ClubRow | null;
}> {
  const user = await env.DB.prepare("SELECT * FROM users WHERE id = ? LIMIT 1").bind(userId).first<UserRow>();
  if (!user) return { user: null, profile: null, club: null };
  const [profile, club] = await Promise.all([
    env.DB.prepare("SELECT * FROM profiles WHERE user_id = ? LIMIT 1").bind(userId).first<ProfileRow>(),
    user.tenant_id
      ? env.DB.prepare("SELECT * FROM clubs WHERE tenant_id = ? LIMIT 1").bind(user.tenant_id).first<ClubRow>()
      : Promise.resolve(null),
  ]);
  return { user, profile: profile ?? null, club: club ?? null };
}

export async function audit(
  env: Env,
  tenantId: string | null,
  actorUserId: string | null,
  action: string,
  entityType: string,
  entityId: string,
  details: unknown = {},
): Promise<void> {
  await env.DB.prepare(
    `INSERT INTO audit_logs (id, tenant_id, actor_user_id, action, entity_type, entity_id, details_json, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(newId(), tenantId, actorUserId, action, entityType, entityId, JSON.stringify(details), nowIso()).run();
}

export async function allRows<T>(statement: D1PreparedStatement): Promise<T[]> {
  const result = await statement.all<T>();
  return result.results;
}

export async function assertTenantEntity(
  env: Env,
  table: "users" | "venues" | "classes" | "training_sessions" | "tuition_invoices" | "uploads",
  id: string,
  tenantId: string,
): Promise<void> {
  const row = await env.DB.prepare(`SELECT id FROM ${table} WHERE id = ? AND tenant_id = ? LIMIT 1`)
    .bind(id, tenantId).first<{ id: string }>();
  if (!row) throw new ApiError(404, "not_found", "Không tìm thấy dữ liệu trong đội của bạn.");
}

export function roleCanSeeMember(viewer: UserRole, target: UserRole): boolean {
  if (viewer === "founder") return target !== "admin";
  if (viewer === "coach") return target === "founder" || target === "coach" || target === "trainee";
  return target === "founder" || target === "coach" || target === "trainee";
}
