import { ApiError } from "./http";
import { AuthUser, UserRole, UserRow, normalizeUsername, nowIso, newId } from "./domain";

const encoder = new TextEncoder();
// Cloudflare Workers Web Crypto currently rejects PBKDF2 iteration counts
// above 100,000. Keep the server verifier within that runtime limit; the
// client never stores or reuses this value for online accounts.
const PASSWORD_ITERATIONS = 100_000;

function ownedBuffer(bytes: Uint8Array): ArrayBuffer {
  const copy = new Uint8Array(bytes.byteLength);
  copy.set(bytes);
  return copy.buffer;
}

interface JwtClaims {
  sub: string;
  tid: string | null;
  role: UserRole;
  sid: string;
  iat: number;
  exp: number;
  iss: "community-football-club-manager";
}

interface SessionRow {
  id: string;
  user_id: string;
  refresh_token_hash: string;
  expires_at: string;
  revoked_at: string | null;
}

export interface LoginTokens {
  accessToken: string;
  refreshToken: string;
  tokenType: "Bearer";
  expiresIn: number;
  accessTokenExpiresAtUtc: string;
  refreshTokenExpiresAtUtc: string;
  sessionId: string;
}

function base64UrlEncode(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/u, "");
}

function base64UrlDecode(value: string): Uint8Array {
  const normalized = value.replaceAll("-", "+").replaceAll("_", "/");
  const padded = normalized + "=".repeat((4 - (normalized.length % 4)) % 4);
  try {
    return Uint8Array.from(atob(padded), (char) => char.charCodeAt(0));
  } catch {
    throw new ApiError(401, "invalid_token", "Access token không hợp lệ.");
  }
}

function randomSecret(byteLength = 32): string {
  return base64UrlEncode(crypto.getRandomValues(new Uint8Array(byteLength)));
}

async function sha256(value: string | Uint8Array): Promise<string> {
  const bytes = typeof value === "string" ? encoder.encode(value) : value;
  return base64UrlEncode(new Uint8Array(await crypto.subtle.digest("SHA-256", ownedBuffer(bytes))));
}

function constantTimeEqual(left: Uint8Array, right: Uint8Array): boolean {
  let difference = left.byteLength ^ right.byteLength;
  const length = Math.max(left.byteLength, right.byteLength);
  for (let index = 0; index < length; index += 1) {
    difference |= (left[index] ?? 0) ^ (right[index] ?? 0);
  }
  return difference === 0;
}

export async function constantTimeSecretEqual(left: string, right: string): Promise<boolean> {
  const [leftHash, rightHash] = await Promise.all([
    crypto.subtle.digest("SHA-256", encoder.encode(left)),
    crypto.subtle.digest("SHA-256", encoder.encode(right)),
  ]);
  return constantTimeEqual(new Uint8Array(leftHash), new Uint8Array(rightHash));
}

export function validatePassword(password: unknown): string {
  if (typeof password !== "string" || password.length < 10 || password.length > 128) {
    throw new ApiError(400, "weak_password", "Mật khẩu phải có từ 10 đến 128 ký tự.");
  }
  if (!/[a-z]/u.test(password) || !/[A-Z]/u.test(password) || !/[0-9]/u.test(password)) {
    throw new ApiError(400, "weak_password", "Mật khẩu phải có chữ hoa, chữ thường và chữ số.");
  }
  return password;
}

export async function hashPassword(password: string): Promise<{ hash: string; salt: string; iterations: number }> {
  const saltBytes = crypto.getRandomValues(new Uint8Array(16));
  const key = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: saltBytes, iterations: PASSWORD_ITERATIONS },
    key,
    256,
  );
  return { hash: base64UrlEncode(new Uint8Array(bits)), salt: base64UrlEncode(saltBytes), iterations: PASSWORD_ITERATIONS };
}

export async function verifyPassword(password: string, row: UserRow): Promise<boolean> {
  const salt = base64UrlDecode(row.password_salt);
  const key = await crypto.subtle.importKey("raw", encoder.encode(password), "PBKDF2", false, ["deriveBits"]);
  const bits = await crypto.subtle.deriveBits(
    { name: "PBKDF2", hash: "SHA-256", salt: ownedBuffer(salt), iterations: row.password_iterations },
    key,
    256,
  );
  return constantTimeEqual(new Uint8Array(bits), base64UrlDecode(row.password_hash));
}

async function jwtKey(secret: string): Promise<CryptoKey> {
  if (secret.length < 32) {
    throw new Error("JWT_SECRET must contain at least 32 characters");
  }
  return crypto.subtle.importKey("raw", encoder.encode(secret), { name: "HMAC", hash: "SHA-256" }, false, ["sign", "verify"]);
}

async function signJwt(env: Env, claims: JwtClaims): Promise<string> {
  const header = base64UrlEncode(encoder.encode(JSON.stringify({ alg: "HS256", typ: "JWT" })));
  const payload = base64UrlEncode(encoder.encode(JSON.stringify(claims)));
  const signingInput = `${header}.${payload}`;
  const signature = await crypto.subtle.sign("HMAC", await jwtKey(env.JWT_SECRET), encoder.encode(signingInput));
  return `${signingInput}.${base64UrlEncode(new Uint8Array(signature))}`;
}

async function verifyJwt(env: Env, token: string): Promise<JwtClaims> {
  const parts = token.split(".");
  if (parts.length !== 3 || !parts[0] || !parts[1] || !parts[2]) {
    throw new ApiError(401, "invalid_token", "Access token không hợp lệ.");
  }

  const valid = await crypto.subtle.verify(
    "HMAC",
    await jwtKey(env.JWT_SECRET),
    ownedBuffer(base64UrlDecode(parts[2])),
    encoder.encode(`${parts[0]}.${parts[1]}`),
  );
  if (!valid) throw new ApiError(401, "invalid_token", "Access token không hợp lệ.");

  let header: { alg?: string; typ?: string };
  let claims: JwtClaims;
  try {
    header = JSON.parse(new TextDecoder().decode(base64UrlDecode(parts[0]))) as { alg?: string; typ?: string };
    claims = JSON.parse(new TextDecoder().decode(base64UrlDecode(parts[1]))) as JwtClaims;
  } catch {
    throw new ApiError(401, "invalid_token", "Access token không hợp lệ.");
  }

  const now = Math.floor(Date.now() / 1000);
  if (header.alg !== "HS256" || header.typ !== "JWT" || claims.iss !== "community-football-club-manager" || claims.exp <= now) {
    throw new ApiError(401, "expired_token", "Phiên đăng nhập đã hết hạn.");
  }
  return claims;
}

async function accessToken(env: Env, user: UserRow, sessionId: string): Promise<LoginTokens> {
  const expiresIn = Number.parseInt(env.ACCESS_TOKEN_TTL_SECONDS, 10) || 900;
  const now = Math.floor(Date.now() / 1000);
  return {
    accessToken: await signJwt(env, {
      sub: user.id,
      tid: user.tenant_id,
      role: user.role,
      sid: sessionId,
      iat: now,
      exp: now + expiresIn,
      iss: "community-football-club-manager",
    }),
    refreshToken: "",
    tokenType: "Bearer",
    expiresIn,
    accessTokenExpiresAtUtc: new Date((now + expiresIn) * 1000).toISOString(),
    refreshTokenExpiresAtUtc: "",
    sessionId,
  };
}

async function requestFingerprint(request: Request, env: Env): Promise<{ ipHash: string; userAgent: string }> {
  const ip = request.headers.get("cf-connecting-ip") ?? "local";
  return {
    ipHash: await sha256(`${env.JWT_SECRET}|${ip}`),
    userAgent: (request.headers.get("user-agent") ?? "").slice(0, 300),
  };
}

export async function createSession(env: Env, request: Request, user: UserRow, deviceName = ""): Promise<LoginTokens> {
  const sessionId = newId();
  const refreshToken = `${sessionId}.${randomSecret()}`;
  const refreshHash = await sha256(refreshToken);
  const refreshDays = Number.parseInt(env.REFRESH_TOKEN_TTL_DAYS, 10) || 30;
  const expiresAt = new Date(Date.now() + refreshDays * 86_400_000).toISOString();
  const now = nowIso();
  const fingerprint = await requestFingerprint(request, env);
  await env.DB.prepare(
    `INSERT INTO auth_sessions
      (id, user_id, refresh_token_hash, device_name, ip_hash, user_agent, expires_at, created_at, last_used_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(sessionId, user.id, refreshHash, deviceName.slice(0, 120), fingerprint.ipHash, fingerprint.userAgent, expiresAt, now, now).run();
  return {
    ...(await accessToken(env, user, sessionId)),
    refreshToken,
    refreshTokenExpiresAtUtc: expiresAt,
  };
}

export async function loginWithPassword(
  env: Env,
  request: Request,
  username: string,
  password: string,
  deviceName = "",
): Promise<{ user: UserRow; tokens: LoginTokens }> {
  const normalized = normalizeUsername(username);
  const user = await env.DB.prepare("SELECT * FROM users WHERE username_normalized = ? LIMIT 1")
    .bind(normalized).first<UserRow>();
  if (!user || user.is_active !== 1) {
    throw new ApiError(401, "invalid_credentials", "Username hoặc mật khẩu không đúng.");
  }
  if (user.role !== "admin") {
    const tenant = await env.DB.prepare("SELECT status FROM tenants WHERE id = ? LIMIT 1")
      .bind(user.tenant_id).first<{ status: string }>();
    if (!tenant || tenant.status !== "active") {
      throw new ApiError(401, "invalid_credentials", "Username hoặc mật khẩu không đúng.");
    }
  }

  if (user.lockout_until && Date.parse(user.lockout_until) > Date.now()) {
    throw new ApiError(429, "account_locked", "Tài khoản đang tạm khóa. Vui lòng thử lại sau.");
  }

  if (!(await verifyPassword(password, user))) {
    const failed = user.failed_login_count + 1;
    const lockout = failed >= 5 ? new Date(Date.now() + 15 * 60_000).toISOString() : null;
    await env.DB.prepare("UPDATE users SET failed_login_count = ?, lockout_until = ?, updated_at = ? WHERE id = ?")
      .bind(lockout ? 0 : failed, lockout, nowIso(), user.id).run();
    throw new ApiError(401, "invalid_credentials", "Username hoặc mật khẩu không đúng.");
  }

  await env.DB.prepare("UPDATE users SET failed_login_count = 0, lockout_until = NULL, updated_at = ? WHERE id = ?")
    .bind(nowIso(), user.id).run();
  return { user, tokens: await createSession(env, request, user, deviceName) };
}

export async function authenticate(request: Request, env: Env): Promise<AuthUser> {
  const authorization = request.headers.get("authorization") ?? "";
  if (!authorization.startsWith("Bearer ")) {
    throw new ApiError(401, "authentication_required", "Vui lòng đăng nhập.");
  }
  const claims = await verifyJwt(env, authorization.slice(7));
  const row = await env.DB.prepare(
    `SELECT u.*, s.id AS session_id
       FROM users u JOIN auth_sessions s ON s.user_id = u.id
       LEFT JOIN tenants t ON t.id = u.tenant_id
      WHERE u.id = ? AND s.id = ? AND u.is_active = 1
        AND (u.role = 'admin' OR t.status = 'active')
        AND s.revoked_at IS NULL AND s.expires_at > ? LIMIT 1`,
  ).bind(claims.sub, claims.sid, nowIso()).first<UserRow & { session_id: string }>();
  if (!row || row.role !== claims.role || row.tenant_id !== claims.tid) {
    throw new ApiError(401, "invalid_session", "Phiên đăng nhập không còn hiệu lực.");
  }
  return {
    id: row.id,
    tenantId: row.tenant_id,
    username: row.username,
    role: row.role,
    sessionId: row.session_id,
    mustChangePassword: row.must_change_password === 1,
  };
}

export function requireRole(user: AuthUser, ...roles: UserRole[]): void {
  if (!roles.includes(user.role)) {
    throw new ApiError(403, "forbidden", "Bạn không có quyền thực hiện thao tác này.");
  }
}

export function requireTenant(user: AuthUser): string {
  if (!user.tenantId) throw new ApiError(403, "tenant_required", "Tài khoản này không thuộc đội bóng.");
  return user.tenantId;
}

export async function rotateRefreshToken(env: Env, request: Request, token: string): Promise<LoginTokens> {
  const separator = token.indexOf(".");
  if (separator < 1) throw new ApiError(401, "invalid_refresh_token", "Refresh token không hợp lệ.");
  const sessionId = token.slice(0, separator);
  const tokenHash = await sha256(token);
  const session = await env.DB.prepare("SELECT * FROM auth_sessions WHERE id = ? LIMIT 1")
    .bind(sessionId).first<SessionRow>();
  if (!session || session.revoked_at || session.expires_at <= nowIso() ||
      !(await constantTimeSecretEqual(session.refresh_token_hash, tokenHash))) {
    if (session) await env.DB.prepare("UPDATE auth_sessions SET revoked_at = ? WHERE id = ?").bind(nowIso(), session.id).run();
    throw new ApiError(401, "invalid_refresh_token", "Refresh token không hợp lệ hoặc đã hết hạn.");
  }

  const user = await env.DB.prepare("SELECT * FROM users WHERE id = ? AND is_active = 1 LIMIT 1")
    .bind(session.user_id).first<UserRow>();
  if (!user) throw new ApiError(401, "invalid_refresh_token", "Tài khoản không còn hiệu lực.");
  if (user.role !== "admin") {
    const tenant = await env.DB.prepare("SELECT status FROM tenants WHERE id = ? LIMIT 1")
      .bind(user.tenant_id).first<{ status: string }>();
    if (!tenant || tenant.status !== "active") {
      await revokeSession(env, session.id);
      throw new ApiError(401, "invalid_refresh_token", "Tài khoản không còn hiệu lực.");
    }
  }

  const nextToken = `${session.id}.${randomSecret()}`;
  const fingerprint = await requestFingerprint(request, env);
  const nextHash = await sha256(nextToken);
  // Refresh rotation is a compare-and-swap operation.  Without the previous
  // hash in the predicate, two concurrent refresh requests can both succeed
  // and mint two valid refresh tokens from one source token.
  const rotated = await env.DB.prepare(
    `UPDATE auth_sessions
        SET refresh_token_hash = ?, ip_hash = ?, user_agent = ?, last_used_at = ?
      WHERE id = ? AND refresh_token_hash = ? AND revoked_at IS NULL`,
  ).bind(nextHash, fingerprint.ipHash, fingerprint.userAgent, nowIso(), session.id, session.refresh_token_hash).run();
  if (!rotated.meta.changes) {
    await revokeSession(env, session.id);
    throw new ApiError(401, "invalid_refresh_token", "Refresh token đã được sử dụng hoặc không còn hiệu lực.");
  }
  return {
    ...(await accessToken(env, user, session.id)),
    refreshToken: nextToken,
    refreshTokenExpiresAtUtc: session.expires_at,
  };
}

export async function revokeSession(env: Env, sessionId: string): Promise<void> {
  await env.DB.prepare("UPDATE auth_sessions SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL")
    .bind(nowIso(), sessionId).run();
}

/**
 * Revoke a session using possession of its refresh token. This supports an
 * immediate mobile logout: the app can erase local credentials first and let
 * the network revocation finish without retaining an access token.
 */
export async function revokeRefreshToken(env: Env, token: string): Promise<void> {
  const separator = token.indexOf(".");
  if (separator < 1) return;
  const sessionId = token.slice(0, separator);
  const session = await env.DB.prepare(
    "SELECT id, refresh_token_hash FROM auth_sessions WHERE id = ? LIMIT 1",
  ).bind(sessionId).first<{ id: string; refresh_token_hash: string }>();
  if (!session) return;
  const tokenHash = await sha256(token);
  if (!(await constantTimeSecretEqual(session.refresh_token_hash, tokenHash))) return;
  await revokeSession(env, session.id);
}

export async function replacePassword(env: Env, userId: string, password: string, mustChangePassword: boolean): Promise<void> {
  const validated = validatePassword(password);
  const next = await hashPassword(validated);
  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      `UPDATE users SET password_hash = ?, password_salt = ?, password_iterations = ?,
         must_change_password = ?, failed_login_count = 0, lockout_until = NULL, updated_at = ? WHERE id = ?`,
    ).bind(next.hash, next.salt, next.iterations, mustChangePassword ? 1 : 0, now, userId),
    env.DB.prepare("UPDATE auth_sessions SET revoked_at = ? WHERE user_id = ? AND revoked_at IS NULL").bind(now, userId),
  ]);
}

export async function passwordMatches(password: string, user: UserRow): Promise<boolean> {
  return verifyPassword(password, user);
}
