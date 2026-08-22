import type { UserRole } from "./domain";

export const MANAGEMENT_ROLES: readonly UserRole[] = ["co_founder", "manager"];
export const TENANT_MEMBER_ROLES: readonly UserRole[] = [
  "founder",
  "co_founder",
  "manager",
  "coach",
  "trainee",
];
export const CREATABLE_TENANT_USER_ROLES: readonly UserRole[] = [
  "co_founder",
  "manager",
  "coach",
  "trainee",
];

export function validateTenantUserRole(value: unknown): Exclude<UserRole, "admin" | "founder"> {
  if (typeof value === "string"
    && (CREATABLE_TENANT_USER_ROLES as readonly string[]).includes(value)) {
    return value as Exclude<UserRole, "admin" | "founder">;
  }
  throw new Error("Role chỉ có thể là co_founder, manager, coach hoặc trainee.");
}

export function isFounderLike(role: UserRole | null | undefined): boolean {
  return role === "founder" || role === "co_founder";
}

export function canCreateMember(
  actorRole: UserRole,
  targetRole: UserRole,
): boolean {
  if (isFounderLike(actorRole)) {
    return TENANT_MEMBER_ROLES.includes(targetRole);
  }
  return actorRole === "manager" && (targetRole === "coach" || targetRole === "trainee");
}

export function canCreateClass(role: UserRole): boolean {
  return isFounderLike(role);
}

/**
 * Tenant members may inspect classes they belong to or manage.  Keep this
 * read capability separate from attendance-write capability so Managers can
 * operate on class administration without submitting attendance.
 */
export function canReadClass(role: UserRole): boolean {
  return (TENANT_MEMBER_ROLES as readonly UserRole[]).includes(role);
}

export function canApproveOperations(role: UserRole): boolean {
  return isFounderLike(role) || role === "manager";
}

export function canEditMemberProfile(actorRole: UserRole, targetRole: UserRole): boolean {
  return isFounderLike(actorRole) && targetRole !== "admin";
}

export function canChangeAccountStatus(actorRole: UserRole, targetRole: UserRole): boolean {
  return isFounderLike(actorRole) && targetRole !== "admin";
}

export function canDeleteTarget(actorRole: UserRole, targetRole: UserRole): boolean {
  if (!isFounderLike(actorRole) || targetRole === "admin" || targetRole === "founder") {
    return false;
  }
  return !(actorRole === "co_founder" && targetRole === "co_founder");
}
