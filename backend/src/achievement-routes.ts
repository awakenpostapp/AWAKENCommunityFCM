import {
  authenticate,
  requireTenant,
} from "./auth";
import {
  AchievementBadgeRow,
  AchievementCategory,
  AchievementStatus,
  AuthUser,
  TraineeAchievementRow,
  isAchievementCategory,
  isAchievementStatus,
  newId,
  nowIso,
} from "./domain";
import {
  ApiError,
  json,
  noContent,
  optionalText,
  readJson,
  requireDateKey,
  requireText,
} from "./http";
import { allRows, audit } from "./repository";
import { isFounderLike } from "./authorization";
import {
  assertCanCreateAchievement,
  assertCanRemoveAchievement,
  assertCanReviewAchievement,
} from "./route-authorization";

type JsonObject = Record<string, unknown>;

type AchievementBadgeWithFlags = AchievementBadgeRow & {
  is_active: number | boolean;
};

type AchievementQueryRow = TraineeAchievementRow & {
  badge_key?: string;
  badge_name?: string;
  badge_asset_key?: string;
  badge_display_size?: string;
  badge_points?: number;
  trainee_name?: string;
  coach_name?: string;
  class_name?: string;
};

const DAY_MS = 24 * 60 * 60 * 1000;

function asActiveFlag(value: unknown): boolean {
  return value === true || Number(value ?? 0) === 1;
}

function achievementBadgeJson(row: AchievementBadgeWithFlags): Record<string, unknown> {
  return {
    id: row.id,
    key: row.key,
    name: row.name,
    category: row.category,
    assetKey: row.asset_key,
    displaySize: row.display_size,
    points: Number(row.points),
    sortOrder: Number(row.sort_order ?? 0),
    isActive: asActiveFlag(row.is_active),
  };
}

function achievementJson(row: AchievementQueryRow): Record<string, unknown> {
  return {
    id: row.id,
    tenantId: row.tenant_id,
    traineeUserId: row.trainee_user_id,
    traineeName: row.trainee_name ?? "Cầu thủ học viên",
    badgeId: row.badge_id,
    badgeKey: row.badge_key ?? "",
    badgeName: row.badge_name ?? "Biểu trưng thành tích",
    badgeAssetKey: row.badge_asset_key ?? "",
    badgeDisplaySize: row.badge_display_size ?? "medium",
    classId: row.class_id ?? "",
    className: row.class_name ?? "",
    category: row.category,
    title: row.title ?? "",
    eventName: row.event_name ?? "",
    reason: row.reason ?? "",
    awardedForDate: `${row.awarded_for_date}T00:00:00.000Z`,
    points: Number(row.points_snapshot ?? row.badge_points ?? 0),
    status: row.status,
    createdByUserId: row.created_by_user_id ?? "",
    coachName: row.coach_name ?? "",
    reviewedByUserId: row.reviewed_by_user_id ?? "",
    reviewedAt: row.reviewed_at,
    reviewNote: row.review_note ?? "",
    visibleUntil: row.visible_until,
    removedAt: row.removed_at,
    createdAt: row.created_at,
    updatedAt: row.updated_at,
  };
}

function parseCategory(value: unknown, required = true): AchievementCategory | "" {
  if (value === undefined || value === null || value === "") {
    if (!required) return "";
    throw new ApiError(400, "validation_error", "category là bắt buộc.");
  }
  const category = requireText(value, "category", 40);
  if (!isAchievementCategory(category)) {
    throw new ApiError(400, "validation_error", "category không hợp lệ.");
  }
  return category;
}

function parseStatus(value: unknown): AchievementStatus | "" {
  if (value === undefined || value === null || value === "") return "";
  const status = requireText(value, "status", 20).toLowerCase();
  if (!isAchievementStatus(status)) {
    throw new ApiError(400, "validation_error", "status thành tích không hợp lệ.");
  }
  return status;
}

function replayResponse(status: number, responseJson: string): Response {
  return new Response(responseJson, {
    status,
    headers: {
      "content-type": "application/json; charset=utf-8",
      "cache-control": "no-store",
    },
  });
}

async function reserveIdempotency(
  request: Request,
  env: Env,
  auth: AuthUser,
  tenantId: string,
): Promise<{ key: string; reserved: boolean; replay: Response | null }> {
  const key = (request.headers.get("idempotency-key") ?? "").trim();
  if (key.length > 120) throw new ApiError(400, "validation_error", "Idempotency-Key quá dài.");
  if (!key) return { key: "", reserved: false, replay: null };

  const now = nowIso();
  await env.DB.prepare("DELETE FROM idempotency_keys WHERE expires_at <= ?").bind(now).run();
  const existing = await env.DB.prepare(
    `SELECT response_status, response_json
       FROM idempotency_keys
      WHERE user_id=? AND idempotency_key=? AND expires_at>?
      LIMIT 1`,
  ).bind(auth.id, key, now).first<{ response_status: number; response_json: string }>();
  if (existing && existing.response_json) {
    return { key, reserved: false, replay: replayResponse(Number(existing.response_status), existing.response_json) };
  }
  if (existing) {
    throw new ApiError(409, "idempotency_in_progress", "Yêu cầu thành tích trước đó đang được xử lý.");
  }

  const inserted = await env.DB.prepare(
    `INSERT OR IGNORE INTO idempotency_keys
      (user_id, idempotency_key, tenant_id, response_status, response_json, created_at, expires_at)
     VALUES (?, ?, ?, 425, '', ?, ?)` ,
  ).bind(auth.id, key, tenantId, now, new Date(Date.now() + DAY_MS).toISOString()).run();
  if (Number(inserted.meta.changes ?? 0) !== 1) {
    const raced = await env.DB.prepare(
      `SELECT response_status, response_json
         FROM idempotency_keys
        WHERE user_id=? AND idempotency_key=? AND expires_at>?
        LIMIT 1`,
    ).bind(auth.id, key, nowIso()).first<{ response_status: number; response_json: string }>();
    if (raced?.response_json) return { key, reserved: false, replay: replayResponse(Number(raced.response_status), raced.response_json) };
    throw new ApiError(409, "idempotency_in_progress", "Yêu cầu thành tích trước đó đang được xử lý.");
  }
  return { key, reserved: true, replay: null };
}

async function finishIdempotency(
  env: Env,
  auth: AuthUser,
  key: string,
  status: number,
  body: Record<string, unknown>,
): Promise<void> {
  if (!key) return;
  await env.DB.prepare(
    `UPDATE idempotency_keys
        SET response_status=?, response_json=?
      WHERE user_id=? AND idempotency_key=? AND response_status=425`,
  ).bind(status, JSON.stringify(body), auth.id, key).run();
}

async function cancelIdempotency(env: Env, auth: AuthUser, key: string): Promise<void> {
  if (!key) return;
  await env.DB.prepare(
    "DELETE FROM idempotency_keys WHERE user_id=? AND idempotency_key=? AND response_status=425",
  ).bind(auth.id, key).run().catch(() => undefined);
}

async function activeFounders(env: Env, tenantId: string): Promise<Array<{ id: string }>> {
  return allRows<{ id: string }>(env.DB.prepare(
    `SELECT id FROM users
      WHERE tenant_id=? AND role IN ('founder', 'co_founder') AND is_active=1`,
  ).bind(tenantId));
}

function notificationStatement(
  env: Env,
  tenantId: string,
  recipientUserId: string,
  kind: string,
  title: string,
  message: string,
  relatedEntityId: string,
  createdAt: string,
): D1PreparedStatement {
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

async function notifyAchievementSubmitted(
  env: Env,
  tenantId: string,
  row: AchievementQueryRow,
): Promise<void> {
  const founders = await activeFounders(env, tenantId);
  if (!founders.length) return;
  const now = nowIso();
  await env.DB.batch(founders.map((founder) => notificationStatement(
    env,
    tenantId,
    founder.id,
    "AchievementSubmitted",
    "Có đề xuất thành tích cần duyệt",
    `${row.coach_name ?? "Huấn luyện viên"} đã đề xuất ${row.badge_name ?? "một thành tích"} cho ${row.trainee_name ?? "Cầu thủ học viên"}.`,
    row.id,
    now,
  )));
}

async function notifyAchievementApproved(
  env: Env,
  tenantId: string,
  row: AchievementQueryRow,
  approved: boolean,
): Promise<void> {
  const now = nowIso();
  const statements: D1PreparedStatement[] = [notificationStatement(
    env,
    tenantId,
    row.trainee_user_id,
    approved ? "AchievementApproved" : "AchievementRejected",
    approved ? "Thành tích đã được xác nhận" : "Đề xuất thành tích chưa được chấp nhận",
    approved
      ? `Bạn đã nhận ${row.badge_name ?? "một biểu trưng thành tích"} (${Number(row.points_snapshot ?? 0)} điểm).`
      : `Đề xuất ${row.badge_name ?? "thành tích"} của bạn chưa được Founder chấp nhận.`,
    `${row.id}:${approved ? "approved" : "rejected"}`,
    now,
  )];
  if (row.created_by_user_id && row.created_by_user_id !== row.trainee_user_id) {
    statements.push(notificationStatement(
      env,
      tenantId,
      row.created_by_user_id,
      approved ? "AchievementApproved" : "AchievementRejected",
      approved ? "Đề xuất thành tích đã được duyệt" : "Đề xuất thành tích bị từ chối",
      approved
        ? `${row.badge_name ?? "Thành tích"} cho ${row.trainee_name ?? "Cầu thủ học viên"} đã được Founder xác nhận.`
        : `Founder đã từ chối ${row.badge_name ?? "thành tích"} cho ${row.trainee_name ?? "Cầu thủ học viên"}.`,
      `${row.id}:${approved ? "approved" : "rejected"}:creator`,
      now,
    ));
  }
  await env.DB.batch(statements);
}

async function getAchievementRow(env: Env, tenantId: string, id: string): Promise<AchievementQueryRow> {
  const row = await env.DB.prepare(
    `SELECT ta.*, b.key AS badge_key, b.name AS badge_name, b.asset_key AS badge_asset_key,
            b.display_size AS badge_display_size, b.points AS badge_points,
            tp.full_name AS trainee_name, cp.full_name AS coach_name, c.name AS class_name
       FROM trainee_achievements ta
       JOIN achievement_badges b ON b.id=ta.badge_id
       LEFT JOIN profiles tp ON tp.user_id=ta.trainee_user_id AND tp.tenant_id=ta.tenant_id
       LEFT JOIN profiles cp ON cp.user_id=ta.created_by_user_id AND cp.tenant_id=ta.tenant_id
       LEFT JOIN classes c ON c.id=ta.class_id AND c.tenant_id=ta.tenant_id
      WHERE ta.id=? AND ta.tenant_id=?
      LIMIT 1`,
  ).bind(id, tenantId).first<AchievementQueryRow>();
  if (!row) throw new ApiError(404, "not_found", "Không tìm thấy thành tích.");
  return row;
}

async function assertAchievementTarget(
  env: Env,
  auth: AuthUser,
  tenantId: string,
  category: AchievementCategory,
  traineeUserId: string,
  classId: string,
): Promise<void> {
  const trainee = await env.DB.prepare(
    `SELECT id FROM users
      WHERE id=? AND tenant_id=? AND role='trainee' AND is_active=1
      LIMIT 1`,
  ).bind(traineeUserId, tenantId).first();
  if (!trainee) throw new ApiError(404, "not_found", "Không tìm thấy Cầu thủ học viên trong đội.");

  if (category === "weekly_class_ranking" && !classId) {
    throw new ApiError(400, "validation_error", "Xếp hạng lớp học theo tuần phải chọn lớp học.");
  }
  if (!classId) {
    if (auth.role === "coach") {
      throw new ApiError(400, "validation_error", "Coach phải chọn lớp học được phân công.");
    }
    return;
  }

  const classRow = await env.DB.prepare(
    "SELECT id FROM classes WHERE id=? AND tenant_id=? AND is_active=1 LIMIT 1",
  ).bind(classId, tenantId).first();
  if (!classRow) throw new ApiError(404, "not_found", "Không tìm thấy lớp học trong đội.");
  const enrolled = await env.DB.prepare(
    `SELECT 1 FROM class_enrollments
      WHERE tenant_id=? AND class_id=? AND trainee_user_id=? AND is_active=1
      LIMIT 1`,
  ).bind(tenantId, classId, traineeUserId).first();
  if (!enrolled) throw new ApiError(400, "trainee_not_enrolled", "Cầu thủ học viên chưa thuộc lớp học này.");

  if (auth.role === "coach") {
    const assigned = await env.DB.prepare(
      `SELECT 1 FROM class_coaches
        WHERE tenant_id=? AND class_id=? AND coach_user_id=? AND is_active=1
        LIMIT 1`,
    ).bind(tenantId, classId, auth.id).first();
    if (!assigned) throw new ApiError(403, "class_access_denied", "Coach không được phân công vào lớp này.");
  }
}

export async function achievementBadges(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  if (request.method !== "GET") throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
  if (!["founder", "co_founder", "manager", "coach", "trainee"].includes(auth.role)) {
    throw new ApiError(403, "forbidden", "Bạn không có quyền xem biểu trưng.");
  }
  const category = parseCategory(new URL(request.url).searchParams.get("category"), false);
  const rows = await allRows<AchievementBadgeWithFlags>(env.DB.prepare(
    `SELECT * FROM achievement_badges
      WHERE is_active=1 AND (?='' OR category=?)
      ORDER BY category, sort_order, name`,
  ).bind(category, category));
  return json({ badges: rows.map(achievementBadgeJson) });
}

export async function expireAchievements(env: Env, tenantId?: string): Promise<number> {
  const now = nowIso();
  const result = tenantId
    ? await env.DB.prepare(
      `UPDATE trainee_achievements SET status='expired', updated_at=?
        WHERE tenant_id=? AND status='approved' AND visible_until<?`,
    ).bind(now, tenantId, now).run()
    : await env.DB.prepare(
      `UPDATE trainee_achievements SET status='expired', updated_at=?
        WHERE status='approved' AND visible_until<?`,
    ).bind(now, now).run();
  return Number(result.meta.changes ?? 0);
}

export async function achievements(request: Request, env: Env): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  await expireAchievements(env, tenantId);

  if (request.method === "GET") {
    if (!["founder", "co_founder", "coach", "trainee"].includes(auth.role)) {
      throw new ApiError(403, "forbidden", "Bạn không có quyền xem thành tích.");
    }
    const query = new URL(request.url).searchParams;
    const traineeFilter = query.get("traineeUserId")?.trim() ?? "";
    const classFilter = query.get("classId")?.trim() ?? "";
    const category = parseCategory(query.get("category"), false);
    const status = parseStatus(query.get("status"));
    if (auth.role === "trainee" && traineeFilter && traineeFilter !== auth.id) {
      throw new ApiError(403, "forbidden", "Bạn chỉ có thể xem thành tích của chính mình.");
    }
    if (auth.role === "coach" && traineeFilter) {
      const exists = await env.DB.prepare(
        `SELECT 1 FROM class_enrollments ce
          JOIN class_coaches cc ON cc.tenant_id=ce.tenant_id AND cc.class_id=ce.class_id AND cc.is_active=1
         WHERE ce.tenant_id=? AND ce.trainee_user_id=? AND cc.coach_user_id=? AND ce.is_active=1
         LIMIT 1`,
      ).bind(tenantId, traineeFilter, auth.id).first();
      if (!exists) throw new ApiError(403, "class_access_denied", "Coach không được xem thành tích học viên này.");
    }

    const values: unknown[] = [tenantId];
    let where = "ta.tenant_id=?";
    if (auth.role === "trainee") {
      where += " AND ta.trainee_user_id=? AND ta.status='approved' AND ta.visible_until>=?";
      values.push(auth.id, nowIso());
    } else if (auth.role === "coach") {
      where += ` AND (
        ta.created_by_user_id=?
        OR EXISTS (
          SELECT 1 FROM class_coaches own_cc
           WHERE own_cc.tenant_id=ta.tenant_id AND own_cc.class_id=ta.class_id
             AND own_cc.coach_user_id=? AND own_cc.is_active=1
        )
      )`;
      values.push(auth.id, auth.id);
    }
    if (traineeFilter) { where += " AND ta.trainee_user_id=?"; values.push(traineeFilter); }
    if (classFilter) { where += " AND ta.class_id=?"; values.push(classFilter); }
    if (category) { where += " AND ta.category=?"; values.push(category); }
    if (status) { where += " AND ta.status=?"; values.push(status); }

    const rows = await allRows<AchievementQueryRow>(env.DB.prepare(
      `SELECT ta.*, b.key AS badge_key, b.name AS badge_name, b.asset_key AS badge_asset_key,
              b.display_size AS badge_display_size, b.points AS badge_points,
              tp.full_name AS trainee_name, cp.full_name AS coach_name, c.name AS class_name
         FROM trainee_achievements ta
         JOIN achievement_badges b ON b.id=ta.badge_id
         LEFT JOIN profiles tp ON tp.user_id=ta.trainee_user_id AND tp.tenant_id=ta.tenant_id
         LEFT JOIN profiles cp ON cp.user_id=ta.created_by_user_id AND cp.tenant_id=ta.tenant_id
         LEFT JOIN classes c ON c.id=ta.class_id AND c.tenant_id=ta.tenant_id
        WHERE ${where}
        ORDER BY ta.awarded_for_date DESC, ta.created_at DESC`,
    ).bind(...values));

    const totalValues: unknown[] = [tenantId];
    let totalWhere = "tenant_id=? AND status IN ('approved','removed','expired')";
    if (auth.role === "trainee") {
      totalWhere += " AND trainee_user_id=?";
      totalValues.push(auth.id);
    } else if (auth.role === "coach") {
      totalWhere += ` AND (
        created_by_user_id=?
        OR EXISTS (
          SELECT 1 FROM class_coaches total_cc
           WHERE total_cc.tenant_id=trainee_achievements.tenant_id
             AND total_cc.class_id=trainee_achievements.class_id
             AND total_cc.coach_user_id=? AND total_cc.is_active=1
        )
      )`;
      totalValues.push(auth.id, auth.id);
    }
    if (traineeFilter) { totalWhere += " AND trainee_user_id=?"; totalValues.push(traineeFilter); }
    if (classFilter) { totalWhere += " AND class_id=?"; totalValues.push(classFilter); }
    if (category) { totalWhere += " AND category=?"; totalValues.push(category); }
    const total = await env.DB.prepare(
      `SELECT COALESCE(SUM(points_snapshot), 0) AS points
         FROM trainee_achievements
        WHERE ${totalWhere}`,
    ).bind(...totalValues).first<{ points: number }>();
    const pending = isFounderLike(auth.role)
      ? await env.DB.prepare("SELECT COUNT(*) AS count FROM trainee_achievements WHERE tenant_id=? AND status='pending'")
        .bind(tenantId).first<{ count: number }>()
      : null;
    return json({
      achievements: rows.map(achievementJson),
      totalPoints: Number(total?.points ?? 0),
      pendingCount: Number(pending?.count ?? 0),
    });
  }

  if (request.method !== "POST") throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
  assertCanCreateAchievement(auth.role);
  const body = await readJson<JsonObject>(request);
  const category = parseCategory(body.category) as AchievementCategory;
  const traineeUserId = requireText(body.traineeUserId, "traineeUserId", 64);
  const classId = body.classId === undefined || body.classId === null || body.classId === ""
    ? ""
    : requireText(body.classId, "classId", 64);
  const badgeId = requireText(body.badgeId, "badgeId", 64);
  const title = optionalText(body.title, "title", 180);
  const eventName = optionalText(body.eventName, "eventName", 180);
  const reason = auth.role === "coach"
    ? requireText(body.reason, "reason", 2_000)
    : optionalText(body.reason, "reason", 2_000);
  const awardedForDate = body.awardedForDate === undefined
    ? nowIso().slice(0, 10)
    : requireDateKey(body.awardedForDate, "awardedForDate");

  await assertAchievementTarget(env, auth, tenantId, category, traineeUserId, classId);
  const badge = await env.DB.prepare(
    `SELECT * FROM achievement_badges
      WHERE id=? AND category=? AND is_active=1 LIMIT 1`,
  ).bind(badgeId, category).first<AchievementBadgeWithFlags>();
  if (!badge) throw new ApiError(400, "invalid_badge", "Biểu trưng không hợp lệ hoặc đã ngừng sử dụng.");

  const idempotency = await reserveIdempotency(request, env, auth, tenantId);
  if (idempotency.replay) return idempotency.replay;
  try {
    const id = newId();
    const now = nowIso();
    const status: AchievementStatus = auth.role === "coach" ? "pending" : "approved";
    const visibleUntil = new Date(Date.parse(now) + 30 * DAY_MS).toISOString();
    await env.DB.prepare(
      `INSERT INTO trainee_achievements
        (id, tenant_id, trainee_user_id, badge_id, class_id, category, title, event_name,
         reason, awarded_for_date, points_snapshot, status, created_by_user_id,
         reviewed_by_user_id, reviewed_at, review_note, visible_until, removed_at,
         created_at, updated_at)
       VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, NULL, NULL, '', ?, NULL, ?, ?)`,
    ).bind(
      id, tenantId, traineeUserId, badgeId, classId || null, category, title, eventName,
      reason, awardedForDate, Number(badge.points), status, auth.id, visibleUntil, now, now,
    ).run();

    const saved = await getAchievementRow(env, tenantId, id);
    if (status === "pending") await notifyAchievementSubmitted(env, tenantId, saved);
    else await notifyAchievementApproved(env, tenantId, saved, true);
    await audit(env, tenantId, auth.id, "achievement.created", "trainee_achievement", id, {
      category,
      traineeUserId,
      classId: classId || null,
      badgeKey: badge.key,
      status,
      points: Number(badge.points),
    });
    const responseBody = { achievement: achievementJson(saved) };
    await finishIdempotency(env, auth, idempotency.key, 201, responseBody);
    return json(responseBody, 201);
  } catch (error) {
    if (idempotency.reserved) await cancelIdempotency(env, auth, idempotency.key);
    throw error;
  }
}

export async function reviewAchievement(request: Request, env: Env, achievementId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  assertCanReviewAchievement(auth.role);
  if (request.method !== "PATCH") throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
  const body = await readJson<JsonObject>(request);
  const approved = body.approved === undefined
    ? body.status === "approved"
    : body.approved;
  if (typeof approved !== "boolean") throw new ApiError(400, "validation_error", "approved phải là boolean.");
  const note = optionalText(body.reviewNote ?? body.note, "reviewNote", 2_000);
  const current = await getAchievementRow(env, tenantId, achievementId);
  if (current.status !== "pending") {
    throw new ApiError(409, "achievement_already_reviewed", "Thành tích này đã được xử lý trước đó.");
  }
  const now = nowIso();
  const status: AchievementStatus = approved ? "approved" : "rejected";
  await env.DB.prepare(
    `UPDATE trainee_achievements
        SET status=?, reviewed_by_user_id=?, reviewed_at=?, review_note=?, updated_at=?
      WHERE id=? AND tenant_id=? AND status='pending'`,
  ).bind(status, auth.id, now, note, now, achievementId, tenantId).run();
  const saved = await getAchievementRow(env, tenantId, achievementId);
  await notifyAchievementApproved(env, tenantId, saved, approved);
  await audit(env, tenantId, auth.id, approved ? "achievement.approved" : "achievement.rejected",
    "trainee_achievement", achievementId, { note });
  return json({ achievement: achievementJson(saved) });
}

export async function removeAchievement(request: Request, env: Env, achievementId: string): Promise<Response> {
  const auth = await authenticate(request, env);
  const tenantId = requireTenant(auth);
  assertCanRemoveAchievement(auth.role);
  if (request.method !== "DELETE") throw new ApiError(405, "method_not_allowed", "Phương thức không được hỗ trợ.");
  const current = await getAchievementRow(env, tenantId, achievementId);
  if (current.status === "removed") return noContent();
  const now = nowIso();
  await env.DB.prepare(
    `UPDATE trainee_achievements
        SET status='removed', removed_at=?, updated_at=?
      WHERE id=? AND tenant_id=? AND status<>'removed'`,
  ).bind(now, now, achievementId, tenantId).run();
  await audit(env, tenantId, auth.id, "achievement.removed", "trainee_achievement", achievementId, {
    previousStatus: current.status,
    pointsRetained: Number(current.points_snapshot ?? 0),
  });
  return noContent();
}
