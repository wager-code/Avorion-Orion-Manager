import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const mode = process.argv[2] === "records" ? "records" : "empty";
const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "backup-real");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
});
const report = { mode, screenshots: [], checks: [], consoleErrors: [], pageErrors: [], failedResponses: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
  });
  const page = await context.newPage();
  page.on("response", (response) => {
    if (response.status() >= 400) report.failedResponses.push(`${viewport.name}: ${response.status()} ${response.url()}`);
  });
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/backup", { waitUntil: "domcontentloaded" });
  await page.getByText(mode === "records" ? "真实备份已读取" : "等待自动备份", { exact: true }).waitFor({ timeout: 15_000 });
  if (mode === "records") await page.waitForFunction(() => document.querySelectorAll(".backup-table tbody tr:not(.backup-table__empty)").length === 3);
  else await page.getByText("尚未生成自动备份", { exact: true }).waitFor();

  const apiEvidence = await page.evaluate(async () => {
    const response = await fetch("/api/v1/servers/local/backups?limit=100");
    return { status: response.status, body: await response.json() };
  });
  const initialSelection = await page.locator(".backup-selection-summary").innerText();
  if (mode === "records") await page.locator(".backup-table tbody tr").nth(1).click();
  const changedSelection = await page.locator(".backup-selection-summary").innerText();
  await page.getByRole("button", { name: "刷新记录" }).click();
  await page.getByRole("button", { name: "刷新记录" }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(2800);

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  const bodyText = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `backup-${mode}-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    apiStatus: apiEvidence.status,
    apiTotal: apiEvidence.body.total,
    apiOnlyBakFiles: apiEvidence.body.items.every((item) => item.fileName.toLowerCase().endsWith(".bak")),
    expectedState: mode === "records" ? apiEvidence.body.total === 3 : apiEvidence.body.total === 0,
    selectionChanged: mode === "records" ? initialSelection !== changedSelection : true,
    restoreLocked: await page.getByRole("button", { name: "恢复功能尚未开放" }).isDisabled(),
    showsMockText: /Mock/i.test(bodyText),
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, `report-${mode}.json`), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
