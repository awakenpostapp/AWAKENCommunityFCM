export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    message: string,
    public readonly details?: unknown,
  ) {
    super(message);
  }
}

export function json(data: unknown, status = 200, headers?: HeadersInit): Response {
  const responseHeaders = new Headers(headers);
  responseHeaders.set("content-type", "application/json; charset=utf-8");
  responseHeaders.set("cache-control", "no-store");
  return new Response(JSON.stringify(data), { status, headers: responseHeaders });
}

export function noContent(): Response {
  return new Response(null, { status: 204, headers: { "cache-control": "no-store" } });
}

export async function readJson<T>(request: Request, maxBytes = 1_048_576): Promise<T> {
  const contentType = request.headers.get("content-type")?.split(";", 1)[0]?.trim().toLowerCase();
  if (contentType !== "application/json") {
    throw new ApiError(415, "unsupported_media_type", "Content-Type phải là application/json.");
  }

  const declaredLength = Number(request.headers.get("content-length") ?? "0");
  if (Number.isFinite(declaredLength) && declaredLength > maxBytes) {
    throw new ApiError(413, "payload_too_large", "Dữ liệu gửi lên vượt quá giới hạn.");
  }

  if (!request.body) {
    throw new ApiError(400, "invalid_json", "Thiếu nội dung JSON.");
  }

  const reader = request.body.getReader();
  const chunks: Uint8Array[] = [];
  let total = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    total += value.byteLength;
    if (total > maxBytes) {
      await reader.cancel();
      throw new ApiError(413, "payload_too_large", "Dữ liệu gửi lên vượt quá giới hạn.");
    }
    chunks.push(value);
  }

  const bytes = new Uint8Array(total);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }

  try {
    return JSON.parse(new TextDecoder().decode(bytes)) as T;
  } catch {
    throw new ApiError(400, "invalid_json", "Nội dung JSON không hợp lệ.");
  }
}

export function requireText(value: unknown, field: string, maxLength: number): string {
  if (typeof value !== "string") {
    throw new ApiError(400, "validation_error", `${field} không hợp lệ.`);
  }
  const result = value.trim();
  if (!result || result.length > maxLength) {
    throw new ApiError(400, "validation_error", `${field} phải có từ 1 đến ${maxLength} ký tự.`);
  }
  return result;
}

export function optionalText(value: unknown, field: string, maxLength: number): string {
  if (value === undefined || value === null) return "";
  if (typeof value !== "string" || value.trim().length > maxLength) {
    throw new ApiError(400, "validation_error", `${field} không hợp lệ.`);
  }
  return value.trim();
}

export function requireDateKey(value: unknown, field: string): string {
  const result = requireText(value, field, 10);
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(result)) {
    throw new ApiError(400, "validation_error", `${field} ph\u1ea3i c\u00f3 d\u1ea1ng YYYY-MM-DD.`);
  }
  const parsed = new Date(`${result}T00:00:00Z`);
  if (!Number.isFinite(parsed.getTime()) || parsed.toISOString().slice(0, 10) !== result) {
    throw new ApiError(400, "validation_error", `${field} kh\u00f4ng ph\u1ea3i l\u00e0 ng\u00e0y h\u1ee3p l\u1ec7.`);
  }
  return result;
}

export function requireInteger(value: unknown, field: string, minimum: number, maximum: number): number {
  if (!Number.isInteger(value) || (value as number) < minimum || (value as number) > maximum) {
    throw new ApiError(400, "validation_error", `${field} phải là số nguyên từ ${minimum} đến ${maximum}.`);
  }
  return value as number;
}

export function withCors(request: Request, response: Response, env: Env): Response {
  const origin = request.headers.get("origin");
  if (!origin) return response;

  const allowed = env.ALLOWED_ORIGINS.split(",").map((item) => item.trim()).filter(Boolean);
  if (!allowed.includes(origin)) return response;

  const headers = new Headers(response.headers);
  headers.set("access-control-allow-origin", origin);
  headers.set("vary", "Origin");
  headers.set("access-control-allow-headers", "Authorization, Content-Type, X-File-Name, X-Upload-Purpose, X-Bootstrap-Secret, Idempotency-Key");
  headers.set("access-control-allow-methods", "GET, POST, PUT, PATCH, DELETE, OPTIONS");
  headers.set("access-control-max-age", "86400");
  return new Response(response.body, { status: response.status, statusText: response.statusText, headers });
}

export function errorResponse(error: unknown): Response {
  // Some Workers runtime boundaries can preserve the error fields but not
  // the prototype identity after an awaited D1 operation. Accept the safe
  // structural shape as well so OAuth/D1 validation errors do not become a
  // misleading 500 response.
  if (error instanceof ApiError || isApiErrorLike(error)) {
    const apiError = error as ApiError;
    return json(
      { error: { code: apiError.code, message: apiError.message, details: apiError.details } },
      apiError.status,
    );
  }
  console.error(JSON.stringify({ level: "error", event: "unhandled_exception", error: String(error) }));
  return json({ error: { code: "internal_error", message: "Máy chủ gặp lỗi. Vui lòng thử lại." } }, 500);
}

function isApiErrorLike(error: unknown): error is ApiError {
  if (!error || typeof error !== "object") return false;
  const candidate = error as Partial<ApiError>;
  const status = candidate.status;
  return typeof status === "number"
    && Number.isInteger(status)
    && typeof candidate.code === "string"
    && typeof candidate.message === "string"
    && status >= 400
    && status <= 599;
}
