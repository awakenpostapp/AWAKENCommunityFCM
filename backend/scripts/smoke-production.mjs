const baseUrl = (process.env.SMOKE_BASE_URL || "https://community-football-club-manager-api.old-mud-b712.workers.dev").replace(/\/$/u, "");

async function request(path, options = {}) {
  const response = await fetch(`${baseUrl}${path}`, {
    ...options,
    headers: { accept: "application/json", ...(options.headers || {}) },
  });
  const text = await response.text();
  let body = null;
  try { body = text ? JSON.parse(text) : null; } catch { body = text; }
  if (!response.ok) {
    throw new Error(`${options.method || "GET"} ${path} -> ${response.status} ${JSON.stringify(body)}`);
  }
  return body;
}

const started = performance.now();
const health = await request("/health");
console.log(`health: ${health.status} (${health.version})`);

const username = process.env.SMOKE_USERNAME?.trim();
const password = process.env.SMOKE_PASSWORD;
if (!username || !password) {
  console.log("auth: skipped (set SMOKE_USERNAME and SMOKE_PASSWORD for a read-only account smoke test)");
  console.log(`smoke passed in ${Math.round(performance.now() - started)} ms`);
  process.exit(0);
}

const session = await request("/v1/auth/login", {
  method: "POST",
  headers: { "content-type": "application/json" },
  body: JSON.stringify({ username, password, deviceName: "production-smoke" }),
});
const accessToken = session.accessToken;
if (!accessToken) throw new Error("login response did not include accessToken");
const authHeaders = { authorization: `Bearer ${accessToken}` };
await request("/v1/auth/me", { headers: authHeaders });
await request("/v1/sync/snapshot", { headers: authHeaders });
await request("/v1/auth/oauth/links", { headers: authHeaders });
await request("/v1/notifications", { headers: authHeaders });
console.log("auth: login, me, snapshot, OAuth links, notifications passed");
console.log(`smoke passed in ${Math.round(performance.now() - started)} ms`);
