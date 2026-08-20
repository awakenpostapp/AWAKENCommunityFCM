import type { UserRole } from "./domain.ts";
import {
  canApproveOperations,
  canCreateClass,
  canCreateMember,
  canChangeAccountStatus,
  canDeleteTarget,
  canEditMemberProfile,
} from "./authorization.ts";

/**
 * A deliberately small, ApiError-compatible error.  Keeping this module free
 * of the HTTP parser makes the authorization matrix unit-testable with
 * Node's strip-only TypeScript runner while the Worker error handler still
 * serializes status/code/message in the same way as ApiError.
 */
export class AuthorizationError extends Error {
  readonly status: number;
  readonly code: string;
  readonly details?: unknown;

  constructor(status: number, code: string, message: string, details?: unknown) {
    super(message);
    this.status = status;
    this.code = code;
    this.details = details;
  }
}

export function assertCanCreateMember(actorRole: UserRole, targetRole: UserRole): void {
  if (!canCreateMember(actorRole, targetRole)) {
    throw new AuthorizationError(403, "forbidden_member_role", "Role hiện tại không được tạo loại account này.");
  }
}

export function assertCanCreateClass(actorRole: UserRole): void {
  if (!canCreateClass(actorRole)) {
    throw new AuthorizationError(403, "forbidden_class_create", "Role hiện tại không được tạo lớp học.");
  }
}

export function assertCanApproveOperations(actorRole: UserRole): void {
  if (!canApproveOperations(actorRole)) {
    throw new AuthorizationError(403, "forbidden_operation_approval", "Role hiện tại không được duyệt nghiệp vụ.");
  }
}

export function assertCanEditMemberProfile(actorRole: UserRole, targetRole: UserRole): void {
  if (!canEditMemberProfile(actorRole, targetRole)) {
    throw new AuthorizationError(403, "forbidden_profile_edit", "Role hiện tại không được sửa hồ sơ thành viên.");
  }
}

export function assertCanChangeAccountStatus(actorRole: UserRole, targetRole: UserRole): void {
  if (!canChangeAccountStatus(actorRole, targetRole)) {
    throw new AuthorizationError(403, "forbidden_account_status", "Role hiện tại không được đổi trạng thái account.");
  }
}

export function assertCanDeleteTarget(actorRole: UserRole, targetRole: UserRole): void {
  if (!canDeleteTarget(actorRole, targetRole)) {
    throw new AuthorizationError(403, "forbidden_account_delete", "Role hiện tại không được xóa account này.");
  }
}
