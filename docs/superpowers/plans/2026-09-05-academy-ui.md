# Academy Community UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking. The user requested inline implementation in the existing local repository and GitHub publication; keep the shared UI integration serial on the dedicated `codex/academy-ui-build127` branch.

**Goal:** Implement the selected Option 3 academy design in the existing Android app and publish signed v3.4 build 127 to AWAKENCommunityFCM GitHub Releases.

**Architecture:** Keep MAUI, existing services and role checks. Use shared warm design tokens and reusable native views, with targeted dashboard, attendance and achievement presentation changes. Use a separate test-only Android harness with in-memory fixtures for visual and interaction verification; never seed production.

**Tech Stack:** .NET MAUI 10, Android, C#, Tabler outline icons, Nunito Sans, existing Worker/Supabase API unchanged.

**Spec:** Selected Option 3 images and review in `C:/Users/mrse7/.codex/.chatgpt-projects/g-p-6a69f7d8d8308191acd5d0f710ca0683/outputs/ux-ui-review-2026-09-05/` (`option-3-founder.png`, `option-3-coach.png`, `option-3-trainee.png`, `review.md`).

## Global constraints

- Retain version 3.4; increment build 126 to 127 after verification.
- Keep application ID `com.awaken.communityfootballclubmanager`, existing signing certificate, endpoints, tenants, database and role permissions unchanged.
- Cream #FAF7F1, forest #103C34, copper #B86435, sage #E4E9DD; no heavy drop shadows.
- Preserve actual logos, profile photos and all 21 achievement assets; use generated photo only as academy illustration, never as a real member photo.
- Achievement points remain personal; badges last 30 days; reward exchange remains coming soon.
- All attendance statuses, draft, historical Founder modes and approval/salary rules remain functional.
- Keep navigation access to every existing feature, including members and notifications when moving them off the tab bar.
- No production database/Worker deployment is needed or authorized for this visual release.

## Checkpoint 1: Shared visual foundation

Files: `Ui/UiKit.cs`, `Resources/Styles/{Colors,Styles}.xaml`, `MauiProgram.cs`, `Resources/{Fonts,Images}`, `tools/UiChecks`.

- [x] Confirm source/remote at build 126 and clean baseline: 63 existing tests passed.
- [ ] Add executable checks against actual MAUI controls: normal text/button contrast >= 4.5:1; secondary/destructive labels remain readable; large badge strips collapse without losing points.
- [ ] Run `dotnet run --project tools/UiChecks/UiChecks.csproj` and confirm baseline contrast failures.
- [ ] Replace color/typography/radius/shadow tokens; keep controls native. Use white icon variants for dark primary buttons.
- [ ] Import licensed Tabler outline SVG assets and Nunito Sans fonts. Keep original logo and badge files untouched.
- [ ] Run checks again and compile Android Debug.

## Checkpoint 2: Navigation and Founder overview

Files: `Views/RoleTabbedPage.cs`, `Views/DashboardPages.cs`, `Views/FounderMorePages.cs`, `Views/ProfileAndNotificationPages.cs`, shared Ui helpers.

- [ ] Use 5 primary tabs in selected-role layouts. Founder: Overview, Classes, Achievements, Finance, Management. Members remain directly reachable from Overview and Management. Coach/Trainee notifications remain reachable from top bell and Profile.
- [ ] Remove tab-change pop-to-root/reset behavior; preserve existing explicit data invalidation after mutations.
- [ ] Rebuild Founder header/photo/needs-attention/today-classes/member-row hierarchy from real scoped data. Keep metrics and all existing actions reachable below the primary content.
- [ ] Verify role navigation, long names, zero classes and no pending requests in the test harness.

## Checkpoint 3: Attendance and personal achievements

Files: `Views/AttendancePages.cs`, `Views/AchievementPages.cs`, `Ui/AchievementBadgeUi.cs`, `Ui/AsyncContentPage.cs` only for guarded form reload.

- [ ] Add roster search and live counts; display compact student rows and status selection with labels. Preserve all 5 enum values and full-roster save despite filtering.
- [ ] Keep draft and primary completion controls visible outside the roster scroll area; preserve unsaved edits on tab return, plus existing explicit submit confirmations.
- [ ] Present Trainee identity, personal points, active badge gallery, recent history, full-history action and coming-soon rewards. Use real feed values, not mockup numbers.
- [ ] Add name search to management achievement index and cap compact badge display at 3 plus +N, retaining the full source collection.
- [ ] Test filtering/selection/draft/submit data shape and personal points with expired/negative awards.

## Checkpoint 4: Visual/runtime QA and release

Files: `tools/UiHarness`, `design-qa.md`, `CommunityFootballClubManager.csproj`, release notes.

- [ ] Run real Android views in an isolated test package with fixture transport; capture the selected three states at phone size and inspect beside source images. Record any intentional native/runtime differences.
- [ ] Exercise navigation, search, status selection, history expansion, empty/error states and larger text. Fix actionable P0/P1/P2 visual or behavior defects before release.
- [ ] Re-run all existing tests, UI checks, Android build and source secret scan. Review complete diff for unauthorized backend/permission changes.
- [ ] Bump only ApplicationVersion to 127 and write accurate release notes with QA limitations.
- [ ] Build signed Release APK using existing keystore, compare certificate with previous release and verify manifest/package/version/hash.
- [ ] Commit scoped files, push without force, create `v3.4-build127` Release in `awakenpostapp/AWAKENCommunityFCM`, upload only signed release artifact and checksum.
- [ ] Re-fetch release metadata and download/check uploaded asset before returning release URL.

## Progress / decisions

- Final implementation and build status: checkpoints 1–3 implemented; six UI checks and 63 backend tests pass. Checkpoint 4 native QA and signed APK verification complete within the limits in `design-qa.md`. The original checklist above is retained as planning history, not a claim of exhaustive execution; live-role/zero-class/tab-timing checks were not performed. Release publication is the final outstanding step.
- Signed APK SHA256: `89b85731f5bba691f244d88702c7a7a73f7669f99903d189123a69dea5c013df`; code127/display3.4, previous signing certificate retained.

- Starting commit: `7d14e5200a2d0d1e6f3176bd5fa67d95b1ef03b3`.
- Runtime is the existing native Android MAUI application, not a new web prototype. Preserve OS bars and accessibility behavior while matching app-owned content.
- Only image production is delegated initially; the shared MAUI integration remains serial to avoid overlapping writers.
