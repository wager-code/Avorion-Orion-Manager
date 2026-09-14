import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "screenshots");
fs.mkdirSync(outputDir, { recursive: true });

const viewports = [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
  { name: "1366x768", width: 1366, height: 768 },
  { name: "1536x864-at-125pct", width: 1536, height: 864, dpr: 1.25 },
];

const report = { reference: "qa/server-backup-approved.png", screenshots: [], consoleErrors: [], pageErrors: [], checks: [] };
const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
});

for (const viewport of viewports) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    deviceScaleFactor: viewport.dpr ?? 1,
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/backup", { waitUntil: "networkidle" });
  await page.evaluate(() => document.fonts.ready);
  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientHeight: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
  }));

  const screenshotPath = path.join(outputDir, `backup-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: false, animations: "disabled" });
  report.screenshots.push(screenshotPath);

  await page.getByRole("radio", { name: "选择 今天 14:42 的备份" }).check();
  const selectedTimeUpdated = await page.locator(".backup-selection-summary").getByText("今天 14:42", { exact: true }).isVisible();

  await page.getByRole("button", { name: "刷新记录" }).click();
  await page.getByRole("button", { name: "刷新记录" }).waitFor({ state: "visible", timeout: 3000 });
  const refreshCompleted = await page.getByRole("button", { name: "刷新记录" }).isEnabled();

  await page.getByRole("button", { name: "立即备份" }).click();
  await page.getByRole("button", { name: "立即备份" }).waitFor({ state: "visible", timeout: 4000 });
  const backupCountAfterCreate = await page.locator(".backup-summary__item").first().locator("strong").textContent();
  const newBackupSelected = await page.locator(".backup-selection-summary").getByText("刚刚", { exact: true }).isVisible();

  await page.reload({ waitUntil: "networkidle" });
  await page.getByRole("button", { name: "开始安全恢复" }).click();
  const restoreDialogVisible = await page.getByRole("dialog").getByText("确认安全恢复？", { exact: true }).isVisible();
  await page.getByRole("button", { name: "确认开始安全恢复" }).click();
  const restoringStateVisible = await page.getByText("恢复中", { exact: true }).first().isVisible();
  await page.getByRole("button", { name: "开始安全恢复" }).waitFor({ state: "visible", timeout: 7000 });
  const restoreCompleted = await page.getByRole("button", { name: "开始安全恢复" }).isEnabled();

  report.checks.push({
    viewport: viewport.name,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    verticalScroll: dimensions.scrollHeight > dimensions.clientHeight,
    selectedTimeUpdated,
    refreshCompleted,
    backupCountAfterCreate,
    newBackupSelected,
    restoreDialogVisible,
    restoringStateVisible,
    restoreCompleted,
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(projectRoot, "qa", "backup-visual-qa-report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
