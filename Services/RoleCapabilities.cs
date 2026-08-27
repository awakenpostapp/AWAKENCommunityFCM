using CommunityFootballClubManager.Models;

namespace CommunityFootballClubManager.Services;

/// <summary>
/// Presentation capability helpers shared by navigation and pages. The Worker
/// repeats these rules server-side; these helpers must never be treated as the
/// security boundary.
/// </summary>
public static class RoleCapabilities
{
    public static string ToWireRole(UserRole role) =>
        role switch
        {
            UserRole.CoFounder => "co_founder",
            UserRole.Manager => "manager",
            UserRole.Founder => "founder",
            UserRole.Coach => "coach",
            UserRole.Trainee => "trainee",
            UserRole.Admin => "admin",
            _ => role.ToString().ToLowerInvariant()
        };

    public static bool IsFounderLike(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder;

    public static bool CanManageMembers(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder or UserRole.Manager;

    public static bool CanCreateMember(UserRole? actorRole, UserRole targetRole) =>
        actorRole is UserRole.Founder or UserRole.CoFounder
            ? targetRole is UserRole.Coach
                or UserRole.Trainee
                or UserRole.CoFounder
                or UserRole.Manager
            : actorRole == UserRole.Manager
                && targetRole is UserRole.Coach or UserRole.Trainee;

    public static bool CanCreateClasses(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder;

    public static bool CanApproveOperations(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder or UserRole.Manager;

    public static bool CanCreateAchievements(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder or UserRole.Coach;

    public static bool CanReviewAchievements(UserRole? role) =>
        role is UserRole.Founder or UserRole.CoFounder;

    public static bool CanRemoveAchievements(UserRole? role) =>
        role == UserRole.Founder;

    public static bool CanEditMemberProfile(UserRole? actorRole, UserRole targetRole) =>
        IsFounderLike(actorRole) && targetRole != UserRole.Admin;

    public static bool CanChangeAccountStatus(UserRole? actorRole, UserRole targetRole) =>
        IsFounderLike(actorRole) && targetRole != UserRole.Admin;

    public static bool CanDeleteTarget(UserRole? actorRole, UserRole targetRole) =>
        IsFounderLike(actorRole)
        && targetRole is not (UserRole.Admin or UserRole.Founder)
        && !(actorRole == UserRole.CoFounder && targetRole == UserRole.CoFounder);
}
