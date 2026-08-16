import { errorResponse, json, withCors } from "./http";
import {
  adminFounderAction,
  announcement,
  auditEvent,
  adminFounders,
  attendance,
  changeOwnPassword,
  checkIn,
  checkInSelfieImage,
  checkOut,
  checkOutSelfieImage,
  clubLogo,
  classes,
  club,
  login,
  logout,
  manageMember,
  me,
  members,
  deleteClass,
  oauthCallback,
  oauthExchange,
  oauthLinks,
  oauthUnlink,
  oauthStart,
  notifications,
  notificationsBulk,
  paymentProofImage,
  profileAvatar,
  refresh,
  registerFounder,
  reviewCheckIn,
  evaluations,
  evaluationRoster,
  reviewEvaluation,
  reviewProof,
  setupAdmin,
  snapshot,
  submitProof,
  tuition,
  updateInvoiceCycles,
  updateSalary,
  updateReceiptPdf,
  updateProfile,
  uploads,
} from "./routes";
import { cleanupExpiredSecurityRows, markMissedCoachCheckInsForAllTenants } from "./snapshot";

function match(pathname: string, pattern: RegExp): string[] | null {
  const result = pattern.exec(pathname);
  return result ? result.slice(1).map(decodeURIComponent) : null;
}

async function route(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/u, "") || "/";
  const method = request.method.toUpperCase();

  if (method === "GET" && path === "/health") {
    const database = await env.DB.prepare("SELECT 1 AS ok").first<{ ok: number }>();
    return json({ status: database?.ok === 1 ? "ok" : "degraded", version: env.APP_API_VERSION, environment: env.APP_ENV });
  }
  if (method === "POST" && path === "/v1/setup/admin") return setupAdmin(request, env);
  if (method === "POST" && path === "/v1/auth/register-founder") return registerFounder(request, env);
  if (method === "GET" && path === "/v1/auth/oauth/start") return oauthStart(request, env);
  if (method === "GET" && path === "/v1/auth/oauth/callback") return oauthCallback(request, env);
  if (method === "POST" && path === "/v1/auth/oauth/exchange") return oauthExchange(request, env);
  if (method === "GET" && path === "/v1/auth/oauth/links") return oauthLinks(request, env);
  const oauthUnlinkParams = match(path, /^\/v1\/auth\/oauth\/links\/([^/]+)$/u);
  if (method === "DELETE" && oauthUnlinkParams) return oauthUnlink(request, env, oauthUnlinkParams[0]!);
  if (method === "POST" && path === "/v1/auth/login") return login(request, env);
  if (method === "POST" && path === "/v1/auth/refresh") return refresh(request, env);
  if (method === "POST" && path === "/v1/auth/logout") return logout(request, env);
  if (method === "GET" && path === "/v1/auth/me") return me(request, env);
  if (method === "PATCH" && path === "/v1/auth/password") return changeOwnPassword(request, env);

  if ((method === "GET" || method === "POST") && path === "/v1/admin/founders") return adminFounders(request, env);
  let params = match(path, /^\/v1\/admin\/founders\/([^/]+)\/status$/u);
  if (method === "PATCH" && params) return adminFounderAction(request, env, params[0]!, "status");
  params = match(path, /^\/v1\/admin\/founders\/([^/]+)\/password$/u);
  if (method === "PATCH" && params) return adminFounderAction(request, env, params[0]!, "password");
  params = match(path, /^\/v1\/admin\/founders\/([^/]+)$/u);
  if (method === "DELETE" && params) return adminFounderAction(request, env, params[0]!, "delete");

  if ((method === "GET" || method === "POST") && path === "/v1/users") return members(request, env);
  params = match(path, /^\/v1\/users\/([^/]+)\/profile$/u);
  if (method === "PATCH" && params) return updateProfile(request, env, params[0]!);
  params = match(path, /^\/v1\/users\/([^/]+)\/avatar$/u);
  if (method === "GET" && params) return profileAvatar(request, env, params[0]!);
  params = match(path, /^\/v1\/users\/([^/]+)\/password$/u);
  if (method === "PATCH" && params) return manageMember(request, env, params[0]!, "password");
  params = match(path, /^\/v1\/users\/([^/]+)\/status$/u);
  if (method === "PATCH" && params) return manageMember(request, env, params[0]!, "status");
  params = match(path, /^\/v1\/users\/([^/]+)\/tuition-support$/u);
  if (method === "PATCH" && params) return manageMember(request, env, params[0]!, "tuitionSupport");
  if ((method === "GET" || method === "PATCH") && path === "/v1/club") return club(request, env);
  if (method === "GET" && path === "/v1/club/logo") return clubLogo(request, env);
  if ((method === "GET" || method === "POST") && path === "/v1/classes") return classes(request, env);
  params = match(path, /^\/v1\/classes\/([^/]+)$/u);
  if (method === "DELETE" && params) return deleteClass(request, env, params[0]!);

  if (method === "GET" && path === "/v1/attendance") return attendance(request, env);
  params = match(path, /^\/v1\/attendance\/([^/]+)$/u);
  if (method === "PUT" && params) return attendance(request, env, params[0]);
  if (method === "POST" && path === "/v1/check-ins") return checkIn(request, env);
  if (method === "POST" && path === "/v1/check-outs") return checkOut(request, env);
  params = match(path, /^\/v1\/check-ins\/([^/]+)\/review$/u);
  if (method === "PATCH" && params) return reviewCheckIn(request, env, params[0]!);
  params = match(path, /^\/v1\/check-ins\/([^/]+)\/selfie$/u);
  if (method === "GET" && params) return checkInSelfieImage(request, env, params[0]!);
  params = match(path, /^\/v1\/check-ins\/([^/]+)\/checkout-selfie$/u);
  if (method === "GET" && params) return checkOutSelfieImage(request, env, params[0]!);

  if (method === "GET" && path === "/v1/evaluations") return evaluations(request, env);
  if (method === "GET" && path === "/v1/evaluations/roster") return evaluationRoster(request, env);
  params = match(path, /^\/v1\/evaluations\/([^/]+)\/review$/u);
  if (method === "PATCH" && params) return reviewEvaluation(request, env, params[0]!);
  params = match(path, /^\/v1\/evaluations\/([^/]+)$/u);
  if ((method === "PATCH" || method === "GET") && params) return evaluations(request, env, params[0]!);
  if (method === "POST" && path === "/v1/evaluations") return evaluations(request, env);

  if ((method === "GET" || method === "POST") && path === "/v1/tuition/invoices") return tuition(request, env);
  params = match(path, /^\/v1\/tuition\/invoices\/([^/]+)\/proofs$/u);
  if (method === "POST" && params) return submitProof(request, env, params[0]!);
  params = match(path, /^\/v1\/tuition\/proofs\/([^/]+)\/review$/u);
  if (method === "PATCH" && params) return reviewProof(request, env, params[0]!);
  params = match(path, /^\/v1\/tuition\/proofs\/([^/]+)\/image$/u);
  if (method === "GET" && params) return paymentProofImage(request, env, params[0]!);
  params = match(path, /^\/v1\/tuition\/invoices\/([^/]+)\/cycles$/u);
  if (method === "PATCH" && params) return updateInvoiceCycles(request, env, params[0]!);

  params = match(path, /^\/v1\/salaries\/([^/]+)$/u);
  if (method === "PATCH" && params) return updateSalary(request, env, params[0]!);
  params = match(path, /^\/v1\/receipts\/([^/]+)\/pdf$/u);
  if (method === "PATCH" && params) return updateReceiptPdf(request, env, params[0]!);

  if ((method === "GET" || method === "POST") && path === "/v1/notifications") return notifications(request, env);
  if (method === "POST" && path === "/v1/notifications/read-all") return notificationsBulk(request, env, "read");
  if (method === "DELETE" && path === "/v1/notifications") return notificationsBulk(request, env, "delete");
  if (method === "POST" && path === "/v1/notifications/announcement") return announcement(request, env);
  params = match(path, /^\/v1\/notifications\/([^/]+)\/read$/u);
  if (method === "PATCH" && params) return notifications(request, env, params[0]!);
  if (method === "POST" && path === "/v1/audit") return auditEvent(request, env);

  if (method === "POST" && path === "/v1/uploads") return uploads(request, env);
  params = match(path, /^\/v1\/uploads\/([^/]+)$/u);
  if (method === "GET" && params) return uploads(request, env, params[0]!);

  if ((method === "GET" || method === "PUT") && path === "/v1/sync/snapshot") return snapshot(request, env);
  return json({ error: { code: "not_found", message: "Không tìm thấy endpoint." } }, 404);
}

export default {
  async fetch(request, env): Promise<Response> {
    const requestId = crypto.randomUUID();
    const startedAt = Date.now();
    let response: Response;
    try {
      if (request.method === "OPTIONS") {
        response = new Response(null, { status: 204 });
      } else {
        response = await route(request, env);
      }
    } catch (error) {
      response = errorResponse(error);
    }
    // Redirect responses have immutable headers in the Workers runtime.
    // Clone the response before adding our request ID so OAuth 302 responses
    // can pass through without becoming an unhandled 1101 error.
    const responseHeaders = new Headers(response.headers);
    responseHeaders.set("x-request-id", requestId);
    response = new Response(response.body, {
      status: response.status,
      statusText: response.statusText,
      headers: responseHeaders,
    });
    console.log(JSON.stringify({
      level: "info",
      event: "request_complete",
      requestId,
      method: request.method,
      path: new URL(request.url).pathname,
      status: response.status,
      durationMs: Date.now() - startedAt,
    }));
    return withCors(request, response, env);
  },
  async scheduled(_controller, env): Promise<void> {
    await cleanupExpiredSecurityRows(env);
    await markMissedCoachCheckInsForAllTenants(env);
  },
} satisfies ExportedHandler<Env>;
