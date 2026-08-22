class ClassValidationError extends Error {
  readonly status = 400;
  readonly code: string;

  constructor(code: string, message: string) {
    super(message);
    this.code = code;
  }
}

type ClassCreationPayload = {
  coachUserIds?: unknown;
};

/** Validate the minimum structural data required to create a class. */
export function validateClassCreationPayload(payload: ClassCreationPayload): string[] {
  const raw = payload.coachUserIds;
  if (!Array.isArray(raw)) {
    throw new ClassValidationError("coach_required", "Lớp học mới phải có ít nhất một Coach.");
  }

  const ids = raw.map((value) => {
    if (typeof value !== "string" || value.trim().length === 0) {
      throw new ClassValidationError("validation_error", "coachUserIds chứa Coach không hợp lệ.");
    }
    return value.trim();
  });
  const uniqueIds = [...new Set(ids)];
  if (uniqueIds.length === 0) {
    throw new ClassValidationError("coach_required", "Lớp học mới phải có ít nhất một Coach.");
  }
  return uniqueIds;
}
