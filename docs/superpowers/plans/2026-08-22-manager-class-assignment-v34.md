# Manager Class Assignment v3.4 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent Manager class creation, add tenant-safe Manager assignment and Coach-required class creation, then publish Android v3.4.

**Architecture:** Keep `manager_user_id` as a nullable foreign-key column on `classes`, so one class has at most one operational Manager without adding a new assignment table. Enforce role and payload rules in the Worker (the security boundary), mirror the field through the .NET entity/snapshot mapper, and surface it in Founder/Coach/Trainee class cards/details. Preserve existing classes and data through additive D1/Supabase migrations.

**Tech Stack:** Cloudflare Worker TypeScript, D1/Supabase SQL migrations, .NET MAUI Android, Node test runner, dotnet publish, GitHub CLI.

**Spec:** `docs/superpowers/specs/2026-08-22-manager-class-assignment-v34.md`

## Global Constraints

- Manager cannot create or structurally update classes; Founder and Co-Founder are the only class creators.
- A newly created class must have at least one Coach assignment; legacy classes without Coach remain readable.
- Manager assignment is optional, tenant-scoped, and stored on `classes.manager_user_id`.
- Existing ApplicationId, Cloudflare/Supabase bindings, OAuth, D1/R2 data, and keystore remain unchanged.
- Release display version is `3.4`, build number is `118`; upload only the signed Release APK to GitHub Release.

### Task 1: Add failing backend authorization and schema-contract tests

**Files:**
- Modify: `backend/tests/authorization.test.ts`
- Modify: `backend/tests/route-authorization.test.ts`
- Modify: `backend/tests/role-schema.test.ts`
- Modify: `backend/tests/supabase-migrations.test.ts`

**Interfaces:**
- Consumes: current `canCreateClass`, `assertCanCreateClass`, and migration-test conventions.
- Produces: failing tests proving Manager is denied class creation, Founder/Co-Founder remain allowed, new class payloads require a Coach, and both migration files expose `manager_user_id`.

- [ ] **Step 1: Change role tests to expect Manager denial.**
  Replace the existing Manager `doesNotThrow` class assertion with `assert.throws(() => assertCanCreateClass("manager"), ...)`, and add explicit Founder/Co-Founder allowed assertions.
- [ ] **Step 2: Add the failing Coach-required contract test.**
  Import the new `validateClassCreationPayload` contract and assert a class with an empty `coachUserIds` array throws `coach_required`, while one Coach id passes.
- [ ] **Step 3: Add migration contract assertions.**
  Read `backend/migrations/0015_class_manager.sql` and `backend/supabase/migrations/20260822120000_class_manager.sql`, assert both contain `manager_user_id`, a tenant-safe foreign key/reference, and an index.
- [ ] **Step 4: Run the focused tests and verify the expected RED failures.**
  Run `npm run test:roles`, `npm run test:management`, `npm run test:schema`, and `node --test --experimental-strip-types backend/tests/supabase-migrations.test.ts`; failures must identify the old Manager permission, missing validator, or missing migration files.

### Task 2: Implement Worker authorization, payload validation, and migrations

**Files:**
- Modify: `backend/src/authorization.ts`
- Modify: `backend/src/routes.ts`
- Modify: `backend/src/snapshot.ts`
- Create: `backend/src/class-validation.ts`
- Create: `backend/migrations/0015_class_manager.sql`
- Create: `backend/supabase/migrations/20260822120000_class_manager.sql`

**Interfaces:**
- Consumes: failing tests from Task 1.
- Produces: `validateClassCreationPayload(payload)` and server-enforced Manager/Coach/Manager-assignment rules used by direct class creation and snapshot sync.

- [ ] **Step 1: Add the pure class payload validator.**
  Export `validateClassCreationPayload(payload: { coachUserIds?: unknown }): string[]`; require a non-empty array, normalize string ids, reject blank/duplicate ids with an `AuthorizationError`-compatible 400 `coach_required`/`validation_error` error, and return unique Coach ids.
- [ ] **Step 2: Change the role matrix.**
  Make `canCreateClass` return true only for Founder and Co-Founder.
- [ ] **Step 3: Add additive migrations.**
  Add nullable `manager_user_id TEXT REFERENCES users(id) ON DELETE SET NULL` to D1 and Supabase, plus tenant/class index. Do not rewrite or drop existing tables.
- [ ] **Step 4: Harden direct `POST /v1/classes`.**
  Keep `assertCanCreateClass`; parse `coachUserIds` (or one backward-compatible `coachUserId`), validate at least one Coach, validate each same-tenant active Coach, validate optional same-tenant active Manager, insert `manager_user_id`, and insert `class_coaches` rows in the same request. Reject Manager with 403 before any write.
- [ ] **Step 5: Harden snapshot class writes.**
  For Founder/Co-Founder class rows, validate optional `managerUserId` is an active same-tenant Manager and write it in class upsert. For a class id not already in D1, require at least one incoming active Coach assignment. For Manager, reject any class/classCoach/classEnrollment rows with `forbidden_manager_class_write`; preserve existing operational approval permissions.
- [ ] **Step 6: Run focused backend tests and typecheck.**
  Run the four tests from Task 1, then `npm run typecheck`. All must pass.

### Task 3: Mirror class Manager field and creation rules in Android

**Files:**
- Modify: `Models/Entities.cs`
- Modify: `Models/DataDtos.cs`
- Modify: `Services/Online/SnapshotDtos.cs`
- Modify: `Services/Online/CloudSnapshotMapper.cs`
- Modify: `Services/AppDatabase.cs`
- Modify: `Services/RoleCapabilities.cs`
- Modify: `Views/ClassPages.cs`
- Modify: `Views/ManagementPages.cs`

**Interfaces:**
- Consumes: Worker `managerUserId` snapshot field and Founder/Co-Founder-only class capability.
- Produces: class editor Manager picker, mandatory Coach validation, and class rows/details that show Manager.

- [ ] **Step 1: Add `TrainingClass.ManagerUserId` and cloud mapping.**
  Add the nullable/empty-compatible property, `CloudTrainingClassSnapshot.ManagerUserId`, and both mapper directions.
- [ ] **Step 2: Extend `ClassRow` with optional `Manager`.**
  Keep the existing four constructor arguments source-compatible by adding an optional fifth positional parameter. Populate it in online/offline `GetClassesAsync` from `TrainingClass.ManagerUserId` and the tenant-visible user/profile map.
- [ ] **Step 3: Remove Manager class creation capability.**
  Change `RoleCapabilities.CanCreateClasses` to Founder/Co-Founder only; remove the Manager Dashboard “Tạo lớp học” action and update its copy to describe operational management. Keep Manager class list/read and approval/finance actions.
- [ ] **Step 4: Add Manager picker to Founder/Co-Founder class editor.**
  Load active Manager members, bind `_manager` picker, restore existing assignment, and assign `trainingClass.ManagerUserId` before save. The picker is optional and only reachable for Founder-like roles.
- [ ] **Step 5: Require a Coach in the editor.**
  Before save, throw a clear validation error when no Coach checkbox is selected. Preserve salary validation and allow existing legacy classes to remain readable; saving a legacy class still requires selecting a Coach because the editor is a structural update.
- [ ] **Step 6: Display Manager in class surfaces.**
  Add a compact `Manager: <name>` line when assigned to current class cards, fixed-class cards, history details, and read-only class details. Do not expose Manager assignment controls to Manager/Coach/Trainee.
- [ ] **Step 7: Run dotnet format/build validation.**
  Run `dotnet build CommunityFootballClubManager.csproj --configuration Debug --framework net10.0-android --no-restore` and verify no C# compile errors.

### Task 4: Update version, changelogs, build artifacts, and publish

**Files:**
- Modify: `CommunityFootballClubManager.csproj`
- Modify: `docs/CHANGELOG.md`
- Modify: `docs/CHANGELOG.txt`
- Modify: `docs/BUILD_ARTIFACTS.md`
- Create: `artifacts/AWAKENCommunityFCM-v3.4-build118-Release.apk`
- Create: `artifacts/AWAKENCommunityFCM-v3.4-build118-Release.aab`
- Create: `artifacts/AWAKENCommunityFCM-v3.4-build118-Debug.apk`

**Interfaces:**
- Consumes: all tested code and migrations from Tasks 1–3.
- Produces: signed v3.4 build 118 artifacts and GitHub Release `v3.4-build118` with APK only.

- [ ] **Step 1: Set Android display/build versions.**
  Change `ApplicationDisplayVersion` from `3.3` to `3.4` and `ApplicationVersion` from `117` to `118`; keep ApplicationId and all backend settings unchanged.
- [ ] **Step 2: Record the release change log.**
  Add a top entry for `Release 3.4 — build 118 — 2026-08-22` in Markdown and TXT covering Manager restrictions, Manager assignment, mandatory Coach, migrations, and tests. Add the artifact hashes/table entry to `docs/BUILD_ARTIFACTS.md` after building.
- [ ] **Step 3: Run backend and Android verification before packaging.**
  Run `npm run check` in `backend` and a Debug Android build with `dotnet build ... --no-restore`.
- [ ] **Step 4: Build all three local artifacts without overwrite.**
  Run `scripts/Build-AndroidArtifact.ps1 -Configuration Release`, the same with `-Bundle`, and `-Configuration Debug`; verify exact v3.4/build118 names (or stop if the script suffixes because a collision exists).
- [ ] **Step 5: Verify signing and artifact integrity.**
  Run Android `apksigner verify --verbose` on the Release APK and `Get-FileHash -Algorithm SHA256` on APK/AAB/Debug APK. Confirm Release APK package/version with `apkanalyzer` or `aapt2 dump badging` if available.
- [ ] **Step 6: Commit, push, and publish GitHub Release.**
  Commit source, migrations, tests, changelogs, and artifact metadata; push `main` to `origin`. Create tag/release `v3.4-build118` titled `AWAKEN Community FCM v3.4 build 118`, upload only `artifacts/AWAKENCommunityFCM-v3.4-build118-Release.apk`, and verify with `gh release view v3.4-build118 --json tagName,name,assets,url`.

## Self-review checklist

- Manager is denied consistently by Android capability, direct Worker route, and snapshot write guard.
- Founder/Co-Founder can assign only a same-tenant active Manager.
- New classes cannot be persisted without an active Coach assignment.
- Existing rows survive additive D1/Supabase migrations.
- Manager information appears in all requested class information surfaces.
- AAB and Debug are built but are not attached to GitHub Release.
