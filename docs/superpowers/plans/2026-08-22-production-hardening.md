# Production Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the audited Supabase security/data-integrity gaps and align the Android UI with the online role model without changing the existing tenant data, package identity, or Cloudflare/Supabase production bindings.

**Architecture:** Keep Cloudflare Worker as the only public data boundary and keep the Supabase service-role adapter server-side. The Auth Bridge will resolve an application user only through a server-created identity mapping or a short-lived server-issued bind ticket; user-editable Supabase metadata and email-only auto-linking will not grant application access. Attendance and salary transitions will be enforced atomically at the Worker boundary, while the MAUI client will use the same role capabilities and safe error mapping.

**Tech Stack:** Cloudflare Workers, TypeScript, Supabase/PostgreSQL, D1 compatibility adapter, Node test runner, .NET MAUI Android/C#.

**Spec:** `docs/superpowers/plans/2026-08-22-production-hardening.md`

## Global Constraints

- Preserve the existing application ID, tenant data, Cloudflare bindings, Supabase project, and OAuth redirect URIs.
- Do not expose Supabase service-role credentials to Android or browser clients.
- Every production behavior change must have a regression test that was observed failing before the implementation.
- Run the checkpoint test/build commands before moving to the next checkpoint.
- Do not delete or rewrite existing production data as part of these code changes.

---

### Task 1: Secure the Supabase Auth Bridge

**Files:**
- Modify: `backend/src/supabase-auth.ts`
- Modify: `backend/src/supabase-d1.ts` only if the Auth Bridge needs a typed transaction helper
- Test: `backend/tests/supabase-auth-bridge.test.ts`

**Interfaces:**
- Consumes: Supabase verified user identity, server-created `auth_user_links` rows, existing application-user lookup.
- Produces: `exchangeSupabaseIdentity()` that refuses metadata/email-only account selection and only returns a user for a verified server mapping.

- [ ] **Step 1: Write failing tests** for metadata spoofing, email-only fallback, unknown identity, and valid pre-created mapping.
- [ ] **Step 2: Run the focused test and confirm it fails because metadata/email fallback currently selects an application user.**
- [ ] **Step 3: Remove trust in `user_metadata.app_user_id` and email-only fallback. Require an existing mapping keyed by the verified Supabase identity ID; preserve explicit bind-ticket creation as the only mapping path.
- [ ] **Step 4: Run the focused Auth Bridge test and all existing role/schema tests.**
- [ ] **Step 5: Run `npm run typecheck` and `npm run build` in `backend`.**

### Task 2: Make Auth Bridge SQL PostgreSQL-safe

**Files:**
- Modify: `backend/src/supabase-auth.ts`
- Test: `backend/tests/supabase-auth-bridge.test.ts`

- [ ] **Step 1: Add a failing assertion that the link upsert contains PostgreSQL `ON CONFLICT` and no `INSERT OR REPLACE`.**
- [ ] **Step 2: Replace SQLite syntax with a parameterized PostgreSQL-compatible upsert and preserve uniqueness semantics.**
- [ ] **Step 3: Run focused tests, typecheck, and build.**

### Task 3: Repair Supabase migration and RLS helper ordering

**Files:**
- Modify: `backend/supabase/migrations/20260818140000_rls_auth_bridge.sql`
- Modify: `backend/supabase/migrations/20260818142000_private_rls_helpers.sql`
- Add: `backend/tests/supabase-migrations.test.ts`

- [ ] **Step 1: Add a migration static test that fails when a referenced function is not created and when policies still call moved `public.current_app_*` helpers.**
- [ ] **Step 2: Rewrite the migration sequence so private helper functions are created before policies, policy bodies use `private.current_app_*`, and no nonexistent `public.rls_auto_enable()` is altered.**
- [ ] **Step 3: Run the migration test and existing schema tests.**
- [ ] **Step 4: Run a clean Supabase migration dry-run/check available in the repository and record the result.**

### Task 4: Enforce attendance and salary state transitions

**Files:**
- Modify: `backend/src/routes.ts`
- Modify: `backend/src/snapshot.ts`
- Add: `backend/src/attendance-state.ts`
- Add: `backend/tests/attendance-salary-state.test.ts`

- [ ] **Step 1: Add failing unit tests proving auto-absent and safety-closed sessions cannot be checked out, approved, or paid; prove Founder manual-teaching-without-Coach does not create Coach salary; prove Founder manual-teaching-with-Coach creates a payable session only when explicitly selected.**
- [ ] **Step 2: Implement a single transition guard and use it in checkout and review endpoints; mark automatic absence/safety close immutable and non-payable.**
- [ ] **Step 3: Make salary/review updates atomic and idempotent for the same session.**
- [ ] **Step 4: Run focused tests and all existing management/authorization tests.**

### Task 5: Remove idempotency and refresh-token races

**Files:**
- Modify: `backend/src/routes.ts`
- Modify: `backend/src/auth.ts`
- Add: `backend/tests/concurrency-guards.test.ts`

- [ ] **Step 1: Add failing tests for duplicate snapshot keys and concurrent refresh of one token.**
- [ ] **Step 2: Reserve idempotency keys atomically before mutation and use compare-and-swap refresh rotation.**
- [ ] **Step 3: Run focused tests and the full backend test suite.**

### Task 6: Align Manager and Co-Founder permissions

**Files:**
- Modify: `backend/src/routes.ts`
- Modify: `Services/AppDatabase.cs`
- Modify: `Services/RoleCapabilities.cs`
- Modify: `Views/RoleTabbedPage.cs`
- Add/modify: `backend/tests/management-permissions.test.ts`

- [ ] **Step 1: Add failing tests for Manager class/session reads and Co-Founder evaluation visibility.**
- [ ] **Step 2: Use the shared role capability rules in API authorization and MAUI class access; preserve Manager’s restricted write scope and Co-Founder’s inability to delete peer Co-Founders.**
- [ ] **Step 3: Run backend authorization tests and Android compile.**

### Task 7: Improve online UX resilience and performance

**Files:**
- Modify: `Services/AppDatabase.cs`
- Modify: `Ui/AsyncContentPage.cs`
- Modify: `Ui/UiKit.cs`
- Modify: `Views/LoginPage.cs`
- Modify: `App.xaml.cs`
- Modify: `Platforms/Android/AndroidReceiptPdfService.cs`
- Add/modify: `tests` only where a pure helper can be tested without UI runtime.

- [ ] **Step 1: Add pure tests for safe error mapping, role-aware class access, and image materialization fallback behavior.**
- [ ] **Step 2: Add bounded/concurrent image loading with placeholder fallback, avoid redundant online identity reset, and preserve the last successful projection during transient refresh failures.**
- [ ] **Step 3: Replace raw exception display with user-safe messages and log correlation IDs; disable/hide online-forbidden password reset instead of exposing a dead action.**
- [ ] **Step 4: Remove misleading offline receipt copy, repair mojibake strings, and honor system dark mode only if the palette is complete.**
- [ ] **Step 5: Run Android compile and UI-related tests.**

### Task 8: Final verification and release gate

**Files:**
- Modify: `backend/openapi.yaml` if role changes require documentation updates
- Add: `docs/verification/2026-08-22-production-hardening.md`

- [ ] **Step 1: Run every backend test, typecheck, build, dry-run deploy, and Android compile command.**
- [ ] **Step 2: Run production health checks with retry and record HTTP status/body.**
- [ ] **Step 3: Run `git diff --check` and verify no unrelated files, secrets, database dumps, or generated APK/AAB files are staged.**
- [ ] **Step 4: Record any remaining limitations and only then create release artifacts if explicitly requested.**
