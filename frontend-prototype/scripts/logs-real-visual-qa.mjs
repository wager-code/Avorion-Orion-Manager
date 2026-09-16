import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "logs-real");
const fixtureLog = path.join(outputDir, "fixture", "server.log");
fs.mkdirSync(path.dirname(fixtureLog), { recursive: true });

const initialLines = [
  "2026-09-06 08:00:00 Save completed for Galaxy Alpha",
  "2026-09-06 08:00:01 Player TestPilot joined",
  "2026-09-06 08:00:02 MOD WorkshopExample error: failed to load",
];

const browser = await launchQaBrowser({ headless: true});
const report = { screenshots: [], checks: [], consoleErrors: [], pageErrors: [], failedResponses: [] };

for (const viewport of [
  { name: "1920x1080", width: 1920, height: 1080 },
  { name: "1440x900", width: 1440, height: 900 },
]) {
  fs.writeFileSync(fixtureLog, `${initialLines.join("\n")}\n`);
  const context = await browser.newContext({
    viewport: { width: viewport.width, height: viewport.height },
    locale: "zh-CN",
    timezoneId: "Asia/Shanghai",
    reducedMotion: "reduce",
    permissions: ["clipboard-read", "clipboard-write"],
  });
  const page = await context.newPage();
  let streamRequests = 0;
  page.on("request", (request) => {
    if (request.url().includes("/logs/stream")) streamRequests += 1;
  });
  page.on("response", (response) => {
    if (response.status() >= 400) report.failedResponses.push(`${viewport.name}: ${response.status()} ${response.url()}`);
  });
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/logs", { waitUntil: "domcontentloaded" });
  await page.getByText("真实日志已连接", { exact: true }).waitFor({ timeout: 15_000 });
  await page.waitForFunction(() => document.querySelectorAll(".logs-row").length === 3);

  const appendedLine = `2026-09-06 08:00:03 Player NewPilot-${viewport.name} joined`;
  fs.appendFileSync(fixtureLog, `${appendedLine}\n`);
  await page.getByText(`Player NewPilot-${viewport.name} joined`, { exact: true }).waitFor({ timeout: 10_000 });
  await page.waitForTimeout(2200);
  const stableRowCount = await page.locator(".logs-row").count();

  await page.getByRole("button", { name: "Error", exact: true }).click();
  const errorFilterCount = await page.locator(".logs-row").count();
  await page.getByRole("button", { name: "全部", exact: true }).click();
  await page.getByRole("searchbox", { name: "搜索日志内容" }).fill(`NewPilot-${viewport.name}`);
  const searchCount = await page.locator(".logs-row").count();
  await page.getByRole("button", { name: "复制可见日志" }).click();
  const clipboard = await page.evaluate(() => navigator.clipboard.readText());

  await page.locator(".logs-auto-scroll").click();
  await page.getByRole("checkbox", { name: "自动滚动" }).waitFor({ state: "attached" });
  await page.waitForTimeout(300);
  const streamRequestsAfterToggle = streamRequests;
  await page.locator(".logs-auto-scroll").click();
  await page.getByRole("searchbox", { name: "搜索日志内容" }).fill("");
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(2800);

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientHeight: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
  }));
  const visibleText = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `logs-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });
  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    initialPlusAppendedRows: stableRowCount,
    noDuplicateRows: stableRowCount === 4,
    errorFilterCount,
    searchCount,
    clipboardContainsOnlyVisibleMatch: clipboard.includes(`NewPilot-${viewport.name}`) && !clipboard.includes("TestPilot"),
    streamRequests,
    toggleDidNotReconnect: streamRequestsAfterToggle === 1,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    showsMockText: /Mock/i.test(visibleText),
    showsRealConnectedState: visibleText.includes("真实日志已连接"),
    ...dimensions,
  });
  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
