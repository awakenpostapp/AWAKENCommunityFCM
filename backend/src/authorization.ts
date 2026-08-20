import type { UserRole } from "./domain";

export const MANAGEMENT_ROLES: readonly UserRole[] = ["co_founder", "manager"];
export const TENANT_MEMBER_ROLES: readonly UserRole[] = [
  "founder",
  "co_founder",
  "manager",
  "coach",
  "trainee",
];

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
  return isFounderLike(role) || role === "manager";
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
