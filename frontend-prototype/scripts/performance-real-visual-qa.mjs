import { createRequire } from "node:module";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const require = createRequire(import.meta.url);
const { launchQaBrowser } = require("../../tools/qa-browser-runtime.cjs");

const projectRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const outputDir = path.join(projectRoot, "qa", "performance-real");
fs.mkdirSync(outputDir, { recursive: true });

const browser = await launchQaBrowser({
  headless: true
});
const report = { screenshots: [], checks: [], consoleErrors: [], pageErrors: [] };

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
  page.on("console", (message) => {
    if (message.type() === "error") report.consoleErrors.push(`${viewport.name}: ${message.text()}`);
  });
  page.on("pageerror", (error) => report.pageErrors.push(`${viewport.name}: ${error.message}`));

  await page.goto("http://127.0.0.1:4173/server/performance?range=1h", { waitUntil: "networkidle" });
  await page.getByRole("heading", { name: "性能监控" }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  await page.waitForTimeout(800);

  const dimensions = await page.evaluate(() => ({
    clientWidth: document.documentElement.clientWidth,
    scrollWidth: document.documentElement.scrollWidth,
    clientHeight: document.documentElement.clientHeight,
    scrollHeight: document.documentElement.scrollHeight,
  }));
  const visibleText = await page.locator("body").innerText();
  const screenshotPath = path.join(outputDir, `performance-${viewport.name}.png`);
  await page.screenshot({ path: screenshotPath, fullPage: true, animations: "disabled" });

  report.screenshots.push(screenshotPath);
  report.checks.push({
    viewport: viewport.name,
    horizontalOverflow: dimensions.scrollWidth > dimensions.clientWidth,
    verticalScroll: dimensions.scrollHeight > dimensions.clientHeight,
    showsMockBadge: /Mock\s*数据正常/i.test(visibleText),
    showsRealUnavailableReason: visibleText.includes("服务器未运行"),
    showsHostNetworkScope: visibleText.includes("主机网络流量"),
    rangeSelected: await page.getByRole("button", { name: "1 小时" }).getAttribute("aria-pressed"),
    chartImageCount: await page.locator('[role="img"][aria-label]').count(),
    ...dimensions,
  });

  await page.goto("http://127.0.0.1:4173/server/update?step=3", { waitUntil: "networkidle" });
  await page.getByRole("heading", { name: "执行前确认" }).waitFor();
  await page.evaluate(() => document.fonts.ready);
  const setupText = await page.locator("body").innerText();
  const setupScreenshotPath = path.join(outputDir, `setup-review-${viewport.name}.png`);
  await page.screenshot({ path: setupScreenshotPath, fullPage: true, animations: "disabled" });
  report.screenshots.push(setupScreenshotPath);
  report.checks.push({
    viewport: viewport.name,
    page: "setup-review",
    stepCount: await page.locator(".setup-wizard-step").count(),
    containsManagementModStep: setupText.includes("管理 MOD"),
    confirmsNoModWrites: setupText.includes("不会安装或写入任何 MOD"),
  });

  await context.close();
}

await browser.close();
fs.writeFileSync(path.join(outputDir, "report.json"), JSON.stringify(report, null, 2));
console.log(JSON.stringify(report, null, 2));
