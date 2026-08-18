import { json } from "./http";
import { SupabaseD1Database } from "./supabase-d1";

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
    const adapter = new SupabaseD1Database(env);
    const row = await adapter.prepare("SELECT 1 AS ok").first<{ ok: number }>();
    if (row?.ok !== 1) return json({ status: "degraded", backend: "supabase", code: "adapter_invalid" }, 502);
    const tableCheck = await adapter.prepare("SELECT COUNT(*) AS count FROM users").first<{ count: number }>();
    if (!tableCheck || Number(tableCheck.count) < 0) {
      return json({ status: "degraded", backend: "supabase", code: "adapter_schema_invalid" }, 502);
    }
    return json({ status: "ok", backend: "supabase", adapter: "ok" });
  } catch {
    return json({ status: "degraded", backend: "supabase", code: "connection_failed" }, 502);
  } finally {
    clearTimeout(timeout);
  }
}
