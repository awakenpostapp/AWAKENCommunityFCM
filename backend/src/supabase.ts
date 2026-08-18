import { json } from "./http";

const SUPABASE_TIMEOUT_MS = 5_000;

function supabaseBaseUrl(env: Env): string | null {
  const value = env.SUPABASE_URL?.trim().replace(/\/+$/u, "");
  return value ? value : null;
}

/**
 * Read-only production preflight. It deliberately does not return any row,
 * key material, or upstream error body. The Worker remains D1-backed until
 * the full Supabase repository/auth cutover has passed its smoke tests.
 */
export async function supabaseHealth(env: Env): Promise<Response> {
  const baseUrl = supabaseBaseUrl(env);
  if (!baseUrl || !env.SUPABASE_SECRET_KEY) {
    return json({ status: "not_configured", backend: "supabase" }, 503);
  }

  const controller = new AbortController();
  const timeout = setTimeout(() => controller.abort(), SUPABASE_TIMEOUT_MS);
  try {
    const response = await fetch(`${baseUrl}/rest/v1/tenants?select=id&limit=1`, {
      method: "GET",
      headers: {
        apikey: env.SUPABASE_SECRET_KEY,
        Authorization: `Bearer ${env.SUPABASE_SECRET_KEY}`,
        Accept: "application/json",
      },
      signal: controller.signal,
    });
    if (!response.ok) {
      return json({ status: "degraded", backend: "supabase", code: "upstream_rejected" }, 502);
    }
    return json({ status: "ok", backend: "supabase" });
  } catch {
    return json({ status: "degraded", backend: "supabase", code: "connection_failed" }, 502);
  } finally {
    clearTimeout(timeout);
  }
}
