/**
 * A small D1-compatible facade backed by Supabase PostgREST.
 *
 * The existing Worker is intentionally written against D1PreparedStatement.
 * Keeping that contract lets us migrate the source of truth without rewriting
 * every route at once. SQL is bound locally (never sent with raw user values)
 * and executed by the locked-down `public.d1_batch` RPC in one transaction.
 */

type QueryMode = "all" | "run";

interface QuerySpec {
  sql: string;
  values: unknown[];
  mode: QueryMode;
}

interface SupabaseBatchRow {
  results?: unknown[];
  meta?: Record<string, unknown>;
}

function baseUrl(env: Env): string {
  const value = env.SUPABASE_URL?.trim().replace(/\/+$/u, "");
  if (!value) throw new Error("SUPABASE_URL is not configured");
  return value;
}

function quote(value: unknown): string {
  if (value === null || value === undefined) return "NULL";
  if (typeof value === "number") {
    if (!Number.isFinite(value)) throw new Error("Unsupported non-finite SQL parameter");
    return String(value);
  }
  if (typeof value === "boolean") return value ? "1" : "0";
  if (typeof value === "bigint") return value.toString();
  if (value instanceof Uint8Array) {
    const binary = String.fromCharCode(...value);
    return `'${binary.replaceAll("'", "''")}'`;
  }
  const text = typeof value === "string" ? value : JSON.stringify(value);
  return `'${(text ?? "").replaceAll("'", "''")}'`;
}

function bindParameters(sql: string, values: unknown[]): string {
  let parameterIndex = 0;
  let result = "";
  let inSingleQuote = false;
  let inDoubleQuote = false;
  for (let index = 0; index < sql.length; index += 1) {
    const character = sql[index]!;
    const next = sql[index + 1];
    if (inSingleQuote) {
      result += character;
      if (character === "'" && next === "'") {
        result += next;
        index += 1;
      } else if (character === "'") {
        inSingleQuote = false;
      }
      continue;
    }
    if (inDoubleQuote) {
      result += character;
      if (character === '"' && next === '"') {
        result += next;
        index += 1;
      } else if (character === '"') {
        inDoubleQuote = false;
      }
      continue;
    }
    if (character === "'") {
      inSingleQuote = true;
      result += character;
      continue;
    }
    if (character === '"') {
      inDoubleQuote = true;
      result += character;
      continue;
    }
    if (character === "?") {
      if (parameterIndex >= values.length) throw new Error("Missing SQL parameter");
      result += quote(values[parameterIndex]);
      parameterIndex += 1;
      continue;
    }
    result += character;
  }
  if (parameterIndex !== values.length) throw new Error("Too many SQL parameters");
  return result;
}

function translateSql(statement: string, values: unknown[]): string {
  let sql = statement.trim().replace(/;\s*$/u, "");
  // SQLite's null-safe comparison is written `column IS ?`; PostgreSQL uses
  // `IS NOT DISTINCT FROM` for the same value-or-NULL semantics.
  sql = sql.replace(/\bIS\s+\?/giu, "IS NOT DISTINCT FROM ?");
  sql = sql.replace(/date\(\s*'now'\s*\)/giu, "CURRENT_DATE::text");
  sql = sql.replace(/lower\(\s*hex\(\s*randomblob\(\s*16\s*\)\s*\)\s*\)/giu,
    "md5(random()::text || clock_timestamp()::text || pg_backend_pid()::text)");
  // SQLite MAX(a, b, ...) is a scalar greatest-value function. PostgreSQL's
  // MAX is aggregate-only for multiple arguments, so translate the two
  // timestamp forms used by the snapshot sync version query.
  sql = sql.replace(/MAX\(\s*checked_in_at\s*,\s*checked_out_at\s*,\s*reviewed_at\s*\)/giu,
    "GREATEST(checked_in_at, checked_out_at, reviewed_at)");
  sql = sql.replace(/MAX\(\s*submitted_at\s*,\s*reviewed_at\s*\)/giu,
    "GREATEST(submitted_at, reviewed_at)");

  const replaceMatch = /^INSERT\s+OR\s+REPLACE\s+INTO\s+idempotency_keys\b/iu.test(sql);
  if (replaceMatch) {
    sql = sql.replace(/^INSERT\s+OR\s+REPLACE\s+INTO/iu, "INSERT INTO");
    sql += " ON CONFLICT (user_id, idempotency_key) DO UPDATE SET "
      + "tenant_id=excluded.tenant_id, response_status=excluded.response_status, "
      + "response_json=excluded.response_json, created_at=excluded.created_at, expires_at=excluded.expires_at";
  } else if (/^INSERT\s+OR\s+IGNORE\s+INTO/iu.test(sql)) {
    sql = sql.replace(/^INSERT\s+OR\s+IGNORE\s+INTO/iu, "INSERT INTO");
    if (!/\bON\s+CONFLICT\b/iu.test(sql)) sql += " ON CONFLICT DO NOTHING";
  }
  return bindParameters(sql, values);
}

function resultMeta(rowCount: number): D1Meta {
  return {
    changed_db: rowCount > 0,
    changes: rowCount,
    duration: 0,
    last_row_id: 0,
    rows_read: 0,
    rows_written: rowCount,
    size_after: 0,
  };
}

function toD1Result<T>(row: SupabaseBatchRow | undefined): D1Result<T> {
  const rows = Array.isArray(row?.results) ? row.results as T[] : [];
  const meta = row?.meta ?? {};
  const changes = typeof meta.changes === "number" ? meta.changes : 0;
  return {
    success: true,
    results: rows,
    meta: { ...resultMeta(changes), ...meta } as D1Meta & Record<string, unknown>,
  };
}

class SupabasePreparedStatement {
  constructor(
    private readonly database: SupabaseD1Database,
    private readonly statement: string,
    private readonly values: unknown[] = [],
  ) {}

  bind(...values: unknown[]): SupabasePreparedStatement {
    return new SupabasePreparedStatement(this.database, this.statement, values);
  }

  async all<T = Record<string, unknown>>(): Promise<D1Result<T>> {
    const [result] = await this.database.execute([{ sql: this.statement, values: this.values, mode: "all" }]);
    return toD1Result<T>(result);
  }

  async first<T = Record<string, unknown>>(colName?: string): Promise<T | null> {
    const result = await this.all<Record<string, unknown>>();
    const row = result.results[0];
    if (!row) return null;
    return (colName ? row[colName] : row) as T;
  }

  async run<T = Record<string, unknown>>(): Promise<D1Result<T>> {
    const [result] = await this.database.execute([{ sql: this.statement, values: this.values, mode: "run" }]);
    return toD1Result<T>(result);
  }

  async raw<T = unknown[]>(options?: { columnNames?: false }): Promise<T[]>;
  async raw<T = unknown[]>(options: { columnNames: true }): Promise<[string[], ...T[]]>;
  async raw<T = unknown[]>(options?: { columnNames?: boolean }): Promise<T[] | [string[], ...T[]]> {
    const result = await this.all<Record<string, unknown>>();
    const rows = result.results;
    if (!options?.columnNames) return rows.map((row) => Object.values(row)) as T[];
    const names = rows.length ? Object.keys(rows[0]!) : [];
    return [names, ...rows.map((row) => names.map((name) => row[name]))] as [string[], ...T[]];
  }
}

export class SupabaseD1Database {
  constructor(private readonly env: Env) {}

  prepare(statement: string): SupabasePreparedStatement {
    return new SupabasePreparedStatement(this, statement);
  }

  async execute(specs: QuerySpec[]): Promise<SupabaseBatchRow[]> {
    if (!this.env.SUPABASE_SECRET_KEY) throw new Error("SUPABASE_SECRET_KEY is not configured");
    const queries = specs.map((spec) => ({
      sql: translateSql(spec.sql, spec.values),
      mode: spec.mode,
    }));
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15_000);
    try {
      const response = await fetch(`${baseUrl(this.env)}/rest/v1/rpc/d1_batch`, {
        method: "POST",
        headers: {
          apikey: this.env.SUPABASE_SECRET_KEY,
          Authorization: `Bearer ${this.env.SUPABASE_SECRET_KEY}`,
          "content-type": "application/json",
          Accept: "application/json",
        },
        body: JSON.stringify({ p_queries: queries }),
        signal: controller.signal,
      });
      if (!response.ok) {
        const body = await response.text().catch(() => "");
        throw new Error(`Supabase RPC failed (${response.status}): ${body.slice(0, 240)}`);
      }
      const payload = await response.json() as unknown;
      if (!Array.isArray(payload)) throw new Error("Supabase RPC returned an invalid result");
      return payload as SupabaseBatchRow[];
    } finally {
      clearTimeout(timeout);
    }
  }

  async batch<T = unknown>(statements: SupabasePreparedStatement[]): Promise<D1Result<T>[]> {
    const specs = statements.map((statement) => statementToSpec(statement));
    const results = await this.execute(specs);
    return results.map((row) => toD1Result<T>(row));
  }
}

function statementToSpec(statement: SupabasePreparedStatement): QuerySpec {
  // The adapter owns its statement instances; this narrow structural read
  // avoids exposing the binding values to the rest of the Worker.
  const value = statement as unknown as {
    statement: string;
    values: unknown[];
  };
  return { sql: value.statement, values: value.values, mode: "run" };
}

export function databaseForRequest(env: Env): Env {
  if (env.DATA_BACKEND !== "supabase") return env;
  const requestEnv = Object.create(env) as Env;
  Object.defineProperty(requestEnv, "DB", {
    configurable: false,
    enumerable: true,
    value: new SupabaseD1Database(env) as unknown as D1Database,
    writable: false,
  });
  return requestEnv;
}
