# Founder, Co-Founder and Manager Roles Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task with verification checkpoints.

**Goal:** Add tenant-scoped Co-Founder and Manager accounts with enforced backend permissions and a matching Android Founder/Manager experience, while preserving all existing data and keeping the app at version 3.3.

**Architecture:** Keep Cloudflare Worker as the authorization/API boundary and Supabase as the production database source of truth. Add stable role keys and shared capability predicates in both layers; Co-Founder reuses Founder navigation, while Manager receives a restricted management dashboard that calls existing domain operations. Preserve the local model only as the existing compatibility facade and add no new parallel database.

**Tech Stack:** .NET MAUI Android, C# records/enums, SQLite compatibility facade, Cloudflare Workers TypeScript, D1 migrations, Supabase SQL/RLS, R2 media, Node `node:test`.

**Spec:** `docs/superpowers/specs/2026-08-20-management-roles-design.md`

## Global Constraints

- App display version stays `3.3`; increase only `<ApplicationVersion>` from `114` to `115`.
- Existing role keys and all existing tenant/user/domain IDs remain valid.
- Persist new role keys exactly as `co_founder` and `manager` in online storage and snapshots.
- Manager can only create Coach/Trainee and perform class creation, check-in/out review, tuition bill/parent-payment confirmation, and salary approval.
- Manager cannot edit profiles, change account status, delete accounts, edit team settings, send announcements, override attendance, evaluate trainees, or create management roles.
- Co-Founder has Founder UI/capabilities but cannot delete another Co-Founder.
- New accounts keep default password `12345678` and `must_change_password=1`.
- Release APK is backed up to GitHub; Debug/AAB remain local only.
- Every production-code change begins with a failing test or a reproducible compile/test assertion.

---

### Task 1: Add shared role and capability contracts

**Files:**
- Modify: `Models/Enums.cs`
- Create: `backend/src/authorization.ts`
- Modify: `backend/src/domain.ts`
- Test: `backend/tests/authorization.test.ts`
- Modify: `backend/package.json`, `backend/tsconfig.json`

**Interfaces:**
- Produces C# `UserRole.CoFounder`, `UserRole.Manager`, `RoleCapabilities.IsFounderLike`, and role labels.
- Produces TypeScript `UserRole`, `isFounderLike`, `canCreateMember`, `canCreateClass`, `canApproveOperations`, `canEditMemberProfile`, and `canDeleteTarget`.
- The predicates accept actor role plus (where needed) target role and return boolean; route handlers never duplicate role-string logic.

- [ ] **Step 1: Write failing role-matrix tests**

  Add `backend/tests/authorization.test.ts` using `node:test` and `assert/strict`. Cover:

  ```ts
  test("manager may create operational members but not management roles", () => {
    assert.equal(canCreateMember("manager", "coach"), true);
    assert.equal(canCreateMember("manager", "trainee"), true);
    assert.equal(canCreateMember("manager", "co_founder"), false);
    assert.equal(canCreateMember("manager", "manager"), false);
  });

  test("co-founder has founder capabilities but cannot delete a co-founder", () => {
    assert.equal(isFounderLike("co_founder"), true);
    assert.equal(canApproveOperations("co_founder"), true);
    assert.equal(canDeleteTarget("co_founder", "co_founder"), false);
    assert.equal(canDeleteTarget("founder", "co_founder"), true);
  });

  test("manager cannot change profiles or account status", () => {
    assert.equal(canEditMemberProfile("manager", "coach"), false);
    assert.equal(canChangeAccountStatus("manager", "trainee"), false);
  });
  ```

- [ ] **Step 2: Run the tests and verify the expected red failure**

  Add an npm script `test:roles` that runs `node --test --experimental-strip-types tests/authorization.test.ts` and run it from `backend`. It must fail because the predicates and role keys do not exist yet.

- [ ] **Step 3: Implement the minimal role contracts**

  Extend the C# enum and `DomainText.Role`. In TypeScript, add `co_founder` and `manager` to `UserRole`, then implement the pure predicates in `authorization.ts`. Change `tsconfig.json` only as needed to include `tests/**/*.ts` for typechecking without changing the production module target.

- [ ] **Step 4: Run the role tests and typecheck**

  Run `npm run test:roles` and `npm run typecheck` from `backend`; both must pass before continuing.

- [ ] **Step 5: Commit**

  Commit `feat: add co-founder and manager role contracts`.

---

### Task 2: Expand D1/Supabase role constraints and auth/snapshot role handling

**Files:**
- Create: `backend/migrations/0014_management_roles.sql`
- Create: `backend/supabase/migrations/20260820100000_management_roles.sql`
- Modify: `backend/src/auth.ts`, `backend/src/repository.ts`, `backend/src/domain.ts`
- Modify: `backend/src/snapshot.ts`
- Modify: `backend/supabase/migrations/20260818140000_rls_auth_bridge.sql`
- Modify: `backend/supabase/migrations/20260818142000_private_rls_helpers.sql`
- Test: `backend/tests/role-schema.test.ts`

**Interfaces:**
- Existing rows stay untouched; new role keys are accepted by D1 and Supabase.
- `createTenantUser` accepts a caller-authorized role from the route and keeps the bootstrap password contract.
- `getSnapshot` has explicit Founder/Co-Founder/Manager branches; management data is tenant-scoped and does not expose password fields.

- [ ] **Step 1: Write failing schema/auth tests**

  Add tests that assert the migration text contains all six non-admin role keys, that `publicUser` preserves `co_founder`/`manager`, and that `createTenantUser` rejects `admin` and accepts `manager` only when the route has authorized it. Run them and confirm failure against the current four-role implementation.

- [ ] **Step 2: Add forward-only migrations**

  For D1, recreate only the `users` table under `PRAGMA foreign_keys=OFF`, copy every existing column/row, expand the role check, restore indexes, and re-enable foreign keys. For Supabase, drop/recreate the role check constraint in one transaction. Do not update or delete any domain row.

- [ ] **Step 3: Update auth/repository/domain types**

  Extend `UserRole`, `publicUser`, and tenant checks. Make `createTenantUser` validate role strings but leave authorization to route predicates. Management profiles use the existing profile columns and default password behavior.

- [ ] **Step 4: Update snapshot branches**

  Add a management snapshot branch returning tenant-scoped users/profiles/classes/venues/assignments/sessions/check-ins/attendance/invoices/proofs/receipts/salaries/notifications. Redact password fields and exclude unsupported mutation collections. Update snapshot apply so Manager can only apply class/member-create and approved-operation deltas; reject profile/status/management-role changes with 403/422.

- [ ] **Step 5: Update Supabase RLS helpers/policies**

  Treat `co_founder` as Founder-like and `manager` as an operations approver where the policy is explicitly for check-in/out, finance approval, salaries, classes, or operational member creation. Keep team-settings, profile mutation, account-status, deletion, announcement, and evaluation policies Founder/Co-Founder only.

- [ ] **Step 6: Run tests, typecheck, and build**

  Run `npm run test:roles`, the schema tests, `npm run typecheck`, and `npm run build`. No migration test may report a changed existing role or missing role check.

- [ ] **Step 7: Commit**

  Commit `feat: persist management roles and scope snapshots`.

---

### Task 3: Enforce Worker route capabilities and audit behavior

**Files:**
- Modify: `backend/src/routes.ts`
- Modify: `backend/src/index.ts`
- Modify: `backend/src/repository.ts`
- Test: `backend/tests/management-routes.test.ts`

**Interfaces:**
- `POST /v1/users` receives `{ role: "coach" | "trainee" | "co_founder" | "manager" }`; authorization comes from JWT role, never the body.
- Existing check-in review, proof review, parent payment, salary update, and class create handlers use shared capability predicates.
- Every successful management mutation writes the existing audit event with actor and target role.

- [ ] **Step 1: Write failing route authorization tests**

  Cover a Manager and Co-Founder matrix using the repository’s Worker test harness (or a minimal in-memory D1 fixture if no harness exists): Manager can POST Coach/Trainee and class, PATCH check-in/proof/salary, POST parent payment; Manager receives 403 for Co-Founder creation, profile/status/delete, announcements, attendance override, and evaluations; Co-Founder can create management roles only when the target operation is Founder-authorized; Co-Founder deletion of Co-Founder returns 403.

- [ ] **Step 2: Run tests and verify red failures**

  Run `npm run test:management` and confirm the current strict Founder checks reject valid Manager operations and the current repository rejects management roles.

- [ ] **Step 3: Implement route predicates and target validation**

  Replace exact Founder checks only on the capabilities listed in the spec. Keep Founder-only handlers unchanged for settings, announcements, evaluations, attendance override, account status, and deletion. Add explicit target-role checks before any account mutation.

- [ ] **Step 4: Add Manager member creation behavior**

  Let Manager create only Coach/Trainee with tenant ID from `requireTenant(auth)`, default password, profile fields, and audit. Founder/Co-Founder may create all tenant management roles; Manager cannot create `admin`, `founder`, `co_founder`, or another `manager`.

- [ ] **Step 5: Run route tests and Worker checks**

  Run `npm run test:management`, `npm run typecheck`, `npm run build`, and `wrangler deploy --dry-run` via the existing `npm run check` command.

- [ ] **Step 6: Commit**

  Commit `feat: enforce manager and co-founder worker permissions`.

---

### Task 4: Add client role mapping and capability helpers

**Files:**
- Modify: `Models/Enums.cs`, `Models/DataDtos.cs`
- Modify: `Services/Online/SnapshotDtos.cs`, `Services/Online/CloudSnapshotMapper.cs`, `Services/Online/OnlineDataState.cs`
- Modify: `Services/AppDatabase.cs`, `Services/AppDatabase.Evaluations.cs`
- Modify: `Services/AppNavigator.cs`, `Services/SessionService.cs`
- Create: `Services/RoleCapabilities.cs`
- Test/verification: `dotnet build -t:Compile -f net10.0-android --no-restore`

**Interfaces:**
- `RoleCapabilities.IsFounderLike(UserRole?)`, `CanManageMembers`, `CanCreateClasses`, `CanApproveOperations`, and `CanEditMemberProfile` are pure client presentation guards mirroring Worker policy.
- Online and offline user/snapshot mapping round-trips `CoFounder` and `Manager`; unknown role strings fail closed.
- `AppDatabase` accepts capability sets rather than exact Founder comparisons for approved operations.

- [ ] **Step 1: Add a failing compile assertion**

  Add a temporary compile-time usage in `Services/RoleCapabilities.cs` and `SessionService` that references `UserRole.CoFounder` and `UserRole.Manager`; run the Android compile and record the expected missing-member failures.

- [ ] **Step 2: Implement role mapping and capability helpers**

  Add enum values after `Admin` without renumbering existing values in persisted local data; use explicit enum values and string converters for online keys. Update `CloudSnapshotMapper` and DTO converters to map `co_founder`/`manager` exactly.

- [ ] **Step 3: Replace client Founder-only checks by capability**

  Update AppDatabase role gates for class creation, management reads, check-in/out approval, tuition approval, parent payment, and salary approval. Keep Founder-only settings/announcement/evaluation/attendance override/status/delete operations as Founder-like only where allowed by the spec. Ensure Manager is never accepted by profile/status methods.

- [ ] **Step 4: Route roles after login**

  Keep Admin routing unchanged; route Co-Founder to `RoleTabbedPage` with the Founder tab set, and route Manager to the new Manager dashboard/tab page. Preserve forced-password-change behavior.

- [ ] **Step 5: Compile and inspect role mapping**

  Run the Android compile and `rg` checks for remaining unsafe exact Founder comparisons in the affected methods. Unknown role mapping must throw rather than silently becoming Founder.

- [ ] **Step 6: Commit**

  Commit `feat: map management roles in client data facade`.

---

### Task 5: Add Founder member creation and Co-Founder/Manager lists

**Files:**
- Modify: `Views/MemberPages.cs`
- Modify: `Views/DashboardPages.cs`
- Modify: `Views/FounderMorePages.cs`
- Modify: `Views/ProfileAndNotificationPages.cs`

**Interfaces:**
- `MemberManagementPage` shows Coach, Trainee, Co-Founder, and Manager cards for Founder/Co-Founder; Manager sees only Coach/Trainee and creation of those roles.
- `MemberEditorPage` supports all roles permitted by the current actor, with Coach position and Trainee tuition controls shown only for those roles.
- Profile/action buttons reflect capability guards and never grant Manager edit/status rights.

- [ ] **Step 1: Add UI test/inspection hooks**

  Add stable semantic descriptions for the four role cards, the create action, and Manager restricted actions. Add a small view-model-level assertion or a testable role-to-options helper and run it red before implementation.

- [ ] **Step 2: Implement role cards and role picker**

  Add management role cards and role labels. Use the existing compact card/pill style. Disable role changes while editing an existing account. Hide Coach/Trainee-only fields for management roles.

- [ ] **Step 3: Wire create/save permissions**

  Pass the selected role to `CreateUserAsync`; block unavailable role choices before network calls and show the existing “đang tạo account” progress UI. Refresh the member page after creation so online data is immediately visible.

- [ ] **Step 4: Apply Co-Founder UI and Manager restrictions**

  Co-Founder gets the Founder dashboard and buttons. Manager gets no Founder settings/announcement/evaluation/attendance-override controls and cannot edit profiles or statuses.

- [ ] **Step 5: Compile and manually inspect navigation**

  Run the Android compile; verify no role card uses an invalid `MemberRoleListPage` role and that Manager navigation cannot reach restricted pages from visible controls.

- [ ] **Step 6: Commit**

  Commit `feat: add management account creation and role pages`.

---

### Task 6: Build the Manager dashboard and operational pages

**Files:**
- Create: `Views/ManagerPages.cs`
- Modify: `Views/RoleTabbedPage.cs`
- Modify: `Views/AttendancePages.cs`, `Views/ClassPages.cs`, `Views/FinancePages.cs`
- Modify: `Views/MemberPages.cs`

**Interfaces:**
- `ManagerDashboardPage(AppDatabase, SessionService, MediaService, RememberedLoginService, IImageSaveService)` exposes only Manager capabilities.
- Existing review/list pages accept Manager where their server operation allows it, while Founder-only edit/delete controls remain hidden.

- [ ] **Step 1: Add failing navigation assertions**

  Add testable role-to-tab/page mapping assertions showing Manager maps to Manager dashboard and Co-Founder maps to Founder dashboard. Run them red before adding the page.

- [ ] **Step 2: Implement Manager dashboard**

  Add compact cards for Coach check-in/out review, Lớp học/Tạo lớp, Thành viên/Tạo Coach-Trainee, Học phí/Bill, and Lương Coach. Cards show counts from the existing metrics/DTOs and push to existing pages.

- [ ] **Step 3: Gate existing pages by capability**

  Permit Manager read access and allowed approval actions. Hide edit team, delete class, status/profile edit, announcements, Founder evaluation, and attendance override controls for Manager.

- [ ] **Step 4: Compile and run app smoke navigation**

  Run the Android compile and launch the app on the existing emulator if available. Verify the role page opens for a Manager session and all five cards navigate without an unauthorized-client crash.

- [ ] **Step 5: Commit**

  Commit `feat: add restricted manager dashboard`.

---

### Task 7: End-to-end regression, documentation, and versioned Release build

**Files:**
- Modify: `CommunityFootballClubManager.csproj` (`ApplicationVersion` 114 → 115 only)
- Modify: `docs/CHANGELOG.md`, `docs/CHANGELOG.txt`, `docs/BUILD_ARTIFACTS.md`
- Modify: `README.md` only if role documentation is currently user-facing
- Create: `artifacts/AWAKENCommunityFCM-v3.3-build115-Release.apk`

**Interfaces:**
- No database or Cloudflare binding changes beyond the role migration and Worker routes.
- Release artifact name is unique and must not overwrite build 114.

- [ ] **Step 1: Run all automated verification**

  Run backend role/route tests, `npm run check`, Android compile, and a clean `git diff --check`. Fix failures before building artifacts.

- [ ] **Step 2: Run production smoke checks**

  Use one existing Founder account plus temporary test accounts to verify: Founder creates Co-Founder/Manager; Manager creates Coach/Trainee and class; Manager approves check-in/out, bill/parent payment, and salary; Manager is denied profile/status/delete; Co-Founder is denied deleting Co-Founder; existing Coach/Trainee flows remain intact.

- [ ] **Step 3: Bump build number and update changelogs**

  Keep display version `3.3`, set build `115`, and record the complete role/migration/UI change list in both Markdown and TXT changelogs. Do not add AAB/Debug to GitHub backup.

- [ ] **Step 4: Build signed Release APK**

  Use the existing keystore environment (`awp1505`) and `scripts/Build-AndroidArtifact.ps1 -Configuration Release -OutputDirectory artifacts`. Refuse overwrite if `AWAKENCommunityFCM-v3.3-build115-Release.apk` already exists.

- [ ] **Step 5: Verify APK and repository state**

  Verify APK package/version, v1/v2/v3 signature, SHA-256, file size, and `git status`. Ensure only the new APK is the release artifact and no secrets/DB dumps are staged.

- [ ] **Step 6: Commit and publish backup**

  Commit source/docs/artifact metadata, push to the existing private repository `awakenpostapp/AWAKENCommunityFCM`, and create GitHub Release tag `v3.3-build115` with only the Release APK attached.

---

## Verification checklist

- [ ] Existing Founder/Coach/Trainee/Admin login and data paths pass regression smoke.
- [ ] Co-Founder receives Founder UI and all allowed operations.
- [ ] Co-Founder cannot delete another Co-Founder.
- [ ] Manager sees only the dedicated management dashboard.
- [ ] Manager can perform exactly the six listed operational capabilities.
- [ ] Manager cannot edit profiles, statuses, settings, announcements, evaluations, attendance overrides, or accounts outside Coach/Trainee creation.
- [ ] D1/Supabase migrations preserve existing rows and tenant IDs.
- [ ] RLS and Worker both reject cross-tenant and role-escalation requests.
- [ ] `AWAKENCommunityFCM-v3.3-build115-Release.apk` is signed, unique, and backed up on GitHub Release.
