import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { chromium } = require(
  "C:/Users/wager/.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright",
);

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "logs-real");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  executablePath: "C:/Program Files/Google/Chrome/Application/chrome.exe",
});
const report = { screenshots: [], checks: [], consoleErrors: [], pageErrors: [], failedResponses: [] };

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

  await page.goto("http://127.0.0.1:4173/server/logs", { waitUntil: "domcontentloaded" });
  await page.getByText("真实日志已连接", { exact: true }).waitFor({ timeout: 15_000 });
  await page.locator(".logs-row").first().waitFor({ timeout: 15_000 });
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);

  const apiEvidence = await page.evaluate(async () => {
    const response = await fetch("/api/v1/servers/local/logs?limit=20");
    const body = await response.json();
    return { status: response.status, items: body.items ?? [] };
  });
  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
  }));
  const visibleText = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `logs-managed-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });

  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    apiStatus: apiEvidence.status,
    apiItemCount: apiEvidence.items.length,
    onlyAvorionServerLogs: apiEvidence.items.length > 0 && apiEvidence.items.every((item) =>
      /^serverlog.*\.txt$/i.test(item.sourceFile) && Number.isInteger(item.sourceLineNumber)),
    showsRealConnectedState: visibleText.includes("真实日志已连接"),
    showsMockText: /Mock/i.test(visibleText),
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "managed-profile-report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
