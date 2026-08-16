import { AuthUser, UserRow, newId, nowIso } from "./domain";
import { ApiError, json, readJson, requireText } from "./http";
import { authenticate, createSession } from "./auth";

type Provider = "google";

interface OAuthIdentity {
  provider: Provider;
  subject: string;
  email: string;
  displayName: string;
}

interface OAuthStateRow {
  state: string;
  provider: Provider;
  redirect_uri: string;
  code_verifier: string;
  expires_at: string;
}

interface OAuthTicketRow extends OAuthIdentity {
  ticket: string;
  display_name: string;
  expires_at: string;
  used_at: string | null;
}

function providerValue(value: string | null): Provider {
  if (value === "google") return value;
  throw new ApiError(400, "invalid_provider", "Provider OAuth không được hỗ trợ.");
}

function callbackUrl(request: Request, env: Env): string {
  return env.OAUTH_CALLBACK_URL?.trim()
    || `${new URL(request.url).origin}/v1/auth/oauth/callback`;
}

function providerConfig(env: Env, provider: Provider): { clientId: string; clientSecret: string } {
  const config = { clientId: env.GOOGLE_OAUTH_CLIENT_ID ?? "", clientSecret: env.GOOGLE_OAUTH_CLIENT_SECRET ?? "" };
  if (!config.clientId || !config.clientSecret) {
    throw new ApiError(503, "oauth_not_configured", `OAuth ${provider} chưa được cấu hình trên backend.`);
  }
  return config;
}

function randomToken(): string {
  return `${newId()}${newId()}`;
}

const APP_OAUTH_REDIRECT_URI = "communityfootballclubmanager://oauth/callback";

function base64Url(bytes: Uint8Array): string {
  let binary = "";
  for (const byte of bytes) binary += String.fromCharCode(byte);
  return btoa(binary).replace(/\+/gu, "-").replace(/\//gu, "_").replace(/=+$/u, "");
}

async function verifyPkce(verifier: string, challenge: string): Promise<boolean> {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier));
  return base64Url(new Uint8Array(digest)) === challenge;
}

async function exchangeGoogle(code: string, state: OAuthStateRow, env: Env): Promise<OAuthIdentity> {
  const config = providerConfig(env, "google");
  const tokenResponse = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "content-type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      code,
      client_id: config.clientId,
      client_secret: config.clientSecret,
      redirect_uri: callbackUrlFromState(state, env),
      grant_type: "authorization_code",
      code_verifier: state.code_verifier,
    }),
  });
  if (!tokenResponse.ok) throw new ApiError(502, "oauth_exchange_failed", "Google OAuth không trả về token hợp lệ.");
  const token = await tokenResponse.json() as { access_token?: string };
  if (!token.access_token) throw new ApiError(502, "oauth_exchange_failed", "Google OAuth thiếu access token.");
  const profileResponse = await fetch("https://openidconnect.googleapis.com/v1/userinfo", {
    headers: { authorization: `Bearer ${token.access_token}` },
  });
  if (!profileResponse.ok) throw new ApiError(502, "oauth_profile_failed", "Không đọc được hồ sơ Google OAuth.");
  const profile = await profileResponse.json() as { sub?: string; email?: string; name?: string };
  if (!profile.sub) throw new ApiError(502, "oauth_profile_failed", "Hồ sơ Google OAuth thiếu subject.");
  return { provider: "google", subject: profile.sub, email: profile.email ?? "", displayName: profile.name ?? profile.email ?? "Google" };
}

function callbackUrlFromState(state: OAuthStateRow, env: Env): string {
  return env.OAUTH_CALLBACK_URL?.trim() || "";
}

export async function oauthStart(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const provider = providerValue(url.searchParams.get("provider"));
  const redirectUri = requireText(url.searchParams.get("redirect_uri"), "redirect_uri", 200);
  const codeChallenge = requireText(url.searchParams.get("code_challenge"), "code_challenge", 200);
  if (redirectUri !== APP_OAUTH_REDIRECT_URI) {
    throw new ApiError(400, "invalid_redirect_uri", "redirect_uri OAuth không hợp lệ.");
  }
  const verifier = requireText(url.searchParams.get("code_verifier"), "code_verifier", 128);
  if (verifier.length < 43 || verifier.length > 128 || !(await verifyPkce(verifier, codeChallenge))) {
    throw new ApiError(400, "invalid_pkce", "PKCE OAuth không hợp lệ.");
  }
  providerConfig(env, provider);
  const state = randomToken();
  const expiresAt = new Date(Date.now() + 10 * 60_000).toISOString();
  await env.DB.prepare(
    "INSERT INTO oauth_states (state, provider, redirect_uri, code_verifier, expires_at, created_at) VALUES (?, ?, ?, ?, ?, ?)",
  ).bind(state, provider, redirectUri, verifier, expiresAt, nowIso()).run();

  const redirect = new URL("https://accounts.google.com/o/oauth2/v2/auth");
  redirect.searchParams.set("client_id", env.GOOGLE_OAUTH_CLIENT_ID!);
  redirect.searchParams.set("redirect_uri", callbackUrl(request, env));
  redirect.searchParams.set("response_type", "code");
  redirect.searchParams.set("state", state);
  redirect.searchParams.set("code_challenge", codeChallenge);
  redirect.searchParams.set("code_challenge_method", "S256");
  redirect.searchParams.set("scope", "openid email profile");
  return Response.redirect(redirect.toString(), 302);
}

export async function oauthCallback(request: Request, env: Env): Promise<Response> {
  const url = new URL(request.url);
  const stateId = url.searchParams.get("state") ?? "";
  const code = url.searchParams.get("code") ?? "";
  const state = await env.DB.prepare(
    "SELECT state, provider, redirect_uri, code_verifier, expires_at FROM oauth_states WHERE state = ? AND expires_at > ? LIMIT 1",
  ).bind(stateId, nowIso()).first<OAuthStateRow>();
  if (!state || !code) throw new ApiError(400, "oauth_callback_invalid", "OAuth callback không hợp lệ hoặc đã hết hạn.");
  const identity = await exchangeGoogle(code, state, env);
  await env.DB.prepare("DELETE FROM oauth_states WHERE state = ?").bind(state.state).run();
  const ticket = randomToken();
  const expiresAt = new Date(Date.now() + 5 * 60_000).toISOString();
  await env.DB.prepare(
    "INSERT INTO oauth_exchange_tickets (ticket, provider, subject, email, display_name, expires_at, created_at) VALUES (?, ?, ?, ?, ?, ?, ?)",
  ).bind(ticket, identity.provider, identity.subject, identity.email, identity.displayName, expiresAt, nowIso()).run();
  const target = new URL(state.redirect_uri);
  target.searchParams.set("ticket", ticket);
  target.searchParams.set("provider", identity.provider);
  return Response.redirect(target.toString(), 302);
}

export async function consumeOAuthTicket(
  request: Request,
  env: Env,
): Promise<{ identity: OAuthIdentity; auth: AuthUser | null }> {
  const body = await readJson<{ authorizationCode?: unknown }>(request);
  const ticket = requireText(body.authorizationCode, "authorizationCode", 200);
  const row = await env.DB.prepare(
    "SELECT ticket, provider, subject, email, display_name, expires_at, used_at FROM oauth_exchange_tickets WHERE ticket = ? AND expires_at > ? AND used_at IS NULL LIMIT 1",
  ).bind(ticket, nowIso()).first<OAuthTicketRow>();
  if (!row) throw new ApiError(401, "oauth_ticket_invalid", "Phiên OAuth đã hết hạn hoặc đã được sử dụng.");
  const auth = request.headers.has("authorization") ? await authenticate(request, env) : null;
  const marked = await env.DB.prepare(
    "UPDATE oauth_exchange_tickets SET used_at = ? WHERE ticket = ? AND used_at IS NULL",
  ).bind(nowIso(), ticket).run();
  if ((marked.meta?.changes ?? 0) !== 1) throw new ApiError(409, "oauth_ticket_used", "Phiên OAuth đã được sử dụng.");
  return { identity: { provider: row.provider, subject: row.subject, email: row.email, displayName: row.display_name }, auth };
}
