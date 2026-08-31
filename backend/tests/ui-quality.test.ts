import test from "node:test";
import assert from "node:assert/strict";
import { readdir, readFile } from "node:fs/promises";

const root = new URL("../../", import.meta.url);
const bootstrap = await readFile(new URL("Views/BootstrapPage.cs", root), "utf8");
const asyncPage = await readFile(new URL("Ui/AsyncContentPage.cs", root), "utf8");
const login = await readFile(new URL("Views/LoginPage.cs", root), "utf8");
const receipt = await readFile(new URL("Platforms/Android/AndroidReceiptPdfService.cs", root), "utf8");
const financePages = await readFile(new URL("Views/FinancePages.cs", root), "utf8");
const uiKit = await readFile(new URL("Ui/UiKit.cs", root), "utf8");
const achievements = await readFile(new URL("Views/AchievementPages.cs", root), "utf8");
const achievementBadgeUi = await readFile(new URL("Ui/AchievementBadgeUi.cs", root), "utf8");
const members = await readFile(new URL("Views/MemberPages.cs", root), "utf8");
const classes = await readFile(new URL("Views/ClassPages.cs", root), "utf8");
const personalProfile = await readFile(new URL("Views/ProfileAndNotificationPages.cs", root), "utf8");
const dataDtos = await readFile(new URL("Models/DataDtos.cs", root), "utf8");
const traineeTuitionPage = financePages.slice(
  financePages.indexOf("public sealed class TuitionPage"),
  financePages.indexOf("public sealed class FounderParentTuitionPage"),
);
const memberProfile = members.slice(members.indexOf("public sealed class MemberProfilePage"));
const badgeExtractor = await readFile(new URL("tools/Extract-AchievementBadgesFromFiles.ps1", root), "utf8");
const imageNames = await readdir(new URL("Resources/Images/", root));

test("startup surfaces safe user-facing errors", () => {
  assert.match(bootstrap, /AsyncContentPage\.UserMessage\(exception\)/u);
  assert.match(asyncPage, /HttpRequestException\s*=>/u);
  assert.match(asyncPage, /TimeoutException\s*=>/u);
  assert.doesNotMatch(bootstrap, /Text\s*=\s*exception\.Message/iu);
});

test("online receipt PDF has no legacy offline-only footer", () => {
  assert.doesNotMatch(receipt, /Dữ liệu bản offline/iu);
  assert.match(receipt, /Hóa đơn được tạo bởi AWAKEN Community FCM/u);
});

test("Trainee receipt export does not upload a privileged receipt object", () => {
  const exportMethod = traineeTuitionPage.slice(
    traineeTuitionPage.indexOf("private async Task ExportReceiptAsync"),
  );
  assert.match(exportMethod, /_pdfService\.GenerateAsync\(/u);
  assert.match(exportMethod, /ShareFile\(path,\s*"application\/pdf"\)/u);
  assert.doesNotMatch(exportMethod, /UpdateReceiptPdfPathAsync\(/u);
});

test("authentication and common actions keep accessible loading/icon affordances", () => {
  assert.match(login, /LoadingOverlay\("Đang đăng nhập"\)/u);
  assert.match(login, /LoadingOverlay\("Đang tạo tài khoản"\)/u);
  assert.match(uiKit, /Source\s*=\s*"password_eye\.svg"/u);
  assert.match(uiKit, /SemanticProperties\.SetDescription\(button, text\)/u);
});

test("achievement hub does not re-parent a shared switch while re-rendering", () => {
  assert.doesNotMatch(achievements, /private readonly Switch _compactMode\b/u);
  assert.match(achievements, /private bool _compactModeEnabled\b/u);
  assert.match(achievements, /new Switch\s*\{\s*IsToggled\s*=\s*_compactModeEnabled/u);
});

test("achievement badges and points are projected per trainee in rosters", () => {
  assert.match(achievementBadgeUi, /GroupBy\(item\s*=>\s*item\.Achievement\.TraineeUserId/u);
  assert.match(achievementBadgeUi, /TotalPoints\s*=\s*feed\.TotalPoints/u);
  assert.match(achievementBadgeUi, /achievement_badge_/u);
  assert.match(achievements, /AchievementBadgeUi\.BadgeImage\(row\.Badge/u);
  assert.match(members, /AchievementBadgeUi\.SummaryView\(/u);
  assert.match(classes, /AchievementBadgeUi\.SummaryView\(/u);
});

test("achievement hub indexes trainees before opening their history", () => {
  assert.match(achievements, /RenderTraineeIndex/u);
  assert.match(achievements, /GroupBy\(item\s*=>\s*new\s*\{\s*item\.Achievement\.TraineeUserId/u);
  assert.match(achievements, /new AchievementHubPage\(\s*_database,\s*Session,\s*traineeId,\s*traineeName\s*\)/u);
  assert.doesNotMatch(achievements, /Tổng điểm trong phạm vi/u);
});

test("trainee profiles expose their own accumulated achievement points", () => {
  assert.match(memberProfile, /GetAchievementsAsync\(\s*CurrentUserId,\s*member\.Account\.Id\s*\)/u);
  assert.match(memberProfile, /AchievementBadgeUi\.Summarize/u);
  assert.match(memberProfile, /Điểm cá nhân tích lũy/u);
  assert.match(personalProfile, /GetAchievementsAsync\(\s*CurrentUserId,\s*CurrentUserId\s*\)/u);
  assert.match(personalProfile, /BuildReadOnlyView\(profile,\s*achievementSummary\)/u);
});

test("achievement create picker binds to a visible class name", () => {
  assert.match(achievements, /ItemDisplayBinding\s*=\s*new Binding\(nameof\(ClassRow\.DisplayName\)\)/u);
  assert.match(dataDtos, /public string DisplayName\s*=>\s*Class\.Name/u);
});

test("the supplied individual badge exports replace the full 21-asset catalog", () => {
  const assets = imageNames.filter((name) => /^achievement_badge_.*\.png$/u.test(name));
  assert.equal(assets.length, 21);
  for (const source of [
    "01_cup_ngoai_hang.png",
    "08_gang_tay_vang.png",
    "13_the_do.png",
    "21_no_luc_xuat_sac.png",
  ]) {
    assert.match(badgeExtractor, new RegExp(source.replaceAll(".", "\\."), "u"));
  }
});
