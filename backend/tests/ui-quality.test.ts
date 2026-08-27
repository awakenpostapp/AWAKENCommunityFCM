import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

const root = new URL("../../", import.meta.url);
const bootstrap = await readFile(new URL("Views/BootstrapPage.cs", root), "utf8");
const asyncPage = await readFile(new URL("Ui/AsyncContentPage.cs", root), "utf8");
const login = await readFile(new URL("Views/LoginPage.cs", root), "utf8");
const receipt = await readFile(new URL("Platforms/Android/AndroidReceiptPdfService.cs", root), "utf8");
const uiKit = await readFile(new URL("Ui/UiKit.cs", root), "utf8");
const achievements = await readFile(new URL("Views/AchievementPages.cs", root), "utf8");

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
