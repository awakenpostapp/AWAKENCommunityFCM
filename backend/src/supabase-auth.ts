import { ApiError, json, optionalText, readJson, requireText } from "./http";
import { UserRow, normalizeEmail, nowIso } from "./domain";
import { audit } from "./repository";
import { authBundle } from "./routes-auth-bridge";

interface SupabaseAuthUser {
  id: string;
  email?: string | null;
  user_metadata?: Record<string, unknown> | null;
}

function supabaseUrl(env: Env): string {
  const value = env.SUPABASE_URL?.trim().replace(/\/+$/u, "");
  if (!value || !env.SUPABASE_SECRET_KEY) throw new ApiError(503, "supabase_not_configured", "Supabase chưa được cấu hình.");
  return value;
}

function supabaseHeaders(env: Env, authorization?: string): Headers {
  const headers = new Headers({
    apikey: env.SUPABASE_SECRET_KEY!,
    Accept: "application/json",
  });
  if (authorization) headers.set("Authorization", authorization);
  else headers.set("Authorization", `Bearer ${env.SUPABASE_SECRET_KEY}`);
  return headers;
}

export async function getSupabaseAuthUser(env: Env, accessToken: string): Promise<SupabaseAuthUser> {
  const response = await fetch(`${supabaseUrl(env)}/auth/v1/user`, {
    headers: supabaseHeaders(env, `Bearer ${accessToken}`),
  });
  if (!response.ok) throw new ApiError(401, "invalid_supabase_token", "Phiên Supabase không hợp lệ.");
  const user = await response.json() as Partial<SupabaseAuthUser>;
  if (typeof user.id !== "string" || !user.id) throw new ApiError(401, "invalid_supabase_token", "Phiên Supabase không hợp lệ.");
  return {
    id: user.id,
    email: typeof user.email === "string" ? user.email : null,
    user_metadata: user.user_metadata && typeof user.user_metadata === "object" ? user.user_metadata : null,
  };
}

async function syncSupabaseAuthLink(env: Env, appUserId: string, authUserId: string): Promise<void> {
  const now = nowIso();
  const headers = supabaseHeaders(env);
  headers.set("content-type", "application/json");
  headers.set("Prefer", "resolution=merge-duplicates,return=minimal");
  const response = await fetch(`${supabaseUrl(env)}/rest/v1/auth_user_links?on_conflict=app_user_id`, {
    method: "POST",
    headers,
    body: JSON.stringify([{
      app_user_id: appUserId,
      auth_user_id: authUserId,
      provider: "supabase_auth",
      is_active: true,
      created_at: now,
      updated_at: now,
    }]),
  });
  if (!response.ok) throw new ApiError(502, "supabase_auth_link_failed", "Không thể lưu liên kết Supabase Auth.");
}

export async function supabaseAuthExchange(request: Request, env: Env): Promise<Response> {
  const body = await readJson<Record<string, unknown>>(request);
  const accessToken = requireText(body.accessToken, "accessToken", 4_096);
  const deviceName = optionalText(body.deviceName, "deviceName", 120);
  const identity = await getSupabaseAuthUser(env, accessToken);
  const metadataUserId = typeof identity.user_metadata?.app_user_id === "string"
    ? identity.user_metadata.app_user_id
    : "";
  const email = normalizeEmail(identity.email ?? "");

  let user = metadataUserId
    ? await env.DB.prepare("SELECT * FROM users WHERE id = ? AND is_active = 1 LIMIT 1")
      .bind(metadataUserId).first<UserRow>()
    : null;
  if (!user) {
    user = await env.DB.prepare(
      `SELECT u.* FROM users u
        JOIN auth_user_links l ON l.app_user_id = u.id
       WHERE l.auth_user_id = ? AND l.is_active = 1 AND u.is_active = 1 LIMIT 1`,
    ).bind(identity.id).first<UserRow>();
  }
  if (!user && email) {
    user = await env.DB.prepare(
      "SELECT * FROM users WHERE email_normalized = ? AND is_active = 1 ORDER BY created_at LIMIT 1",
    ).bind(email).first<UserRow>();
  }
  if (!user) {
    throw new ApiError(403, "oauth_not_linked", "Tài khoản Google của bạn chưa liên kết với tài khoản");
  }
  if (user.role !== "admin") {
    const tenant = await env.DB.prepare("SELECT status FROM tenants WHERE id = ? LIMIT 1")
      .bind(user.tenant_id).first<{ status: string }>();
    if (!tenant || tenant.status !== "active") throw new ApiError(403, "account_inactive", "Tài khoản chưa được kích hoạt hoặc đang bị khóa.");
  }

  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      `INSERT OR REPLACE INTO auth_user_links
       (app_user_id, auth_user_id, provider, is_active, created_at, updated_at)
       VALUES (?, ?, 'supabase_auth', 1, ?, ?)`,
    ).bind(user.id, identity.id, now, now),
  ]);
  // Keep the mapping in Supabase even while D1 remains authoritative. This
  // makes the first post-cutover exchange idempotent.
  await syncSupabaseAuthLink(env, user.id, identity.id);
  await audit(env, user.tenant_id, user.id, "auth.supabase_linked", "auth_user_link", identity.id, {
    email: email || undefined,
  });
  const { createSession } = await import("./auth");
  const tokens = await createSession(env, request, user, deviceName || "supabase_auth");
  return json(await authBundle(env, user, tokens));
}

// Kept as a narrow export for future admin diagnostics without exposing the
// Supabase secret or the raw Auth response.
export function authBridgeUserSummary(user: SupabaseAuthUser): { id: string; email: string } {
  return { id: user.id, email: user.email ?? "" };
}
