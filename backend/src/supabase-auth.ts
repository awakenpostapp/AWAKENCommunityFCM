import { ApiError, json, optionalText, readJson, requireText } from "./http";
import { UserRow, normalizeEmail, nowIso } from "./domain";
import { audit } from "./repository";
import { authBundle } from "./routes-auth-bridge";

interface SupabaseAuthUser {
  id: string;
  email?: string | null;
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
  };
}

export async function supabaseAuthExchange(request: Request, env: Env): Promise<Response> {
  const body = await readJson<Record<string, unknown>>(request);
  const accessToken = requireText(body.accessToken, "accessToken", 4_096);
  const deviceName = optionalText(body.deviceName, "deviceName", 120);
  const identity = await getSupabaseAuthUser(env, accessToken);
  const email = normalizeEmail(identity.email ?? "");

  // The mapping is created only by the authenticated server-side bind flow.
  // Never select an application account from user-editable Supabase metadata
  // or from an email match: either value can be shared or changed by a user.
  const user = await env.DB.prepare(
    `SELECT u.* FROM users u
      JOIN auth_user_links l ON l.app_user_id = u.id
     WHERE l.auth_user_id = ? AND l.is_active = 1 AND u.is_active = 1 LIMIT 1`,
  ).bind(identity.id).first<UserRow>();
  if (!user) {
    throw new ApiError(403, "oauth_not_linked", "Tài khoản Google của bạn chưa liên kết với tài khoản");
  }
  if (user.role !== "admin") {
    const tenant = await env.DB.prepare("SELECT status FROM tenants WHERE id = ? LIMIT 1")
      .bind(user.tenant_id).first<{ status: string }>();
    if (!tenant || tenant.status !== "active") throw new ApiError(403, "account_inactive", "Tài khoản chưa được kích hoạt hoặc đang bị khóa.");
  }

  await audit(env, user.tenant_id, user.id, "auth.supabase_exchanged", "auth_user_link", identity.id, {
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
