import {
  authenticate,
  constantTimeSecretEqual,
  createSession,
  hashPassword,
  loginWithPassword,
  passwordMatches,
  replacePassword,
  requireRole,
  requireTenant,
  revokeRefreshToken,
  rotateRefreshToken,
  validatePassword,
} from "./auth";
import {
  AuthUser,
  ClubRow,
  ProfileRow,
  UserRow,
  newId,
  normalizeEmail,
  normalizeUsername,
  nowIso,
  isCoachPositionKey,
  publicClub,
  publicProfile,
  publicUser,
} from "./domain";
import { ApiError, json, noContent, optionalText, readJson, requireDateKey, requireInteger, requireText } from "./http";
import { allRows, assertTenantEntity, audit, createFounder, createTenantUser, getUserBundle } from "./repository";
import {
  AUTO_ABSENT_REVIEW_NOTE,
  CHECKIN_LOCK_AFTER_END_MINUTES,
  CHECKIN_OPEN_LEAD_MINUTES,
  FOUNDER_SUBSTITUTED_COACH_REVIEW_NOTE,
  MAX_OPEN_CHECKIN_SECONDS,
  applySnapshot,
  autoCloseStaleCheckIns,
  getSnapshot,
  runTenantMaintenance,
  salaryDueDateForConfirmation,
} from "./snapshot";
import { consumeOAuthTicket, oauthCallback, oauthStart } from "./oauth";

type JsonObject = Record<string, unknown>;

async function authBundle(env: Env, user: UserRow, tokens?: object): Promise<Record<string, unknown>> {
  const bundle = await getUserBundle(env, user.id);
  return {
    ...(tokens ?? {}),
    user: publicUser(user),
    profile: publicProfile(bundle.profile),
    activeClub: publicClub(bundle.club),
    club: publicClub(bundle.club),
  };
}

export async function setupAdmin(request: Request, env: Env): Promise<Response> {
  const supplied = request.headers.get("x-bootstrap-secret") ?? "";
  if (!env.ADMIN_BOOTSTRAP_SECRET || !(await constantTimeSecretEqual(supplied, env.ADMIN_BOOTSTRAP_SECRET))) {
    throw new ApiError(404, "not_found", "Không tìm thấy endpoint.");
  }
  if (await env.DB.prepare("SELECT id FROM users WHERE role = 'admin' LIMIT 1").first()) {
    throw new ApiError(409, "admin_exists", "Admin đã được khởi tạo.");
  }
  const body = await readJson<JsonObject>(request);
  const username = requireText(body.username, "Username", 80);
  const fullName = requireText(body.fullName, "Họ tên", 180);
  const email = optionalText(body.email, "Email", 200);
  const passwordData = await hashPassword(validatePassword(body.password));
  const id = newId();
  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO users (id, tenant_id, username, username_normalized, email, email_normalized, password_hash,
       password_salt, password_iterations, role, is_active, must_change_password, created_at, updated_at)
       VALUES (?, NULL, ?, ?, ?, ?, ?, ?, ?, 'admin', 1, 0, ?, ?)`,
    ).bind(id, username, normalizeUsername(username), email, normalizeEmail(email), passwordData.hash,
      passwordData.salt, passwordData.iterations, now, now),
    env.DB.prepare("INSERT INTO profiles (user_id, tenant_id, full_name, email, updated_at) VALUES (?, NULL, ?, ?, ?)")
      .bind(id, fullName, email, now),
  ]);
  const user = await env.DB.prepare("SELECT * FROM users WHERE id = ?").bind(id).first<UserRow>();
  return json({ user: publicUser(user!) }, 201);
}

export async function registerFounder(request: Request, env: Env): Promise<Response> {
  if (env.ALLOW_PUBLIC_FOUNDER_REGISTRATION.toLowerCase() !== "true") {
    throw new ApiError(403, "registration_disabled", "Tạo tài khoản Founder công khai đang tắt.");
  }
  const body = await readJson<JsonObject>(request);
  const usernameNormalized = normalizeUsername(requireText(body.username, "Username", 80));
  const idempotencyKey = (request.headers.get("idempotency-key") ?? "").trim();
  if (idempotencyKey.length > 120) {
    throw new ApiError(400, "validation_error", "Idempotency-Key quá dài.");
  }

  let existingRequest: {
    username_normalized: string;
    response_json: string;
  } | null = null;
  if (idempotencyKey) {
    // Keep the public retry ledger bounded. Expired rows are no longer valid
    // for replay and can be removed before reserving a new request key.
    await env.DB.prepare(
      "DELETE FROM public_registration_requests WHERE expires_at<=?",
    ).bind(nowIso()).run();
    existingRequest = await env.DB.prepare(
      `SELECT username_normalized, response_json
         FROM public_registration_requests
        WHERE idempotency_key=? AND expires_at>? LIMIT 1`,
    ).bind(idempotencyKey, nowIso()).first<{
      username_normalized: string;
      response_json: string;
    }>();
    if (existingRequest && existingRequest.username_normalized !== usernameNormalized) {
      throw new ApiError(409, "idempotency_conflict", "Yêu cầu tạo tài khoản không khớp lần gửi trước.");
    }
    if (existingRequest?.response_json) {
      return new Response(existingRequest.response_json, {
        status: 202,
        headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
      });
    }
    if (existingRequest) {
      const existingUser = await env.DB.prepare(
        "SELECT * FROM users WHERE username_normalized=? AND role='founder' LIMIT 1",
      ).bind(usernameNormalized).first<UserRow>();
      if (existingUser) {
        const recovered = {
          ...(await authBundle(env, existingUser)),
          pendingApproval: true,
          message: "Account đã được tạo và đang chờ Admin xác nhận thành lập.",
        };
        const responseJson = JSON.stringify(recovered);
        await env.DB.prepare(
          "UPDATE public_registration_requests SET response_json=? WHERE idempotency_key=?",
        ).bind(responseJson, idempotencyKey).run();
        return new Response(responseJson, {
          status: 202,
          headers: { "content-type": "application/json; charset=utf-8", "cache-control": "no-store" },
        });
      }
    } else {
      await env.DB.prepare(
        `INSERT INTO public_registration_requests
         (idempotency_key, username_normalized, response_json, created_at, expires_at)
         VALUES (?, ?, '', ?, ?)`,
      ).bind(
        idempotencyKey,
        usernameNormalized,
        nowIso(),
        new Date(Date.now() + 24 * 60 * 60 * 1000).toISOString(),
      ).run();
      // Treat a newly reserved idempotency key as an in-flight request so a
      // client retry does not consume another rate-limit slot.
      existingRequest = { username_normalized: usernameNormalized, response_json: "" };
    }
  }

  if (!existingRequest) {
    const ipAddress = (request.headers.get("cf-connecting-ip")
      ?? request.headers.get("x-forwarded-for")?.split(",", 1)[0]
      ?? "unknown").trim().slice(0, 120) || "unknown";
    const now = nowIso();
    const windowStart = new Date(Date.now() - 60 * 60 * 1000).toISOString();
    await env.DB.prepare("DELETE FROM public_registration_attempts WHERE expires_at <= ?")
      .bind(now).run();
    const recent = await env.DB.prepare(
      `SELECT COUNT(*) AS count FROM public_registration_attempts
       WHERE ip_address = ? AND created_at > ?`,
    ).bind(ipAddress, windowStart).first<{ count: number }>();
    if (Number(recent?.count ?? 0) >= 5) {
      throw new ApiError(429, "registration_rate_limited", "Bạn đã tạo quá nhiều yêu cầu. Vui lòng thử lại sau một giờ.");
    }
    await env.DB.prepare(
      `INSERT INTO public_registration_attempts
       (id, ip_address, username_normalized, created_at, expires_at)
       VALUES (?, ?, ?, ?, ?)`,
    ).bind(newId(), ipAddress, usernameNormalized, now,
      new Date(Date.now() + 60 * 60 * 1000).toISOString()).run();
  }

  try {
    const created = await createFounder(env, body, false, true);
    const responseBody = {
      ...(await authBundle(env, created.user)),
      pendingApproval: true,
      message: "Account đã được tạo và đang chờ Admin xác nhận thành lập.",
    };
    if (idempotencyKey) {
      await env.DB.prepare(
        "UPDATE public_registration_requests SET response_json=? WHERE idempotency_key=?",
      ).bind(JSON.stringify(responseBody), idempotencyKey).run();
    }
    await audit(env, created.user.tenant_id, created.user.id, "founder.self_registered", "user", created.user.id);
    return json(responseBody, 202);
  } catch (error) {
    if (idempotencyKey) {
      await env.DB.prepare(
        "DELETE FROM public_registration_requests WHERE idempotency_key=? AND response_json=''",
      ).bind(idempotencyKey).run();
    }
    throw error;
  }
}

export async function login(request: Request, env: Env): Promise<Response> {
  const body = await readJson<JsonObject>(request);
  const result = await loginWithPassword(
    env,
    request,
    requireText(body.username, "Username", 80),
    requireText(body.password, "Password", 128),
    optionalText(body.deviceName, "deviceName", 120),
  );
  return json(await authBundle(env, result.user, result.tokens));
}

export async function refresh(request: Request, env: Env): Promise<Response> {
  const body = await readJson<JsonObject>(request);
  const tokens = await rotateRefreshToken(env, request, requireText(body.refreshToken, "refreshToken", 300));
  const session = await env.DB.prepare("SELECT user_id FROM auth_sessions WHERE id = ?")
    .bind(tokens.sessionId).first<{ user_id: string }>();
  const user = session
    ? await env.DB.prepare("SELECT * FROM users WHERE id = ?").bind(session.user_id).first<UserRow>()
    : null;
  if (!user) throw new ApiError(401, "invalid_refresh_token", "Refresh token không hợp lệ.");
  return json(await authBundle(env, user, tokens));
}

export { oauthCallback, oauthStart };

export async function oauthExchange(request: Request, env: Env): Promise<Response> {
  const { identity, auth } = await consumeOAuthTicket(request, env);
  if (auth) {
    const user = await env.DB.prepare("SELECT * FROM users WHERE id = ? LIMIT 1").bind(auth.id).first<UserRow>();
    if (!user || user.is_active !== 1) throw new ApiError(403, "account_inactive", "Tài khoản chưa được kích hoạt.");
    const existing = await env.DB.prepare(
      "SELECT user_id FROM external_account_links WHERE provider = ? AND subject = ? LIMIT 1",
    ).bind(identity.provider, identity.subject).first<{ user_id: string }>();
    if (existing && existing.user_id !== user.id) {
      throw new ApiError(409, "oauth_already_linked", "Tài khoản OAuth đã liên kết với account khác.");
    }
    const current = await env.DB.prepare(
      "SELECT id FROM external_account_links WHERE user_id = ? AND provider = ? LIMIT 1",
    ).bind(user.id, identity.provider).first<{ id: string }>();
    const now = nowIso();
    await env.DB.prepare(
      current
        ? "UPDATE external_account_links SET subject = ?, email = ?, display_name = ?, updated_at = ? WHERE id = ?"
        : "INSERT INTO external_account_links (id, user_id, provider, subject, email, display_name, linked_at, updated_at) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
    ).bind(...(current
      ? [identity.subject, identity.email, identity.displayName, now, current.id]
      : [newId(), user.id, identity.provider, identity.subject, identity.email, identity.displayName, now, now])).run();
    await audit(env, user.tenant_id, user.id, "oauth.account_linked", "external_account", identity.subject, { provider: identity.provider });
    return json({ provider: identity.provider, externalSubject: identity.subject, email: identity.email, displayName: identity.displayName });
  }

  const link = await env.DB.prepare(
    "SELECT user_id FROM external_account_links WHERE provider = ? AND subject = ? LIMIT 1",
  ).bind(identity.provider, identity.subject).first<{ user_id: string }>();
  if (!link) throw new ApiError(404, "oauth_not_linked", "Tài khoản Google của bạn chưa liên kết với tài khoản");
  const user = await env.DB.prepare("SELECT * FROM users WHERE id = ? LIMIT 1").bind(link.user_id).first<UserRow>();
  if (!user || user.is_active !== 1) throw new ApiError(401, "account_inactive", "Tài khoản chưa được Admin xác nhận hoặc đang bị khóa.");
  const tokens = await createSession(env, request, user, "oauth");
  return json(await authBundle(env, user, tokens));
}

/** Return only the current user's OAuth links; never expose links from another tenant/account. */
export async function oauthLinks(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const links = await allRows<{
    id: string;
    provider: string;
    subject: string;
    email: string;
    display_name: string;
    linked_at: string;
    updated_at: string;
  }>(env.DB.prepare(
    `SELECT id, provider, subject, email, display_name, linked_at, updated_at
       FROM external_account_links
      WHERE user_id = ?
      ORDER BY updated_at DESC`,
  ).bind(auth.id));
  return json({ links: links.map(link => ({
    id: link.id,
    provider: link.provider,
    externalSubject: link.subject,
    email: link.email,
    displayName: link.display_name,
    linkedAtUtc: link.linked_at,
    updatedAtUtc: link.updated_at,
  })) });
}

/** Unlink only the authenticated user's Google account and record the mutation in the same D1 batch. */
export async function oauthUnlink(request: Request, env: Env, provider: string): Promise<Response> {
  const auth = await authenticate(request, env);
  if (provider !== "google") {
    throw new ApiError(400, "oauth_provider_not_supported", "Chỉ hỗ trợ huỷ liên kết Google.");
  }

  const link = await env.DB.prepare(
    `SELECT id, subject
       FROM external_account_links
      WHERE user_id = ? AND provider = ?
      LIMIT 1`,
  ).bind(auth.id, provider).first<{ id: string; subject: string }>();
  if (!link) return noContent();

  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      "DELETE FROM external_account_links WHERE id = ? AND user_id = ? AND provider = ?",
    ).bind(link.id, auth.id, provider),
    env.DB.prepare(
      `INSERT INTO audit_logs (id, tenant_id, actor_user_id, action, entity_type, entity_id, details_json, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(
      newId(),
      auth.tenantId,
      auth.id,
      "oauth.account_unlinked",
      "external_account",
      link.id,
      JSON.stringify({ provider, externalSubject: link.subject }),
      now,
    ),
  ]);
  return noContent();
}

export async function logout(request: Request, env: Env): Promise<Response> {
  const body = await readJson<JsonObject>(request);
  await revokeRefreshToken(env, requireText(body.refreshToken, "refreshToken", 300));
  return noContent();
}

export async function me(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const bundle = await getUserBundle(env, auth.id);
  if (!bundle.user) throw new ApiError(404, "not_found", "Không tìm thấy tài khoản.");
  return json(await authBundle(env, bundle.user));
}

export async function changeOwnPassword(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const body = await readJson<JsonObject>(request);
  const user = await env.DB.prepare("SELECT * FROM users WHERE id = ?").bind(auth.id).first<UserRow>();
  if (!user || !(await passwordMatches(requireText(body.currentPassword, "currentPassword", 128), user))) {
    throw new ApiError(400, "invalid_current_password", "Mật khẩu hiện tại không đúng.");
  }
  await replacePassword(env, auth.id, validatePassword(body.newPassword), false);
  return noContent();
}

export async function adminFounders(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "admin");
  if (request.method === "GET") {
    const rows = await allRows<UserRow & {
      full_name: string;
      team_name: string;
      tenant_status?: string;
      founder_status?: string;
    }>(env.DB.prepare(
      `SELECT u.*, p.full_name, c.team_name, t.status AS tenant_status, t.founder_status FROM users u
       LEFT JOIN profiles p ON p.user_id = u.id LEFT JOIN clubs c ON c.tenant_id = u.tenant_id
       LEFT JOIN tenants t ON t.id = u.tenant_id
       WHERE u.role = 'founder' ORDER BY u.created_at DESC`,
    ));
    return json({ founders: rows.map((row) => ({
      ...publicUser(row),
      fullName: row.full_name,
      teamName: row.team_name,
      tenantStatus: row.tenant_status ?? "active",
      founderStatus: row.founder_status
        ?? (row.tenant_status === "active" && row.is_active === 1 ? "approved" : "pending"),
      approvalStatus: row.founder_status
        ?? (row.tenant_status === "active" && row.is_active === 1
          ? "approved"
          : row.tenant_status === "suspended" && row.is_active === 0
            ? "pending"
            : "inactive"),
    })) });
  }
  const created = await createFounder(env, await readJson<JsonObject>(request), true);
  await audit(env, created.user.tenant_id, auth.id, "admin.founder_created", "user", created.user.id);
  return json(await authBundle(env, created.user), 201);
}

export async function adminFounderAction(
  request: Request,
  env: Env,
  founderId: string,
  action: "password" | "delete" | "status",
): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "admin");
  const founder = await env.DB.prepare("SELECT * FROM users WHERE id = ? AND role = 'founder' LIMIT 1")
    .bind(founderId).first<UserRow>();
  if (!founder) throw new ApiError(404, "not_found", "Không tìm thấy Founder.");
  if (action === "status") {
    const body = await readJson<JsonObject>(request);
    if (typeof body.isActive !== "boolean") {
      throw new ApiError(400, "validation_error", "isActive phải là boolean.");
    }
    const isActive = body.isActive;
    const now = nowIso();
    const founderStatus = isActive ? "approved" : "disabled";
    const statements = [
      env.DB.prepare("UPDATE users SET is_active = ?, updated_at = ? WHERE id = ?")
        .bind(isActive ? 1 : 0, now, founder.id),
      env.DB.prepare("UPDATE tenants SET status = ?, founder_status = ?, updated_at = ? WHERE id = ?")
        .bind(isActive ? "active" : "suspended", founderStatus, now, founder.tenant_id),
    ];
    if (!isActive) {
      statements.push(env.DB.prepare(
        "UPDATE auth_sessions SET revoked_at = ? WHERE user_id = ? AND revoked_at IS NULL",
      ).bind(now, founder.id));
    }
    await env.DB.batch(statements);
    await audit(env, founder.tenant_id, auth.id, isActive ? "admin.founder_approved" : "admin.founder_suspended", "user", founder.id);
    return noContent();
  }
  if (action === "password") {
    const body = await readJson<JsonObject>(request);
    await replacePassword(env, founder.id, validatePassword(body.password), true);
    await audit(env, founder.tenant_id, auth.id, "admin.founder_password_reset", "user", founder.id);
    return noContent();
  }
  const tenantId = founder.tenant_id;
  if (!tenantId) throw new ApiError(409, "invalid_founder", "Founder không có đội hợp lệ.");
  const uploads = await allRows<{ object_key: string }>(env.DB.prepare(
    "SELECT object_key FROM uploads WHERE tenant_id = ?",
  ).bind(tenantId));
  await env.DB.batch([
    // Delete audit/idempotency rows explicitly because those tables retain
    // tenant references with SET NULL for normal lifecycle operations.
    env.DB.prepare("DELETE FROM audit_logs WHERE tenant_id = ?").bind(tenantId),
    env.DB.prepare("DELETE FROM idempotency_keys WHERE tenant_id = ?").bind(tenantId),
    env.DB.prepare("DELETE FROM sync_cursors WHERE tenant_id = ?").bind(tenantId),
    env.DB.prepare("DELETE FROM tenants WHERE id = ?").bind(tenantId),
  ]);
  // D1 deletion is authoritative. R2 cleanup is best-effort so a stale object
  // can never make the account appear to exist again.
  await Promise.allSettled(uploads.map((item) => env.FILES.delete(item.object_key)));
  return noContent();
}

export async function members(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (request.method === "POST") {
    requireRole(auth, "founder");
    const user = await createTenantUser(env, tenantId, await readJson<JsonObject>(request));
    await audit(env, tenantId, auth.id, "member.created", "user", user.id, { role: user.role });
    return json(await authBundle(env, user), 201);
  }
  const role = new URL(request.url).searchParams.get("role");
  const filter = role === "coach" || role === "trainee" ? role : null;
  if (auth.role === "trainee" || auth.role === "coach") {
    const snapshot = await getSnapshot(env, auth);
    return json({ users: snapshot.users, profiles: snapshot.profiles });
  }
  const rows = await allRows<UserRow & { full_name: string }>(env.DB.prepare(
    `SELECT u.*, p.full_name FROM users u LEFT JOIN profiles p ON p.user_id = u.id
     WHERE u.tenant_id = ? AND u.role <> 'admin' AND (? IS NULL OR u.role = ?) ORDER BY p.full_name`,
  ).bind(tenantId, filter, filter));
  return json({ members: rows.map((row) => ({ ...publicUser(row), fullName: row.full_name })) });
}

export async function updateProfile(request: Request, env: Env, userId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (auth.id !== userId && auth.role !== "founder") throw new ApiError(403, "forbidden", "Không được sửa hồ sơ này.");
  await assertTenantEntity(env, "users", userId, tenantId);
  const body = await readJson<JsonObject>(request);
  const existing = await env.DB.prepare(
    "SELECT * FROM profiles WHERE user_id=? AND tenant_id=? LIMIT 1",
  ).bind(userId, tenantId).first<ProfileRow>();
  if (!existing) throw new ApiError(404, "not_found", "Không tìm thấy hồ sơ.");
  const target = await env.DB.prepare("SELECT role FROM users WHERE id=? AND tenant_id=? LIMIT 1")
    .bind(userId, tenantId).first<{ role: string }>();
  const coachPosition = target?.role === "coach"
    ? optionalText(body.coachPosition, "coachPosition", 80)
    : "";
  if (coachPosition && !isCoachPositionKey(coachPosition)) {
    throw new ApiError(400, "validation_error", "Vị trí Coach không hợp lệ.");
  }
  let photoObjectKey = existing.photo_object_key ?? "";
  if (body.photoUploadId !== undefined) {
    const uploadId = requireText(body.photoUploadId, "photoUploadId", 64);
    photoObjectKey = await uploadObjectForOwner(env, tenantId, auth.id, uploadId, "avatar");
  }
  await env.DB.prepare(
    `UPDATE profiles SET full_name=?, phone=?, email=?, date_of_birth=?, height_cm=?, weight_kg=?,
     guardian_name=?, guardian_phone=?, coach_position=?, photo_object_key=?, updated_at=? WHERE user_id=? AND tenant_id=?`,
  ).bind(requireText(body.fullName, "fullName", 180), optionalText(body.phone, "phone", 40),
    optionalText(body.email, "email", 200), body.dateOfBirth ? requireText(body.dateOfBirth, "dateOfBirth", 10) : null,
    Number(body.heightCm ?? 0), Number(body.weightKg ?? 0), optionalText(body.guardianName, "guardianName", 180),
    optionalText(body.guardianPhone, "guardianPhone", 40), coachPosition, photoObjectKey, nowIso(), userId, tenantId).run();
  return json({ profile: publicProfile(await env.DB.prepare(
    "SELECT * FROM profiles WHERE user_id=? AND tenant_id=?",
  ).bind(userId, tenantId).first<ProfileRow>()) });
}

export async function manageMember(
  request: Request,
  env: Env,
  userId: string,
  action: "password" | "status" | "tuitionSupport",
): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const target = await env.DB.prepare(
    "SELECT * FROM users WHERE id=? AND tenant_id=? AND role IN ('coach','trainee') LIMIT 1",
  ).bind(userId, tenantId).first<UserRow>();
  if (!target) throw new ApiError(404, "not_found", "Không tìm thấy thành viên.");
  const body = await readJson<JsonObject>(request);
  const now = nowIso();
  if (action === "tuitionSupport") {
    if (target.role !== "trainee" || typeof body.isSupported !== "boolean") {
      throw new ApiError(400, "validation_error", "isSupported chỉ áp dụng cho Cầu thủ học viên.");
    }
    await env.DB.prepare(
      "UPDATE users SET is_tuition_supported=?, updated_at=? WHERE id=? AND tenant_id=? AND role='trainee'",
    ).bind(body.isSupported ? 1 : 0, now, userId, tenantId).run();
    await audit(env, tenantId, auth.id, "member.tuition_support_changed", "user", userId,
      { isSupported: body.isSupported });
    return noContent();
  }
  if (action === "status") {
    if (typeof body.isActive !== "boolean") throw new ApiError(400, "validation_error", "isActive phải là boolean.");
    await env.DB.batch([
      env.DB.prepare("UPDATE users SET is_active=?, updated_at=? WHERE id=? AND tenant_id=?")
        .bind(body.isActive ? 1 : 0, now, userId, tenantId),
      ...(!body.isActive
        ? [env.DB.prepare("UPDATE auth_sessions SET revoked_at=? WHERE user_id=? AND revoked_at IS NULL").bind(now, userId)]
        : []),
    ]);
    await audit(env, tenantId, auth.id, "member.status_changed", "user", userId, { isActive: body.isActive });
    return noContent();
  }
  const password = body.password === undefined ? "12345678" : validatePassword(body.password);
  const next = await hashPassword(password);
  await env.DB.batch([
    env.DB.prepare(
      `UPDATE users SET password_hash=?, password_salt=?, password_iterations=?, must_change_password=1,
       failed_login_count=0, lockout_until=NULL, updated_at=? WHERE id=? AND tenant_id=?`,
    ).bind(next.hash, next.salt, next.iterations, now, userId, tenantId),
    env.DB.prepare("UPDATE auth_sessions SET revoked_at=? WHERE user_id=? AND revoked_at IS NULL").bind(now, userId),
  ]);
  await audit(env, tenantId, auth.id, "member.password_reset", "user", userId);
  return noContent();
}

export async function club(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (request.method === "GET") {
    return json({ club: publicClub(await env.DB.prepare("SELECT * FROM clubs WHERE tenant_id=?").bind(tenantId).first<ClubRow>()) });
  }
  requireRole(auth, "founder");
  const body = await readJson<JsonObject>(request);
  const existing = await env.DB.prepare(
    "SELECT * FROM clubs WHERE tenant_id=? LIMIT 1",
  ).bind(tenantId).first<ClubRow>();
  if (!existing) throw new ApiError(404, "not_found", "Không tìm thấy thông tin đội.");
  let logoObjectKey = existing.logo_object_key ?? "";
  if (body.logoUploadId !== undefined) {
    const uploadId = requireText(body.logoUploadId, "logoUploadId", 64);
    logoObjectKey = await uploadObjectForOwner(env, tenantId, auth.id, uploadId, "club_logo");
  }
  await env.DB.prepare(
    `UPDATE clubs SET team_name=?, phone=?, email=?, bank_name=?, bank_bin=?, bank_account_number=?,
     bank_account_name=?, logo_object_key=?, updated_at=? WHERE tenant_id=?`,
  ).bind(requireText(body.teamName, "teamName", 180), optionalText(body.phone, "phone", 40),
    optionalText(body.email, "email", 200), optionalText(body.bankName, "bankName", 180),
    optionalText(body.bankBin, "bankBin", 20), optionalText(body.bankAccountNumber, "bankAccountNumber", 80),
    optionalText(body.bankAccountName, "bankAccountName", 180), logoObjectKey, nowIso(), tenantId).run();
  return club(new Request(request.url, { method: "GET", headers: request.headers }), env);
}

export async function classes(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (request.method === "GET") {
    const snapshot = await getSnapshot(env, auth);
    return json({ classes: snapshot.classes, venues: snapshot.venues,
      classCoaches: snapshot.classCoaches, classEnrollments: snapshot.classEnrollments });
  }
  requireRole(auth, "founder");
  const body = await readJson<JsonObject>(request);
  const id = newId();
  const venueId = body.venueId ? requireText(body.venueId, "venueId", 64) : null;
  if (venueId) await assertTenantEntity(env, "venues", venueId, tenantId);
  const now = nowIso();
  const startDate = body.startDate === undefined
    ? now.slice(0, 10)
    : requireDateKey(body.startDate, "startDate");
  await env.DB.prepare(
    `INSERT INTO classes (id, tenant_id, venue_id, name, schedule_days, start_date, start_time_minutes, end_time_minutes,
     tuition_session_count, default_cycle_fee_vnd, is_active, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?)`,
  ).bind(id, tenantId, venueId, requireText(body.name, "name", 180), optionalText(body.scheduleDays, "scheduleDays", 50),
    startDate,
    requireInteger(body.startTimeMinutes, "startTimeMinutes", 0, 1439), requireInteger(body.endTimeMinutes, "endTimeMinutes", 1, 1440),
    requireInteger(body.tuitionSessionCount, "tuitionSessionCount", 1, 100),
    requireInteger(body.defaultCycleFeeVnd, "defaultCycleFeeVnd", 0, 2_000_000_000), now, now).run();
  return json({ id }, 201);
}

type EvaluationRow = Record<string, unknown> & {
  id: string;
  tenant_id: string;
  class_id: string;
  trainee_user_id: string;
  coach_user_id: string;
  evaluation_type: string;
  title: string;
  evaluation_date: string;
  overall_score: number;
  technical_score: number;
  tactical_score: number;
  physical_score: number;
  attitude_score: number;
  strengths: string;
  improvements: string;
  notes: string;
  status: string;
  review_note: string;
  reviewed_by_user_id: string | null;
  reviewed_at: string | null;
  created_at: string;
  updated_at: string;
  trainee_name?: string;
  coach_name?: string;
  coach_position?: string;
  class_name?: string;
  evaluation_request_open?: number;
};

function evaluationJson(row: EvaluationRow): Record<string, unknown> {
  return {
    id: row.id,
    tenantId: row.tenant_id,
    classId: row.class_id,
    traineeUserId: row.trainee_user_id,
    coachUserId: row.coach_user_id,
    evaluationType: row.evaluation_type,
    title: row.title,
    evaluationDate: `${row.evaluation_date}T00:00:00.000Z`,
    overallScore: Number(row.overall_score ?? 0),
    technicalScore: Number(row.technical_score ?? 0),
    tacticalScore: Number(row.tactical_score ?? 0),
    physicalScore: Number(row.physical_score ?? 0),
    attitudeScore: Number(row.attitude_score ?? 0),
    strengths: row.strengths ?? "",
    improvements: row.improvements ?? "",
    notes: row.notes ?? "",
    status: row.status,
    reviewNote: row.review_note ?? "",
    reviewedByUserId: row.reviewed_by_user_id ?? "",
    reviewedAt: row.reviewed_at,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
    traineeName: row.trainee_name ?? "Cầu thủ học viên",
    coachName: row.coach_name ?? "Huấn luyện viên",
    coachPosition: row.coach_position ?? "",
    className: row.class_name ?? "Lớp học",
    evaluationRequestOpen: Number(row.evaluation_request_open ?? 0) === 1,
  };
}

async function getEvaluationRow(env: Env, tenantId: string, id: string): Promise<EvaluationRow> {
  const row = await env.DB.prepare(
    `SELECT te.*, tp.full_name AS trainee_name, cp.full_name AS coach_name, c.name AS class_name,
            c.evaluation_request_open
       FROM trainee_evaluations te
       LEFT JOIN profiles tp ON tp.user_id=te.trainee_user_id AND tp.tenant_id=te.tenant_id
       LEFT JOIN profiles cp ON cp.user_id=te.coach_user_id AND cp.tenant_id=te.tenant_id
       LEFT JOIN classes c ON c.id=te.class_id AND c.tenant_id=te.tenant_id
      WHERE te.id=? AND te.tenant_id=? LIMIT 1`,
  ).bind(id, tenantId).first<EvaluationRow>();
  if (!row) throw new ApiError(404, "not_found", "Không tìm thấy đánh giá học viên.");
  return row;
}

function evaluationType(value: unknown): string {
  const type = requireText(value, "evaluationType", 40).toLowerCase();
  if (type !== "periodic" && type !== "tournament_match") {
    throw new ApiError(400, "validation_error", "Loại đánh giá không hợp lệ.");
  }
  return type;
}

function optionalScore(value: unknown, field: string, defaultValue = 0): number {
  return value === undefined || value === null || value === ""
    ? defaultValue
    : requireInteger(value, field, 0, 5);
}

async function assertCoachCanEvaluate(
  env: Env,
  tenantId: string,
  coachId: string,
  classId: string,
  traineeId: string,
): Promise<void> {
  const classRow = await env.DB.prepare(
    `SELECT evaluation_request_open FROM classes
      WHERE tenant_id=? AND id=? LIMIT 1`,
  ).bind(tenantId, classId).first<{ evaluation_request_open: number }>();
  if (!classRow) throw new ApiError(404, "not_found", "Không tìm thấy lớp học.");
  if (Number(classRow.evaluation_request_open ?? 0) !== 1) {
    throw new ApiError(403, "evaluation_request_closed",
      "Founder chưa mở yêu cầu đánh giá cho lớp này.");
  }
  const assigned = await env.DB.prepare(
    `SELECT 1 FROM class_coaches cc
      WHERE cc.tenant_id=? AND cc.class_id=? AND cc.coach_user_id=? AND cc.is_active=1 LIMIT 1`,
  ).bind(tenantId, classId, coachId).first();
  if (!assigned) throw new ApiError(403, "class_access_denied", "Coach không được phân công vào lớp này.");
  const enrolled = await env.DB.prepare(
    `SELECT 1 FROM class_enrollments
      WHERE tenant_id=? AND class_id=? AND trainee_user_id=? AND is_active=1 LIMIT 1`,
  ).bind(tenantId, classId, traineeId).first();
  if (!enrolled) throw new ApiError(404, "not_found", "Học viên không thuộc lớp này.");
}

async function activeUsersByRole(
  env: Env,
  tenantId: string,
  role: "founder" | "coach" | "trainee",
): Promise<Array<{ id: string }>> {
  return allRows<{ id: string }>(env.DB.prepare(
    "SELECT id FROM users WHERE tenant_id=? AND role=? AND is_active=1",
  ).bind(tenantId, role));
}

function notificationInsertIfMissing(
  env: Env,
  tenantId: string,
  recipientUserId: string,
  kind: string,
  title: string,
  message: string,
  relatedEntityId: string,
  createdAt: string,
) {
  return env.DB.prepare(
    `INSERT INTO notifications
       (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     SELECT ?, ?, ?, ?, ?, ?, ?, ?
      WHERE NOT EXISTS (
        SELECT 1 FROM notifications
         WHERE tenant_id=? AND recipient_user_id=? AND kind=? AND related_entity_id=?
      )`,
  ).bind(
    newId(), tenantId, recipientUserId, kind, title, message, relatedEntityId, createdAt,
    tenantId, recipientUserId, kind, relatedEntityId,
  );
}

async function notifyEvaluationRequestOpened(
  env: Env,
  tenantId: string,
  classId: string,
  className: string,
): Promise<void> {
  const coaches = await allRows<{ id: string }>(env.DB.prepare(
    `SELECT DISTINCT u.id
       FROM users u
       JOIN class_coaches cc
         ON cc.tenant_id=u.tenant_id AND cc.coach_user_id=u.id AND cc.is_active=1
      WHERE u.tenant_id=? AND u.role='coach' AND u.is_active=1 AND cc.class_id=?`,
  ).bind(tenantId, classId));
  if (coaches.length === 0) return;

  const now = nowIso();
  await env.DB.batch(coaches.map((coach) => notificationInsertIfMissing(
    env,
    tenantId,
    coach.id,
    "EvaluationRequestOpened",
    "Mở yêu cầu đánh giá học viên",
    `Founder đã mở yêu cầu đánh giá cho lớp ${className}. Vui lòng hoàn tất đánh giá các Cầu thủ học viên.`,
    classId,
    now,
  )));
}

async function notifyEvaluationSubmitted(
  env: Env,
  tenantId: string,
  row: EvaluationRow,
): Promise<void> {
  const founders = await activeUsersByRole(env, tenantId, "founder");
  if (founders.length === 0) return;

  // Include updated_at so a rejected evaluation that is corrected and sent
  // again creates a fresh Founder notification.
  const relatedEntityId = `${row.id}:${row.updated_at}`;
  const now = nowIso();
  await env.DB.batch(founders.map((founder) => env.DB.prepare(
    `INSERT INTO notifications
       (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(
    newId(), tenantId, founder.id, "EvaluationSubmitted",
    "Có đánh giá học viên cần xác nhận",
    `${row.coach_name ?? "Huấn luyện viên"} đã gửi đánh giá cho ${row.trainee_name ?? "Cầu thủ học viên"} trong lớp ${row.class_name ?? "Lớp học"}. Vui lòng kiểm tra và xác nhận.`,
    relatedEntityId,
    now,
  )));
}

async function notifyEvaluationClassCompleted(
  env: Env,
  tenantId: string,
  classId: string,
  coachId: string,
  className: string,
  coachName: string,
): Promise<void> {
  const total = Number((await env.DB.prepare(
    "SELECT COUNT(*) AS count FROM class_enrollments WHERE tenant_id=? AND class_id=? AND is_active=1",
  ).bind(tenantId, classId).first<{ count: number }>())?.count ?? 0);
  if (total === 0) return;

  const completed = Number((await env.DB.prepare(
    `SELECT COUNT(DISTINCT trainee_user_id) AS count
       FROM trainee_evaluations
      WHERE tenant_id=? AND class_id=? AND coach_user_id=? AND status IN ('pending','approved')`,
  ).bind(tenantId, classId, coachId).first<{ count: number }>())?.count ?? 0);
  if (completed < total) return;

  const alreadySent = await env.DB.prepare(
    `SELECT 1 FROM notifications
      WHERE tenant_id=? AND kind='EvaluationClassCompleted' AND related_entity_id=? LIMIT 1`,
  ).bind(tenantId, classId).first();
  if (alreadySent) return;

  const founders = await activeUsersByRole(env, tenantId, "founder");
  if (founders.length === 0) return;
  const now = nowIso();
  await env.DB.batch(founders.map((founder) => env.DB.prepare(
    `INSERT INTO notifications
       (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(
    newId(), tenantId, founder.id, "EvaluationClassCompleted",
    "Coach đã đánh giá hoàn tất",
    `${coachName} đã đánh giá đủ ${total} Cầu thủ học viên trong lớp ${className}. Cần Founder xác nhận tất cả.`,
    classId,
    now,
  )));
}

export async function evaluationRequest(
  request: Request,
  env: Env,
  classId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  requireRole(auth, "founder");
  const body = await readJson<JsonObject>(request);
  if (typeof body.isOpen !== "boolean") {
    throw new ApiError(400, "validation_error", "isOpen phải là boolean.");
  }

  const current = await env.DB.prepare(
    "SELECT id, name, evaluation_request_open FROM classes WHERE tenant_id=? AND id=? LIMIT 1",
  ).bind(tenantId, classId).first<{ id: string; name: string; evaluation_request_open: number }>();
  if (!current) throw new ApiError(404, "not_found", "Không tìm thấy lớp học.");

  const isOpen = body.isOpen;
  const wasOpen = Number(current.evaluation_request_open ?? 0) === 1;
  const now = nowIso();
  await env.DB.prepare(
    "UPDATE classes SET evaluation_request_open=?, updated_at=? WHERE tenant_id=? AND id=?",
  ).bind(isOpen ? 1 : 0, now, tenantId, classId).run();
  if (isOpen && !wasOpen) {
    await notifyEvaluationRequestOpened(env, tenantId, classId, current.name);
  }
  await audit(env, tenantId, auth.id,
    isOpen ? "trainee_evaluation.request_opened" : "trainee_evaluation.request_closed",
    "class", classId, { className: current.name });
  return json({ evaluationRequestOpen: isOpen });
}

export async function evaluations(request: Request, env: Env, evaluationId?: string): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);

  if (request.method === "GET") {
    const query = new URL(request.url).searchParams;
    const classId = query.get("classId")?.trim() ?? "";
    const traineeId = query.get("traineeUserId")?.trim() ?? "";
    if (auth.role === "trainee" && traineeId && traineeId !== auth.id) {
      throw new ApiError(403, "forbidden", "Bạn chỉ có thể xem đánh giá của chính mình.");
    }
    if (classId && auth.role === "coach") {
      const assigned = await env.DB.prepare(
        `SELECT 1 FROM class_coaches
          WHERE tenant_id=? AND class_id=? AND coach_user_id=? AND is_active=1 LIMIT 1`,
      ).bind(tenantId, classId, auth.id).first();
      if (!assigned) throw new ApiError(403, "class_access_denied", "Coach không được phân công vào lớp này.");
    }
    if (classId && auth.role === "trainee") {
      const enrolled = await env.DB.prepare(
        `SELECT 1 FROM class_enrollments
          WHERE tenant_id=? AND class_id=? AND trainee_user_id=? AND is_active=1 LIMIT 1`,
      ).bind(tenantId, classId, auth.id).first();
      if (!enrolled) throw new ApiError(403, "class_access_denied", "Bạn không thuộc lớp này.");
    }
    const rows = await allRows(env.DB.prepare(
      `SELECT te.*, tp.full_name AS trainee_name, cp.full_name AS coach_name, c.name AS class_name,
              c.evaluation_request_open
         FROM trainee_evaluations te
         LEFT JOIN profiles tp ON tp.user_id=te.trainee_user_id AND tp.tenant_id=te.tenant_id
         LEFT JOIN profiles cp ON cp.user_id=te.coach_user_id AND cp.tenant_id=te.tenant_id
         LEFT JOIN classes c ON c.id=te.class_id AND c.tenant_id=te.tenant_id
        WHERE te.tenant_id=?
          AND (?='' OR te.class_id=?)
          AND (?='' OR te.trainee_user_id=?)
          AND (
            ?='founder'
            OR (?='trainee' AND te.trainee_user_id=?)
            OR (?='coach' AND EXISTS (
              SELECT 1 FROM class_coaches cc
               WHERE cc.tenant_id=te.tenant_id AND cc.class_id=te.class_id
                 AND cc.coach_user_id=? AND cc.is_active=1
            ))
          )
        ORDER BY te.evaluation_date DESC, te.created_at DESC`,
    ).bind(
      tenantId,
      classId, classId,
      traineeId, traineeId,
      auth.role,
      auth.role, auth.id,
      auth.role, auth.id,
    ));
    const evaluationRows = rows.map((row) => evaluationJson(row as EvaluationRow));
    const requestOpen = rows.length > 0
      ? Number((rows[0] as EvaluationRow).evaluation_request_open ?? 0) === 1
      : classId !== ""
        ? Number((await env.DB.prepare(
          "SELECT evaluation_request_open FROM classes WHERE tenant_id=? AND id=? LIMIT 1",
        ).bind(tenantId, classId).first<{ evaluation_request_open: number }>())?.evaluation_request_open ?? 0) === 1
        : false;
    return json({ evaluations: evaluationRows, evaluationRequestOpen: requestOpen });
  }

  if (request.method === "POST") {
    requireRole(auth, "coach");
    const body = await readJson<JsonObject>(request);
    const classId = requireText(body.classId, "classId", 64);
    const traineeId = requireText(body.traineeUserId, "traineeUserId", 64);
    await assertCoachCanEvaluate(env, tenantId, auth.id, classId, traineeId);
    const evaluationDate = body.evaluationDate === undefined
      ? nowIso().slice(0, 10)
      : requireDateKey(body.evaluationDate, "evaluationDate");
    const id = newId();
    const now = nowIso();
    await env.DB.prepare(
      `INSERT INTO trainee_evaluations
       (id, tenant_id, class_id, trainee_user_id, coach_user_id, evaluation_type, title,
        evaluation_date, overall_score, technical_score, tactical_score, physical_score,
        attitude_score, strengths, improvements, notes, status, review_note,
        reviewed_by_user_id, reviewed_at, created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 'pending', '', NULL, NULL, ?, ?)`,
    ).bind(
      id, tenantId, classId, traineeId,
      auth.id,
      evaluationType(body.evaluationType ?? "periodic"),
      optionalText(body.title, "title", 180),
      evaluationDate,
      requireInteger(body.overallScore, "overallScore", 1, 5),
      optionalScore(body.technicalScore, "technicalScore"),
      optionalScore(body.tacticalScore, "tacticalScore"),
      optionalScore(body.physicalScore, "physicalScore"),
      optionalScore(body.attitudeScore, "attitudeScore"),
      optionalText(body.strengths, "strengths", 2000),
      optionalText(body.improvements, "improvements", 2000),
      optionalText(body.notes, "notes", 2000),
      now, now,
    ).run();
    const saved = await getEvaluationRow(env, tenantId, id);
    await notifyEvaluationSubmitted(env, tenantId, saved);
    await notifyEvaluationClassCompleted(
      env,
      tenantId,
      classId,
      auth.id,
      saved.class_name ?? "Lớp học",
      saved.coach_name ?? "Huấn luyện viên",
    );
    await audit(env, tenantId, auth.id, "trainee_evaluation.created", "trainee_evaluation", id,
      { classId, traineeUserId: traineeId });
    return json({ evaluation: evaluationJson(saved) }, 201);
  }

  if (!evaluationId) throw new ApiError(404, "not_found", "Không tìm thấy đánh giá học viên.");
  const current = await getEvaluationRow(env, tenantId, evaluationId);
  const body = await readJson<JsonObject>(request);
  if (request.method === "PATCH") {
    requireRole(auth, "coach");
    if (current.coach_user_id !== auth.id) {
      throw new ApiError(403, "forbidden", "Chỉ Coach tạo đánh giá mới được chỉnh sửa.");
    }
    if (current.status === "approved") {
      throw new ApiError(409, "evaluation_locked", "Đánh giá đã được Founder xác nhận và không thể chỉnh sửa.");
    }
    await assertCoachCanEvaluate(env, tenantId, auth.id, current.class_id, current.trainee_user_id);
    const now = nowIso();
    await env.DB.prepare(
      `UPDATE trainee_evaluations SET evaluation_type=?, title=?, evaluation_date=?,
        overall_score=?, technical_score=?, tactical_score=?, physical_score=?, attitude_score=?,
        strengths=?, improvements=?, notes=?, status='pending', review_note='',
        reviewed_by_user_id=NULL, reviewed_at=NULL, updated_at=?
       WHERE id=? AND tenant_id=?`,
    ).bind(
      evaluationType(body.evaluationType ?? current.evaluation_type),
      body.title === undefined ? current.title : optionalText(body.title, "title", 180),
      body.evaluationDate === undefined ? current.evaluation_date : requireDateKey(body.evaluationDate, "evaluationDate"),
      body.overallScore === undefined ? Number(current.overall_score) : requireInteger(body.overallScore, "overallScore", 1, 5),
      optionalScore(body.technicalScore, "technicalScore", Number(current.technical_score)),
      optionalScore(body.tacticalScore, "tacticalScore", Number(current.tactical_score)),
      optionalScore(body.physicalScore, "physicalScore", Number(current.physical_score)),
      optionalScore(body.attitudeScore, "attitudeScore", Number(current.attitude_score)),
      body.strengths === undefined ? current.strengths : optionalText(body.strengths, "strengths", 2000),
      body.improvements === undefined ? current.improvements : optionalText(body.improvements, "improvements", 2000),
      body.notes === undefined ? current.notes : optionalText(body.notes, "notes", 2000),
      now, evaluationId, tenantId,
    ).run();
    const saved = await getEvaluationRow(env, tenantId, evaluationId);
    await notifyEvaluationSubmitted(env, tenantId, saved);
    await notifyEvaluationClassCompleted(
      env,
      tenantId,
      saved.class_id,
      auth.id,
      saved.class_name ?? "Lớp học",
      saved.coach_name ?? "Huấn luyện viên",
    );
    await audit(env, tenantId, auth.id, "trainee_evaluation.updated", "trainee_evaluation", evaluationId);
    return json({ evaluation: evaluationJson(saved) });
  }

  throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
}

/**
 * Returns the deliberately small roster used by the Coach evaluation page.
 * It is separate from the attendance roster grant: Founder opening the
 * evaluation request authorizes only name, birth date, height and weight for
 * the assigned class.
 */
export async function evaluationRoster(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  requireRole(auth, "coach");
  if (request.method !== "GET") {
    throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
  }

  const classId = new URL(request.url).searchParams.get("classId")?.trim() ?? "";
  if (!classId) {
    throw new ApiError(400, "validation_error", "Thiếu classId.");
  }

  const classRow = await env.DB.prepare(
    `SELECT c.id, c.evaluation_request_open
       FROM classes c
       JOIN class_coaches cc
         ON cc.tenant_id=c.tenant_id AND cc.class_id=c.id
        AND cc.coach_user_id=? AND cc.is_active=1
      WHERE c.tenant_id=? AND c.id=? AND c.is_active=1
      LIMIT 1`,
  ).bind(auth.id, tenantId, classId).first<{
    id: string;
    evaluation_request_open: number;
  }>();
  if (!classRow) {
    throw new ApiError(404, "not_found", "Không tìm thấy lớp học được phân công.");
  }

  if (Number(classRow.evaluation_request_open ?? 0) !== 1) {
    return json({ classId, evaluationRequestOpen: false, trainees: [] });
  }

  const rows = await allRows<{
    user_id: string;
    full_name: string;
    date_of_birth: string | null;
    height_cm: number;
    weight_kg: number;
  }>(env.DB.prepare(
    `SELECT u.id AS user_id,
            COALESCE(NULLIF(p.full_name,''), u.username) AS full_name,
            p.date_of_birth, p.height_cm, p.weight_kg
       FROM class_enrollments ce
       JOIN users u
         ON u.id=ce.trainee_user_id AND u.tenant_id=ce.tenant_id
        AND u.role='trainee' AND u.is_active=1
       LEFT JOIN profiles p
         ON p.user_id=u.id AND p.tenant_id=ce.tenant_id
      WHERE ce.tenant_id=? AND ce.class_id=? AND ce.is_active=1
      ORDER BY full_name COLLATE NOCASE`,
  ).bind(tenantId, classId));

  return json({
    classId,
    evaluationRequestOpen: true,
    trainees: rows.map((row) => ({
      userId: row.user_id,
      fullName: row.full_name ?? "",
      dateOfBirth: row.date_of_birth || null,
      heightCm: Number(row.height_cm ?? 0),
      weightKg: Number(row.weight_kg ?? 0),
    })),
  });
}

export async function reviewEvaluation(
  request: Request,
  env: Env,
  evaluationId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  requireRole(auth, "founder");
  const current = await getEvaluationRow(env, tenantId, evaluationId);
  const body = await readJson<JsonObject>(request);
  if (typeof body.approved !== "boolean") {
    throw new ApiError(400, "validation_error", "approved phải là boolean.");
  }
  if (current.status === "approved") {
    return json({ evaluation: evaluationJson(current) });
  }
  const now = nowIso();
  const status = body.approved ? "approved" : "rejected";
  await env.DB.prepare(
    `UPDATE trainee_evaluations SET status=?, review_note=?, reviewed_by_user_id=?, reviewed_at=?, updated_at=?
     WHERE id=? AND tenant_id=?`,
  ).bind(status, optionalText(body.note, "note", 500), auth.id, now, now, evaluationId, tenantId).run();
  const reviewed = await getEvaluationRow(env, tenantId, evaluationId);
  if (body.approved) {
    await env.DB.prepare(
      `INSERT INTO notifications
         (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(
      newId(), tenantId, reviewed.trainee_user_id, "EvaluationApproved",
      "Đánh giá học viên đã được xác nhận",
      `Founder đã xác nhận đánh giá của bạn trong lớp ${reviewed.class_name ?? "Lớp học"}. Bạn có thể mở Lịch sử đánh giá để xem chi tiết.`,
      evaluationId,
      now,
    ).run();
  } else {
    const note = optionalText(body.note, "note", 500);
    await env.DB.prepare(
      `INSERT INTO notifications
         (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(
      newId(), tenantId, reviewed.coach_user_id, "EvaluationRejected",
      "Đánh giá cần chỉnh sửa",
      note
        ? `Founder yêu cầu chỉnh sửa đánh giá: ${note}`
        : "Founder yêu cầu bạn kiểm tra và gửi lại đánh giá học viên.",
      evaluationId,
      now,
    ).run();
  }
  await audit(env, tenantId, auth.id,
    body.approved ? "trainee_evaluation.approved" : "trainee_evaluation.rejected",
    "trainee_evaluation", evaluationId);
  return json({ evaluation: evaluationJson(reviewed) });
}

/**
 * Permanently removes one Founder-owned class and its class-scoped data.
 * D1 foreign-key cascades remove assignments, sessions, attendance, check-ins,
 * invoices, proofs and receipts. Coach salary rows are deliberately retained
 * because a monthly salary can include more than one class.
 */
export async function deleteClass(
  request: Request,
  env: Env,
  classId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  requireRole(auth, "founder");
  const existing = await env.DB.prepare(
    "SELECT id, name FROM classes WHERE id = ? AND tenant_id = ? LIMIT 1",
  ).bind(classId, tenantId).first<{ id: string; name: string }>();
  if (!existing) throw new ApiError(404, "not_found", "Không tìm thấy lớp học.");

  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare("DELETE FROM classes WHERE id = ? AND tenant_id = ?")
      .bind(classId, tenantId),
    env.DB.prepare(
      `INSERT INTO audit_logs (id, tenant_id, actor_user_id, action, entity_type, entity_id, details_json, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(
      newId(),
      tenantId,
      auth.id,
      "class.deleted",
      "class",
      classId,
      JSON.stringify({ name: existing.name }),
      now,
    ),
  ]);
  return noContent();
}

export async function attendance(request: Request, env: Env, sessionId?: string): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  await autoCloseStaleCheckIns(env, tenantId, auth.role === "coach" ? auth.id : undefined);
  const actualSessionId = sessionId ?? new URL(request.url).searchParams.get("sessionId") ?? "";
  await assertTenantEntity(env, "training_sessions", actualSessionId, tenantId);
  if (request.method === "GET") {
    if (auth.role === "trainee") {
      const rows = await allRows(env.DB.prepare(
        "SELECT * FROM attendance_records WHERE tenant_id=? AND session_id=? AND trainee_user_id=?",
      ).bind(tenantId, actualSessionId, auth.id));
      return json({ records: rows });
    }
    if (auth.role === "coach") {
      const open = await env.DB.prepare(
        "SELECT id FROM coach_checkins WHERE tenant_id=? AND session_id=? AND coach_user_id=? AND checked_out_at IS NULL",
      ).bind(tenantId, actualSessionId, auth.id).first();
      if (!open) throw new ApiError(403, "checkin_required", "Danh sách chỉ mở sau check-in và đóng khi check-out.");
    }
    return json({ records: await allRows(env.DB.prepare(
      "SELECT * FROM attendance_records WHERE tenant_id=? AND session_id=?",
    ).bind(tenantId, actualSessionId)) });
  }
  requireRole(auth, "coach", "founder");
  const body = await readJson<JsonObject>(request);
  const submit = body.submit === true;
  if (body.submit !== undefined && typeof body.submit !== "boolean") {
    throw new ApiError(400, "validation_error", "submit phải là boolean.");
  }

  // "Điểm danh hoàn tất" is a separate state from Coach check-out.  Before
  // applying the records, validate that the submit action contains one final
  // status for every trainee in this session.  This prevents a partial roster
  // from being published as completed while still allowing draft saves.
  if (submit) {
    const session = await env.DB.prepare(
      "SELECT class_id, created_at FROM training_sessions WHERE id=? AND tenant_id=? LIMIT 1",
    ).bind(actualSessionId, tenantId).first<{ class_id: string; created_at: string }>();
    if (!session) throw new ApiError(404, "not_found", "Không tìm thấy buổi học.");
    const incoming = body.records;
    if (!Array.isArray(incoming)) {
      throw new ApiError(400, "validation_error", "records phải là một mảng object.");
    }
    const expected = await allRows<{ trainee_user_id: string }>(env.DB.prepare(
      `SELECT trainee_user_id FROM class_enrollments
       WHERE tenant_id=? AND class_id=? AND is_active=1 AND enrolled_at<=?`,
    ).bind(tenantId, session.class_id, session.created_at));
    const incomingIds = new Set<string>();
    for (const item of incoming) {
      if (!item || typeof item !== "object" || Array.isArray(item)) {
        throw new ApiError(400, "validation_error", "records phải là một mảng object.");
      }
      const record = item as JsonObject;
      const traineeId = requireText(record.traineeUserId, "attendance.traineeUserId", 64);
      const status = requireText(record.status, "attendance.status", 20);
      if (status === "unmarked") {
        throw new ApiError(400, "attendance_incomplete", "Vui lòng ghi nhận trạng thái cho tất cả học viên.");
      }
      incomingIds.add(traineeId);
    }
    const missing = expected.some((row) => !incomingIds.has(row.trainee_user_id));
    if (missing || incomingIds.size !== expected.length) {
      throw new ApiError(400, "attendance_incomplete", "Vui lòng ghi nhận trạng thái cho tất cả học viên.");
    }
    if (auth.role === "founder" && !optionalText(body.overrideReason, "overrideReason", 500)) {
      throw new ApiError(400, "override_reason_required", "Founder cần nhập lý do khi điểm danh thay.");
    }
  }
  const result = await applySnapshot(env, auth, { attendanceRecords: body.records ?? [] });
  if (submit) {
    const now = nowIso();
    const rawOverrideReason = auth.role === "founder"
      ? optionalText(body.overrideReason, "overrideReason", 500)
      : "";
    const coachTaughtManually = auth.role === "founder" && body.coachTaughtManually === true;
    const overrideReason = auth.role === "founder"
      ? coachTaughtManually
        ? `${rawOverrideReason} · Founder ghi nhận buổi học cũ; Coach đã dạy`
        : `${rawOverrideReason} · Coach không dạy; Founder điểm danh thay Coach`
      : "";
    await env.DB.prepare(
      `UPDATE training_sessions SET status='submitted', submitted_by_user_id=?, submitted_at=?, updated_at=?,
       override_reason=? WHERE id=? AND tenant_id=?`,
    ).bind(auth.id, now, now, overrideReason, actualSessionId, tenantId).run();

    if (auth.role === "founder" && !coachTaughtManually) {
      // A Founder can deliver the class when the assigned Coach did not.
      // Keep an explicit, non-payable history row so the class remains
      // completed while the Coach timeline records “Coach không dạy”.
      const session = await env.DB.prepare(
        "SELECT class_id FROM training_sessions WHERE id=? AND tenant_id=? LIMIT 1",
      ).bind(actualSessionId, tenantId).first<{ class_id: string }>();
      if (session) {
        const assignments = await allRows<{
          coach_user_id: string;
          salary_per_session_vnd: number;
        }>(env.DB.prepare(
          `SELECT coach_user_id, salary_per_session_vnd FROM class_coaches
           WHERE tenant_id=? AND class_id=? AND is_active=1`,
        ).bind(tenantId, session.class_id));
        const existing = await allRows<{
          id: string;
          coach_user_id: string;
          checkin_selfie_object_key: string;
        }>(env.DB.prepare(
          `SELECT id, coach_user_id, checkin_selfie_object_key FROM coach_checkins
           WHERE tenant_id=? AND session_id=?`,
        ).bind(tenantId, actualSessionId));
        const existingByCoach = new Map(existing.map((row) => [row.coach_user_id, row]));
        const substitutionStatements = assignments.flatMap((assignment) => {
          const current = existingByCoach.get(assignment.coach_user_id);
          if (current?.checkin_selfie_object_key) return [];
          if (current) {
            return [env.DB.prepare(
              `UPDATE coach_checkins SET checkin_selfie_object_key='', checkout_selfie_object_key='',
               salary_per_session_vnd_snapshot=?, checked_in_at=?, checked_out_at=?, duration_seconds=0,
               approval_status='approved', reviewed_by_user_id=?, reviewed_at=?, review_note=?
               WHERE id=? AND tenant_id=?`,
            ).bind(assignment.salary_per_session_vnd, now, now, auth.id, now,
              FOUNDER_SUBSTITUTED_COACH_REVIEW_NOTE, current.id, tenantId)];
          }
          return [env.DB.prepare(
            `INSERT INTO coach_checkins
             (id, tenant_id, session_id, coach_user_id, checkin_selfie_object_key,
              checkout_selfie_object_key, salary_per_session_vnd_snapshot, checked_in_at,
              checked_out_at, duration_seconds, approval_status, reviewed_by_user_id,
              reviewed_at, review_note)
             VALUES (?, ?, ?, ?, '', '', ?, ?, ?, 0, 'approved', ?, ?, ?)`,
          ).bind(newId(), tenantId, actualSessionId, assignment.coach_user_id,
            assignment.salary_per_session_vnd, now, now, auth.id, now,
            FOUNDER_SUBSTITUTED_COACH_REVIEW_NOTE)];
        });
        for (let offset = 0; offset < substitutionStatements.length; offset += 100) {
          await env.DB.batch(substitutionStatements.slice(offset, offset + 100));
        }
      }
    }

    const incoming = Array.isArray(body.records) ? body.records : [];
    const statusText: Record<string, string> = {
      present: "Có mặt",
      late: "Đi trễ",
      absent: "Vắng mặt",
      excused: "Vắng có phép",
    };
    const notificationStatements = incoming.map((item) => {
      const record = item as JsonObject;
      const traineeId = requireText(record.traineeUserId, "attendance.traineeUserId", 64);
      const status = requireText(record.status, "attendance.status", 20);
      return env.DB.prepare(
        `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
         VALUES (?, ?, ?, 'attendance_updated', 'Điểm danh đã được cập nhật', ?, ?, ?)`,
      ).bind(newId(), tenantId, traineeId,
        statusText[status] ?? status, actualSessionId, now);
    });
    if (notificationStatements.length > 0) {
      await env.DB.batch(notificationStatements);
    }
    // Attendance submission changes cycle progress immediately.  Recompute
    // the invoice, second-lesson warning and next-cycle reminder before the
    // client refreshes its snapshot instead of waiting for the hourly cron.
    await runTenantMaintenance(env, tenantId);
  }
  await audit(env, tenantId, auth.id,
    submit ? "attendance.submitted" : "attendance.draft_saved",
    "training_session", actualSessionId,
    {
      submit,
      coachTaughtManually: auth.role === "founder" && body.coachTaughtManually === true,
      overrideReason: auth.role === "founder" ? optionalText(body.overrideReason, "overrideReason", 500) : "",
    });
  return json(result);
}

async function uploadObjectForOwner(env: Env, tenantId: string, ownerId: string, uploadId: string, purpose: string): Promise<string> {
  const row = await env.DB.prepare(
    "SELECT object_key FROM uploads WHERE id=? AND tenant_id=? AND owner_user_id=? AND purpose=?",
  ).bind(uploadId, tenantId, ownerId, purpose).first<{ object_key: string }>();
  if (!row) throw new ApiError(400, "invalid_upload", "Ảnh upload không hợp lệ cho thao tác này.");
  return row.object_key;
}

export async function checkIn(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "coach");
  const tenantId = requireTenant(auth);
  await autoCloseStaleCheckIns(env, tenantId, auth.id);
  const body = await readJson<JsonObject>(request);
  const sessionId = requireText(body.sessionId, "sessionId", 64);
  const existingSession = await env.DB.prepare(
    "SELECT id, class_id, session_date FROM training_sessions WHERE id=? AND tenant_id=? LIMIT 1",
  ).bind(sessionId, tenantId).first<{ id: string; class_id: string; session_date: string }>();
  let classId = existingSession?.class_id ?? "";
  let sessionDate = existingSession?.session_date ?? "";
  if (!existingSession) {
    classId = requireText(body.classId, "classId", 64);
    await assertTenantEntity(env, "classes", classId, tenantId);
    sessionDate = requireText(body.sessionDate, "sessionDate", 10);
    const now = nowIso();
    await env.DB.prepare(
      `INSERT INTO training_sessions (id, tenant_id, class_id, session_date, status,
       created_at, updated_at) VALUES (?, ?, ?, ?, 'draft', ?, ?)`,
    ).bind(sessionId, tenantId, classId, sessionDate, now, now).run();
  }
  const classRow = await env.DB.prepare(
    "SELECT start_time_minutes, end_time_minutes FROM classes WHERE id=? AND tenant_id=? LIMIT 1",
  ).bind(classId, tenantId).first<{ start_time_minutes: number; end_time_minutes: number }>();
  if (!classRow) throw new ApiError(404, "not_found", "KhÃ´ng tÃ¬m tháº¥y lá»›p.");
  const [year, month, day] = sessionDate.split("-").map(Number);
  const scheduledStart = new Date(Date.UTC(year!, month! - 1, day!, 0, 0, 0) + classRow.start_time_minutes * 60_000 - 7 * 60 * 60_000);
  const scheduledEnd = new Date(Date.UTC(year!, month! - 1, day!, 0, 0, 0) + classRow.end_time_minutes * 60_000 - 7 * 60 * 60_000);
  const nowMs = Date.now();
  if (nowMs < scheduledStart.getTime() - CHECKIN_OPEN_LEAD_MINUTES * 60_000) {
    throw new ApiError(403, "checkin_not_open",
      `Check-in chỉ mở ${CHECKIN_OPEN_LEAD_MINUTES} phút trước giờ học.`);
  }
  if (nowMs >= scheduledEnd.getTime() + CHECKIN_LOCK_AFTER_END_MINUTES * 60_000) {
    throw new ApiError(403, "checkin_locked",
      "Đã quá 2 giờ sau khi lớp kết thúc. Coach đã được ghi nhận vắng check-in và ca đã bị khóa.");
  }
  const autoAbsent = await env.DB.prepare(
    `SELECT id FROM coach_checkins
     WHERE tenant_id=? AND session_id=? AND coach_user_id=? AND review_note=? LIMIT 1`,
  ).bind(tenantId, sessionId, auth.id, AUTO_ABSENT_REVIEW_NOTE).first();
  if (autoAbsent) {
    throw new ApiError(403, "checkin_locked",
      "Coach đã được ghi nhận vắng check-in và ca đã bị khóa.");
  }
  const assigned = await env.DB.prepare(
    `SELECT cc.salary_per_session_vnd FROM class_coaches cc JOIN training_sessions ts ON ts.class_id=cc.class_id
     WHERE ts.id=? AND cc.tenant_id=? AND cc.coach_user_id=? AND cc.is_active=1 LIMIT 1`,
  ).bind(sessionId, tenantId, auth.id).first<{ salary_per_session_vnd: number }>();
  if (!assigned) throw new ApiError(403, "not_assigned", "Coach chưa được phân công lớp này.");
  const objectKey = await uploadObjectForOwner(env, tenantId, auth.id,
    requireText(body.uploadId, "uploadId", 64), "checkin_selfie");
  // A rejected check-in must be reusable.  Do not rely on the composite
  // ON CONFLICT target here: older D1 migrations and imported databases can
  // have a different unique-index shape.  Resolve the existing row by its
  // tenant/session/coach key and update it explicitly when present.
  const existingCheckIn = await env.DB.prepare(
    "SELECT id FROM coach_checkins WHERE tenant_id=? AND session_id=? AND coach_user_id=? LIMIT 1",
  ).bind(tenantId, sessionId, auth.id).first<{ id: string }>();
  const id = existingCheckIn?.id ?? newId();
  const now = nowIso();
  const checkInMutation = existingCheckIn
    ? env.DB.prepare(
      `UPDATE coach_checkins SET checkin_selfie_object_key=?, checkout_selfie_object_key='',
       checked_in_at=?, checked_out_at=NULL, duration_seconds=0, approval_status='pending', reviewed_by_user_id=NULL,
       reviewed_at=NULL, review_note='', salary_per_session_vnd_snapshot=?
       WHERE id=? AND tenant_id=? AND session_id=? AND coach_user_id=?`,
    ).bind(objectKey, now, assigned.salary_per_session_vnd, id, tenantId, sessionId, auth.id)
    : env.DB.prepare(
      `INSERT INTO coach_checkins (id, tenant_id, session_id, coach_user_id, checkin_selfie_object_key,
       salary_per_session_vnd_snapshot, checked_in_at, approval_status) VALUES (?, ?, ?, ?, ?, ?, ?, 'pending')`,
    ).bind(id, tenantId, sessionId, auth.id, objectKey, assigned.salary_per_session_vnd, now);
  await env.DB.batch([
    // A rejected check-in may have left the session submitted after checkout.
    // Re-opening it lets the Coach submit attendance again on the retry.
    env.DB.prepare(
      `UPDATE training_sessions SET status='draft', submitted_by_user_id=NULL,
       submitted_at=NULL, updated_at=? WHERE id=? AND tenant_id=? AND status='submitted'`,
    ).bind(now, sessionId, tenantId),
    checkInMutation,
  ]);
  const saved = await env.DB.prepare(
    "SELECT id FROM coach_checkins WHERE tenant_id=? AND session_id=? AND coach_user_id=? LIMIT 1",
  ).bind(tenantId, sessionId, auth.id).first<{ id: string }>();
  const savedId = saved?.id ?? id;
  await env.DB.prepare(
    `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     SELECT ?, ?, id, 'coach_checkin', 'Chờ duyệt check-in', 'Huấn luyện viên đã gửi ảnh check-in.', ?, ?
     FROM users WHERE tenant_id=? AND role='founder' AND is_active=1`,
  ).bind(newId(), tenantId, savedId, now, tenantId).run();
  await audit(env, tenantId, auth.id, "coach.checkin_submitted", "coach_checkin", savedId,
    { sessionId, checkedInAt: now });
  return json({ id: savedId, status: "pending", checkedInAt: now }, 201);
}

export async function checkOut(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "coach");
  const tenantId = requireTenant(auth);
  await autoCloseStaleCheckIns(env, tenantId, auth.id);
  const body = await readJson<JsonObject>(request);
  const sessionId = requireText(body.sessionId, "sessionId", 64);
  const objectKey = await uploadObjectForOwner(env, tenantId, auth.id,
    requireText(body.uploadId, "uploadId", 64), "checkout_selfie");
  const now = nowIso();
  const openCheckIn = await env.DB.prepare(
    `SELECT id, checked_in_at, checked_out_at, checkout_selfie_object_key, duration_seconds
     FROM coach_checkins
     WHERE tenant_id=? AND session_id=? AND coach_user_id=?
       AND (checked_out_at IS NULL OR checkout_selfie_object_key='') LIMIT 1`,
  ).bind(tenantId, sessionId, auth.id).first<{
    id: string;
    checked_in_at: string;
    checked_out_at: string | null;
    checkout_selfie_object_key: string;
    duration_seconds: number;
  }>();
  if (!openCheckIn) throw new ApiError(409, "not_checked_in", "Không có check-in đang mở.");
  const checkedInMs = Date.parse(openCheckIn.checked_in_at);
  const checkedOutMs = Date.parse(now);
  const safetyClosed = Boolean(openCheckIn.checked_out_at)
    && !openCheckIn.checkout_selfie_object_key;
  const durationSeconds = safetyClosed
    ? Math.max(1, Number(openCheckIn.duration_seconds ?? 0))
    : Number.isFinite(checkedInMs) && Number.isFinite(checkedOutMs)
      ? Math.min(MAX_OPEN_CHECKIN_SECONDS,
        Math.max(0, Math.floor((checkedOutMs - checkedInMs) / 1000)))
      : 0;
  const result = await env.DB.prepare(
    `UPDATE coach_checkins SET checkout_selfie_object_key=?, checked_out_at=?, duration_seconds=?
     WHERE tenant_id=? AND session_id=? AND coach_user_id=?
       AND (checked_out_at IS NULL OR checkout_selfie_object_key='')`,
  ).bind(objectKey, now, durationSeconds, tenantId, sessionId, auth.id).run();
  if (!result.meta.changes) throw new ApiError(409, "not_checked_in", "Không có check-in đang mở.");
  await env.DB.prepare(
    `UPDATE training_sessions SET status='submitted', submitted_by_user_id=?, submitted_at=?, updated_at=?
     WHERE id=? AND tenant_id=? AND status='draft'`,
  ).bind(auth.id, now, now, sessionId, tenantId).run();
  await env.DB.prepare(
    `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     SELECT ?, ?, id, 'coach_checkin', 'Chờ xác nhận check-out',
       'Huấn luyện viên đã gửi đủ ảnh check-in và check-out. Vui lòng kiểm tra để xác nhận và tính lương.', ?, ?
     FROM users WHERE tenant_id=? AND role='founder' AND is_active=1`,
  ).bind(newId(), tenantId, openCheckIn.id, now, tenantId).run();
  await audit(env, tenantId, auth.id, "coach.checkout_submitted", "coach_checkin", openCheckIn.id,
    { sessionId, checkedOutAt: now, durationSeconds });
  return noContent();
}

export async function reviewCheckIn(request: Request, env: Env, id: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const body = await readJson<JsonObject>(request);
  const status = requireText(body.status, "status", 20);
  if (status !== "approved" && status !== "rejected") throw new ApiError(400, "validation_error", "status không hợp lệ.");
  const checkInRow = await env.DB.prepare(
    "SELECT * FROM coach_checkins WHERE id=? AND tenant_id=? LIMIT 1",
  ).bind(id, tenantId).first<Record<string, unknown>>();
  if (!checkInRow) throw new ApiError(404, "not_found", "Không tìm thấy check-in.");
  if (!checkInRow.checked_out_at || !checkInRow.checkout_selfie_object_key) {
    throw new ApiError(409, "checkout_required",
      "Founder chỉ có thể xác nhận sau khi Coach đã check-out và gửi đủ ảnh check-in, check-out.");
  }
  if (checkInRow.approval_status === "approved" && status !== "approved") {
    throw new ApiError(409, "approved_checkin_locked", "Check-in đã tính lương không thể chuyển sang từ chối.");
  }
  const now = nowIso();
  const statements: D1PreparedStatement[] = [env.DB.prepare(
    `UPDATE coach_checkins SET approval_status=?, reviewed_by_user_id=?, reviewed_at=?, review_note=?
     WHERE id=? AND tenant_id=?`,
  ).bind(status, auth.id, now, optionalText(body.note, "note", 500), id, tenantId)];
  if (status === "approved" && checkInRow.approval_status !== "approved") {
    const period = String(checkInRow.checked_in_at).slice(0, 7);
    const dueDate = salaryDueDateForConfirmation(now);
    statements.push(env.DB.prepare(
      `INSERT INTO coach_salaries (id, tenant_id, coach_user_id, period, amount_vnd, due_date, status, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, 'pending', ?)
       ON CONFLICT(coach_user_id, period) DO UPDATE SET
       amount_vnd=coach_salaries.amount_vnd+excluded.amount_vnd, updated_at=excluded.updated_at
       WHERE coach_salaries.tenant_id=excluded.tenant_id`,
    ).bind(newId(), tenantId, checkInRow.coach_user_id, period,
       Number(checkInRow.salary_per_session_vnd_snapshot ?? 0), dueDate, now));
  }
  if (status === "rejected") {
    // Rejection is a retryable outcome, not a terminal session state.  Reset
    // the submitted marker created by check-out so a new selfie can reopen
    // the roster and the Coach can submit attendance again.
    statements.push(env.DB.prepare(
      `UPDATE training_sessions SET status='draft', submitted_by_user_id=NULL,
       submitted_at=NULL, updated_at=? WHERE id=? AND tenant_id=?`,
    ).bind(now, String(checkInRow.session_id), tenantId));
  }
  await env.DB.batch(statements);
  await env.DB.prepare(
    `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     SELECT ?, ?, coach_user_id, 'coach_checkin', ?, ?, ?, ?
     FROM coach_checkins WHERE id=? AND tenant_id=? LIMIT 1`,
  ).bind(
    newId(), tenantId,
    status === "approved" ? "Check-in đã được xác nhận" : "Check-in bị từ chối",
    status === "approved" ? "Check-in đã được Founder xác nhận và được tính lương." : "Vui lòng chụp lại selfie check-in.",
    id, now, id, tenantId,
  ).run();
  await audit(env, tenantId, auth.id,
    status === "approved" ? "coach.checkin_approved" : "coach.checkin_rejected",
    "coach_checkin", id, { note: optionalText(body.note, "note", 500) });
  return noContent();
}

/**
 * Streams a Coach check-in selfie from private R2 after checking the
 * check-in's tenant and caller. Cloud snapshots intentionally contain only
 * the R2 object key, so the Android client uses this check-in-scoped endpoint
 * to materialize a local preview copy for Founder review/history pages.
 */
export async function checkInSelfieImage(
  request: Request,
  env: Env,
  checkInId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (!env.FILES) {
    throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  }

  const row = await env.DB.prepare(
    `SELECT ci.checkin_selfie_object_key, ci.coach_user_id, up.content_type
     FROM coach_checkins ci
     LEFT JOIN uploads up
       ON up.tenant_id=ci.tenant_id AND up.object_key=ci.checkin_selfie_object_key
     WHERE ci.id=? AND ci.tenant_id=? LIMIT 1`,
  ).bind(checkInId, tenantId).first<{
    checkin_selfie_object_key: string;
    coach_user_id: string;
    content_type: string | null;
  }>();
  if (!row || !row.checkin_selfie_object_key) {
    throw new ApiError(404, "not_found", "Không tìm thấy selfie check-in.");
  }
  if (auth.role !== "founder" && auth.id !== row.coach_user_id) {
    throw new ApiError(403, "forbidden", "Bạn không được xem selfie check-in này.");
  }

  const object = await env.FILES.get(row.checkin_selfie_object_key);
  if (!object) {
    throw new ApiError(404, "not_found", "Ảnh selfie check-in không còn tồn tại trên R2.");
  }

  const headers = new Headers({
    "content-type": row.content_type || object.httpMetadata?.contentType || "image/jpeg",
    "cache-control": "private, no-store",
    "content-disposition": `inline; filename="coach-check-in-${checkInId}"`,
    etag: object.httpEtag,
  });
  return new Response(object.body, { headers });
}

/** Streams the Coach checkout selfie from private R2 for Founder review. */
export async function checkOutSelfieImage(
  request: Request,
  env: Env,
  checkInId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (!env.FILES) {
    throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  }

  const row = await env.DB.prepare(
    `SELECT ci.checkout_selfie_object_key, ci.coach_user_id, up.content_type
     FROM coach_checkins ci
     LEFT JOIN uploads up
       ON up.tenant_id=ci.tenant_id AND up.object_key=ci.checkout_selfie_object_key
     WHERE ci.id=? AND ci.tenant_id=? LIMIT 1`,
  ).bind(checkInId, tenantId).first<{
    checkout_selfie_object_key: string;
    coach_user_id: string;
    content_type: string | null;
  }>();
  if (!row || !row.checkout_selfie_object_key) {
    throw new ApiError(404, "not_found", "Không tìm thấy selfie check-out.");
  }
  if (auth.role !== "founder" && auth.id !== row.coach_user_id) {
    throw new ApiError(403, "forbidden", "Bạn không được xem selfie check-out này.");
  }

  const object = await env.FILES.get(row.checkout_selfie_object_key);
  if (!object) {
    throw new ApiError(404, "not_found", "Ảnh selfie check-out không còn tồn tại trên R2.");
  }

  const headers = new Headers({
    "content-type": row.content_type || object.httpMetadata?.contentType || "image/jpeg",
    "cache-control": "private, no-store",
    "content-disposition": `inline; filename="coach-check-out-${checkInId}"`,
    etag: object.httpEtag,
  });
  return new Response(object.body, { headers });
}

/**
 * Streams a private profile avatar after applying the same tenant and shared
 * class visibility rules as the member lists.  The mobile client stores only
 * the returned R2 object key in its online projection and materializes the
 * bytes locally for the existing Avatar control.
 */
export async function profileAvatar(
  request: Request,
  env: Env,
  userId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (!env.FILES) throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  const target = await env.DB.prepare(
    `SELECT u.role, p.photo_object_key
       FROM users u LEFT JOIN profiles p ON p.user_id=u.id AND p.tenant_id=u.tenant_id
      WHERE u.id=? AND u.tenant_id=? LIMIT 1`,
  ).bind(userId, tenantId).first<{ role: string; photo_object_key: string | null }>();
  if (!target || !target.photo_object_key) throw new ApiError(404, "not_found", "Không tìm thấy ảnh hồ sơ.");

  let allowed = auth.role === "founder" || auth.id === userId || target.role === "founder";
  if (!allowed && auth.role === "coach") {
    const shared = await env.DB.prepare(
      `SELECT 1
         FROM class_coaches mine
         JOIN class_coaches other ON other.class_id=mine.class_id AND other.tenant_id=mine.tenant_id
         JOIN users target ON target.id=other.coach_user_id AND target.tenant_id=other.tenant_id
        WHERE mine.tenant_id=? AND mine.coach_user_id=? AND mine.is_active=1
          AND other.is_active=1 AND target.id=?
       UNION SELECT 1
         FROM class_coaches mine
         JOIN class_enrollments other ON other.class_id=mine.class_id AND other.tenant_id=mine.tenant_id
         JOIN users target ON target.id=other.trainee_user_id AND target.tenant_id=other.tenant_id
        WHERE mine.tenant_id=? AND mine.coach_user_id=? AND mine.is_active=1
          AND other.is_active=1 AND target.id=? LIMIT 1`,
    ).bind(tenantId, auth.id, userId, tenantId, auth.id, userId).first();
    allowed = Boolean(shared);
  } else if (!allowed && auth.role === "trainee") {
    const shared = await env.DB.prepare(
      `SELECT 1
         FROM class_enrollments mine
         JOIN class_enrollments other ON other.class_id=mine.class_id AND other.tenant_id=mine.tenant_id
         JOIN users target ON target.id=other.trainee_user_id AND target.tenant_id=other.tenant_id
        WHERE mine.tenant_id=? AND mine.trainee_user_id=? AND mine.is_active=1
          AND other.is_active=1 AND target.id=?
       UNION SELECT 1
         FROM class_enrollments mine
         JOIN class_coaches other ON other.class_id=mine.class_id AND other.tenant_id=mine.tenant_id
         JOIN users target ON target.id=other.coach_user_id AND target.tenant_id=other.tenant_id
        WHERE mine.tenant_id=? AND mine.trainee_user_id=? AND mine.is_active=1
          AND other.is_active=1 AND target.id=? LIMIT 1`,
    ).bind(tenantId, auth.id, userId, tenantId, auth.id, userId).first();
    allowed = Boolean(shared);
  }
  if (!allowed) throw new ApiError(403, "forbidden", "Bạn không được xem ảnh hồ sơ này.");

  const object = await env.FILES.get(target.photo_object_key);
  if (!object) throw new ApiError(404, "not_found", "Ảnh hồ sơ không còn tồn tại trên R2.");
  const headers = new Headers({
    "content-type": object.httpMetadata?.contentType || "image/jpeg",
    "cache-control": "private, no-store",
    "content-disposition": `inline; filename="avatar-${userId}"`,
    etag: object.httpEtag,
  });
  return new Response(object.body, { headers });
}

/** Streams the current tenant's private club logo from R2. */
export async function clubLogo(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (!env.FILES) throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  const club = await env.DB.prepare(
    "SELECT logo_object_key FROM clubs WHERE tenant_id=? LIMIT 1",
  ).bind(tenantId).first<{ logo_object_key: string | null }>();
  if (!club?.logo_object_key) throw new ApiError(404, "not_found", "Đội chưa có logo.");
  const object = await env.FILES.get(club.logo_object_key);
  if (!object) throw new ApiError(404, "not_found", "Logo đội không còn tồn tại trên R2.");
  const headers = new Headers({
    "content-type": object.httpMetadata?.contentType || "image/jpeg",
    "cache-control": "private, no-store",
    "content-disposition": "inline; filename=club-logo",
    etag: object.httpEtag,
  });
  return new Response(object.body, { headers });
}

export async function tuition(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (request.method === "GET") {
    const query = auth.role === "trainee"
      ? env.DB.prepare("SELECT * FROM tuition_invoices WHERE tenant_id=? AND trainee_user_id=? ORDER BY created_at DESC")
        .bind(tenantId, auth.id)
      : env.DB.prepare("SELECT * FROM tuition_invoices WHERE tenant_id=? ORDER BY created_at DESC").bind(tenantId);
    if (auth.role === "coach") throw new ApiError(403, "forbidden", "Coach không được xem học phí.");
    return json({ invoices: await allRows(query) });
  }
  requireRole(auth, "founder");
  const body = await readJson<JsonObject>(request);
  const enrollmentId = requireText(body.enrollmentId, "enrollmentId", 64);
  const enrollment = await env.DB.prepare(
    `SELECT ce.*, u.is_tuition_supported, p.full_name FROM class_enrollments ce
     JOIN users u ON u.id=ce.trainee_user_id JOIN profiles p ON p.user_id=u.id
     WHERE ce.id=? AND ce.tenant_id=? LIMIT 1`,
  ).bind(enrollmentId, tenantId).first<Record<string, unknown>>();
  if (!enrollment) throw new ApiError(404, "not_found", "Không tìm thấy ghi danh.");
  const cycleCount = requireInteger(body.cycleCount ?? 1, "cycleCount", 1, 24);
  const cycleNumber = requireInteger(body.cycleNumber, "cycleNumber", 1, 10000);
  const fee = Number(enrollment.cycle_fee_vnd ?? 0);
  const waived = enrollment.is_tuition_supported === 1;
  const amount = waived ? 0 : fee * cycleCount;
  const id = newId();
  const now = nowIso();
  await env.DB.prepare(
    `INSERT INTO tuition_invoices (id, tenant_id, enrollment_id, trainee_user_id, class_id, cycle_number,
     cycle_count, cycle_fee_vnd, amount_vnd, planned_session_count, due_date, status, payment_content, created_at, updated_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(id, tenantId, enrollmentId, enrollment.trainee_user_id, enrollment.class_id, cycleNumber, cycleCount,
    fee, amount, Number(body.plannedSessionCount ?? 0), requireText(body.dueDate, "dueDate", 10),
    waived ? "waived" : "pending", `${String(enrollment.full_name)} dong hoc phi`, now, now).run();
  return json({ id, amountVnd: amount, status: waived ? "waived" : "pending" }, 201);
}

export async function submitProof(request: Request, env: Env, invoiceId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "trainee");
  const tenantId = requireTenant(auth);
  const invoice = await env.DB.prepare(
    "SELECT * FROM tuition_invoices WHERE id=? AND tenant_id=? AND trainee_user_id=?",
  ).bind(invoiceId, tenantId, auth.id).first<Record<string, unknown>>();
  if (!invoice) throw new ApiError(404, "not_found", "Không tìm thấy học phí.");
  const body = await readJson<JsonObject>(request);
  const objectKey = await uploadObjectForOwner(env, tenantId, auth.id,
    requireText(body.uploadId, "uploadId", 64), "payment_proof");
  const id = newId();
  const now = nowIso();
  await env.DB.batch([
    env.DB.prepare(
      `INSERT INTO payment_proofs (id, tenant_id, invoice_id, image_object_key, note, submitted_at, review_status)
       VALUES (?, ?, ?, ?, ?, ?, 'pending')`,
    ).bind(id, tenantId, invoiceId, objectKey, optionalText(body.note, "note", 500), now),
    env.DB.prepare("UPDATE tuition_invoices SET status='proof_submitted', updated_at=? WHERE id=? AND tenant_id=?")
      .bind(now, invoiceId, tenantId),
    env.DB.prepare(
      `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
       SELECT ?, ?, id, 'tuition_proof', 'Có bill học phí mới', 'Học viên đã tải bill học phí lên để Founder kiểm tra.', ?, ?
       FROM users WHERE tenant_id=? AND role='founder' AND is_active=1`,
    ).bind(newId(), tenantId, invoiceId, now, tenantId),
  ]);
  await audit(env, tenantId, auth.id, "tuition.proof_submitted", "payment_proof", id, { invoiceId });
  return json({ id }, 201);
}

export async function reviewProof(request: Request, env: Env, proofId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const body = await readJson<JsonObject>(request);
  const accepted = body.accepted === true;
  const proof = await env.DB.prepare("SELECT * FROM payment_proofs WHERE id=? AND tenant_id=?")
    .bind(proofId, tenantId).first<Record<string, unknown>>();
  if (!proof) throw new ApiError(404, "not_found", "Không tìm thấy bill.");
  const invoice = await env.DB.prepare(
    `SELECT ti.*, c.team_name, cl.name AS class_name, p.full_name AS trainee_name,
            fp.full_name AS founder_name
     FROM tuition_invoices ti
     JOIN clubs c ON c.tenant_id=ti.tenant_id
     JOIN classes cl ON cl.id=ti.class_id AND cl.tenant_id=ti.tenant_id
     JOIN profiles p ON p.user_id=ti.trainee_user_id AND p.tenant_id=ti.tenant_id
     JOIN profiles fp ON fp.user_id=? AND fp.tenant_id=ti.tenant_id
     WHERE ti.id=? AND ti.tenant_id=? LIMIT 1`,
  ).bind(auth.id, proof.invoice_id, tenantId).first<Record<string, unknown>>();
  if (!invoice) throw new ApiError(404, "not_found", "Không tìm thấy khoản học phí.");
  const now = nowIso();
  const statements: D1PreparedStatement[] = [
    env.DB.prepare(
      "UPDATE payment_proofs SET review_status=?, reviewed_by_user_id=?, reviewed_at=? WHERE id=? AND tenant_id=?",
    ).bind(accepted ? "accepted" : "rejected", auth.id, now, proofId, tenantId),
    env.DB.prepare("UPDATE tuition_invoices SET status=?, updated_at=? WHERE id=? AND tenant_id=?")
      .bind(accepted ? "paid" : "rejected", now, proof.invoice_id, tenantId),
  ];
  if (accepted) {
    const existingReceipt = await env.DB.prepare(
      "SELECT id FROM receipts WHERE invoice_id=? AND tenant_id=? LIMIT 1",
    ).bind(proof.invoice_id, tenantId).first<{ id: string }>();
    if (!existingReceipt) {
      const receiptId = newId();
      const receiptNumber = `CFC-${now.slice(0, 10).replaceAll("-", "")}-${String(proof.invoice_id).slice(0, 6).toUpperCase()}`;
      statements.push(env.DB.prepare(
        `INSERT INTO receipts (id, tenant_id, invoice_id, receipt_number, team_name_snapshot,
         trainee_name_snapshot, class_name_snapshot, cycle_snapshot, amount_vnd_snapshot,
         confirmed_by_name_snapshot, confirmed_at, pdf_object_key)
         VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, '')`,
      ).bind(receiptId, tenantId, proof.invoice_id, receiptNumber, invoice.team_name,
        invoice.trainee_name, invoice.class_name, `Chu kỳ ${invoice.cycle_number}`,
        Number(invoice.amount_vnd ?? 0), invoice.founder_name, now));
    }
  }
  statements.push(env.DB.prepare(
    `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(
    newId(), tenantId, proof.trainee_user_id ?? invoice.trainee_user_id,
    accepted ? "tuition_confirmed" : "tuition_rejected",
    accepted ? "Học phí đã được xác nhận" : "Cần tải lại bill học phí",
    accepted ? "Bill đã được Founder xác nhận. Bạn có thể xuất hóa đơn PDF." : "Bill chưa được chấp nhận. Vui lòng kiểm tra và tải lại.",
    proof.invoice_id, now,
  ));
  await env.DB.batch(statements);
  await audit(env, tenantId, auth.id, accepted ? "tuition.proof_accepted" : "tuition.proof_rejected",
    "payment_proof", proofId, { invoiceId: proof.invoice_id });
  const receipt = accepted
    ? await env.DB.prepare("SELECT * FROM receipts WHERE invoice_id=? AND tenant_id=? LIMIT 1")
      .bind(proof.invoice_id, tenantId).first<Record<string, unknown>>()
    : null;
  return json({
    proofId,
    invoiceId: proof.invoice_id,
    accepted,
    receipt: receipt
      ? {
          id: receipt.id,
          invoiceId: receipt.invoice_id,
          receiptNumber: receipt.receipt_number,
          teamNameSnapshot: receipt.team_name_snapshot,
          traineeNameSnapshot: receipt.trainee_name_snapshot,
          classNameSnapshot: receipt.class_name_snapshot,
          cycleSnapshot: receipt.cycle_snapshot,
          amountVndSnapshot: receipt.amount_vnd_snapshot,
          confirmedByNameSnapshot: receipt.confirmed_by_name_snapshot,
          confirmedAt: receipt.confirmed_at,
          pdfObjectKey: receipt.pdf_object_key,
        }
      : null,
  });
}

/**
 * Streams a payment proof from private R2 after checking the proof's tenant
 * and whether the caller is the Founder of that tenant or the owning Trainee.
 * The mobile client stores only the R2 object key in its cloud projection, so
 * it uses this proof-scoped endpoint to materialize a local preview copy.
 */
export async function paymentProofImage(
  request: Request,
  env: Env,
  proofId: string,
): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (!env.FILES) {
    throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  }

  const row = await env.DB.prepare(
    `SELECT pp.image_object_key, ti.trainee_user_id, up.content_type
     FROM payment_proofs pp
     JOIN tuition_invoices ti
       ON ti.id=pp.invoice_id AND ti.tenant_id=pp.tenant_id
     LEFT JOIN uploads up
       ON up.tenant_id=pp.tenant_id AND up.object_key=pp.image_object_key
     WHERE pp.id=? AND pp.tenant_id=? LIMIT 1`,
  ).bind(proofId, tenantId).first<{
    image_object_key: string;
    trainee_user_id: string;
    content_type: string | null;
  }>();
  if (!row) throw new ApiError(404, "not_found", "Không tìm thấy bill.");
  if (auth.role !== "founder" && auth.id !== row.trainee_user_id) {
    throw new ApiError(403, "forbidden", "Bạn không được xem bill này.");
  }

  const object = await env.FILES.get(row.image_object_key);
  if (!object) throw new ApiError(404, "not_found", "Ảnh bill không còn tồn tại trên R2.");
  const headers = new Headers({
    "content-type": row.content_type || "image/jpeg",
    "cache-control": "private, no-store",
    "content-disposition": `inline; filename="payment-proof-${proofId}"`,
    etag: object.httpEtag,
  });
  return new Response(object.body, { headers });
}

export async function updateInvoiceCycles(request: Request, env: Env, invoiceId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "trainee");
  const tenantId = requireTenant(auth);
  const invoice = await env.DB.prepare(
    `SELECT ti.*, c.tuition_session_count FROM tuition_invoices ti
     JOIN classes c ON c.id=ti.class_id AND c.tenant_id=ti.tenant_id
     WHERE ti.id=? AND ti.tenant_id=? AND ti.trainee_user_id=? LIMIT 1`,
  ).bind(invoiceId, tenantId, auth.id).first<Record<string, unknown>>();
  if (!invoice) throw new ApiError(404, "not_found", "Không tìm thấy khoản học phí.");
  if (invoice.status === "paid" || invoice.status === "proof_submitted") {
    throw new ApiError(409, "invoice_locked", "Khoản học phí đã gửi bill hoặc đã thanh toán.");
  }
  const body = await readJson<JsonObject>(request);
  const cycleCount = requireInteger(body.cycleCount, "cycleCount", 1, 24);
  const cycleFee = Number(invoice.cycle_fee_vnd ?? 0);
  const plannedPerCycle = Math.max(1, Number(invoice.tuition_session_count ?? 1));
  const amount = cycleFee * cycleCount;
  const planned = plannedPerCycle * cycleCount;
  const now = nowIso();
  await env.DB.prepare(
    `UPDATE tuition_invoices SET cycle_count=?, amount_vnd=?, planned_session_count=?, updated_at=?
     WHERE id=? AND tenant_id=? AND trainee_user_id=?`,
  ).bind(cycleCount, amount, planned, now, invoiceId, tenantId, auth.id).run();
  await audit(env, tenantId, auth.id, "tuition.prepaid_cycles_changed", "tuition_invoice", invoiceId,
    { cycleCount, amount });
  return json({ cycleCount, amountVnd: amount, plannedSessionCount: planned });
}

export async function updateSalary(request: Request, env: Env, salaryId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const salary = await env.DB.prepare("SELECT * FROM coach_salaries WHERE id=? AND tenant_id=? LIMIT 1")
    .bind(salaryId, tenantId).first<Record<string, unknown>>();
  if (!salary) throw new ApiError(404, "not_found", "Không tìm thấy kỳ lương.");
  const body = await readJson<JsonObject>(request);
  if (typeof body.isPaid !== "boolean") throw new ApiError(400, "validation_error", "isPaid phải là boolean.");
  if (salary.status === "paid" && body.isPaid === false) {
    throw new ApiError(409, "salary_locked", "Kỳ lương đã thanh toán không thể chuyển lại.");
  }
  const now = nowIso();
  await env.DB.prepare(
    `UPDATE coach_salaries SET status=?, paid_at=?, paid_by_user_id=?, notes=?, updated_at=?
     WHERE id=? AND tenant_id=?`,
  ).bind(body.isPaid ? "paid" : "pending", body.isPaid ? now : null,
    body.isPaid ? auth.id : null, optionalText(body.notes, "notes", 500), now, salaryId, tenantId).run();
  await audit(env, tenantId, auth.id, body.isPaid ? "salary.paid" : "salary.updated", "coach_salary", salaryId);
  return noContent();
}

export async function updateReceiptPdf(request: Request, env: Env, receiptId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const body = await readJson<JsonObject>(request);
  const uploadId = requireText(body.uploadId, "uploadId", 64);
  const objectKey = await uploadObjectForOwner(env, tenantId, auth.id, uploadId, "receipt");
  const result = await env.DB.prepare(
    "UPDATE receipts SET pdf_object_key=? WHERE id=? AND tenant_id=?",
  ).bind(objectKey, receiptId, tenantId).run();
  if (!result.meta.changes) throw new ApiError(404, "not_found", "Không tìm thấy hóa đơn.");
  await audit(env, tenantId, auth.id, "receipt.pdf_uploaded", "receipt", receiptId);
  return noContent();
}

export async function announcement(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  requireRole(auth, "founder");
  const tenantId = requireTenant(auth);
  const body = await readJson<JsonObject>(request);
  const title = requireText(body.title, "title", 180);
  const message = requireText(body.message, "message", 1000);
  const traineeUserId = typeof body.traineeUserId === "string" && body.traineeUserId.trim()
    ? body.traineeUserId.trim()
    : null;
  const recipientRole = body.recipientRole === "coach" ? "coach" : "trainee";
  const recipients = await allRows<{ id: string }>(env.DB.prepare(
    `SELECT id FROM users WHERE tenant_id=? AND role=? AND is_active=1
     AND (? IS NULL OR id=?)`,
  ).bind(tenantId, recipientRole, traineeUserId, traineeUserId));
  if (recipients.length === 0) throw new ApiError(400, "no_recipients", "Không có người nhận phù hợp.");
  const now = nowIso();
  await env.DB.batch(recipients.map((recipient) => env.DB.prepare(
    `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
     VALUES (?, ?, ?, 'announcement', ?, ?, '', ?)`,
  ).bind(newId(), tenantId, recipient.id, title, message, now)));
  await audit(env, tenantId, auth.id, "announcement.sent", "notification",
    traineeUserId ?? (recipientRole === "coach" ? "ALL_COACHES" : "ALL_TRAINEES"));
  return json({ count: recipients.length });
}

export async function notifications(request: Request, env: Env, id?: string): Promise<Response> {
  const auth = await authenticate(request, env);
  if (request.method === "POST") {
    const tenantId = requireTenant(auth);
    const body = await readJson<JsonObject>(request);
    const recipientUserId = requireText(body.recipientUserId, "recipientUserId", 64);
    const recipient = await env.DB.prepare(
      "SELECT id, role FROM users WHERE id=? AND tenant_id=? AND is_active=1 LIMIT 1",
    ).bind(recipientUserId, tenantId).first<{ id: string; role: string }>();
    if (!recipient) throw new ApiError(404, "not_found", "Không tìm thấy người nhận thông báo.");
    if (auth.role === "trainee" && recipientUserId !== auth.id) {
      throw new ApiError(403, "forbidden", "Không được gửi thông báo cho account khác.");
    }
    const id = newId();
    await env.DB.prepare(
      `INSERT INTO notifications (id, tenant_id, recipient_user_id, kind, title, message, related_entity_id, created_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?)`,
    ).bind(id, tenantId, recipientUserId, requireText(body.kind, "kind", 60),
      requireText(body.title, "title", 180), requireText(body.message, "message", 1000),
      optionalText(body.relatedEntityId, "relatedEntityId", 64), nowIso()).run();
    return json({ id }, 201);
  }
  if (id) {
    await env.DB.prepare("UPDATE notifications SET is_read=1 WHERE id=? AND recipient_user_id=?")
      .bind(id, auth.id).run();
    return noContent();
  }
  return json({ notifications: await allRows(env.DB.prepare(
    "SELECT * FROM notifications WHERE recipient_user_id=? ORDER BY created_at DESC LIMIT 500",
  ).bind(auth.id)) });
}

export async function notificationsBulk(
  request: Request,
  env: Env,
  action: "read" | "delete",
): Promise<Response> {
  const auth = await authenticate(request, env);
  if (action === "read") {
    await env.DB.prepare(
      "UPDATE notifications SET is_read=1 WHERE recipient_user_id=?",
    ).bind(auth.id).run();
    return noContent();
  }
  await env.DB.prepare(
    "DELETE FROM notifications WHERE recipient_user_id=?",
  ).bind(auth.id).run();
  return noContent();
}

export async function auditEvent(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  const body = await readJson<JsonObject>(request);
  await audit(env, tenantId, auth.id, requireText(body.action, "action", 100),
    requireText(body.entityType, "entityType", 80), requireText(body.entityId, "entityId", 80),
    optionalText(body.details, "details", 1000));
  return noContent();
}

const ALLOWED_UPLOAD_TYPES = new Set(["image/jpeg", "image/png", "image/webp", "application/pdf"]);
const ALLOWED_PURPOSES = new Set(["avatar", "club_logo", "checkin_selfie", "checkout_selfie", "payment_proof", "receipt"]);

export async function uploads(request: Request, env: Env, id?: string): Promise<Response> {
  const auth = await authenticate(request, env);
  if (!env.FILES) throw new ApiError(503, "storage_unavailable", "R2 chưa được bật cho tài khoản Cloudflare.");
  if (request.method === "GET") {
    if (!id) throw new ApiError(404, "not_found", "Thiếu upload ID.");
    // Scope the lookup in SQL before loading object metadata.  This keeps a
    // guessed upload ID from becoming a cross-tenant oracle and avoids
    // relying on a post-query JavaScript check for tenant isolation.
    const row = auth.role === "admin"
      ? null
      : await env.DB.prepare("SELECT * FROM uploads WHERE id=? AND tenant_id=? LIMIT 1")
        .bind(id, auth.tenantId).first<Record<string, unknown>>();
    if (!row) throw new ApiError(404, "not_found", "Không tìm thấy file.");
    const purpose = String(row.purpose);
    const isOwner = row.owner_user_id === auth.id;
    const canReadSensitive = auth.role === "founder" || isOwner;
    if (["checkin_selfie", "checkout_selfie", "payment_proof"].includes(purpose) && !canReadSensitive) {
      throw new ApiError(403, "forbidden", "Không được xem file này.");
    }
    const object = await env.FILES.get(String(row.object_key));
    if (!object) throw new ApiError(404, "not_found", "File không còn tồn tại.");
    const headers = new Headers({ "content-type": String(row.content_type), "cache-control": "private, no-store" });
    headers.set("content-disposition", `attachment; filename="${id}"`);
    headers.set("etag", object.httpEtag);
    return new Response(object.body, { headers });
  }
  const tenantId = auth.tenantId;
  if (!tenantId || auth.role === "admin") throw new ApiError(403, "forbidden", "Admin không dùng kho media của đội.");
  const purpose = request.headers.get("x-upload-purpose") ?? new URL(request.url).searchParams.get("purpose") ?? "";
  const contentType = request.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase() ?? "";
  if (!ALLOWED_PURPOSES.has(purpose) || !ALLOWED_UPLOAD_TYPES.has(contentType)) {
    throw new ApiError(415, "unsupported_upload", "Loại file hoặc mục đích upload không được hỗ trợ.");
  }
  const rolePurposes: Record<string, string[]> = {
    founder: ["avatar", "club_logo", "receipt"],
    coach: ["avatar", "checkin_selfie", "checkout_selfie"],
    trainee: ["avatar", "payment_proof"],
  };
  if (!(rolePurposes[auth.role] ?? []).includes(purpose)) {
    throw new ApiError(403, "forbidden_upload_purpose", "Role hiện tại không được upload mục đích này.");
  }
  const size = Number(request.headers.get("content-length") ?? "0");
  const max = Number.parseInt(env.MAX_UPLOAD_BYTES, 10) || 10_485_760;
  if (!request.body || !Number.isInteger(size) || size <= 0 || size > max) {
    throw new ApiError(413, "invalid_upload_size", `File phải có kích thước từ 1 đến ${max} bytes.`);
  }
  const bytes = await request.arrayBuffer();
  if (bytes.byteLength !== size || bytes.byteLength > max) throw new ApiError(413, "invalid_upload_size", "Kích thước file không hợp lệ.");
  const hash = Array.from(new Uint8Array(await crypto.subtle.digest("SHA-256", bytes)))
    .map((value) => value.toString(16).padStart(2, "0")).join("");
  const uploadId = newId();
  const objectKey = `${tenantId ?? "system"}/${auth.id}/${purpose}/${uploadId}`;
  await env.FILES.put(objectKey, bytes, {
    httpMetadata: { contentType }, customMetadata: { ownerUserId: auth.id, purpose, sha256: hash },
  });
  await env.DB.prepare(
    `INSERT INTO uploads (id, tenant_id, owner_user_id, purpose, object_key, content_type, byte_size, sha256, created_at)
     VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)`,
  ).bind(uploadId, tenantId, auth.id, purpose, objectKey, contentType, bytes.byteLength, hash, nowIso()).run();
  return json({ id: uploadId, purpose, contentType, byteSize: bytes.byteLength, downloadUrl: `/v1/uploads/${uploadId}` }, 201);
}

export async function snapshot(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  if (request.method === "GET") {
    const rawAfter = new URL(request.url).searchParams.get("afterSyncVersion");
    const afterSyncVersion = rawAfter && /^\d+$/u.test(rawAfter) ? Number(rawAfter) : undefined;
    return json(await getSnapshot(env, auth, afterSyncVersion));
  }
  const idempotencyKey = request.headers.get("idempotency-key")?.trim() ?? "";
  if (idempotencyKey.length > 120) throw new ApiError(400, "validation_error", "Idempotency-Key quá dài.");
  if (idempotencyKey) {
    const cached = await env.DB.prepare(
      "SELECT response_status, response_json FROM idempotency_keys WHERE user_id=? AND idempotency_key=? AND expires_at>?",
    ).bind(auth.id, idempotencyKey, nowIso()).first<{ response_status: number; response_json: string }>();
    if (cached) return json(JSON.parse(cached.response_json) as unknown, cached.response_status);
  }
  const body = await readJson<JsonObject>(request, 5_242_880);
  const changes = body.changes && typeof body.changes === "object" && !Array.isArray(body.changes)
    ? body.changes as JsonObject
    : body;
  const result = await applySnapshot(env, auth, changes);
  const deviceId = optionalText(body.deviceId, "deviceId", 120);
  const clientMutationId = optionalText(body.clientMutationId, "clientMutationId", 120);
  const statements: D1PreparedStatement[] = [];
  if (deviceId) {
    statements.push(env.DB.prepare(
      `INSERT INTO sync_cursors (user_id, device_id, tenant_id, last_client_mutation_id, last_sync_at)
       VALUES (?, ?, ?, ?, ?) ON CONFLICT(user_id, device_id) DO UPDATE SET
       tenant_id=excluded.tenant_id, last_client_mutation_id=excluded.last_client_mutation_id,
       last_sync_at=excluded.last_sync_at`,
    ).bind(auth.id, deviceId, auth.tenantId, clientMutationId, nowIso()));
  }
  if (idempotencyKey) {
    statements.push(env.DB.prepare(
      `INSERT OR REPLACE INTO idempotency_keys
       (user_id, idempotency_key, tenant_id, response_status, response_json, created_at, expires_at)
       VALUES (?, ?, ?, 200, ?, ?, ?)`,
    ).bind(auth.id, idempotencyKey, auth.tenantId, JSON.stringify(result), nowIso(),
      new Date(Date.now() + 24 * 60 * 60_000).toISOString()));
  }
  if (statements.length) await env.DB.batch(statements);
  return json(result);
}
