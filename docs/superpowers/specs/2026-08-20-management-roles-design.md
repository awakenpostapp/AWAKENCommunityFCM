# Founder, Co-Founder and Manager Roles Design

## Goal

Add tenant-scoped `Co-Founder` and `Manager` accounts without changing the existing team data, while enforcing their permissions in both the Android client and the Cloudflare/Supabase-backed Worker.

## Confirmed product rules

- Founder creates accounts from the Founder member area.
- Co-Founder has the Founder interface and all Founder capabilities, except a Co-Founder cannot delete another Co-Founder account.
- Manager has a dedicated management interface and may only:
  - create Coach and Trainee accounts;
  - create classes;
  - review Coach check-in and check-out submissions;
  - confirm tuition bills;
  - confirm parent-paid tuition directly;
  - approve Coach salary payments.
- Manager cannot edit profiles, deactivate/reactivate accounts, delete accounts, edit team settings, send Founder announcements, override attendance, or manage Founder/Co-Founder/Manager accounts.
- New accounts use the existing bootstrap password `12345678` and must change it at first login.
- Existing `Founder`, `Coach`, `Trainee`, and `Admin` accounts and all tenant data remain valid.
- App display version remains `3.3`; after implementation only the Android build number increases to `115`.

## Role model and capability boundaries

Persist stable role keys `founder`, `co_founder`, `manager`, `coach`, `trainee`, and `admin`. The client maps them to `UserRole.Founder`, `UserRole.CoFounder`, `UserRole.Manager`, `UserRole.Coach`, `UserRole.Trainee`, and `UserRole.Admin`.

Use capability predicates at both layers instead of scattering role comparisons:

| Capability | Founder | Co-Founder | Manager | Coach/Trainee |
|---|---:|---:|---:|---:|
| Founder dashboard/UI | yes | yes | no | no |
| Read tenant members/classes/finance | yes | yes | yes, scoped to management data | role-scoped |
| Create Co-Founder/Manager | yes | no | no | no |
| Create Coach/Trainee | yes | yes | yes | no |
| Create/edit classes | yes | yes | create only | no |
| Review Coach check-in/out | yes | yes | yes | no |
| Review tuition bill / parent payment | yes | yes | yes | no |
| Approve Coach salary | yes | yes | yes | no |
| Edit team settings/announcements/attendance override/evaluations | yes | yes | no | no |
| Edit another member profile | yes | yes | no | no |
| Deactivate/reactivate member | yes | yes | no | no |
| Delete another Co-Founder | yes | no | no | no |

The final delete rule is enforced server-side even when a future UI path exposes deletion. Existing admin-only Founder deletion remains unchanged.

## Android UI and data flow

### Founder and Co-Founder

`MemberManagementPage` displays four role cards: Coach, Trainee, Co-Founder, and Manager. The primary action is `Thêm Huấn Luyện Viên/Cầu Thủ Học Viên/Co-Founder/Manager`. `MemberEditorPage` gains a role picker with all four tenant-created roles, while role-specific fields remain unchanged for Coach/Trainee and are hidden for management roles.

Co-Founder sessions route through the same Founder dashboard, navigation, pages, and data facade. All existing Founder-only client checks are replaced by the shared `IsFounderLike` capability where the operation is allowed for Co-Founder. The client never relies on these checks for security; they only control presentation.

### Manager

Add `ManagerDashboardPage` and a Manager navigation map. The page contains compact cards/buttons for:

1. Check-in/check-out review (`CoachCheckInReviewPage` and related history).
2. Class management and class creation (without Founder-only delete/settings controls).
3. Coach/Trainee member lists and the create-member page.
4. Tuition bill review and parent-payment confirmation.
5. Coach salary review and payment confirmation.

Manager profile/team information is read-only. Manager pages use the existing DTOs and `AppDatabase` methods, with capability checks added so the same online source of truth is used; no parallel local data model is introduced.

### Client role mapping

Update role text, snapshot mapping, session restoration, account creation, member filtering, page routing, and role-specific visibility. Unknown role strings must fail closed rather than defaulting to Founder.

## Worker, D1, Supabase and RLS

### Schema

Add a forward-only migration for the users role constraint in D1 and Supabase. The migration preserves every existing row and expands the allowed role set. No tenant IDs, user IDs, classes, attendance, finance, uploads, or audit rows are rewritten.

### Auth and membership

`publicUser`, JWT claims, login, refresh, snapshot, and member responses carry the new role keys. Founder/Co-Founder/Manager accounts are tenant users and require an active tenant. Admin remains global and cannot use a tenant snapshot.

### Route authorization

Introduce shared Worker predicates such as `isFounderLike`, `canManageMembers`, `canCreateClasses`, and `canApproveOperations`. Apply them to every affected route:

- `POST /v1/users`: Founder/Co-Founder may create Co-Founder/Manager/Coach/Trainee; Manager may create only Coach/Trainee.
- `POST /v1/classes`: Founder/Co-Founder/Manager.
- check-in/check-out review: Founder/Co-Founder/Manager.
- payment proof review and parent-payment confirmation: Founder/Co-Founder/Manager.
- salary approval: Founder/Co-Founder/Manager.
- profile/account status/delete operations: Founder/Co-Founder only where allowed; Manager receives 403.

Target-role validation prevents privilege escalation. A Manager cannot submit `co_founder`, `manager`, or `admin` in a create request. Co-Founder creation is only available to Founder. A Co-Founder cannot delete a target whose role is `co_founder`.

### Snapshot and RLS

Management roles receive a management snapshot containing member profiles, classes, venues, check-in/out review data, tuition proofs/invoices, receipts, salaries, notifications, and audit-safe fields needed by their pages. Sensitive profile fields remain filtered according to existing role rules. Snapshot apply permits management-role mutations only for the exact capabilities above; unsupported collections return a clear 403/422 and are never silently dropped.

Supabase RLS helper functions and policies recognize `co_founder` and `manager`. Policies use the same capability predicates as the Worker and retain tenant isolation. Cloudflare remains the API/CDN/R2 boundary; Supabase remains the production database/auth source of truth as currently configured.

## Error handling and safety

- All authorization failures return 403 with a stable `forbidden` code.
- Invalid role values return 400 `validation_error`.
- Cross-tenant IDs remain 404/403 according to the existing API contract.
- Account creation is idempotent only through the existing request path; duplicate usernames remain 409.
- Every management mutation writes an audit event with actor, tenant, action, target, and role.
- Existing Founder and Admin behavior is regression-tested before release.

## Testing strategy

1. Worker unit tests for role parsing and capability predicates.
2. Worker route tests for Manager allow/deny matrix, Co-Founder creation restrictions, and Co-Founder deletion guard.
3. Snapshot/RLS tests for tenant scope and management collections.
4. Client compile tests and focused UI/navigation tests for Founder, Co-Founder, and Manager role paths.
5. Production smoke tests for one Founder, one Co-Founder, one Manager, one Coach, and one Trainee account.
6. Signed Android Release APK verification and artifact hash verification.

## Rollout and compatibility

1. Apply schema and Worker changes first; old clients continue to work because existing role keys are unchanged.
2. Ship the Android client with the new role mapping and Manager UI.
3. Keep D1/R2 and Supabase data untouched apart from the role-constraint migration.
4. Build `AWAKENCommunityFCM-v3.3-build115-Release.apk`; keep AAB/Debug artifacts local only.
5. Back up source and the Release APK to the existing private GitHub repository using a new unique release tag `v3.3-build115`.
