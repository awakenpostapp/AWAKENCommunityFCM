# Hệ thống thành tích Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Thêm hệ thống thành tích online cho Founder/Co-Founder, Coach và Trainee với catalog huy hiệu dùng chung, quy trình Coach gửi–Founder duyệt, điểm tích lũy vĩnh viễn và hiển thị thành tích trong 30 ngày.

**Architecture:** Worker tiếp tục là API duy nhất và Supabase production là nguồn dữ liệu; D1 migration giữ schema dự phòng. Thành tích dùng bảng riêng và endpoint riêng, không mở rộng snapshot đăng nhập. MAUI gọi API khi mở tab, lưu projection trong `OnlineDataState`, và dựng giao diện compact theo Apple HIG với hai chế độ thẻ lớn/danh sách.

**Tech Stack:** Cloudflare Workers + TypeScript + D1/Supabase adapter, Supabase Postgres/RLS, .NET 10 MAUI Android, SQLite-net chỉ cho offline legacy, Node test runner.

**Spec:** `docs/superpowers/specs/2026-08-27-achievement-system-design.md`

## Global Constraints

- Production writes remain online through the Worker/Supabase adapter; do not initialize SQLite when `CloudBackendOptions.IsConfigured` is true.
- Preserve existing tenants, users, classes, attendance, tuition, OAuth, Cloudflare bindings, R2 and Supabase data.
- Only Founder/Co-Founder may review; only Founder may remove an achievement; Coach creation requires a non-empty reason; Manager has no achievement mutation rights.
- Approved achievements are visible for 30 days, then become `expired` without deleting their `points_snapshot` or audit history.
- Point catalog uses only the confirmed values `500, 150, 100, 60, 30, 20, 15, 10, -10, -30`; UI must never recompute or silently normalize points.
- Every mutation uses an idempotency key, tenant-scoped queries, an audit row and role-appropriate notifications.
- Do not add a new FCM dependency; use the existing notification table and refresh flow.
- Update `docs/CHANGELOG.md` and `docs/CHANGELOG.txt` after the feature is verified; keep source backup on `origin/main`.

## File Map

- Create `backend/migrations/0016_achievements.sql` for additive D1 tables, indexes and seed catalog.
- Create `backend/supabase/migrations/20260827090000_achievements.sql` for equivalent Postgres tables, checks, RLS and seed catalog.
- Modify `backend/src/domain.ts`, `backend/src/authorization.ts`, `backend/src/route-authorization.ts`, `backend/src/routes.ts`, `backend/src/index.ts`, and `backend/src/snapshot.ts` for domain types, permission gates, endpoints and expiry maintenance.
- Create `backend/tests/achievement-authorization.test.ts`, `backend/tests/achievement-domain.test.ts`, and `backend/tests/achievement-routes.test.ts` for red/green coverage.
- Modify `Models/Entities.cs`, `Models/Enums.cs`, `Services/Online/OperationalDtos.cs`, `Services/Online/OnlineDataState.cs`, `Services/Online/CloudApiClient.cs`, and `Services/AppDatabase.cs`/new partial for mobile data contracts.
- Create `Views/AchievementPages.cs` and `Resources/Images/tab_achievements.svg`; modify `Views/RoleTabbedPage.cs` and any shared UI helper only where needed.
- Modify `docs/CHANGELOG.md`, `docs/CHANGELOG.txt`, `README.md` only for the verified feature contract and migration/deploy notes.

### Task 1: Establish failing domain and authorization tests

**Files:**
- Create: `backend/tests/achievement-domain.test.ts`
- Create: `backend/tests/achievement-authorization.test.ts`
- Modify: `backend/package.json` only if a focused test script is required.

**Interfaces:**
- Tests expect pure helpers `achievementVisibilityStatus(visibleUntil, now)`, `achievementPointsStatus(status)`, `canCreateAchievement(role)`, `canReviewAchievement(role)`, and `canRemoveAchievement(role)` to be exported from production modules in later tasks.

- [ ] **Step 1: Write the failing point/expiry tests.** Add tests with concrete cases:

```ts
test("expired approved achievement is hidden but retains points", () => {
  assert.equal(achievementVisibilityStatus("2026-07-01T00:00:00.000Z", "2026-07-02T00:00:00.000Z"), "expired");
  assert.equal(achievementPointsStatus("expired"), true);
  assert.equal(achievementPointsStatus("rejected"), false);
});

test("pending achievement remains visible to reviewers only", () => {
  assert.equal(achievementVisibilityStatus("2026-09-01T00:00:00.000Z", "2026-08-27T00:00:00.000Z"), "visible");
});
```

- [ ] **Step 2: Write the failing role matrix tests.** Assert Founder and Co-Founder can create/review, Coach can create but cannot review/remove, Trainee and Manager cannot mutate, and only Founder can remove.

```ts
test("achievement mutation matrix follows role hierarchy", () => {
  assert.equal(canCreateAchievement("founder"), true);
  assert.equal(canCreateAchievement("coach"), true);
  assert.equal(canCreateAchievement("manager"), false);
  assert.equal(canReviewAchievement("co_founder"), true);
  assert.equal(canReviewAchievement("coach"), false);
  assert.equal(canRemoveAchievement("founder"), true);
  assert.equal(canRemoveAchievement("co_founder"), false);
});
```

- [ ] **Step 3: Run only the new tests and verify RED.** Run `node --test --experimental-strip-types tests/achievement-domain.test.ts tests/achievement-authorization.test.ts` from `backend`; expected failure is missing exports, not a syntax error.

### Task 2: Add additive database migrations and catalog seed

**Files:**
- Create: `backend/migrations/0016_achievements.sql`
- Create: `backend/supabase/migrations/20260827090000_achievements.sql`
- Test: `backend/tests/supabase-migrations.test.ts` and a new SQL smoke assertion in `backend/tests/achievement-routes.test.ts`.

**Interfaces:**
- Tables expose `achievement_badges` and `trainee_achievements` with snake_case columns; the Worker queries those names in both D1 and Supabase adapter modes.
- `achievement_badges.key` is unique; `trainee_achievements.points_snapshot` is immutable application data.

- [ ] **Step 1: Write the D1 migration.** Create both tables with foreign keys to `tenants`, `users`, `classes`, and `achievement_badges`; add checks for category/status, `points >= -30`, `visible_until`, and indexes for tenant/status, trainee visibility, category/date and creator. Seed every badge name in the reference board using only confirmed point values; use `asset_key` strings so images can be supplied later.

- [ ] **Step 2: Write the Supabase migration.** Mirror the D1 schema with `timestamptz`/`date` types, `ON DELETE CASCADE` for tenant/trainee/class ownership and `SET NULL` for reviewer. Enable RLS, grant access only through the Worker service path, and add policies that prevent direct cross-tenant reads/writes. Seed with `ON CONFLICT (key) DO UPDATE` only for display metadata, never overwrite an existing `points` value.

- [ ] **Step 3: Run migration syntax tests.** Run `npm run test:schema` and a local D1 migration apply (`npm run db:migrate:local`); expected result is both tables and seed rows present without altering existing table counts.

- [ ] **Step 4: Verify Supabase schema.** Apply the migration through the configured Supabase migration workflow, then execute `select count(*) from public.achievement_badges` and `select count(*) from public.trainee_achievements`; record the results in the task evidence file.

### Task 3: Implement pure domain helpers and permission gates

**Files:**
- Modify: `backend/src/domain.ts`
- Modify: `backend/src/authorization.ts`
- Modify: `backend/src/route-authorization.ts`
- Test: `backend/tests/achievement-domain.test.ts`, `backend/tests/achievement-authorization.test.ts`

**Interfaces:**
- Export `AchievementCategory`, `AchievementStatus`, `AchievementBadgeRow`, `AchievementRow` and helpers:
  `achievementVisibilityStatus(visibleUntil: string, nowIso?: string): "visible" | "expired"`,
  `achievementPointsStatus(status: string): boolean`.
- Export assertions `assertCanCreateAchievement(role)`, `assertCanReviewAchievement(role)`, and `assertCanRemoveAchievement(role)` that throw `AuthorizationError` with stable codes.

- [ ] **Step 1: Implement the smallest helpers needed by the failing tests.** Keep category/status validation independent of D1 so tests run in Node.

- [ ] **Step 2: Run the focused tests and verify GREEN.** Run the two test files; expected output is all tests passing.

- [ ] **Step 3: Run existing role tests.** Run `npm run test:roles` and `npm run test:management`; fix only compatibility regressions in the authorization matrix.

### Task 4: Add Worker catalog/list/create/review/remove routes

**Files:**
- Modify: `backend/src/routes.ts`
- Modify: `backend/src/index.ts`
- Test: `backend/tests/achievement-routes.test.ts`

**Interfaces:**
- Add `achievementBadges(request, env)`, `achievements(request, env, achievementId?)`, and response serializers `achievementBadgeJson`/`achievementJson`.
- Register `GET /v1/achievement-badges`, `GET|POST /v1/achievements`, `PATCH /v1/achievements/:id/review`, and `DELETE /v1/achievements/:id`.
- POST request body: `{ traineeUserId, badgeId, classId?, category, title?, eventName?, reason?, awardedForDate }`.
- Review body: `{ approved: boolean, note?: string }`; DELETE is soft-remove and returns `{ removed: true }`.

- [ ] **Step 1: Write route tests with an in-memory fake D1.** Cover catalog read, Coach POST without reason → 400, Coach POST with foreign trainee/class → 403/404, Manager POST → 403, Founder review pending → approved, Trainee cross-user GET → 403, Founder DELETE → removed, and repeated idempotency key returning the original response.

- [ ] **Step 2: Run route tests and verify RED.** Run `node --test --experimental-strip-types tests/achievement-routes.test.ts`; expected failure is unregistered route/missing handler.

- [ ] **Step 3: Implement catalog GET.** Return active badges ordered by `sort_order`, never expose inactive entries for creation, and preserve `points`/`assetKey` in JSON.

- [ ] **Step 4: Implement role-scoped GET.** Founder/Co-Founder see tenant rows (including pending/rejected/removed); Coach sees rows for assigned classes and rows created by self; Trainee sees only own approved rows with `visible_until >= now`. Include `totalPoints` computed from approved/removed/expired rows and `pendingCount` for reviewer roles.

- [ ] **Step 5: Implement POST.** Authenticate, require tenant, validate category/badge/date, verify trainee enrollment and Coach assignment when actor is Coach, require trimmed Coach reason, snapshot badge points, set `visible_until` to `created_at + 30 days`, set Coach status pending and Founder/Co-Founder status approved, and insert an audit row.

- [ ] **Step 6: Implement review and remove.** Review only pending rows and only Founder/Co-Founder; update status/reviewer/note atomically, send notifications, write audit. DELETE only Founder, set `status='removed'`/`removed_at`, and never delete the row or alter points.

- [ ] **Step 7: Run route tests and verify GREEN.** Re-run the focused route test file, then `npm run typecheck`.

### Task 5: Add expiry maintenance, notifications and observability

**Files:**
- Modify: `backend/src/snapshot.ts`
- Modify: `backend/src/index.ts`
- Modify: `backend/src/routes.ts` only for shared notification helpers
- Test: `backend/tests/achievement-routes.test.ts`, `backend/tests/attendance-salary-state.test.ts`

**Interfaces:**
- Export `expireAchievements(env): Promise<number>` from `snapshot.ts`.
- Scheduled handler invokes `expireAchievements` after existing security/check-in maintenance.

- [ ] **Step 1: Add a failing expiry test.** Insert one approved row with `visible_until` in the past and one future row, call the helper, and assert only the past row changes to `expired` and both `points_snapshot` values remain unchanged.

- [ ] **Step 2: Implement idempotent expiry.** Use one tenant-safe UPDATE with `WHERE status='approved' AND visible_until < now`, return affected count, and do not touch rejected/removed rows.

- [ ] **Step 3: Add notification assertions.** Verify Coach creation creates one Founder/Co-Founder notification; approval creates Coach and Trainee notifications; rejection creates Coach notification; duplicate POST/retry does not duplicate notifications.

- [ ] **Step 4: Run scheduled/route tests and existing maintenance tests.** Run `node --test --experimental-strip-types tests/achievement-routes.test.ts tests/attendance-salary-state.test.ts`.

### Task 6: Add mobile models, DTOs, API client and volatile state

**Files:**
- Modify: `Models/Entities.cs`
- Modify: `Models/Enums.cs`
- Modify: `Services/Online/OperationalDtos.cs`
- Modify: `Services/Online/OnlineDataState.cs`
- Modify: `Services/Online/CloudApiClient.cs`
- Create: `Services/AppDatabase.Achievements.cs`
- Test: compile via `dotnet build` and focused serialization checks in `backend/tests` where possible.

**Interfaces:**
- Add `AchievementCategory`, `AchievementStatus`, `AchievementBadge`, `TraineeAchievement` with `PointsSnapshot`, `VisibleUntilUtc`, review metadata and `AssetKey`.
- Add DTOs `CloudAchievementBadge`, `CloudAchievement`, `CloudAchievementListResponse`, `CloudAchievementResponse`, `CloudCreateAchievementRequest`, `CloudReviewAchievementRequest`.
- Add `CloudApiClient` methods:
  `GetAchievementBadgesAsync`, `GetAchievementsAsync(traineeUserId?, classId?, category?, status?)`, `CreateAchievementAsync`, `ReviewAchievementAsync`, `RemoveAchievementAsync`.
- Add `OnlineDataState.AchievementBadges` and `.Achievements` plus clear/replace/upsert methods.
- Add `AppDatabase` methods `GetAchievementBadgesAsync`, `GetAchievementsAsync`, `CreateAchievementAsync`, `ReviewAchievementAsync`, `RemoveAchievementAsync` that use online endpoints when configured and safe local fallback otherwise.

- [ ] **Step 1: Add enums/entities/DTOs with JSON names matching Worker camelCase.** Keep `pointsSnapshot` and dates server-authoritative.

- [ ] **Step 2: Add API client methods using existing `GetAsync`, `PostAsync`, `PatchAsync`, `DeleteAsync` and idempotency keys.** Map `ApiException` through the existing `CloudOperationException` path.

- [ ] **Step 3: Add in-memory state updates.** A successful mutation must upsert/remove only the affected achievement and leave existing class/finance lists untouched; logout clears both new lists.

- [ ] **Step 4: Add AppDatabase role checks.** Founder/Co-Founder can list/review/remove; Coach can create/list assigned; Trainee can list self; Manager methods throw `UnauthorizedAccessException` before a network call.

- [ ] **Step 5: Run `dotnet build -f net10.0-android -c Debug`. Confirm exit code 0 and fix any compile/nullable errors without re-enabling SQLite in online mode.

### Task 7: Build the Achievement UI and navigation

**Files:**
- Create: `Views/AchievementPages.cs`
- Create: `Resources/Images/tab_achievements.svg`
- Modify: `Views/RoleTabbedPage.cs`
- Modify: `Ui/UiKit.cs` only if a compact reusable badge card helper is missing.

**Interfaces:**
- `AchievementHubPage(AppDatabase, SessionService)` loads catalog and role-scoped achievements.
- `AchievementCreatePage(AppDatabase, SessionService, IReadOnlyList<AchievementBadge>, IReadOnlyList<MemberRow>)` validates Coach reason before calling the API.
- `AchievementReviewPage(AppDatabase, SessionService)` lists pending rows and exposes approve/reject/remove actions for Founder-like users.
- `AchievementDetailsPage(...)` shows event, category, reason, points, creator/reviewer and visibility expiry.

- [ ] **Step 1: Add the tab icon and role tabs.** Add a sixth `Thành tích` tab for Founder/Co-Founder, Coach and Trainee; keep Manager out of the feature tab and preserve tab reset behavior.

- [ ] **Step 2: Implement compact hub.** Header shows total points and “Đổi quà — Coming soon”; segmented category filters show “Xếp hạng giao hữu/giải đấu” and “Xếp hạng lớp học theo tuần”; toggle switches between large badge cards and compact list using the same rows.

- [ ] **Step 3: Implement Coach create flow.** Provide class and enrolled trainee selectors, shared badge catalog selector, event/title/date fields and a required reason editor. Disable submit until reason is non-empty; show “Chờ Founder xác nhận” after success.

- [ ] **Step 4: Implement Founder review flow.** Show pending count/card, reason and creator, with approve/reject; allow Founder (not Co-Founder) to remove approved/expired rows. Refresh only achievement state after each action.

- [ ] **Step 5: Implement Trainee view.** Show only currently visible approved badges, lifetime total and a compact history indicator; never show other trainees or review controls.

- [ ] **Step 6: Add UI error/loading states.** Use existing `AsyncContentPage`, `UiKit.EmptyState`, `DisplayAlertAsync`, and a small “Đang tải…” state; avoid blocking the tab on an unrelated full snapshot refresh.

- [ ] **Step 7: Run MAUI build and static UI checks.** Run `dotnet build -f net10.0-android -c Debug`; run the existing `ui-quality.test.ts` if it checks icon/page conventions.

### Task 8: Integrate, verify production schema/API and document

**Files:**
- Modify: `docs/CHANGELOG.md`
- Modify: `docs/CHANGELOG.txt`
- Modify: `README.md` only if endpoint/migration notes are missing.
- Create: `test-evidence/achievement-system-20260827.md`

- [ ] **Step 1: Run the complete Worker checks.** Run `npm run typegen`, `npm run typecheck`, `npm run build`, `npm run test:roles`, `npm run test:schema`, `npm run test:management`, `node --test --experimental-strip-types tests/achievement-domain.test.ts tests/achievement-authorization.test.ts tests/achievement-routes.test.ts`, and `wrangler deploy --dry-run`.

- [ ] **Step 2: Apply/verify production migrations.** Apply Supabase migration and remote D1 migration with the existing production credentials; run schema probes for both tables, seed count and indexes. Do not alter existing data rows.

- [ ] **Step 3: Deploy the Worker.** Run `wrangler deploy --keep-vars`; call `/health` and `GET /v1/achievement-badges` with an authenticated test session; verify route returns catalog and no secrets are exposed.

- [ ] **Step 4: Run Android release-quality compile.** Run `dotnet build -f net10.0-android -c Release`; if a package is produced, name it with the next build number and preserve prior artifacts. Do not upload Debug/AAB unless explicitly requested; upload only Release APK to GitHub Release according to repository practice.

- [ ] **Step 5: Write evidence and changelog.** Record commands, exit codes, migration verification, endpoint smoke result, and known catalog assumptions. Add a concise Markdown and TXT changelog entry.

- [ ] **Step 6: Commit and push source backup.** Run `git status --short`, `git diff --check`, `git diff --stat`, commit with `feat: add online achievement system`, then push `origin/main` only after all verification commands pass.
