# Option 3 native Android design QA

final result: pass (selected UI surfaces; fixture-backed Android validation, not a production business audit)

## Evidence and normalization

- Source: `C:/Users/mrse7/.codex/.chatgpt-projects/g-p-6a69f7d8d8308191acd5d0f710ca0683/outputs/ux-ui-review-2026-09-05/option-3-{founder,coach,trainee}.png`.
- Source pixels: Founder 853x1844, Coach 852x1846, Trainee 853x1844 (approximately 393dp wide).
- Native evidence: `test-evidence/academy127/founder-swiftshader.png`, `coach-first.png`, `trainee-first.png`.
- Emulator: Android 12/API31; 1179x2556 pixels, 480dpi / 3x = 393x852dp. Native OS status/navigation bars are retained; the source does not include those bars. Comparison is by equal content width; do not treat the 72dp OS region as missing app content.
- Real production MAUI views are linked into the separate, network-isolated `com.awaken.fcm.uiharness` package with fictional data. No production accounts or database writes.
- Founder and Coach source/captures opened together in each comparison input. Initial all-black screenshots were emulator GPU capture failure, resolved by restarting the owned emulator with SwiftShader; not product evidence.

## Iteration 1 findings

- P1: Trainee gallery throws for `FlexBasis(33.333f, true)`. Native request returns HTTP200 but page shows a generic error. Executable MAUI reproduction confirms relative basis must be in [0,1]. Fix: one-third fractional width; full-gallery regression test.
- P1: Successful retry does not restore AchievementHub's original content after the shared error view replaces it. Source-traced and independently reviewed. Fix: retain and restore the original root only after successful render; verify transient error then retry on Android.
- P1: Black OS status icons on forest background have insufficient contrast. Fix: cream Android status-bar background consistent with the selected light theme.
- P2: Coach search and mark-all consume two full rows; a two-row footer further reduces roster density compared with the source. Fix: inline search/mark-all, move draft action into native toolbar, retain fixed completion action and all status options.
- P3: Supplied AWAKEN mark includes a pale square background, unlike the generated source logo. Intentional preservation of the owner's supplied branding.

## Required fidelity surfaces

- Typography: Nunito Sans weights 400/700, Vietnamese readable, forest title hierarchy. Pending larger-text verification.
- Spacing/layout: warm 16–18dp radii and shared 20dp margins. Coach density fix pending. Extra real operational data remains scrollable rather than omitted to mimic mock values.
- Colors: cream/forest/sage/copper match design direction; native status-bar contrast fix pending.
- Image quality: academy photo matches warm teal training direction; generated illustration only, supplied member photos/badges are not replaced. Original 21 badge assets retained.
- Copy/content: real team/class/counts replace mock labels. Date remains Vietnamese. Full draft, completion, approvals and personal totals are preserved.

## Remaining acceptance checks

The following was the iteration-2 checklist. Final disposition is recorded below; it is not a claim that every production role or backend operation was exercised.

- Iteration 2: error recovery succeeds on Android (`achievement-transient-error.png` → `trainee-fixed.png`), personal365 and expired-point behavior confirmed. Native FlexLayout wraps the third award despite fractional basis; P2 gallery density remains. Replaced wrap calculation with three explicit Star grid tracks and strengthened the eight-award component test. Re-capture required.

- Capture revised Founder/Coach/Trainee at equal phone width; compare again.
- Verify search, all status choices, full-roster draft/save, tab return, history, error recovery and empty states.
- Verify 1.3x text and compact phone width.
- Record final evidence and close all actionable P0/P1/P2 findings before release.

## Final verification — 2026-09-05

- Compared `founder-final.png`, `coach-final.png`, and `trainee-grid.png` beside their respective option-3 source images at equal content width. Warm palette, typography, hierarchy, compact attendance rows and three-column badge gallery are accepted. Native OS bars, real longer labels, supplied logo and extra operational content intentionally require more scrolling than the concepts.
- All P1/P2 findings above are resolved: valid bounded gallery Grid, restored content after successful retry, light status bar, inline search/mark-all and native draft toolbar with fixed completion footer.
- Attendance: searched “Minh Khang”; only matching row visible; all five status choices available; selected Late; saved draft, reloaded with Late retained; completed with confirmation. Fixture logs show `records=18 submit=False` and `records=18 submit=True`, both retaining `trainee-5=late`. Filtering never reduced the saved roster.
- Review follow-up: removed stale draft toolbar at reload start and blocked save/submit until `_rosterReady`; independently identified issue was corrected and source-checked. Final follow-up reviewer unavailable; no claim of a second independent approval.
- Trainee: personal 365 includes expired and negative awards; three active awards rendered. Empty fixture shows 0 and explanatory copy (`trainee-empty.png`); transient error recovers on retry. History opened both through recent row and explicit history button; rewards remain disabled.
- At 360dp width and system font scale 1.3, labels wrap without overlap; all three awards remain visible, history/reward actions reachable by native swipe (`trainee-large-text.png`, `trainee-large-scroll.png`). Reset QA device to 393dp and scale 1.0 afterward.
- Fresh checks: 6 UI component checks and 63 backend tests pass; secret scanner passes 157 files. Source whitespace check passes excluding the verbatim upstream Nunito OFL license (one pre-existing trailing space retained). Signed Release build exits 0. APK installs and launches to the production login screen on Android 12.
- APK identity: `com.awaken.communityfootballclubmanager`, version 3.4, code 127. Signer SHA256 matches build126: `98cd363b8a135b597393458fbd9e688601793186bcaa1520c446b27bbcf2e19d`.
- Limits: no live account login, payment, salary, deletion, or production database writes; no exhaustive Manager/Co-Founder interaction audit, tab-return timing test, zero-class dashboard fixture or physical-device matrix. Existing business tests and source review cover unchanged service/permission paths. The separate harness is excluded from production project items.
- Remaining P3: supplied logo's pale background and original badge edge quality are retained intentionally. No actionable P0/P1/P2 remains on the inspected surfaces.
