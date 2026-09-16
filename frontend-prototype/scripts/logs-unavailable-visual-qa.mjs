import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "logs-real");
fs.mkdirSync(outputDir, { recursive: true });
const browser = await launchQaBrowser({ headless: true});
const report = { screenshots: [], checks: [], pageErrors: [], expectedUnavailableResponses: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  const context = await browser.newContext({ viewport: { width: viewport.width, height: viewport.height }, locale: "zh-CN", timezoneId: "Asia/Shanghai", reducedMotion: "reduce" });
  const page = await context.newPage();
  let streamRequests = 0;
  page.on("request", (request) => { if (request.url().includes("/logs/stream")) streamRequests += 1; });
  page.on("response", (response) => {
    if (response.status() === 503 && response.url().includes("/api/v1/servers/local/logs")) report.expectedUnavailableResponses.push(`${viewport.name}: ${response.status()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/logs", { waitUntil: "domcontentloaded" });
  await page.getByText("日志不可用", { exact: true }).waitFor({ timeout: 15_000 });
  await page.getByRole("button", { name: "重新读取" }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(500);
  const dimensions = await page.evaluate(() => ({ clientWidth: document.documentElement.clientWidth, scrollWidth: document.documentElement.scrollWidth }));
  const text = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `logs-unavailable-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    showsMockText: /Mock/i.test(text),
    showsExactReason: text.includes("日志路径未配置或不可访问"),
    streamNotOpenedAfterCapabilityFailure: streamRequests === 0,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "unavailable-report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
